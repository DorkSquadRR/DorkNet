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
    public byte[] GetRroEditableBlob() => GetRroEditableBlob(string.Empty);

    /// <summary>The blob served for a baked Rec Room Original whose blob name
    /// misses S3 (nothing saved yet). <paramref name="activityId"/> is the
    /// room's <c>LocationReplicationId</c> — a real save carries it as
    /// <c>activity_id</c> (Rec Center's saved blob: activity_id ==
    /// cbad71af-…, its location id).</summary>
    public byte[] GetRroEditableBlob(string activityId) =>
        BuildRroEditableBlob(_rroEditableRoleData, activityId, "Room");

    /// <param name="roomName">Name of the room's default game role (Rec
    /// Center's is "Rec Center", Stunt Runner's "StuntRunner").</param>
    public byte[] GetRroEditableBlob(string activityId, string roomName) =>
        BuildRroEditableBlob(_rroEditableRoleData, activityId, roomName);

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
                // NOTE: read the length into a local first. `pos += ReadVarint(ref pos)`
                // adds the payload length to the PRE-length-varint position (C# evaluates
                // the left operand of += before the right), landing the skip short by
                // the size of the length varint and desyncing the whole walk.
                var length = checked((int)ReadVarint(input, ref pos));
                pos += length;
                break;
            case 5:
                pos += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
        }

        if (pos > input.Length) throw new EndOfStreamException("Unexpected end of protobuf field.");
    }

    /// <summary>Highest <c>PersistedRoomVersion</c> (field 30) the running
    /// March-2023 client (build 2023-03-21) actually accepts. NOT the same
    /// as our proto's <see cref="PersistedRoomVersion.LatestVersion"/> (19):
    /// that enum came from a runtime dump of a NEWER build, and this client
    /// rejects 19 — its real ceiling is 16 (observed on the .room blobs of
    /// rooms this client loads without complaint). Clamp to this, not the
    /// proto's Latest.</summary>
    public const int Client2023MaxPersistedRoomVersion = 16;

    /// <summary>Clamp the two top-level version varints of a
    /// <c>PersistedRoomData</c> blob to the newest values the March-2023
    /// client knows: field 1 (<c>DEPRECATED_RoomPersistenceVersion</c>,
    /// max 38) and field 30 (<c>PersistedRoomVersion</c>, max
    /// <see cref="Client2023MaxPersistedRoomVersion"/> = 16). Modern RecNet
    /// exports stamp much higher values (a Sep-2025 save carries
    /// version=131) and the client refuses the whole room with its
    /// "update Rec Room to visit this room" gate. Pure wire-format rewrite —
    /// every other byte is preserved verbatim, so blobs already at or below
    /// the 2023 versions round-trip unchanged. On malformed input the
    /// original bytes are returned. NOTE: this only addresses the room-HEADER
    /// version gate; a room whose CircuitsV2 graph (field 18) is too new is
    /// rejected regardless and this clamp cannot help it.</summary>
    public static (byte[] Bytes, bool Changed) ClampVersionsFor2023(byte[] input)
    {
        try
        {
            using var output = new MemoryStream(input.Length);
            var pos = 0;
            var changed = false;
            while (pos < input.Length)
            {
                var fieldStart = pos;
                var tag = ReadVarint(input, ref pos);
                var fieldNumber = (int)(tag >> 3);
                var wireType = (int)(tag & 0x07);
                if (wireType == 0 && fieldNumber is 1 or 30)
                {
                    var value = ReadVarint(input, ref pos);
                    var max = fieldNumber == 1
                        ? (ulong)LatestDeprecatedPersistenceVersion
                        : (ulong)Client2023MaxPersistedRoomVersion;
                    if (value > max)
                    {
                        value = max;
                        changed = true;
                    }
                    WriteVarint(output, tag);
                    WriteVarint(output, value);
                }
                else
                {
                    SkipField(input, ref pos, wireType);
                    output.Write(input, fieldStart, pos - fieldStart);
                }
            }
            return changed ? (output.ToArray(), true) : (input, false);
        }
        catch
        {
            // Not parseable as a protobuf message — serve the original
            // bytes rather than dropping the download.
            return (input, false);
        }
    }

    private static void WriteVarint(Stream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        output.WriteByte((byte)value);
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

    /// <summary>Shape of the stub matters: the March-2023 client fetches this
    /// through <c>RecNet.Rooms.GetRoomData</c> and REJECTS a blob it deems
    /// unusable (seen as the bare "Failed to copy room" on clones, and as a
    /// baked room that silently loads without any room roles / Maker Pen).
    /// Two things were wrong with the old six-field stub, measured against
    /// Rec Center's real saved blob (room_100_v1.dat, which the same client
    /// loads with Maker Pen):
    ///
    ///  • <c>version</c> was stamped <c>PersistedRoomVersion.LatestVersion</c>
    ///    (19) — past the client's ceiling of <see cref="Client2023MaxPersistedRoomVersion"/>
    ///    (16). The S3-hit path clamps that; the miss path never did.
    ///  • it lacked the skeleton every real save carries: <c>last_save_time</c>,
    ///    <c>activity_id</c> (= the room's LocationReplicationId), an empty
    ///    <c>connectable_graph_data</c>, <c>scene_settings_data</c>,
    ///    <c>object_model_data</c> and the four <c>room_mood_*</c> blocks.
    ///
    /// Our server proto only types the role/tag fields, so the rest is
    /// appended as raw wire-format fields (protobuf field order is free).
    /// The byte payloads below are Rec Center's own defaults, verbatim.</summary>
    private static byte[] BuildRroEditableBlob(RoomRoleCollectionData roleData, string activityId, string roomName)
    {
        var msg = new PersistedRoomData
        {
            DEPRECATEDVersion = LatestDeprecatedPersistenceVersion,
            Version = (PersistedRoomVersion)Client2023MaxPersistedRoomVersion,
            CreativeRolesEnabled = true,
            ToolTagSettingsData = BuildDefaultToolTagSettingsData(),
            GameRoleData = BuildDefaultGameRoleData(),
            RoomRoleData = roleData.Clone(),
        };

        using var output = new MemoryStream(4096);
        var typed = msg.ToByteArray();
        output.Write(typed, 0, typed.Length);

        // 3: last_save_time (google.protobuf.Timestamp{seconds})
        var seconds = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var ts = new MemoryStream(12))
        {
            WriteVarint(ts, 0x08);
            WriteVarint(ts, seconds);
            AppendLengthDelimited(output, 3, ts.ToArray());
        }
        // 4: activity_id
        if (!string.IsNullOrWhiteSpace(activityId))
            AppendLengthDelimited(output, 4, System.Text.Encoding.UTF8.GetBytes(activityId));

        // 2: persistence_views — the room's own GAME-ROLE objects. Every real
        // room save carries three GameRoleNode chips (default role named after
        // the room, "In-Game", "Eliminated"; Rec Center and Stunt Runner both
        // do, with the same well-known role GUIDs), and the 2023 client reads
        // Maker Pen / save / spawn rights off the player's GameRole
        // (ICreatorRole.get_CreatorRoleCanUseMakerPen on
        // RecRoom.Systems.PlayerRoles.GameRole). A blob with no role objects
        // gives a co-owner nothing to hold, so MakerPen.CheckMakerPen-
        // PermissionAndUpdateMode fails even though the room-role collection
        // says yes. View ids are derived from the activity id so repeated
        // downloads describe the same objects.
        var seed = string.IsNullOrWhiteSpace(activityId) ? "rro" : activityId;
        AppendLengthDelimited(output, 2, BuildGameRoleView(seed, roomName, DefaultGameRoleGuid, nodeId: 7,
            transform: "0a0f0db7968041152c798b3f1da05e8dc02d0000803f32050d0000803f",
            roleId: 0, roleRank: 0, isDefaultRole: true));
        AppendLengthDelimited(output, 2, BuildGameRoleView(seed, "In-Game", InGameGameRoleGuid, nodeId: 9,
            transform: "0a0f0d51308241152c798b3f1da05e8dc02d0000803f32050d0000803f",
            roleId: 2_097_152, roleRank: 1, isDefaultRole: false));
        AppendLengthDelimited(output, 2, BuildGameRoleView(seed, "Eliminated", EliminatedGameRoleGuid, nodeId: 11,
            transform: "0a0f0deac98341152c798b3f1da05e8dc02d0000803f32050d0000803f",
            roleId: 4_194_304, roleRank: 1015, isDefaultRole: false));

        // 2: the room's GAME-RULE chip, for baked originals whose rule is not
        // in the scene. Rec Royale's Frontier scene has no baked
        // *_GameRuleWrapperCircuit (the paintball scenes do); real Rec Room
        // delivered it in the blob. A saved RecRoyaleSquads chip (custom
        // Rec Royale room export) is just header + prefab id + palette tag +
        // node id + an EMPTY GameRuleWrapperData (field 41), so it can be
        // generated. Prefab ids come from the client's spawnable-tool table
        // in resources.assets (SpawnableTool_Runtime_<ENUM> → 16-byte id),
        // cross-checked against ids seen in real saves.
        var chip = GameRuleChipFor(roomName);
        if (chip is not null)
            AppendLengthDelimited(output, 2, BuildGameRuleChipView(seed, chip.Value.PrefabId, chip.Value.Tag, nodeId: 3));
        // 21: connectable_graph_data { root_node { slot_index=0-ish, field 7 = 1 } }
        AppendLengthDelimited(output, 21, new byte[] { 0x0A, 0x04, 0x0A, 0x00, 0x38, 0x01 });
        // 24: scene_settings_data { enforce_host_only_areas=true, use_new_materials=true }
        AppendLengthDelimited(output, 24, new byte[] { 0x18, 0x01, 0x30, 0x01 });
        // 29: object_model_data (opaque bytes; empty model)
        AppendLengthDelimited(output, 29, new byte[] { 0x08, 0x02, 0x1A, 0x00 });
        // 32-35: room_fog_data / room_mood_sun_data / room_mood_background_objects / room_mood_skydome
        AppendLengthDelimited(output, 32, Convert.FromHexString("0a070d00000040100110122500004844"));
        AppendLengthDelimited(output, 33, Convert.FromHexString("0a070d000000401001580162140dffffff3e15ffffff3e1dffffffbe25ffffff3e"));
        AppendLengthDelimited(output, 34, Convert.FromHexString("0a070d000000401001121208ffffffffffffffffff01100d250000803f1a1208ffffffffffffffffff01100d250000803f221208ffffffffffffffffff01100d250000803f2a1208ffffffffffffffffff01100d250000803f"));
        AppendLengthDelimited(output, 35, Convert.FromHexString("0a070d0000004010011032182920252825300d3dae47613e45713d8a3e"));

        return output.ToArray();
    }

    private static void AppendLengthDelimited(Stream output, int fieldNumber, byte[] payload)
    {
        WriteVarint(output, ((ulong)fieldNumber << 3) | 2);
        WriteVarint(output, (ulong)payload.Length);
        output.Write(payload, 0, payload.Length);
    }

    /// <summary>Game-rule wrapper chips for baked originals whose rule is not
    /// baked into the scene, keyed by (a substring of) the room name. Ids are
    /// the client's <c>SpawnableTool_Runtime_*</c> prefab ids (2023-03-21
    /// resources.assets); the Squads id also matches a real newer-client
    /// save. Tags are what the client generates: prefab name lower-cased,
    /// cut to 25 characters.</summary>
    private static (byte[] PrefabId, string Tag)? GameRuleChipFor(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName)) return null;
        var n = roomName.Replace("-", "").Replace("_", "").Replace(" ", "");
        if (n.Contains("RecRoyaleSquads", StringComparison.OrdinalIgnoreCase))
            return (Convert.FromHexString("857f750016ca77408ae9f72f9877748a"), "recroyalesquadsgameruleci");
        if (n.Contains("RecRoyaleSolos", StringComparison.OrdinalIgnoreCase))
            return (Convert.FromHexString("983db81a68927f419edb0daf1243e93e"), "recroyalesolosgamerulecir");
        return null;
    }

    /// <summary>PersistenceViewData for a GameRuleCircuitWrapperTool chip,
    /// laid out like the saved RecRoyaleSquads chip: id, empty
    /// tool_entity_data, transform, spawnable_tool_data{prefab_id},
    /// creation_object_data{is_frozen,is_grabbable}, tagged_tool_data{tag},
    /// empty tool_cleanup_data, circuit_node_data{id}, empty
    /// game_rule_wrapper_data (field 41).</summary>
    private static byte[] BuildGameRuleChipView(string seed, byte[] prefabId, string tag, int nodeId)
    {
        using var view = new MemoryStream(256);
        AppendLengthDelimited(view, 1, System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"dorknet-rro-gamerule:{seed}:{tag}")));
        AppendLengthDelimited(view, 9, Array.Empty<byte>());
        // Same spot the saved Squads chip sat at (position/scale/rotation).
        AppendLengthDelimited(view, 10, Convert.FromHexString("0a0f0d804081c21520b58b431d941d50c42d0000803f32140d7cbd5d3f150034353a1d75deffbe250034353a"));
        using (var spawnable = new MemoryStream(20))
        {
            AppendLengthDelimited(spawnable, 1, prefabId);
            AppendLengthDelimited(view, 11, spawnable.ToArray());
        }
        AppendLengthDelimited(view, 14, new byte[] { 0x18, 0x01, 0x30, 0x01 });
        using (var tagged = new MemoryStream(40))
        {
            using var tagData = new MemoryStream(32);
            AppendLengthDelimited(tagData, 1, System.Text.Encoding.UTF8.GetBytes(tag));
            AppendLengthDelimited(tagged, 1, tagData.ToArray());
            AppendLengthDelimited(view, 15, tagged.ToArray());
        }
        AppendLengthDelimited(view, 22, Array.Empty<byte>());
        using (var node = new MemoryStream(4))
        {
            AppendVarint(node, 1, (ulong)nodeId);
            AppendLengthDelimited(view, 25, node.ToArray());
        }
        AppendLengthDelimited(view, 41, Array.Empty<byte>());
        return view.ToArray();
    }

    public sealed record UploadedBlobSummary(int ViewsIn, int ViewsKept, int RoleViews, int GameRuleViews,
        int VersionIn, int VersionOut, bool CircuitsStripped, string ActivityId);

    /// <summary>Turn a real <c>PersistedRoomData</c> save (any client
    /// version) into something the March-2023 client can load as room
    /// <paramref name="activityId"/>'s blob:
    ///  • field 4 <c>activity_id</c> is rewritten to the target room's
    ///    location id (real saves carry their own room's);
    ///  • field 5 <c>output_log</c>, field 6 <c>sub_room_id</c> and every
    ///    field the 2023 schema does not know (&gt; 36) are dropped;
    ///  • with <paramref name="basicsOnly"/>, only the persistence views that
    ///    carry a game role (field 50) or a game rule (fields 40/41) are kept
    ///    and the circuit payloads (18 circuit_data, 21 connectable graph,
    ///    28 circuit_v2_data) are dropped — a newer CircuitsV2 graph is
    ///    rejected by the client's own CV2 version gate no matter what;
    ///  • the two header versions are clamped to the 2023 maxima.
    /// Every other byte is copied verbatim.</summary>
    public static (byte[] Bytes, UploadedBlobSummary Summary) PrepareUploadedRoomBlob(
        byte[] input, string activityId, bool basicsOnly)
    {
        using var output = new MemoryStream(input.Length);
        var pos = 0;
        int viewsIn = 0, viewsKept = 0, roleViews = 0, ruleViews = 0, versionIn = 0;
        var wroteActivity = false;
        while (pos < input.Length)
        {
            var fieldStart = pos;
            var tag = ReadVarint(input, ref pos);
            var fieldNumber = (int)(tag >> 3);
            var wireType = (int)(tag & 0x07);
            var payloadStart = pos;
            if (wireType == 0)
            {
                var value = ReadVarint(input, ref pos);
                if (fieldNumber == 30) versionIn = (int)value;
                if (fieldNumber is 5 or 6 || fieldNumber > 36) continue;
                output.Write(input, fieldStart, pos - fieldStart);
                continue;
            }
            SkipField(input, ref pos, wireType);
            if (wireType != 2)
            {
                if (fieldNumber > 36) continue;
                output.Write(input, fieldStart, pos - fieldStart);
                continue;
            }

            var lenPos = payloadStart;
            var len = checked((int)ReadVarint(input, ref lenPos));
            var payload = new ReadOnlySpan<byte>(input, lenPos, len);

            switch (fieldNumber)
            {
                case 4:
                    AppendLengthDelimited(output, 4, System.Text.Encoding.UTF8.GetBytes(activityId));
                    wroteActivity = true;
                    continue;
                case 5:
                case 18 when basicsOnly:
                case 21 when basicsOnly:
                case 28 when basicsOnly:
                    continue;
                case 2:
                {
                    viewsIn++;
                    var (isRole, isRule) = ClassifyView(payload);
                    if (isRole) roleViews++;
                    if (isRule) ruleViews++;
                    if (basicsOnly && !isRole && !isRule) continue;
                    viewsKept++;
                    output.Write(input, fieldStart, pos - fieldStart);
                    continue;
                }
                default:
                    if (fieldNumber > 36) continue;
                    output.Write(input, fieldStart, pos - fieldStart);
                    continue;
            }
        }
        if (!wroteActivity && !string.IsNullOrWhiteSpace(activityId))
            AppendLengthDelimited(output, 4, System.Text.Encoding.UTF8.GetBytes(activityId));

        var (clamped, _) = ClampVersionsFor2023(output.ToArray());
        return (clamped, new UploadedBlobSummary(viewsIn, viewsKept, roleViews, ruleViews, versionIn,
            Math.Min(versionIn == 0 ? Client2023MaxPersistedRoomVersion : versionIn, Client2023MaxPersistedRoomVersion),
            basicsOnly, activityId));
    }

    /// <summary>Does this PersistenceViewData carry a GameRoleNode (field 50)
    /// or a game rule (40 game_configuration_data / 41 game_rule_wrapper_data)?</summary>
    private static (bool IsRole, bool IsRule) ClassifyView(ReadOnlySpan<byte> view)
    {
        var bytes = view.ToArray();
        var pos = 0; bool role = false, rule = false;
        try
        {
            while (pos < bytes.Length)
            {
                var tag = ReadVarint(bytes, ref pos);
                var fieldNumber = (int)(tag >> 3);
                var wireType = (int)(tag & 0x07);
                if (fieldNumber == 50) role = true;
                if (fieldNumber is 40 or 41) rule = true;
                SkipField(bytes, ref pos, wireType);
            }
        }
        catch { /* malformed view — treat as neither */ }
        return (role, rule);
    }

    private static void AppendVarint(Stream output, int fieldNumber, ulong value)
    {
        WriteVarint(output, ((ulong)fieldNumber << 3) | 0);
        WriteVarint(output, value);
    }

    // Well-known game-role GUIDs, identical in every room save inspected
    // (Rec Center room_100_v1.dat, Stunt Runner room_125_v1.dat).
    private const string DefaultGameRoleGuid = "c48bac02-a743-4797-94c4-e2d6f0749004";
    private const string InGameGameRoleGuid = "2c721342-c7ed-49fe-b8f0-93f8d282a6df";
    private const string EliminatedGameRoleGuid = "0b9ec39e-2cc9-4825-8321-da11561f1c4f";

    // SpawnableToolData.prefab_id of the GameRoleNode chip prefab (16 raw
    // bytes, same in both reference saves).
    private static readonly byte[] GameRoleNodePrefabId = Convert.FromHexString("c46e153588a47147835d6cc2f69bfd6c");

    /// <summary>One <c>PersistenceViewData</c> carrying a GameRoleNode chip,
    /// laid out exactly as the client's own writer emits it: id, empty
    /// tool_entity_data, transform, spawnable_tool_data{prefab_id},
    /// creation_object_data{is_frozen, is_grabbable}, empty tool_cleanup_data,
    /// circuit_node_data{id}, game_role_node_data{PlayerGameRoleData}. The
    /// PlayerGameRoleData mirrors the reference saves field-for-field,
    /// including the empty Overridable* submessages the writer always emits
    /// (the reader expects them present).</summary>
    private static byte[] BuildGameRoleView(string seed, string roleName, string roleGuid, int nodeId,
        string transform, int roleId, int roleRank, bool isDefaultRole)
    {
        using var view = new MemoryStream(512);
        // 1: id — stable per (room, role)
        AppendLengthDelimited(view, 1, System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"dorknet-rro-role:{seed}:{roleGuid}")));
        AppendLengthDelimited(view, 9, Array.Empty<byte>());                 // tool_entity_data
        AppendLengthDelimited(view, 10, Convert.FromHexString(transform));  // transform
        using (var spawnable = new MemoryStream(20))                         // spawnable_tool_data
        {
            AppendLengthDelimited(spawnable, 1, GameRoleNodePrefabId);
            AppendLengthDelimited(view, 11, spawnable.ToArray());
        }
        AppendLengthDelimited(view, 14, new byte[] { 0x18, 0x01, 0x30, 0x01 }); // creation_object_data
        AppendLengthDelimited(view, 22, Array.Empty<byte>());                // tool_cleanup_data
        using (var node = new MemoryStream(4))                               // circuit_node_data
        {
            AppendVarint(node, 1, (ulong)nodeId);
            AppendLengthDelimited(view, 25, node.ToArray());
        }
        using (var roleNode = new MemoryStream(512))                         // game_role_node_data
        {
            AppendLengthDelimited(roleNode, 1, BuildPlayerGameRole(roleName, roleGuid, roleId, roleRank, isDefaultRole));
            AppendLengthDelimited(view, 50, roleNode.ToArray());
        }
        return view.ToArray();
    }

    private static byte[] BuildPlayerGameRole(string roleName, string roleGuid, int roleId, int roleRank, bool isDefaultRole)
    {
        // Field order and the set of always-present empty submessages copied
        // from Rec Center's save (role_version 33 = the 2023 writer).
        int[] order =
        {
            3, 5, 8, 11, 12, 13, 14, 15, 17, 18, 19, 21, 22, 23, 24, 26, 27, 30, 31, 32, 33, 34, 36, 37, 38, 39,
            40, 41, 42, 43, 44, 46, 47, 48, 49, 50, 51, 52, 62, 63, 64, 65, 66, 67, 70, 71, 72, 73, 74, 75, 76,
            77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100,
        };
        static byte[] OverrideBool(bool value) => value ? new byte[] { 0x08, 0x01, 0x10, 0x01 } : new byte[] { 0x08, 0x01 };

        using var role = new MemoryStream(320);
        if (!isDefaultRole)
        {
            AppendVarint(role, 1, (ulong)roleId);   // role_id (AG bitmask)
            AppendVarint(role, 2, (ulong)roleRank); // role_rank
        }
        foreach (var f in order)
        {
            switch (f)
            {
                case 3: AppendVarint(role, 3, 1); break;                                   // is_role_active
                case 5: AppendLengthDelimited(role, 5, System.Text.Encoding.UTF8.GetBytes(roleName)); break;
                case 17 when roleName == "Eliminated":
                    AppendLengthDelimited(role, 17, new byte[] { 0x08, 0x01, 0x10, 0x01 }); break; // item_pickup_restrictions
                case 26 when isDefaultRole:
                    AppendLengthDelimited(role, 26, new byte[] { 0x08, 0x01, 0x10, 0x14 }); break; // voice_rolloff_max_distance=20
                case 27: AppendVarint(role, 27, 33); break;                                // role_version
                case 31 when roleName == "Eliminated":
                    AppendLengthDelimited(role, 31, OverrideBool(false)); break;            // can_move
                case 49: AppendLengthDelimited(role, 49, System.Text.Encoding.UTF8.GetBytes(roleGuid)); break;
                // DorkNet policy for editable RROs: the default role may use
                // the Maker Pen regardless of room role (matches the
                // permissive room-role overlay the CDN applies).
                case 94 when isDefaultRole:
                    AppendLengthDelimited(role, 94, OverrideBool(true)); break;             // can_use_maker_pen
                default: AppendLengthDelimited(role, f, Array.Empty<byte>()); break;
            }
        }
        return role.ToArray();
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
