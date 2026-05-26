using Google.Protobuf;
using RecRoom.Protobuf2020;

namespace DorkNet.Server.Services;

/// <summary>
/// Round-trips an uploaded <c>.room</c> protobuf blob through the
/// 2020.03.06 client's full <c>PersistedRoomData</c> schema (generated
/// at build time from <c>Protos/recroom_2020.proto</c>, which
/// <c>tools/gen-proto-from-decompiled.py</c> produces by scanning
/// <c>Cpp2IL_CS/.../RecRoom/Protobuf/*.cs</c>).
///
/// Why: newer Rec Room clients sometimes serialize PersistedRoomData
/// with redundant default-zero fields, packed/unpacked variants the
/// 2020 reader doesn't tolerate, or other non-canonical encodings.
/// The 2020 watch's stricter <c>Google.Protobuf.MessageParser</c>
/// then throws "Error attempting to parse room data stream" on
/// download. Re-serialising via the 2020 schema strips redundant
/// encodings and emits canonical proto3 wire format that the watch
/// accepts.
///
/// On parse failure we hand the original bytes back unchanged — better
/// to upload a blob that MAY work than to drop the user's content.
/// Callers (the admin import endpoint) log the outcome.
/// </summary>
public sealed class RoomBlobNormalizerService(ILogger<RoomBlobNormalizerService> logger)
{
    public sealed record Result(byte[] Bytes, bool Normalized, int InputBytes, int OutputBytes, string? Error);

    public Result Normalize(byte[] input)
    {
        if (input is null || input.Length == 0)
            return new Result(Array.Empty<byte>(), false, 0, 0, "empty_input");

        try
        {
            // Parse with the FULL 2020 schema. proto3 silently keeps
            // unknown fields, so even if the input has fields the
            // generated 2020 schema doesn't know about, they survive
            // the round-trip.
            var msg = PersistedRoomData.Parser.ParseFrom(input);
            var output = msg.ToByteArray();
            logger.LogInformation(
                "[normalize] parse OK, in={In:N0} bytes, out={Out:N0} bytes ({Pct:F2}%)",
                input.Length, output.Length, (double)output.Length / input.Length * 100);
            return new Result(output, true, input.Length, output.Length, null);
        }
        catch (InvalidProtocolBufferException ex)
        {
            logger.LogWarning(
                "[normalize] parse FAIL ({Bytes:N0} bytes): {Message} — passing original bytes through",
                input.Length, ex.Message);
            return new Result(input, false, input.Length, input.Length, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[normalize] unexpected exception ({Bytes:N0} bytes) — passing original bytes through",
                input.Length);
            return new Result(input, false, input.Length, input.Length, ex.Message);
        }
    }
}
