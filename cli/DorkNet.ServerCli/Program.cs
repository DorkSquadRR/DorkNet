using System.Diagnostics;
using DorkNet.Launcher.Backend;

namespace DorkNet.ServerCli;

/// <summary>Cross-platform host-side CLI for DorkNet. Drives the same
/// server + tunnel pipeline the Windows GUI launcher uses, but headless,
/// so Linux and macOS hosts can serve Windows joiners without needing
/// the WPF launcher (or any GUI). Joiners still use the Windows GUI
/// launcher to apply the patch to their copy of Rec Room.
///
/// <para>Run <c>dorknet-server --help</c> for the option list.</para></summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parsed = ParseArgs(args);
        if (parsed is null) return 1; // ParseArgs already wrote --help or an error.

        // Validate required values up front so the user sees the
        // failure before any download / process spin-up.
        if (string.IsNullOrWhiteSpace(parsed.PhotonAppId))
        {
            Console.Error.WriteLine("error: --photon-id is required.");
            Console.Error.WriteLine("       Grab one free at https://dashboard.photonengine.com");
            return 2;
        }

        AppPaths.EnsureDirectoriesExist();
        var state = BuildState(parsed);

        Console.WriteLine($"DorkNet server CLI · v{typeof(Program).Assembly.GetName().Version}");
        Console.WriteLine($"  mode:        {parsed.Mode}");
        Console.WriteLine($"  server:      {parsed.ServerName}");
        Console.WriteLine($"  photon:      {Redact(parsed.PhotonAppId)} ({parsed.Region})");
        if (parsed.Mode == HostingMode.RemoteWildcard)
            Console.WriteLine($"  apex:        {state.RemoteWildcardApex}");
        Console.WriteLine();

        var manifest = await new VersionsManifest().FetchAsync();
        if (manifest is null)
        {
            Console.Error.WriteLine("error: couldn't fetch versions.json (offline?).");
            Console.Error.WriteLine("       Set DORKNET_LOCAL_MANIFEST=/path/to/versions.json to override.");
            return 3;
        }
        var version = manifest.Branches
            .FirstOrDefault(b => b.VersionKey.Equals(parsed.VersionKey, StringComparison.OrdinalIgnoreCase));
        if (version is null)
        {
            Console.Error.WriteLine($"error: version '{parsed.VersionKey}' not found in manifest.");
            Console.Error.WriteLine("       Supported versions:");
            foreach (var b in manifest.Branches.Where(b => b.Supported))
                Console.Error.WriteLine($"         {b.VersionKey}  (Rec Room {b.ClientBuild})");
            return 4;
        }

        var releases = new ReleaseDownloader();
        var server = new ServerProcess();
        TunneltoTunnel? tunnel = null;
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            // Step 1 — server binary
            string serverDir;
            if (!string.IsNullOrEmpty(parsed.ServerDir))
            {
                if (!Directory.Exists(parsed.ServerDir))
                {
                    Console.Error.WriteLine($"error: --server-dir not found: {parsed.ServerDir}");
                    return 5;
                }
                serverDir = parsed.ServerDir;
                Console.WriteLine($"[server] using local build: {serverDir}");
            }
            else
            {
                Console.WriteLine("[server] downloading…");
                var progress = new Progress<DownloadProgress>(p =>
                {
                    if (p.TotalBytes > 0)
                        Console.Write($"\r          {p.Fraction * 100,5:F1}%  ({p.BytesRead / (1024.0 * 1024):F1} / {p.TotalBytes / (1024.0 * 1024):F1} MB)   ");
                });
                serverDir = await releases.EnsureServerAsync(version, progress, cts.Token);
                Console.WriteLine($"\n[server] cached at {serverDir}");
            }

            // Step 2 — tunnel (or LAN)
            string apex;
            if (parsed.Mode == HostingMode.LocalNetwork)
            {
                var lan = LocalNetwork.GetLanAddress();
                apex = lan.Host;
                Console.WriteLine($"[lan]    bound on {lan.Ip} (apex={apex})");
            }
            else
            {
                Console.WriteLine("[tunnel] starting tunnelto…");
                tunnel = new TunneltoTunnel();
                var publicUrl = await tunnel.StartAsync(
                    state.RemoteWildcardApex,
                    ServerProcess.DefaultLocalPort,
                    cts.Token);
                apex = TunneltoTunnel.NormalizeHost(state.RemoteWildcardApex);
                Console.WriteLine($"[tunnel] live at {publicUrl} (apex={apex})");
            }

            // Step 3 — server process
            Console.WriteLine("[server] starting…");
            await server.StartAsync(serverDir, state, apex, parsed.Mode, cts.Token);
            Console.WriteLine($"[server] listening (logs: {server.StdoutLogPath})");

            // Step 4 — emit join code
            var code = JoinCode.Encode(new JoinPayload
            {
                Host = apex,
                VersionKey = version.VersionKey,
                PhotonAppId = state.PhotonAppId,
                PhotonVoiceAppId = state.PhotonVoiceAppId,
                PhotonRegion = state.PhotonRegion,
                Name = state.ServerName,
            });
            var scheme = parsed.Mode == HostingMode.LocalNetwork ? "http" : "https";
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════════════");
            Console.WriteLine($"  Server live: {parsed.ServerName}");
            Console.WriteLine($"  Address:     {scheme}://{apex}");
            Console.WriteLine($"  Admin panel: {scheme}://admin.{apex}");
            Console.WriteLine("               (first account on a fresh server becomes admin)");
            Console.WriteLine();
            Console.WriteLine("  Join code (paste this into your friend's launcher):");
            Console.WriteLine();
            Console.WriteLine($"    {code}");
            Console.WriteLine();
            Console.WriteLine("  Ctrl-C to stop.");
            Console.WriteLine("══════════════════════════════════════════════════════");

            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            catch (OperationCanceledException) { /* clean Ctrl-C */ }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"error: {ex.Message}");
            return 10;
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("[shutdown] stopping server…");
            try { await server.StopAsync(); } catch { }
            if (tunnel is not null) { try { await tunnel.DisposeAsync(); } catch { } }
            Console.WriteLine("[shutdown] done.");
        }
        return 0;
    }

    /// <summary>Builds an in-memory <see cref="AppState"/> matching
    /// the CLI args. The CLI is stateless — it never reads or writes
    /// the launcher's state.json, so a Windows + Linux user on the
    /// same machine don't fight over the same file.</summary>
    private static AppState BuildState(ParsedArgs p) => new()
    {
        Mode = AppMode.Host,
        PhotonAppId = p.PhotonAppId,
        PhotonVoiceAppId = p.PhotonVoiceAppId,
        PhotonRegion = p.Region,
        HostingMode = p.Mode,
        RemoteWildcardApex = p.Mode == HostingMode.RemoteWildcard && !string.IsNullOrEmpty(p.Apex)
            ? TunneltoTunnel.NormalizeHost(p.Apex)
            : TunneltoTunnel.GenerateBaseHost(),
        ServerName = p.ServerName,
        SelectedVersion = p.VersionKey,
        SetupComplete = true,
        WelcomeSeen = true,
    };

    /// <summary>Mask all but the last 4 chars of an AppId so it's safe
    /// to print to a shared terminal / CI log.</summary>
    private static string Redact(string s) =>
        string.IsNullOrEmpty(s) || s.Length < 8
            ? "(missing)"
            : new string('*', s.Length - 4) + s[^4..];

    // ── Arg parsing ─────────────────────────────────────────────────────

    private sealed record ParsedArgs(
        string PhotonAppId, string PhotonVoiceAppId, string Region,
        HostingMode Mode, string Apex, string ServerName, string VersionKey,
        string? ServerDir);

    private static ParsedArgs? ParseArgs(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return null;
        }

        string photon = "", voice = "", region = "us";
        string mode = "tunnelto", apex = "", name = "DorkNet Server";
        string versionKey = "march_2020_03_10";
        string? serverDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            string Next()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"missing value for {args[i]}");
                return args[++i];
            }
            switch (args[i])
            {
                case "--photon-id":     photon = Next(); break;
                case "--voice-id":      voice = Next(); break;
                case "--region":        region = Next(); break;
                case "--mode":          mode = Next(); break;
                case "--apex":          apex = Next(); break;
                case "--name":          name = Next(); break;
                case "--version":       versionKey = Next(); break;
                case "--server-dir":    serverDir = Next(); break;
                default:
                    Console.Error.WriteLine($"error: unknown arg '{args[i]}' (try --help)");
                    return null;
            }
        }

        HostingMode parsedMode = mode.ToLowerInvariant() switch
        {
            "tunnelto" or "tunnel" or "internet" => HostingMode.Internet,
            "wildcard" or "remote"               => HostingMode.RemoteWildcard,
            "lan" or "local" or "wifi"           => HostingMode.LocalNetwork,
            _ => throw new ArgumentException($"unknown --mode '{mode}' (try tunnelto, wildcard, lan)"),
        };

        return new ParsedArgs(photon, voice, region, parsedMode, apex, name, versionKey, serverDir);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            dorknet-server — host a private Rec Room server (Linux / macOS / Windows)

            USAGE
              dorknet-server --photon-id <APPID> [options]

            REQUIRED
              --photon-id <guid>      Photon Realtime AppId (free at
                                      dashboard.photonengine.com)

            OPTIONS
              --voice-id <guid>       Photon Voice AppId. Empty = no voice.
              --region <code>         Photon cloud region (us | eu | asia | jp |
                                      sa | kr | in | au). Default: us.
              --mode <kind>           tunnelto (default) — friends anywhere
                                      wildcard          — Tunnelto wildcard
                                                          base (needs --apex)
                                      lan               — same WiFi only,
                                                          binds on 0.0.0.0:80
              --apex <hostname>       Wildcard apex (e.g. dorknet.tunnelto.me).
                                      Required for --mode wildcard.
              --name "<text>"         Server name shown in the join code.
                                      Default: "DorkNet Server".
              --version <key>         Rec Room version to host for.
                                      Default: march_2020_03_10.
              --server-dir <path>     Skip download, use this local server
                                      build. Useful for dev iteration.

            EXAMPLES
              # Linux host, default Tunnelto mode
              dorknet-server --photon-id 00000000-1111-... --name "Sunday games"

              # Wildcard base, custom region
              dorknet-server --photon-id ... --mode wildcard \
                             --apex dorknet.example.tunnelto.me --region eu

              # LAN-only on the local network
              dorknet-server --photon-id ... --mode lan
            """);
    }
}
