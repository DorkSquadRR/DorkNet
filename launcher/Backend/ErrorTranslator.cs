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
        // Network — GitHub releases, Cloudflare quick-tunnel API, etc.
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

        if (ex is SocketException sock)
        {
            return new FriendlyError(
                "Network error.",
                $"Couldn't open a connection ({sock.SocketErrorCode}). Check your firewall isn't blocking DorkNet, then retry.",
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

        // cloudflared specifics
        if (ex is InvalidOperationException inv &&
            inv.Message.Contains("cloudflared", StringComparison.OrdinalIgnoreCase))
        {
            if (inv.Message.Contains("unexpectedly small", StringComparison.OrdinalIgnoreCase))
            {
                return new FriendlyError(
                    "cloudflared download was interrupted.",
                    "The launcher needs to download a small (~17 MB) Cloudflare helper on first run. Try again — if it keeps failing, switch to \"Same WiFi only\" mode under WHO CAN JOIN.",
                    "Retry");
            }
            return new FriendlyError(
                "Cloudflare tunnel didn't start.",
                TrimToSentence(inv.Message) +
                " Try again, or switch to \"Same WiFi only\" mode if you only need LAN players.",
                "Retry");
        }

        // Server port already in use
        if (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Only one usage of each socket address",
                StringComparison.OrdinalIgnoreCase))
        {
            return new FriendlyError(
                "Port 5005 is already in use.",
                "Another DorkNet server (or some other app) is already listening on port 5005. Close it and retry, or reboot.",
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
