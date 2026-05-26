# DorkNet Launcher

Single-download desktop launcher for self-hosting / joining DorkNet
servers. **Native WPF** — no embedded browser, no WebView2 dependency.
.NET 9 single-file self-contained build.

This README is for **developers** of the launcher. End-users see
[the main README](../README.md) and download a prebuilt `.exe` from
[Releases](https://github.com/DorkSquadRR/DorkNet/releases).

## What it does

- **Host mode:** fetches the matching server binary from GitHub
  Releases for the user's picked Rec Room version, runs it as a
  subprocess, opens a Cloudflare quick-tunnel, patches the user's
  Rec Room install, hands the user a shareable join code.
- **Join mode:** parses the join code, fetches the matching client
  patcher from GitHub Releases, patches the user's Rec Room install
  to point at the host.

## Layout

```
launcher/
├── DorkNet.Launcher.csproj    .NET 9 WPF, single-file self-contained
├── App.xaml + App.xaml.cs     WPF Application bootstrap, dark-theme palette
├── MainWindow.xaml            All views (first-run / host / join / settings)
├── MainWindow.xaml.cs         Code-behind: event handlers → Backend/* calls
├── app.manifest               Windows DPI-awareness manifest
└── Backend/                   Orchestration — UI-framework-agnostic
    ├── AppPaths.cs            %APPDATA%\DorkNet layout
    ├── AppState.cs            Persisted user choices (mode, paths, Photon AppId)
    ├── VersionsManifest.cs    Fetches versions.json from main
    ├── ReleaseDownloader.cs   GitHub Releases API + artifact unpack
    ├── ServerProcess.cs       Spawns the downloaded server binary
    ├── ClientPatcher.cs       Invokes install-plugin.ps1 from the unpacked patcher
    ├── Tunnel.cs              Wraps `cloudflared tunnel --url …`
    ├── RecRoomPicker.cs       Microsoft.Win32.OpenFolderDialog
    └── JoinCode.cs            base64url-encoded {host, version, photonAppId, …}
```

`MainWindow.xaml` holds all four views (first-run wizard, host, join,
settings) as sibling panels; code-behind toggles `Visibility` to
switch between them. No MVVM framework — direct event handlers in
the code-behind read/write `Backend/*` services.

## Build + run

```pwsh
cd launcher
dotnet run
```

Window opens immediately on the first-run wizard.

## Publish a self-contained .exe

```pwsh
dotnet publish -c Release
# Output: bin\Release\net9.0-windows\win-x64\publish\dorknet.exe (~70 MB)
```

The published exe is what ships in GitHub Releases on `main` (use the
launcher release workflow once it exists).

## Dependencies on per-version branches

The launcher fetches release artifacts from GitHub Releases tagged on
the per-version branches. See [RELEASES.md](RELEASES.md) for the
exact naming convention each branch must follow.

## Adding a new feature

1. New backend logic → add to (or create) a `Backend/*.cs` class.
2. New UI controls → add XAML in `MainWindow.xaml`.
3. New event handlers → method in `MainWindow.xaml.cs` calling the
   backend service.

Keep code-behind handlers small: they marshal between WPF dispatcher
thread + the backend services. Long work goes in `Backend/*`.
