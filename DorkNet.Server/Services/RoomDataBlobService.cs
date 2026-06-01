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

    public byte[] GetDefaultBlob() => _allPermsBlob;

    public byte[] OverlayAllPermsRoleData(byte[] existingBlob)
    {
        var msg = PersistedRoomData.Parser.ParseFrom(existingBlob);
        msg.RoomRoleData = _allPermsRoleData.Clone();
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
        // OverridableBoolData(overrides=true, inner_value=true) — used for
        // every Can* field on the role.
        OverridableBoolData OverrideTrue() => new()
        {
            Overrides = true,
            InnerValue = true,
        };

        var role = new PlayerRoomRoleData
        {
            // Identity fields — these are deprecated in the 2026 schema
            // but the 2020 client still reads them. role_id=0 = AG_EVERYONE
            // which the client treats as "applies to all players in an AG
            // room"; combined with all permissions overridden true, every
            // player slot inherits maker pen.
            DEPRECATEDRoleId = 0,
            DEPRECATEDRoleRank = 100,
            DEPRECATEDIsRoleActive = true,
            DEPRECATEDIsAgRole = true,
            RoleName = "Creator",
            RoleVersion = 1,
            RoleGuid = Guid.NewGuid().ToString(),

            // Display name override — keeps the watch's role-list UI from
            // showing a blank label.
            Name = new OverridableStringData
            {
                Overrides = true,
                InnerValue = "Creator",
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

        var collection = new RoomRoleCollectionData();
        collection.RoomRoles.Add(role);

        return collection;
    }
}
