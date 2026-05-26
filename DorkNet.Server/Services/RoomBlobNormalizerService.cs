using Google.Protobuf;
using RecRoom.Protobuf2020;

namespace DorkNet.Server.Services;

/// <summary>
/// Normalises an uploaded <c>.room</c> protobuf blob so the 2020.12
/// watch can actually render it.
///
/// Two passes:
///
///   1. <b>Quaternion → Euler projection</b>. Modern Rec Room (2024+)
///      writes every <c>TransformData</c>'s rotation as a quaternion
///      at field 6 (<c>quaternion_rotation</c>, a
///      <c>core.QuaternionData</c> sub-message), leaving the legacy
///      Euler Vector3 at field 2 empty. The 2020.12 watch reads only
///      field 2 — so every shape, gizmo, and child view loads at
///      <c>rotation=(0,0,0)</c>. The result is a room whose pieces
///      are at the right positions but pointing the wrong way: trains
///      floating off tracks, walls turned 90° wrong, panels facing
///      the floor, etc. (Verified empirically against several Studio-
///      imported scenes vs. the working Rockulator.)
///
///      We walk <c>PersistedRoomData.persistence_views</c>, recurse
///      into each <c>PersistenceViewData.transform</c> and
///      <c>child_views[].data.transform</c>, and when we find a
///      <c>quaternion_rotation</c> field with no
///      <c>rotation</c> Euler set, convert the quaternion to Euler
///      degrees (Unity convention, Tait-Bryan ZXY) and emit it as
///      field 2.
///
///   2. <b>Round-trip through the 2020 schema</b>. After the
///      projection pass, parse the bytes with
///      <c>RecRoom.Protobuf2020.PersistedRoomData.Parser</c> and
///      re-serialise. Strips non-canonical encodings that the
///      stricter 2020 parser sometimes rejected and ensures the
///      output is exactly what the watch expects on the wire.
///
/// On parse failure we hand the original bytes back unchanged —
/// better to upload a blob that MAY work than to drop the user's
/// content. Callers (the admin import endpoint) log the outcome.
/// </summary>
public sealed class RoomBlobNormalizerService(ILogger<RoomBlobNormalizerService> logger)
{
    public sealed record Result(byte[] Bytes, bool Normalized, int InputBytes, int OutputBytes, string? Error, int QuaternionsProjected);

    public Result Normalize(byte[] input)
    {
        if (input is null || input.Length == 0)
            return new Result(Array.Empty<byte>(), false, 0, 0, "empty_input", 0);

        try
        {
            // Pass 1: project quaternion rotations to Euler so the
            // 2020.12 watch's TransformData.rotation deserialiser
            // sees something at field 2.
            var (projected, quatsProjected) = ProjectQuaternionsToEulerOnTransforms(input);

            // Pass 2: canonical re-encode via the 2020 schema.
            var msg = PersistedRoomData.Parser.ParseFrom(projected);
            var output = msg.ToByteArray();
            logger.LogInformation(
                "[normalize] OK quats_projected={Quats} in={In:N0} bytes, after_projection={Mid:N0} bytes, out={Out:N0} bytes ({Pct:F2}%)",
                quatsProjected, input.Length, projected.Length, output.Length, (double)output.Length / input.Length * 100);
            return new Result(output, true, input.Length, output.Length, null, quatsProjected);
        }
        catch (InvalidProtocolBufferException ex)
        {
            logger.LogWarning(
                "[normalize] parse FAIL ({Bytes:N0} bytes): {Message} — passing original bytes through",
                input.Length, ex.Message);
            return new Result(input, false, input.Length, input.Length, ex.Message, 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[normalize] unexpected exception ({Bytes:N0} bytes) — passing original bytes through",
                input.Length);
            return new Result(input, false, input.Length, input.Length, ex.Message, 0);
        }
    }

    // ── Wire-format rewriter ──────────────────────────────────────────
    //
    // Doing this through google.protobuf would require either compiling
    // the modern proto schema as a parallel C# namespace OR using
    // reflection + UnknownFieldSet hacks. Neither is appealing —
    // straight wire-format manipulation is ~100 lines and surgical.
    //
    // Protobuf wire format primer:
    //   tag = (field_number << 3) | wire_type, encoded as varint.
    //   wire_type 0 = varint, 1 = fixed64, 2 = length-delimited,
    //              5 = fixed32.
    // Length-delimited values are prefixed with a varint length. To
    // edit children we have to recompute the length after editing,
    // so we accumulate children into a per-level MemoryStream and
    // emit `tag + length + payload` once we know the final byte count.

    private const int FIELD_PERSISTED_ROOM_DATA_PERSISTENCE_VIEWS = 2;
    private const int FIELD_PERSISTENCE_VIEW_DATA_CHILD_VIEWS = 3;
    private const int FIELD_PERSISTENCE_VIEW_DATA_TRANSFORM = 10;
    private const int FIELD_CHILD_PERSISTENCE_VIEW_DATA_DATA = 2;
    private const int FIELD_TRANSFORM_ROTATION_EULER = 2;
    private const int FIELD_TRANSFORM_QUATERNION_ROTATION = 6;
    private const int FIELD_QUATERNION_W = 1;
    private const int FIELD_QUATERNION_X = 2;
    private const int FIELD_QUATERNION_Y = 3;
    private const int FIELD_QUATERNION_Z = 4;

    private static (byte[] Bytes, int QuatsProjected) ProjectQuaternionsToEulerOnTransforms(byte[] input)
    {
        using var ms = new MemoryStream();
        int quatsProjected = 0;
        RewriteRoot(input, 0, input.Length, ms, ref quatsProjected);
        return (ms.ToArray(), quatsProjected);
    }

    private static void RewriteRoot(byte[] buf, int pos, int end, Stream output, ref int quatsProjected)
    {
        while (pos < end)
        {
            ulong tag = ReadVarint(buf, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x07);

            if (wire == 2 && field == FIELD_PERSISTED_ROOM_DATA_PERSISTENCE_VIEWS)
            {
                int len = (int)ReadVarint(buf, ref pos);
                using var childMs = new MemoryStream();
                RewritePersistenceView(buf, pos, pos + len, childMs, ref quatsProjected);
                EmitLengthDelimited(output, field, childMs.ToArray());
                pos += len;
            }
            else
            {
                CopyField(buf, ref pos, wire, output, tag);
            }
        }
    }

    private static void RewritePersistenceView(byte[] buf, int pos, int end, Stream output, ref int quatsProjected)
    {
        while (pos < end)
        {
            ulong tag = ReadVarint(buf, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x07);

            if (wire == 2 && field == FIELD_PERSISTENCE_VIEW_DATA_TRANSFORM)
            {
                int len = (int)ReadVarint(buf, ref pos);
                using var childMs = new MemoryStream();
                RewriteTransform(buf, pos, pos + len, childMs, ref quatsProjected);
                EmitLengthDelimited(output, field, childMs.ToArray());
                pos += len;
            }
            else if (wire == 2 && field == FIELD_PERSISTENCE_VIEW_DATA_CHILD_VIEWS)
            {
                int len = (int)ReadVarint(buf, ref pos);
                using var childMs = new MemoryStream();
                RewriteChildPersistenceView(buf, pos, pos + len, childMs, ref quatsProjected);
                EmitLengthDelimited(output, field, childMs.ToArray());
                pos += len;
            }
            else
            {
                CopyField(buf, ref pos, wire, output, tag);
            }
        }
    }

    private static void RewriteChildPersistenceView(byte[] buf, int pos, int end, Stream output, ref int quatsProjected)
    {
        // ChildPersistenceViewData has field 1 (child_id, varint) and
        // field 2 (data, a nested PersistenceViewData).
        while (pos < end)
        {
            ulong tag = ReadVarint(buf, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x07);

            if (wire == 2 && field == FIELD_CHILD_PERSISTENCE_VIEW_DATA_DATA)
            {
                int len = (int)ReadVarint(buf, ref pos);
                using var childMs = new MemoryStream();
                RewritePersistenceView(buf, pos, pos + len, childMs, ref quatsProjected);
                EmitLengthDelimited(output, field, childMs.ToArray());
                pos += len;
            }
            else
            {
                CopyField(buf, ref pos, wire, output, tag);
            }
        }
    }

    private static void RewriteTransform(byte[] buf, int pos, int end, Stream output, ref int quatsProjected)
    {
        // Pass 1 over the TransformData: detect whether field 2 (Euler)
        // is set and whether field 6 (quaternion) is set. If we have
        // a quaternion but no Euler, we'll synthesise an Euler at the
        // end and skip the quaternion field on the second pass.
        bool hasEuler = false;
        bool hasQuat = false;
        float qw = 1, qx = 0, qy = 0, qz = 0;

        int scanPos = pos;
        while (scanPos < end)
        {
            ulong tag = ReadVarint(buf, ref scanPos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x07);
            if (wire == 2)
            {
                int len = (int)ReadVarint(buf, ref scanPos);
                if (field == FIELD_TRANSFORM_ROTATION_EULER) hasEuler = true;
                else if (field == FIELD_TRANSFORM_QUATERNION_ROTATION)
                {
                    hasQuat = true;
                    ReadQuaternion(buf, scanPos, scanPos + len, out qw, out qx, out qy, out qz);
                }
                scanPos += len;
            }
            else
            {
                SkipField(buf, ref scanPos, wire);
            }
        }

        bool shouldProject = hasQuat && !hasEuler;

        // Pass 2: re-emit the TransformData. Copy every field through
        // unchanged EXCEPT the quaternion (field 6) which we drop when
        // we're going to synthesise an Euler from it.
        int rewritePos = pos;
        while (rewritePos < end)
        {
            int fieldStart = rewritePos;
            ulong tag = ReadVarint(buf, ref rewritePos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x07);

            if (shouldProject && field == FIELD_TRANSFORM_QUATERNION_ROTATION)
            {
                // Skip — we're replacing it.
                SkipField(buf, ref rewritePos, wire);
                continue;
            }
            // Re-emit field unchanged (tag + value bytes).
            rewritePos = fieldStart;
            CopyFieldWithTag(buf, ref rewritePos, output);
        }

        if (shouldProject)
        {
            var (ex, ey, ez) = QuaternionToUnityEulerDegrees(qx, qy, qz, qw);
            using var eulerMs = new MemoryStream();
            // Vector3Data: float x = 1, y = 2, z = 3 (all fixed32 wire type 5).
            WriteFloat(eulerMs, 1, ex);
            WriteFloat(eulerMs, 2, ey);
            WriteFloat(eulerMs, 3, ez);
            EmitLengthDelimited(output, FIELD_TRANSFORM_ROTATION_EULER, eulerMs.ToArray());
            quatsProjected++;
        }
    }

    private static void ReadQuaternion(byte[] buf, int pos, int end, out float w, out float x, out float y, out float z)
    {
        w = 1; x = 0; y = 0; z = 0;
        while (pos < end)
        {
            ulong tag = ReadVarint(buf, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x07);
            if (wire == 5) // fixed32 = float
            {
                float v = BitConverter.ToSingle(buf, pos);
                pos += 4;
                switch (field)
                {
                    case FIELD_QUATERNION_W: w = v; break;
                    case FIELD_QUATERNION_X: x = v; break;
                    case FIELD_QUATERNION_Y: y = v; break;
                    case FIELD_QUATERNION_Z: z = v; break;
                }
            }
            else
            {
                SkipField(buf, ref pos, wire);
            }
        }
    }

    // Unity uses the ZXY Tait-Bryan convention for Quaternion.eulerAngles.
    // Formula adapted from Unity's source (Quaternion → euler in
    // degrees), with a clamped asin to guard against floating-point
    // values just outside [-1, 1] from non-unit quaternions.
    private static (float x, float y, float z) QuaternionToUnityEulerDegrees(float qx, float qy, float qz, float qw)
    {
        const double rad2deg = 180.0 / Math.PI;

        double sinrCosp = 2.0 * (qw * qx + qy * qz);
        double cosrCosp = 1.0 - 2.0 * (qx * qx + qy * qy);
        double xRad = Math.Atan2(sinrCosp, cosrCosp);

        double sinp = 2.0 * (qw * qy - qz * qx);
        if (sinp > 1.0) sinp = 1.0; else if (sinp < -1.0) sinp = -1.0;
        double yRad = Math.Asin(sinp);

        double sinyCosp = 2.0 * (qw * qz + qx * qy);
        double cosyCosp = 1.0 - 2.0 * (qy * qy + qz * qz);
        double zRad = Math.Atan2(sinyCosp, cosyCosp);

        return ((float)(xRad * rad2deg), (float)(yRad * rad2deg), (float)(zRad * rad2deg));
    }

    // ── Varint + field helpers ────────────────────────────────────────

    private static ulong ReadVarint(byte[] buf, ref int pos)
    {
        ulong val = 0;
        int shift = 0;
        while (true)
        {
            byte b = buf[pos++];
            val |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return val;
            shift += 7;
            if (shift >= 64) throw new InvalidDataException("varint too long");
        }
    }

    private static void WriteVarint(Stream s, ulong v)
    {
        while (v >= 0x80)
        {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        s.WriteByte((byte)v);
    }

    private static void EmitLengthDelimited(Stream s, int field, byte[] payload)
    {
        ulong tag = ((ulong)field << 3) | 2;
        WriteVarint(s, tag);
        WriteVarint(s, (ulong)payload.Length);
        s.Write(payload, 0, payload.Length);
    }

    private static void WriteFloat(Stream s, int field, float value)
    {
        ulong tag = ((ulong)field << 3) | 5;
        WriteVarint(s, tag);
        var bytes = BitConverter.GetBytes(value);
        s.Write(bytes, 0, 4);
    }

    private static void SkipField(byte[] buf, ref int pos, int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(buf, ref pos); break;
            case 1: pos += 8; break;
            case 2: { int n = (int)ReadVarint(buf, ref pos); pos += n; break; }
            case 5: pos += 4; break;
            default: throw new InvalidDataException($"unsupported wire type {wire}");
        }
    }

    /// <summary>Copy a single field's value (post-tag) verbatim from
    /// <paramref name="buf"/> to <paramref name="output"/>, advancing
    /// <paramref name="pos"/> past the value. <paramref name="tag"/>
    /// is the already-decoded wire tag the caller already consumed
    /// from the input — we re-emit it to <paramref name="output"/>
    /// before the value bytes.</summary>
    private static void CopyField(byte[] buf, ref int pos, int wire, Stream output, ulong tag)
    {
        WriteVarint(output, tag);
        switch (wire)
        {
            case 0: // varint
                {
                    int start = pos;
                    ReadVarint(buf, ref pos);
                    output.Write(buf, start, pos - start);
                    break;
                }
            case 1: // fixed64
                output.Write(buf, pos, 8); pos += 8; break;
            case 2: // length-delimited
                {
                    int lengthStart = pos;
                    int len = (int)ReadVarint(buf, ref pos);
                    output.Write(buf, lengthStart, pos - lengthStart); // length varint
                    output.Write(buf, pos, len);
                    pos += len;
                    break;
                }
            case 5: // fixed32
                output.Write(buf, pos, 4); pos += 4; break;
            default:
                throw new InvalidDataException($"unsupported wire type {wire}");
        }
    }

    /// <summary>Copy an entire field (tag + value) verbatim to
    /// <paramref name="output"/>. Used when re-emitting fields we
    /// didn't decide to rewrite — we re-read the tag from
    /// <paramref name="buf"/> rather than pass it through, which makes
    /// the call site cleaner.</summary>
    private static void CopyFieldWithTag(byte[] buf, ref int pos, Stream output)
    {
        int tagStart = pos;
        ulong tag = ReadVarint(buf, ref pos);
        int wire = (int)(tag & 0x07);
        // Write tag verbatim from original bytes (cheaper than re-encoding).
        output.Write(buf, tagStart, pos - tagStart);
        switch (wire)
        {
            case 0:
                {
                    int start = pos;
                    ReadVarint(buf, ref pos);
                    output.Write(buf, start, pos - start);
                    break;
                }
            case 1: output.Write(buf, pos, 8); pos += 8; break;
            case 2:
                {
                    int lengthStart = pos;
                    int len = (int)ReadVarint(buf, ref pos);
                    output.Write(buf, lengthStart, pos - lengthStart);
                    output.Write(buf, pos, len);
                    pos += len;
                    break;
                }
            case 5: output.Write(buf, pos, 4); pos += 4; break;
            default:
                throw new InvalidDataException($"unsupported wire type {wire}");
        }
    }
}
