using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// 2023 room roles, the co-owner invite flow and the sub-room role-permission
/// matrix, against the client contract:
/// <list type="bullet">
/// <item>Role enum OMMBGJMJJPN: None 0, Host 10, Moderator 20, CoOwner 30,
/// TemporaryCoOwner 31, Creator 255. The creator must be listed as 255 —
/// the client takes the local role from the Roles list and only lets a
/// player edit roles strictly below their own, Creator excepted
/// (OMJKHJLFOCO.DIIMLMKEJLC).</item>
/// <item>Invite: owner <c>PUT rooms/{id}/roles/{target}/invite</c> role=30;
/// invitee gets message Type 62 with RoomId; accepts with
/// <c>PUT rooms/{id}/roles/{me}</c> role=30, rejects with
/// <c>PUT rooms/{id}/roles/{me}/invite</c> role=0.</item>
/// <item>Permissions: <c>PUT rooms/{id}/subrooms/{sub}/permissions</c> raw
/// JSON array of {Permission, Role, Override, Type, Value}; read back from
/// <c>GET photon_access_token</c>.</item>
/// </list>
/// </summary>
public sealed class RoomRoles2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RoomRoles2023Tests(DorkNetServerFactory factory) => _factory = factory;

    // ── pure rules ──────────────────────────────────────────────────────

    private static RoomEntity Room(long creator) => new() { Id = 42, CreatorPlayerId = creator };

    private static RoomRoleEntity Row(long player, int stored, bool accepted = true) =>
        new() { RoomId = 42, PlayerId = player, Role = stored, Accepted = accepted };

    [Fact]
    public void Creator_is_255_and_listed_once()
    {
        var room = Room(creator: 7);
        var wires = RoomRoleRules.BuildWires(room, new[]
        {
            Row(7, RoomRoleRules.StoredCoOwner), // a stray row for the creator
            Row(8, RoomRoleRules.StoredModerator),
            Row(9, RoomRoleRules.StoredCoOwner, accepted: false),
        });

        Assert.Single(wires, w => w.AccountId == 7);
        Assert.Equal(255, wires.Single(w => w.AccountId == 7).Role);
        Assert.Equal(20, wires.Single(w => w.AccountId == 8).Role);
        var invited = wires.Single(w => w.AccountId == 9);
        Assert.Equal(0, invited.Role);
        Assert.Equal(30, invited.InvitedRole);
    }

    [Fact]
    public void Creator_may_edit_every_role_including_co_owner_and_creator_permissions()
    {
        var room = Room(creator: 7);
        var rows = Array.Empty<RoomRoleEntity>();
        foreach (var role in new[] { 0, 10, 20, 30, 31, 255 })
            Assert.True(RoomRoleRules.CanEditRolePermissions(room, rows, 7, role), $"creator role {role}");
    }

    [Fact]
    public void Co_owner_may_edit_only_roles_below_their_own()
    {
        var room = Room(creator: 7);
        var rows = new[] { Row(8, RoomRoleRules.StoredCoOwner), Row(9, RoomRoleRules.StoredModerator) };

        Assert.True(RoomRoleRules.CanEditRolePermissions(room, rows, 8, 20));
        Assert.True(RoomRoleRules.CanEditRolePermissions(room, rows, 8, 0));
        Assert.False(RoomRoleRules.CanEditRolePermissions(room, rows, 8, 30));
        Assert.False(RoomRoleRules.CanEditRolePermissions(room, rows, 8, 31));
        Assert.False(RoomRoleRules.CanEditRolePermissions(room, rows, 8, 255));
        // A moderator edits nothing.
        Assert.False(RoomRoleRules.CanEditRolePermissions(room, rows, 9, 0));
        // Nor does a stranger.
        Assert.False(RoomRoleRules.CanEditRolePermissions(room, rows, 99, 0));
    }

    [Fact]
    public void Role_changes_follow_the_ladder()
    {
        var room = Room(creator: 7);
        var rows = new List<RoomRoleEntity>
        {
            Row(8, RoomRoleRules.StoredCoOwner),
            Row(9, RoomRoleRules.StoredModerator),
            Row(10, RoomRoleRules.StoredCoOwner, accepted: false),
        };

        // Creator: anything on anyone but themselves.
        Assert.Null(RoomRoleRules.CheckChange(room, rows, 7, 8, 0, false, out var k));
        Assert.Equal(RoomRoleRules.ChangeKind.Clear, k);
        Assert.Null(RoomRoleRules.CheckChange(room, rows, 7, 11, 30, true, out k));
        Assert.Equal(RoomRoleRules.ChangeKind.Invite, k);
        Assert.Equal("cannot_change_creator_role", RoomRoleRules.CheckChange(room, rows, 7, 7, 0, false, out _));
        Assert.Equal("cannot_change_creator_role", RoomRoleRules.CheckChange(room, rows, 8, 7, 0, false, out _));

        // Co-owner: only below 30, on targets below 30.
        Assert.Null(RoomRoleRules.CheckChange(room, rows, 8, 11, 20, false, out _));
        Assert.Null(RoomRoleRules.CheckChange(room, rows, 8, 9, 10, false, out _));
        Assert.Equal("forbidden", RoomRoleRules.CheckChange(room, rows, 8, 11, 30, true, out _));
        Assert.Equal("forbidden", RoomRoleRules.CheckChange(room, rows, 8, 10, 0, true, out _)); // invited co-owner
        // …and cannot remove a peer co-owner.
        Assert.Equal("forbidden", RoomRoleRules.CheckChange(
            room, new List<RoomRoleEntity> { Row(8, 0), Row(12, 0) }, 8, 12, 0, false, out _));

        // Moderator and strangers manage nobody.
        Assert.Equal("forbidden", RoomRoleRules.CheckChange(room, rows, 9, 11, 10, false, out _));
        Assert.Equal("forbidden", RoomRoleRules.CheckChange(room, rows, 99, 11, 10, false, out _));

        // Self-service: accept exactly the invited role, decline, leave.
        Assert.Null(RoomRoleRules.CheckChange(room, rows, 10, 10, 30, false, out k));
        Assert.Equal(RoomRoleRules.ChangeKind.AcceptInvite, k);
        Assert.Null(RoomRoleRules.CheckChange(room, rows, 10, 10, 0, true, out k));
        Assert.Equal(RoomRoleRules.ChangeKind.Clear, k);
        Assert.Null(RoomRoleRules.CheckChange(room, rows, 9, 9, 0, false, out _));
        Assert.Equal("forbidden", RoomRoleRules.CheckChange(room, rows, 9, 9, 30, false, out _)); // no invite
        Assert.Equal("forbidden", RoomRoleRules.CheckChange(room, rows, 10, 10, 20, false, out _)); // not the invited role

        Assert.Equal("invalid_role", RoomRoleRules.CheckChange(room, rows, 7, 11, 255, false, out _));
        Assert.Equal("invalid_role", RoomRoleRules.CheckChange(room, rows, 7, 11, 31, false, out _));
    }

    [Fact]
    public void Permission_matrix_merges_stored_cells_over_defaults()
    {
        var merged = RoomPermissionMatrix.Merge(new[]
        {
            new RoomRolePermissionEntity { Id = 1, Role = 30, Permission = "13", Type = 0, Override = true, Value = "false" },
            new RoomRolePermissionEntity { Id = 2, Role = 20, Permission = "13", Type = 0, Override = true, Value = "true" },
        });

        Assert.Equal("false", merged.Single(c => c.Role == 30 && c.Permission == "13").Value);
        Assert.Equal("true", merged.Single(c => c.Role == 20 && c.Permission == "13").Value);
        Assert.Equal("true", merged.Single(c => c.Role == 255 && c.Permission == "13").Value);
        Assert.Equal(RoomPermissionMatrix.Defaults.Count + 1, merged.Count);
    }

    [Fact]
    public void Permission_body_accepts_numbers_strings_and_enum_names()
    {
        var cells = RoomPermissionMatrix.Parse(
            """[{"Permission":"13","Role":30,"Override":true,"Type":0,"Value":"false"},""" +
            """{"permission":"CAN_USE_MAKER_PEN","role":"Moderator","override":"true","type":"Bool","value":true},""" +
            """{"Permission":"20","Role":"255","Override":false,"Type":1,"Value":5}]""");

        Assert.NotNull(cells);
        Assert.Equal(3, cells!.Count);
        Assert.Equal(new RoomPermissionMatrix.Cell("13", 30, true, 0, "false"), cells[0]);
        Assert.Equal(new RoomPermissionMatrix.Cell("CAN_USE_MAKER_PEN", 20, true, 0, "true"), cells[1]);
        Assert.Equal(new RoomPermissionMatrix.Cell("20", 255, false, 1, "5"), cells[2]);
        Assert.Null(RoomPermissionMatrix.Parse("role=30"));
    }

    // ── over HTTP ───────────────────────────────────────────────────────

    [Fact]
    public async Task Roles_list_reports_the_creator_as_Creator_255()
    {
        var (owner, _, _) = await PlayersAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId);

        using var rooms = Client("rooms", owner);
        var list = await GetJsonAsync(rooms, $"/roomserver/rooms/{roomId}/roles");
        var me = list.EnumerateArray().Single(e => e.GetProperty("AccountId").GetInt64() == owner.PlayerId);
        Assert.Equal(255, me.GetProperty("Role").GetInt32());

        var details = await GetJsonAsync(rooms, $"/roomserver/rooms/{roomId}");
        var detailsRole = details.GetProperty("Roles").EnumerateArray()
            .Single(e => e.GetProperty("AccountId").GetInt64() == owner.PlayerId);
        Assert.Equal(255, detailsRole.GetProperty("Role").GetInt32());

        var single = await GetJsonAsync(rooms, $"/roomserver/rooms/{roomId}/roles/{owner.PlayerId}");
        Assert.Equal(255, single.GetProperty("Role").GetInt32());
    }

    [Fact]
    public async Task Co_owner_invite_is_notified_then_accepted_by_the_invitee()
    {
        var (owner, invitee, _) = await PlayersAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId);

        using var ownerRooms = Client("rooms", owner);
        var invited = await PutFormAsync(ownerRooms, $"/roomserver/rooms/{roomId}/roles/{invitee.PlayerId}/invite", ("role", "30"));
        var inviteeRow = RoleOf(invited, invitee.PlayerId);
        Assert.Equal(0, inviteeRow.GetProperty("Role").GetInt32());
        Assert.Equal(30, inviteeRow.GetProperty("InvitedRole").GetInt32());

        // The invitee's inbox carries message Type 62 (RoomCoOwnerInvited) with RoomId.
        long messageId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var msg = await db.Messages.SingleAsync(m =>
                m.RecipientPlayerId == invitee.PlayerId && m.RoomId == roomId
                && m.Type == SystemNotificationService.MessageType.RoomCoOwnerInvited);
            Assert.Equal(owner.PlayerId, msg.SenderPlayerId);
            Assert.Null(msg.ReadAt);
            messageId = msg.Id;
        }

        // Accept: the invitee PUTs their own role to 30.
        using var inviteeRooms = Client("rooms", invitee);
        var accepted = await PutFormAsync(inviteeRooms, $"/roomserver/rooms/{roomId}/roles/{invitee.PlayerId}", ("role", "30"));
        var acceptedRow = RoleOf(accepted, invitee.PlayerId);
        Assert.Equal(30, acceptedRow.GetProperty("Role").GetInt32());
        Assert.Equal(0, acceptedRow.GetProperty("InvitedRole").GetInt32());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            Assert.NotNull((await db.Messages.SingleAsync(m => m.Id == messageId)).ReadAt);
            Assert.True(await db.RoomRoles.AnyAsync(r =>
                r.RoomId == roomId && r.PlayerId == invitee.PlayerId && r.Accepted && r.Role == RoomRoleRules.StoredCoOwner));
        }
    }

    [Fact]
    public async Task Invitee_rejects_through_the_invite_route_and_cannot_self_promote()
    {
        var (owner, invitee, stranger) = await PlayersAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId);

        using var ownerRooms = Client("rooms", owner);
        await PutFormAsync(ownerRooms, $"/roomserver/rooms/{roomId}/roles/{invitee.PlayerId}/invite", ("role", "30"));

        using var inviteeRooms = Client("rooms", invitee);
        var rejected = await PutFormAsync(inviteeRooms, $"/roomserver/rooms/{roomId}/roles/{invitee.PlayerId}/invite", ("role", "0"));
        Assert.DoesNotContain(rejected.GetProperty("Roles").EnumerateArray(),
            e => e.GetProperty("AccountId").GetInt64() == invitee.PlayerId);

        // With the invite gone, the same accept is refused.
        using var again = await PutFormRawAsync(inviteeRooms, $"/roomserver/rooms/{roomId}/roles/{invitee.PlayerId}", ("role", "30"));
        await AssertRefusedAsync(again);

        // A stranger can neither promote themselves nor invite anyone.
        using var strangerRooms = Client("rooms", stranger);
        using var self = await PutFormRawAsync(strangerRooms, $"/roomserver/rooms/{roomId}/roles/{stranger.PlayerId}", ("role", "20"));
        await AssertRefusedAsync(self);
        using var other = await PutFormRawAsync(strangerRooms, $"/roomserver/rooms/{roomId}/roles/{invitee.PlayerId}/invite", ("role", "30"));
        await AssertRefusedAsync(other);
    }

    [Fact]
    public async Task Co_owner_manages_roles_below_but_not_co_owners()
    {
        var (owner, coOwner, target) = await PlayersAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId, (coOwner.PlayerId, RoomRoleRules.StoredCoOwner));

        using var coRooms = Client("rooms", coOwner);
        var granted = await PutFormAsync(coRooms, $"/roomserver/rooms/{roomId}/roles/{target.PlayerId}", ("role", "20"));
        Assert.Equal(20, RoleOf(granted, target.PlayerId).GetProperty("Role").GetInt32());

        using var inviteCo = await PutFormRawAsync(coRooms, $"/roomserver/rooms/{roomId}/roles/{target.PlayerId}/invite", ("role", "30"));
        await AssertRefusedAsync(inviteCo);

        // The creator can directly add and remove a co-owner.
        using var ownerRooms = Client("rooms", owner);
        var promoted = await PutFormAsync(ownerRooms, $"/roomserver/rooms/{roomId}/roles/{target.PlayerId}", ("role", "30"));
        Assert.Equal(30, RoleOf(promoted, target.PlayerId).GetProperty("Role").GetInt32());

        // …and now the co-owner can't touch their peer.
        using var demote = await PutFormRawAsync(coRooms, $"/roomserver/rooms/{roomId}/roles/{target.PlayerId}", ("role", "0"));
        await AssertRefusedAsync(demote);

        var removed = await PutFormAsync(ownerRooms, $"/roomserver/rooms/{roomId}/roles/{target.PlayerId}", ("role", "0"));
        Assert.DoesNotContain(removed.GetProperty("Roles").EnumerateArray(),
            e => e.GetProperty("AccountId").GetInt64() == target.PlayerId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        Assert.True(await db.Messages.AnyAsync(m => m.RecipientPlayerId == target.PlayerId && m.RoomId == roomId
            && m.Type == SystemNotificationService.MessageType.RoomCoOwnerAdded));
        Assert.True(await db.Messages.AnyAsync(m => m.RecipientPlayerId == target.PlayerId && m.RoomId == roomId
            && m.Type == SystemNotificationService.MessageType.RoomCoOwnerRemoved));
    }

    [Fact]
    public async Task Creator_saves_co_owner_and_creator_permissions_and_reads_them_back()
    {
        var (owner, coOwner, _) = await PlayersAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId, (coOwner.PlayerId, RoomRoleRules.StoredCoOwner));

        using var ownerRooms = Client("rooms", owner);
        var body = """[{"Permission":"13","Role":30,"Override":true,"Type":0,"Value":"false"},{"Permission":"20","Role":255,"Override":true,"Type":0,"Value":"false"}]""";
        using (var put = await PutJsonRawAsync(ownerRooms, $"/roomserver/rooms/{roomId}/subrooms/1/permissions", body))
        {
            var text = await put.Content.ReadAsStringAsync();
            Assert.True(put.IsSuccessStatusCode, $"{(int)put.StatusCode}: {text}");
            // {success, value: full room details, error}
            var value = Envelope(text);
            Assert.Equal(roomId, value.GetProperty("RoomId").GetInt64());
        }

        // The owner enters the room, then the matrix comes back with the token.
        using var match = Client("match", owner);
        using (var join = await match.PostAsync($"/matchmake/room/{roomId}", new FormUrlEncodedContent(new Dictionary<string, string>())))
            Assert.True(join.IsSuccessStatusCode, await join.Content.ReadAsStringAsync());
        var token = await GetJsonAsync(ownerRooms, "/roomserver/photon_access_token");
        var cells = token.GetProperty("Permissions").EnumerateArray().ToList();
        Assert.Equal("false", cells.Single(c => c.GetProperty("Role").GetInt32() == 30
            && c.GetProperty("Permission").GetString() == "13").GetProperty("Value").GetString());
        Assert.Equal("false", cells.Single(c => c.GetProperty("Role").GetInt32() == 255
            && c.GetProperty("Permission").GetString() == "20").GetProperty("Value").GetString());
        Assert.Equal("true", cells.Single(c => c.GetProperty("Role").GetInt32() == 255
            && c.GetProperty("Permission").GetString() == "13").GetProperty("Value").GetString());

        // A co-owner may not edit the Co-Owner row, but may edit Moderator.
        using var coRooms = Client("rooms", coOwner);
        using (var denied = await PutJsonRawAsync(coRooms, $"/roomserver/rooms/{roomId}/subrooms/1/permissions",
                   """[{"Permission":"13","Role":30,"Override":true,"Type":0,"Value":"true"}]"""))
            await AssertRefusedAsync(denied);
        using (var allowed = await PutJsonRawAsync(coRooms, $"/roomserver/rooms/{roomId}/subrooms/1/permissions",
                   """[{"Permission":"13","Role":20,"Override":true,"Type":0,"Value":"true"}]"""))
            Envelope(await allowed.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var stored = await db.RoomRolePermissions.Where(p => p.RoomId == roomId).ToListAsync();
        Assert.Equal(3, stored.Count);
        Assert.All(stored, p => Assert.Equal(0, p.SubRoomId)); // wire 1 → OrderIndex 0
        Assert.Equal("false", stored.Single(p => p.Role == 30).Value); // the denied write changed nothing
    }

    [Fact]
    public async Task Invite_pushes_the_message_and_room_and_permission_saves_push_the_access_token()
    {
        var hub = new CapturingHubContext();
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
                    .RemoveAll<Microsoft.AspNetCore.SignalR.IHubContext<DorkNet.Server.Hubs.NotifyHub>>(services);
                services.AddSingleton<Microsoft.AspNetCore.SignalR.IHubContext<DorkNet.Server.Hubs.NotifyHub>>(hub);
            }));

        HttpClient FactoryClient(string sub, GameClientSession? session = null)
        {
            var c = factory.CreateClient(new() { AllowAutoRedirect = false });
            c.BaseAddress = new Uri($"http://{sub}.{_factory.ApexDomain}");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
            if (session is not null)
                c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            return c;
        }

        using var setup = FactoryClient("api");
        var owner = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var invitee = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var roomId = await SeedRoomAsync(owner.PlayerId);

        using var ownerRooms = FactoryClient("rooms", owner);
        await PutFormAsync(ownerRooms, $"/roomserver/rooms/{roomId}/roles/{invitee.PlayerId}/invite", ("role", "30"));

        var message = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(invitee.PlayerId), "2"));
        Assert.Equal(62, message.Msg.GetProperty("Type").GetInt32());
        Assert.Equal(roomId, message.Msg.GetProperty("RoomId").GetInt64());
        Assert.Equal((int)owner.PlayerId, message.Msg.GetProperty("FromPlayerId").GetInt32());
        var roomPush = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(invitee.PlayerId), "RoomUpdate"));
        Assert.Equal(30, RoleOf(roomPush.Msg, invitee.PlayerId).GetProperty("InvitedRole").GetInt32());

        using var match = FactoryClient("match", owner);
        using (var join = await match.PostAsync($"/matchmake/room/{roomId}", new FormUrlEncodedContent(new Dictionary<string, string>())))
            Assert.True(join.IsSuccessStatusCode, await join.Content.ReadAsStringAsync());
        using (var put = await PutJsonRawAsync(ownerRooms, $"/roomserver/rooms/{roomId}/subrooms/1/permissions",
                   """[{"Permission":"21","Role":20,"Override":true,"Type":0,"Value":"true"}]"""))
            Envelope(await put.Content.ReadAsStringAsync());

        var token = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(owner.PlayerId), "PhotonAccessToken"));
        Assert.True(token.Msg.GetProperty("RoomInstanceId").GetInt64() > 0);
        Assert.Contains(token.Msg.GetProperty("Permissions").EnumerateArray(),
            c => c.GetProperty("Role").GetInt32() == 20 && c.GetProperty("Permission").GetString() == "21");
    }

    [Fact]
    public void Envelope_filter_wraps_success_and_turns_refusals_into_messages()
    {
        static JsonElement Body(Microsoft.AspNetCore.Mvc.IActionResult r) =>
            JsonDocument.Parse(JsonSerializer.Serialize(((Microsoft.AspNetCore.Mvc.ObjectResult)r).Value)).RootElement.Clone();

        var ok = Body(Controllers.RecNetEnvelopeAttribute.Wrap(new Microsoft.AspNetCore.Mvc.OkObjectResult(new { RoomId = 5 })));
        Assert.True(ok.GetProperty("success").GetBoolean());
        Assert.Equal(5, ok.GetProperty("value").GetProperty("RoomId").GetInt32());
        Assert.Equal("", ok.GetProperty("error").GetString());

        var forbid = Body(Controllers.RecNetEnvelopeAttribute.Wrap(new Microsoft.AspNetCore.Mvc.ForbidResult()));
        Assert.False(forbid.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(forbid.GetProperty("error").GetString()));

        var bad = Body(Controllers.RecNetEnvelopeAttribute.Wrap(
            new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { error = "cannot_change_creator_role" })));
        Assert.Equal("cannot_change_creator_role", bad.GetProperty("error").GetString());

        // An envelope the handler built itself is left alone.
        var already = new Microsoft.AspNetCore.Mvc.OkObjectResult(new { success = false, value = (object?)null, error = "x" });
        Assert.Same(already, Controllers.RecNetEnvelopeAttribute.Wrap(already));
    }

    [Fact]
    public async Task Room_mutations_answer_the_success_value_error_envelope_on_bare_and_roomserver_paths()
    {
        var (owner, invitee, _) = await PlayersAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId);
        using var rooms = Client("rooms", owner);

        // The exact request the client sent in production (bare path, form role=30).
        var invited = await PutFormAsync(rooms, $"/rooms/{roomId}/roles/{invitee.PlayerId}/invite", ("role", "30"));
        Assert.Equal(30, RoleOf(invited, invitee.PlayerId).GetProperty("InvitedRole").GetInt32());

        foreach (var (path, key, value) in new[]
                 {
                     ($"/rooms/{roomId}/description", "description", "hello"),
                     ($"/roomserver/rooms/{roomId}/accessibility", "accessibility", "0"),
                     ($"/roomserver/rooms/{roomId}/subrooms/1/name", "name", "Lobby"),
                     ($"/roomserver/rooms/{roomId}/subrooms/1/maxplayers", "maxPlayers", "10"),
                 })
        {
            var details = await PutFormAsync(rooms, path, (key, value));
            Assert.Equal(roomId, details.GetProperty("RoomId").GetInt64());
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<(GameClientSession A, GameClientSession B, GameClientSession C)> PlayersAsync()
    {
        using var setup = Client("api");
        return (
            await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain),
            await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain),
            await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain));
    }

    private async Task<long> SeedRoomAsync(long creatorId, params (long PlayerId, int StoredRole)[] roles)
    {
        var roomId = 9_300_000 + Random.Shared.Next(1, 99_999);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        db.Rooms.Add(new RoomEntity
        {
            Id = roomId,
            Name = $"Roles{Guid.NewGuid():N}"[..18],
            CreatorPlayerId = creatorId,
            ImageName = RoomService.DefaultRoomImageName,
            Accessibility = 0,
            LocationReplicationId = "a75f7547-79eb-47c6-8986-6767abcb4f92",
        });
        db.RoomScenes.Add(new RoomSceneEntity { RoomId = roomId, OrderIndex = 0, Name = "Home" });
        foreach (var (pid, role) in roles)
            db.RoomRoles.Add(new RoomRoleEntity { RoomId = roomId, PlayerId = pid, Role = role, Accepted = true });
        await db.SaveChangesAsync();
        return roomId;
    }

    private static JsonElement RoleOf(JsonElement details, long playerId) =>
        details.GetProperty("Roles").EnumerateArray()
            .Single(e => e.GetProperty("AccountId").GetInt64() == playerId);

    private static Task<HttpResponseMessage> PutFormRawAsync(HttpClient client, string path, params (string Key, string Value)[] fields)
        => client.PutAsync(path, new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));

    private static async Task<JsonElement> PutFormAsync(HttpClient client, string path, params (string Key, string Value)[] fields)
    {
        using var response = await PutFormRawAsync(client, path, fields);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT {path} -> {(int)response.StatusCode}: {body}");
        return Envelope(body);
    }

    /// <summary>Every room mutation answers the {success, value, error}
    /// envelope (NLDBPDCNNCF.BDBHCEGKGDC&lt;FGCPNAACHIK&gt;): assert success and
    /// return the room details in <c>value</c>.</summary>
    private static JsonElement Envelope(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), body);
        Assert.Equal(string.Empty, root.GetProperty("error").GetString());
        return root.GetProperty("value").Clone();
    }

    /// <summary>A refusal is HTTP 200 with success=false and a message: the
    /// client shows the message, while a 403/404 surfaces only as the
    /// message-less "Exception of type 'CEMNLBKJABA' was thrown".</summary>
    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()), body);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("value").ValueKind);
    }

    private static Task<HttpResponseMessage> PutJsonRawAsync(HttpClient client, string path, string json)
        => client.PutAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private HttpClient Client(string subdomain, GameClientSession? session = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{subdomain}.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
