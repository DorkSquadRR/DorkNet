using System.IO;
using Google.Protobuf;
using RecRoom.Protobuf;

namespace DorkNet.Server.Services;

/// <summary>
/// Builds the binary `PersistedRoomData` protobuf the client downloads when
/// it loads a room scene. When a real room blob is missing, serve captured
/// root room data with saved objects stripped and a 2023-compatible role
/// collection instead of fabricating saved objects into the scene.
/// </summary>
public class RoomDataBlobService
{
    private const DEPRECATED_RoomPersistenceVersion LatestDeprecatedPersistenceVersion =
        DEPRECATED_RoomPersistenceVersion.LatestRoomPersistenceVersion;

    private const PersistedRoomVersion LatestPersistenceVersion =
        PersistedRoomVersion.LatestVersion;

    private readonly RoomRoleCollectionData _allPermsRoleData = BuildAllPermsRoleData();
    private readonly byte[] _rroEditableBlob = BuildRroEditableBlob();
    private readonly RoomRoleCollectionData _rroEditableRoleData = BuildRroEditableRoleData();

    /// <summary>The blob served when a room_&lt;id&gt;_v1.dat misses S3
    /// (unsaved dorms / fresh customisable rooms). Uses the captured
    /// <c>data/default_room.room</c> as an input, but strips top-level
    /// persistence_views before serving it. Those views can reference
    /// prefabs absent from older clients and crash spawn with
    /// "Invalid Prefab Name: \"\""; room-role data uses the 2023 migration
    /// marker so the client does not re-run the legacy role importer.</summary>
    private readonly byte[] _defaultBlob;

    public RoomDataBlobService()
    {
        _defaultBlob = LoadDefaultBlob();
    }

    public byte[] GetDefaultBlob() => _defaultBlob;
    public byte[] GetRroEditableBlob() => _rroEditableBlob;

    private static byte[] LoadDefaultBlob()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "data", "default_room.room");
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0)
                {
                    var msg = PersistedRoomData.Parser.ParseFrom(StripTopLevelField(bytes, 2));
                    Ensure2023PersistenceHeader(msg);
                    msg.RoomRoleData = BuildDefaultRoomRoleData();
                    msg.GameRoleData ??= BuildDefaultGameRoleData();
                    msg.ToolTagSettingsData ??= BuildDefaultToolTagSettingsData();
                    return msg.ToByteArray();
                }
            }
        }
        catch
        {
            // Fall through to a minimal valid blob — never let a missing/locked
            // file take down room loads entirely.
        }
        return BuildMinimalDefaultBlob();
    }

    private static byte[] StripTopLevelField(byte[] input, int fieldNumberToStrip)
    {
        using var output = new MemoryStream(input.Length);
        var pos = 0;
        while (pos < input.Length)
        {
            var fieldStart = pos;
            var tag = ReadVarint(input, ref pos);
            var fieldNumber = (int)(tag >> 3);
            var wireType = (int)(tag & 0x07);
            SkipField(input, ref pos, wireType);
            if (fieldNumber != fieldNumberToStrip)
                output.Write(input, fieldStart, pos - fieldStart);
        }
        return output.ToArray();
    }

    private static ulong ReadVarint(byte[] input, ref int pos)
    {
        ulong value = 0;
        var shift = 0;
        while (pos < input.Length)
        {
            var b = input[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("Invalid protobuf varint.");
        }
        throw new EndOfStreamException("Unexpected end of protobuf varint.");
    }

    private static void SkipField(byte[] input, ref int pos, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint(input, ref pos);
                break;
            case 1:
                pos += 8;
                break;
            case 2:
                pos += checked((int)ReadVarint(input, ref pos));
                break;
            case 5:
                pos += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
        }

        if (pos > input.Length) throw new EndOfStreamException("Unexpected end of protobuf field.");
    }

    public byte[] OverlayAllPermsRoleData(byte[] existingBlob)
    {
        var msg = PersistedRoomData.Parser.ParseFrom(existingBlob);
        Ensure2023PersistenceHeader(msg);
        msg.RoomRoleData = _allPermsRoleData.Clone();
        msg.GameRoleData ??= BuildDefaultGameRoleData();
        msg.ToolTagSettingsData ??= BuildDefaultToolTagSettingsData();
        return msg.ToByteArray();
    }

    public byte[] OverlayRroEditableRoleData(byte[] existingBlob)
    {
        var msg = PersistedRoomData.Parser.ParseFrom(existingBlob);
        Ensure2023PersistenceHeader(msg);
        msg.RoomRoleData = _rroEditableRoleData.Clone();
        msg.GameRoleData ??= BuildDefaultGameRoleData();
        msg.ToolTagSettingsData ??= BuildDefaultToolTagSettingsData();
        return msg.ToByteArray();
    }

    private static byte[] BuildAllPermsBlob()
    {
        var msg = new PersistedRoomData
        {
            DEPRECATEDVersion = LatestDeprecatedPersistenceVersion,
            Version = LatestPersistenceVersion,
            CreativeRolesEnabled = true,
            ToolTagSettingsData = BuildDefaultToolTagSettingsData(),
            GameRoleData = BuildDefaultGameRoleData(),
            RoomRoleData = BuildAllPermsRoleData(),
        };

        return msg.ToByteArray();
    }

    private static byte[] BuildMinimalDefaultBlob()
    {
        var msg = new PersistedRoomData
        {
            DEPRECATEDVersion = LatestDeprecatedPersistenceVersion,
            Version = LatestPersistenceVersion,
            CreativeRolesEnabled = true,
            ToolTagSettingsData = BuildDefaultToolTagSettingsData(),
            GameRoleData = BuildDefaultGameRoleData(),
            RoomRoleData = BuildDefaultRoomRoleData(),
        };

        return msg.ToByteArray();
    }

    private static RoomRoleCollectionData BuildAllPermsRoleData()
    {
        var collection = new RoomRoleCollectionData
        {
            RecNetMigrationVersion = MigratedToRecNetVersion.MigratedToRecNet,
        };
        collection.RoomRoles.Add(BuildPermissiveRole(0, 100, "Creator"));

        return collection;
    }

    private static RoomRoleCollectionData BuildDefaultRoomRoleData()
    {
        return BuildRroEditableRoleData();
    }

    private static byte[] BuildRroEditableBlob()
    {
        var msg = new PersistedRoomData
        {
            DEPRECATEDVersion = LatestDeprecatedPersistenceVersion,
            Version = LatestPersistenceVersion,
            CreativeRolesEnabled = true,
            ToolTagSettingsData = BuildDefaultToolTagSettingsData(),
            GameRoleData = BuildDefaultGameRoleData(),
            RoomRoleData = BuildRroEditableRoleData(),
        };

        return msg.ToByteArray();
    }

    private static RoomRoleCollectionData BuildRroEditableRoleData()
    {
        var collection = new RoomRoleCollectionData
        {
            RecNetMigrationVersion = MigratedToRecNetVersion.MigratedToRecNet,
        };

        // The 2023 room-permissions runtime looks up roles by RecNet account-role
        // values during load. Keep the AG role bitmasks below, but also provide
        // compatibility aliases for the keys OMJKHJLFOCO.ONECLLALJEO can return.
        collection.RoomRoles.Add(BuildPermissiveRole(0, 10, "Default"));
        collection.RoomRoles.Add(BuildPermissiveRole(10, 60, "Player"));
        collection.RoomRoles.Add(BuildPermissiveRole(20, 80, "Host"));
        collection.RoomRoles.Add(BuildPermissiveRole(30, 90, "Co-owner"));
        collection.RoomRoles.Add(BuildPermissiveRole(255, 100, "Creator"));

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
            RoleVersion = 20,
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
            DEPRECATEDCanEditCircuits = OverrideTrue(),
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
            CanSpawnInventions = OverrideTrue(),
            CanSpawnConsumables = OverrideTrue(),
            CanUseRoomResetButton = OverrideTrue(),
            CanUsePlayGizmosToggle = OverrideTrue(),

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

    private static GameRoleCollectionData BuildDefaultGameRoleData()
    {
        return new GameRoleCollectionData();
    }

    private static ToolTagSettingsData BuildDefaultToolTagSettingsData()
    {
        return new ToolTagSettingsData
        {
            RuntimeTagData = new TagData(),
        };
    }

    private static void Ensure2023PersistenceHeader(PersistedRoomData msg)
    {
        if ((int)msg.DEPRECATEDVersion < 20)
        {
            msg.DEPRECATEDVersion = LatestDeprecatedPersistenceVersion;
        }

        msg.Version = LatestPersistenceVersion;
        msg.CreativeRolesEnabled = true;
        msg.ToolTagSettingsData ??= BuildDefaultToolTagSettingsData();
    }

    private static string StableRoleGuid(int roleId) =>
        roleId switch
        {
            2_097_152 => "D8B12451-23C7-4B1D-B573-8C3717A47915",
            4_194_304 => "88EAECE9-D885-4568-BC96-AF316AD56663",
            8_388_608 => "BD0F2F3A-F931-419E-B50C-ECDFF3F56B52",
            16_777_216 => "32300035-3BEA-457E-95C0-1630AFDFA6BD",
            0 => Guid.Empty.ToString(),
            10 => "3C66F53A-6B76-4DB1-A93F-76F1F59E03B8",
            20 => "1E8890ED-D729-446B-835B-3C96D8C7D939",
            30 => "FA1825F9-8F41-54E8-8DB4-530C6B24B3E5",
            255 => "6F31F2B8-AC6E-549F-8F8C-F8344C9BAE1E",
            _ => GuidUtility.Create(GuidUtility.UrlNamespace, $"dorknet-room-role:{roleId}").ToString(),
        };
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
