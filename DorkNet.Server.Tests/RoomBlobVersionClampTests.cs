using DorkNet.Server.Services;
using Google.Protobuf;
using RecRoom.Protobuf;

namespace DorkNet.Server.Tests;

/// <summary>
/// A modern RecNet export's subroom save stamps PersistedRoomData
/// versions past what the March-2023 client knows (field 1
/// DEPRECATED_RoomPersistenceVersion, max 38; field 30
/// PersistedRoomVersion, max 19 = V19February23BetaRelease). The client
/// rejects the whole room with its "update Rec Room to visit this room"
/// gate — the ShibuyaCrossing import (Sep-2025 save, version=131) is the
/// canonical repro. The CDN serve path clamps both varints down; these
/// tests pin the rewrite to exactly those two fields.
/// </summary>
public sealed class RoomBlobVersionClampTests
{
    private static byte[] BuildModernBlob(int deprecatedVersion, int version)
    {
        // Emit the wire shape a modern save actually has: version fields
        // plus payload fields the 2023 schema does and doesn't know about.
        using var ms = new MemoryStream();
        var output = new CodedOutputStream(ms);
        output.WriteTag(1, WireFormat.WireType.Varint);
        output.WriteInt32(deprecatedVersion);
        // Field 2: persistence_views payload (opaque here — the clamp
        // must copy it verbatim).
        output.WriteTag(2, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(0x0A, 0x03, 0x01, 0x02, 0x03));
        // Field 6: sub_room_id.
        output.WriteTag(6, WireFormat.WireType.Varint);
        output.WriteInt64(19589779);
        output.WriteTag(30, WireFormat.WireType.Varint);
        output.WriteInt32(version);
        // Field 37: unknown-to-2023 varint field from newer schemas.
        output.WriteTag(37, WireFormat.WireType.Varint);
        output.WriteInt32(1);
        output.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Clamps_modern_versions_to_2023_maxima()
    {
        var input = BuildModernBlob(deprecatedVersion: 38, version: 131);

        var (bytes, changed) = RoomDataBlobService.ClampVersionsFor2023(input);

        Assert.True(changed);
        var msg = PersistedRoomData.Parser.ParseFrom(bytes);
        Assert.Equal(PersistedRoomVersion.LatestVersion, msg.Version);
        Assert.Equal(DEPRECATED_RoomPersistenceVersion.LatestRoomPersistenceVersion, msg.DEPRECATEDVersion);
    }

    [Fact]
    public void Preserves_every_non_version_byte()
    {
        var input = BuildModernBlob(deprecatedVersion: 38, version: 131);

        var (bytes, _) = RoomDataBlobService.ClampVersionsFor2023(input);

        // version=131 is a two-byte varint, 19 is one byte — the output
        // shrinks by exactly that and matches the input everywhere else.
        Assert.Equal(input.Length - 1, bytes.Length);
        var expected = BuildModernBlob(deprecatedVersion: 38, version: 19);
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Blob_already_at_2023_versions_passes_through_unchanged()
    {
        var input = BuildModernBlob(deprecatedVersion: 38, version: 19);

        var (bytes, changed) = RoomDataBlobService.ClampVersionsFor2023(input);

        Assert.False(changed);
        Assert.Same(input, bytes);
    }

    [Fact]
    public void Malformed_input_is_returned_verbatim()
    {
        var input = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };

        var (bytes, changed) = RoomDataBlobService.ClampVersionsFor2023(input);

        Assert.False(changed);
        Assert.Same(input, bytes);
    }
}
