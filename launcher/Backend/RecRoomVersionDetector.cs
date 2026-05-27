using System.IO;

namespace DorkNet.Launcher.Backend;

/// <summary>Figures out which Rec Room build the user has installed so
/// the launcher doesn't need to ask. Maps the result against the
/// branches in <see cref="VersionsManifest"/> to pick the right server
/// + patcher.
///
/// <para>Three signals, tried in order of reliability:
/// <list type="number">
///   <item><b><c>StreamingAssets\version.txt</c></b> — when present in
///   the install, it contains the canonical build string
///   (e.g. <c>20201218</c>). Fastest + most reliable.</item>
///   <item><b>Executable PE timestamp</b> — every PE binary stamps a
///   compile-time UTC timestamp into its header. Rec Room builds line
///   up against known release dates (2020-03-10 → March, 2020-12-18 →
///   December). Good fallback.</item>
///   <item><b>File size of <c>Recroom_Release.exe</c></b> — last resort.
///   Builds drift in size across versions; useful when neither
///   version.txt nor a reasonable PE timestamp are present (some
///   manually-extracted dumps have these zeroed).</item>
/// </list></para></summary>
public static class RecRoomVersionDetector
{
    /// <summary>Probe <paramref name="recRoomDataPath"/> for a build
    /// identifier. Returns null when no signal is reliable enough to
    /// match a known branch — callers should fall back to the version
    /// dropdown.</summary>
    public static DetectedVersion? Detect(string recRoomDataPath)
    {
        if (string.IsNullOrEmpty(recRoomDataPath) || !Directory.Exists(recRoomDataPath))
            return null;

        // 1. StreamingAssets\version.txt
        var versionTxt = Path.Combine(recRoomDataPath, "StreamingAssets", "version.txt");
        if (File.Exists(versionTxt))
        {
            try
            {
                var raw = File.ReadAllText(versionTxt).Trim();
                var build = NormalizeBuild(raw);
                if (!string.IsNullOrEmpty(build))
                    return new DetectedVersion(build, "version.txt");
            }
            catch { /* fall through */ }
        }

        // Locate the executable next to *_Data.
        var parent = Path.GetDirectoryName(recRoomDataPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent)) return null;
        var exe = Path.Combine(parent, "Recroom_Release.exe");
        if (!File.Exists(exe)) return null;

        // 2. PE timestamp → known release date map.
        var peStamp = TryReadPeTimestamp(exe);
        if (peStamp is DateTime stamp)
        {
            var match = MatchByDate(stamp);
            if (match is not null) return new DetectedVersion(match, $"PE timestamp {stamp:yyyy-MM-dd}");
        }

        // 3. File size — coarse but deterministic.
        var size = new FileInfo(exe).Length;
        var sizeMatch = MatchByFileSize(size);
        if (sizeMatch is not null) return new DetectedVersion(sizeMatch, $"exe size {size:N0}");

        return null;
    }

    /// <summary>Pick the <see cref="VersionEntry"/> from <paramref name="manifest"/>
    /// that matches the detected build, including the <c>alt_builds</c>
    /// list (March 2020.03.10 also covers 2020.03.06, etc).</summary>
    public static VersionEntry? MatchToManifest(string detectedBuild, VersionsManifest manifest)
    {
        if (string.IsNullOrEmpty(detectedBuild)) return null;
        foreach (var b in manifest.Branches)
        {
            if (string.Equals(b.ClientBuild, detectedBuild, StringComparison.OrdinalIgnoreCase))
                return b;
            if (b.AltBuilds.Any(a => string.Equals(a, detectedBuild, StringComparison.OrdinalIgnoreCase)))
                return b;
        }
        return null;
    }

    /// <summary>Normalize the various formats <c>version.txt</c> ships
    /// in across builds. Rec Room has shipped <c>2020.12.18</c>,
    /// <c>20201218</c>, and timestamped variants — flatten to the
    /// dotted form the manifest uses (<c>2020.12.18</c>).</summary>
    private static string NormalizeBuild(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return string.Empty;
        // Dotted (already canonical).
        if (trimmed.Length == 10 && trimmed[4] == '.' && trimmed[7] == '.')
            return trimmed;
        // Compact (YYYYMMDD).
        if (trimmed.Length == 8 && trimmed.All(char.IsDigit))
            return $"{trimmed[..4]}.{trimmed.Substring(4, 2)}.{trimmed.Substring(6, 2)}";
        // Some builds embed extra suffixes — strip after first space.
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace > 0) return NormalizeBuild(trimmed[..firstSpace]);
        return string.Empty;
    }

    /// <summary>Known release dates for the builds the launcher
    /// supports. Maps DATE → canonical build string.</summary>
    private static readonly (DateTime Date, string Build)[] KnownReleases =
    {
        // March 2020 family: build dates clustered in early March.
        (new DateTime(2020, 3, 6),  "2020.03.06"),
        (new DateTime(2020, 3, 10), "2020.03.10"),
        // December 2020 (the late-2020 build the launcher's december
        // branch targets).
        (new DateTime(2020, 12, 18), "2020.12.18"),
    };

    private static string? MatchByDate(DateTime stamp)
    {
        // Match within +/- 3 days. PE timestamps reflect build time, not
        // release date; small drift is normal.
        var stampDate = stamp.Date;
        foreach (var (date, build) in KnownReleases)
        {
            if (Math.Abs((stampDate - date).TotalDays) <= 3) return build;
        }
        return null;
    }

    /// <summary>Coarse file-size fingerprint. Each known build's
    /// <c>Recroom_Release.exe</c> falls in a narrow size window — they
    /// don't collide across versions. Update this table when we
    /// onboard a new branch.</summary>
    private static readonly (long Min, long Max, string Build)[] KnownSizes =
    {
        // Empirical: 2020.03.10 exe is ~600-650 KB.
        (550_000, 700_000, "2020.03.10"),
        // 2020.12.18 exe is ~700-800 KB.
        (700_000, 850_000, "2020.12.18"),
    };

    private static string? MatchByFileSize(long size)
    {
        foreach (var (min, max, build) in KnownSizes)
            if (size >= min && size <= max) return build;
        return null;
    }

    /// <summary>Read the PE COFF header timestamp field. Returns null
    /// on any IO / parse failure.</summary>
    private static DateTime? TryReadPeTimestamp(string exe)
    {
        try
        {
            using var fs = File.OpenRead(exe);
            using var br = new BinaryReader(fs);
            // PE header offset is stored at file offset 0x3C as a 4-byte
            // little-endian uint.
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            // PE signature ("PE\0\0") + COFF header. Timestamp is at
            // peOffset + 4 (signature) + 4 (machine + numSections) =
            // peOffset + 8. ReadInt32() at that offset = Unix epoch seconds.
            fs.Seek(peOffset + 8, SeekOrigin.Begin);
            var unix = br.ReadInt32();
            if (unix <= 0) return null;
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        }
        catch { return null; }
    }
}

public sealed record DetectedVersion(string ClientBuild, string Source);
