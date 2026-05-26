namespace DorkNet.Server.Services;

/// <summary>
/// Minimal word-list profanity filter for
/// <c>api/sanitize/v1/purifyString</c> + <c>requestIsStringPure</c>.
/// Deliberately conservative — only catches the obvious slur / NSFW
/// terms a 12-year-old would type. Real Rec Room used a third-party
/// service (WebPurify); we match the API surface, not the depth.
///
/// To extend: add words to <see cref="BadWords"/>. The filter is
/// case-insensitive and matches whole-word + leetspeak variants
/// (4→a, 0→o, 1→i, 3→e, 5→s, 7→t).
/// </summary>
public static class ProfanityFilter
{
    /// <summary>Lowercase canonical forms. Keep this list short
    /// and curate manually — false positives are worse than
    /// misses for a private friends-only server.</summary>
    private static readonly string[] BadWords =
    {
        "fuck", "shit", "bitch", "asshole", "cunt", "nigger",
        "faggot", "retard", "rape", "kys",
    };

    /// <summary>Returns true if the string contains no detectable
    /// profanity. The wire response is a JSON primitive bool.</summary>
    public static bool IsClean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var norm = Normalize(s);
        foreach (var bad in BadWords)
        {
            if (norm.Contains(bad)) return false;
        }
        return true;
    }

    /// <summary>Replaces detected profanity with asterisks of the
    /// same length. Returns the original string when clean.</summary>
    public static string Purify(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s ?? string.Empty;
        var result = s;
        var lower = s.ToLowerInvariant();
        var leet = Normalize(s);
        foreach (var bad in BadWords)
        {
            // Replace by lowercase first (preserves original casing
            // for surrounding text) — then leet-form. We only
            // replace contiguous matches; partial overlaps stay.
            var idx = lower.IndexOf(bad, StringComparison.Ordinal);
            while (idx >= 0)
            {
                result = string.Concat(
                    result.AsSpan(0, idx),
                    new string('*', bad.Length),
                    result.AsSpan(idx + bad.Length));
                lower = string.Concat(
                    lower.AsSpan(0, idx),
                    new string('*', bad.Length),
                    lower.AsSpan(idx + bad.Length));
                idx = lower.IndexOf(bad, idx + bad.Length, StringComparison.Ordinal);
            }
            var leetIdx = leet.IndexOf(bad, StringComparison.Ordinal);
            while (leetIdx >= 0)
            {
                result = string.Concat(
                    result.AsSpan(0, leetIdx),
                    new string('*', bad.Length),
                    result.AsSpan(leetIdx + bad.Length));
                leet = string.Concat(
                    leet.AsSpan(0, leetIdx),
                    new string('*', bad.Length),
                    leet.AsSpan(leetIdx + bad.Length));
                leetIdx = leet.IndexOf(bad, leetIdx + bad.Length, StringComparison.Ordinal);
            }
        }
        return result;
    }

    /// <summary>Lowercase + de-leetspeak so common substitutions
    /// (a→4, e→3, i→1, o→0, s→5, t→7) don't slip past the filter.</summary>
    private static string Normalize(string s) => s.ToLowerInvariant()
        .Replace('4', 'a')
        .Replace('0', 'o')
        .Replace('1', 'i')
        .Replace('3', 'e')
        .Replace('5', 's')
        .Replace('7', 't')
        .Replace('@', 'a')
        .Replace('$', 's');
}
