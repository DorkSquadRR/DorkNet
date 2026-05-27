using System.IO;
using System.Net.Http;
using System.Net.Sockets;

namespace DorkNet.Launcher.Backend;

/// <summary>Maps the most common failure modes from the host/join flow
/// into plain-language messages with a hint about what to try. The
/// goal: never leave the user staring at a raw .NET stack trace.
///
/// <para>Order of checks matters — more specific patterns are checked
/// first. Unknown exceptions fall through with the raw message so we
/// don't silently lose information on novel failures.</para></summary>
public static class ErrorTranslator
{
    public static FriendlyError Translate(Exception ex)
    {
        // Walk the inner-exception chain — async stacks often wrap the
        // real cause inside AggregateException or TaskCanceledException.
        for (var cur = ex; cur is not null; cur = cur.InnerException!)
        {
            var hit = TryMatch(cur);
            if (hit is not null) return hit;
            if (cur.InnerException is null) break;
        }

        // Fallback: surface the first sentence of the message and a
        // generic "try again" hint. Better than nothing, worse than a
        // proper translation — add a case above when you see it twice.
        return new FriendlyError(
            "Something went wrong.",
            TrimToSentence(ex.Message),
            "Retry");
    }

    public static FriendlyError TranslateMessage(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new FriendlyError("Something went wrong.", "(no detail)", "Retry");

        // Patcher returns a raw log on failure (not an Exception), so we
        // pattern-match on the message string directly here.
        if (raw.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("being used by another process", StringComparison.OrdinalIgnoreCase))
        {
            return new FriendlyError(
                "Rec Room is currently open.",
                "Close Rec Room (and any DorkNet launcher copies) so we can update the files, then retry.",
                "Retry");
        }
        if (raw.Contains("Could not find", StringComparison.OrdinalIgnoreCase) &&
            raw.Contains("Recroom_Release", StringComparison.OrdinalIgnoreCase))
        {
            return new FriendlyError(
                "That doesn't look like a Rec Room install.",
                "The folder you picked is missing Rec Room's files. Pick the *_Data folder inside your Rec Room install (the one with StreamingAssets in it).",
                "Pick folder again");
        }
        return new FriendlyError("Patch failed.", TrimToSentence(raw), "Retry");
    }

    private static FriendlyError? TryMatch(Exception ex)
    {
        if (ex.Message.Contains("localtunnel", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("loca.lt", StringComparison.OrdinalIgnoreCase))
        {
            return new FriendlyError(
                "Localtunnel couldn't get a URL.",
                TrimToSentence(ex.Message) +
                " Localtunnel sometimes rate-limits or blips. Retry in a minute, or switch hosting mode to LAN if your friends are local.",
                "Retry");
        }

        // Network — GitHub releases, legacy tunnel APIs, etc.
        if (ex is HttpRequestException http)
        {
            return new FriendlyError(
                "Couldn't reach the download server.",
                "Check your internet connection and try again. If GitHub is having an outage, wait a minute and retry. " +
                $"({TrimToSentence(http.Message)})",
                "Retry");
        }

        if (ex is TaskCanceledException tce && tce.CancellationToken == default)
        {
            // Most TaskCanceledException with default token = HttpClient timeout
            return new FriendlyError(
                "Download timed out.",
                "Your connection is slow or unstable. Try again — the launcher resumes from scratch but can use cached files.",
                "Retry");
        }

        if (ex is TimeoutException timeout)
        {
            var title = timeout.Message.Contains("Tunnelto", StringComparison.OrdinalIgnoreCase)
                ? "Tunnelto tunnel timed out."
                : "Download timed out.";
            return new FriendlyError(
                title,
                TrimToSentence(timeout.Message) + " Try again in a minute; if it repeats, Tunnelto or GitHub may be blocked on this network.",
                "Retry");
        }

        if (ex is SocketException sock)
        {
            return new FriendlyError(
                "Network error.",
                $"Couldn't open a connection ({sock.SocketErrorCode}). Check your firewall isn't blocking DorkNet, then retry.",
                "Retry");
        }

        // Tunnelto specifics
        if (ex.Message.Contains("tunnelto", StringComparison.OrdinalIgnoreCase))
        {
            if (ex is FileNotFoundException)
            {
                return new FriendlyError(
                    "Tunnelto isn't installed.",
                    TrimToSentence(ex.Message),
                    "Retry");
            }
            return new FriendlyError(
                "Tunnelto tunnel didn't start.",
                TrimToSentence(ex.Message) +
                " Check that Tunnelto is signed in and that the base host is available, then retry.",
                "Retry");
        }

        // File-system — locked DLL, missing install path, etc.
        if (ex is UnauthorizedAccessException)
        {
            return new FriendlyError(
                "Rec Room is currently open.",
                "Close Rec Room (and any DorkNet launcher copies) so we can update the files, then retry.",
                "Retry");
        }
        if (ex is IOException io && io.Message.Contains("being used by another process",
                StringComparison.OrdinalIgnoreCase))
        {
            return new FriendlyError(
                "Rec Room is locked by another program.",
                "Close Rec Room so we can patch it, then retry.",
                "Retry");
        }
        if (ex is DirectoryNotFoundException || ex is FileNotFoundException)
        {
            return new FriendlyError(
                "Missing files.",
                TrimToSentence(ex.Message) +
                " If you re-installed or moved Rec Room, point the launcher at the new location.",
                "Pick folder again");
        }

        // Server port already in use
        if (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Only one usage of each socket address",
                StringComparison.OrdinalIgnoreCase))
        {
            return new FriendlyError(
                "Server port is already in use.",
                "Another DorkNet server (or some other app) is already listening on the required port. Close it and retry, or reboot.",
                "Retry");
        }

        return null;
    }

    /// <summary>Keep messages tight — anything past the first sentence
    /// in a .NET exception is usually noise. Strips trailing newlines
    /// too so it lays out cleanly in the step row.</summary>
    private static string TrimToSentence(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var trimmed = s.Trim().Replace("\r", " ").Replace("\n", " ");
        var dot = trimmed.IndexOf('.');
        if (dot > 20 && dot < trimmed.Length - 1) return trimmed[..(dot + 1)];
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "...";
    }
}

/// <summary>Translated error: a short headline, a longer explanation,
/// and the label for the action button (usually "Retry").</summary>
public sealed record FriendlyError(string Title, string Explanation, string ActionLabel);
