using Google.Protobuf;
using RecRoom.Protobuf;

namespace DorkNet.Server.Services;

/// <summary>
/// Builds the binary `PersistedRoomData` protobuf the client downloads when
/// it loads a room scene. The 2020 RecRoom client gates the Maker Pen,
/// invention spawning, room-role editing and a dozen other tools behind
/// `RoomRoleCollectionData.RoomRoles[i].CanUseMakerPen.InnerValue` etc.,
/// all of which default to `false` if the bytes we serve are empty.
///
/// We emit a minimal blob with one PlayerRoomRoleData entry that flips
/// every relevant `OverridableBoolData` to `(overrides=true, inner_value=true)`,
/// which the client picks up regardless of which RoomRoleId the local
/// player ends up assigned to. Net result: the room's creator (and
/// everyone else, since this is a private server) gets all build/admin
/// tools the moment they spawn into the scene.
///
/// Wire compatibility: our trimmed .proto only defines the messages and
/// field numbers we set — the 2020 client's full PersistedRoomData has
/// many more fields, but proto3 silently ignores unknown wire entries and
/// defaults missing ones, so the bytes we generate deserialize cleanly.
/// </summary>
public class RoomDataBlobService
{
    /// <summary>
    /// Cached bytes of the "all-permissions-on" blob. Identical for every
    /// room — there's no per-room state we vary yet, so building once at
    /// startup beats rebuilding on every download request.
    /// </summary>
    private readonly byte[] _allPermsBlob = BuildAllPermsBlob();
    private readonly RoomRoleCollectionData _allPermsRoleData = BuildAllPermsRoleData();
    private readonly byte[] _rroEditableBlob = BuildRroEditableBlob();
    private readonly RoomRoleCollectionData _rroEditableRoleData = BuildRroEditableRoleData();

    public byte[] GetDefaultBlob() => _allPermsBlob;
    public byte[] GetRroEditableBlob() => _rroEditableBlob;

    public byte[] OverlayAllPermsRoleData(byte[] existingBlob)
    {
        var msg = PersistedRoomData.Parser.ParseFrom(existingBlob);
        msg.RoomRoleData = _allPermsRoleData.Clone();
        return msg.ToByteArray();
    }

    public byte[] OverlayRroEditableRoleData(byte[] existingBlob)
    {
        var msg = PersistedRoomData.Parser.ParseFrom(existingBlob);
        msg.RoomRoleData = _rroEditableRoleData.Clone();
        return msg.ToByteArray();
    }

    private static byte[] BuildAllPermsBlob()
    {
        var msg = new PersistedRoomData
        {
            RoomRoleData = BuildAllPermsRoleData(),
        };

        return msg.ToByteArray();
    }

    private static RoomRoleCollectionData BuildAllPermsRoleData()
    {
        var collection = new RoomRoleCollectionData();
        collection.RoomRoles.Add(BuildPermissiveRole(0, 100, "Creator"));

        return collection;
    }

    private static byte[] BuildRroEditableBlob()
    {
        var msg = new PersistedRoomData
        {
            RoomRoleData = BuildRroEditableRoleData(),
        };

        return msg.ToByteArray();
    }

    private static RoomRoleCollectionData BuildRroEditableRoleData()
    {
        var collection = new RoomRoleCollectionData();
        collection.RoomRoles.Add(BuildPermissiveRole(2_097_152, 100, "Creator"));   // AG_CREATOR
        collection.RoomRoles.Add(BuildPermissiveRole(4_194_304, 90, "Co-owner"));   // AG_COOWNER
        collection.RoomRoles.Add(BuildPermissiveRole(8_388_608, 80, "Host"));       // AG_HOST
        collection.RoomRoles.Add(BuildPermissiveRole(16_777_216, 70, "Moderator")); // AG_MODERATOR

        return collection;
    }

    private static PlayerRoomRoleData BuildPermissiveRole(int roleId, int rank, string name)
    {
        // OverridableBoolData(overrides=true, inner_value=true) — used for
        // every Can* field on the role.
        OverridableBoolData OverrideTrue() => new()
        {
            Overrides = true,
            InnerValue = true,
        };

        return new PlayerRoomRoleData
        {
            // Identity fields — these are deprecated in the 2026 schema
            // but the 2020 client still reads them.
            DEPRECATEDRoleId = roleId,
            DEPRECATEDRoleRank = rank,
            DEPRECATEDIsRoleActive = true,
            DEPRECATEDIsAgRole = true,
            RoleName = name,
            RoleVersion = 1,
            RoleGuid = StableRoleGuid(roleId),

            // Display name override — keeps the watch's role-list UI from
            // showing a blank label.
            Name = new OverridableStringData
            {
                Overrides = true,
                InnerValue = name,
            },

            // Permission grants. Each Overridable*Data(overrides=true,
            // inner_value=true) tells the client "the room has explicitly
            // configured this permission to true". Without overrides=true
            // the client falls back to its own defaults, which for most
            // perms is false.
            CanAssignRoles = OverrideTrue(),
            CanInvite = OverrideTrue(),
            CanStartGames = OverrideTrue(),
            CanTalk = OverrideTrue(),
            CanPrintPhotos = OverrideTrue(),
            CanEditRoomRoles = OverrideTrue(),
            CanSelfRevive = OverrideTrue(),
            CanEndGamesEarly = OverrideTrue(),
            CanChangeGameMode = OverrideTrue(),
            CanUseMakerPen = OverrideTrue(),
            CanUseDeleteAllButton = OverrideTrue(),
            CanSaveInventions = OverrideTrue(),
            DisableMicAutoMute = OverrideTrue(),
            CanUseShareCam = OverrideTrue(),

            // Vote-kick permission — int field, set to a permissive value.
            // 0 = AnyoneCanVoteKickAnyone in the 2020 enum (verified via
            // VoteKickPermission disasm). overrides=true to make sure the
            // client respects it rather than falling back to a stricter
            // default.
            VoteKickPermission = new OverridableIntData
            {
                Overrides = true,
                InnerValue = 0,
            },
        };
    }

    private static string StableRoleGuid(int roleId) =>
        GuidUtility.Create(GuidUtility.UrlNamespace, $"dorknet-room-role:{roleId}").ToString();
}

internal static class GuidUtility
{
    public static readonly Guid UrlNamespace = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(data);
        var newGuid = new byte[16];
        Array.Copy(hash, 0, newGuid, 0, 16);

        newGuid[6] = (byte)((newGuid[6] & 0x0F) | 0x50);
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

        SwapByteOrder(newGuid);
        return new Guid(newGuid);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
