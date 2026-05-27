using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DorkNet.Launcher.Backend;

namespace DorkNet.Launcher;

/// <summary>The launcher's only Window. Code-behind directly drives the
/// XAML controls — no MVVM framework. The <c>Backend/*</c> classes do
/// the work; the code-behind is glue + UI updates marshalled back to
/// the dispatcher thread.</summary>
public partial class MainWindow : Window
{
    private readonly AppState _state;
    private readonly ReleaseDownloader _releases = new();
    private readonly ClientPatcher _patcher = new();
    private readonly ServerProcess _server = new();
    private Tunnel? _tunnel;
    private VersionsManifest? _manifest;
    private string? _hostApex;
    private ObservableCollection<StartupStep>? _hostSteps;
    private ObservableCollection<StartupStep>? _joinSteps;

    public MainWindow()
    {
        InitializeComponent();
        _state = AppState.Load();
        Loaded += OnWindowLoaded;
        Closed += async (_, _) => await ShutdownServerAsync();
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // First-run convenience: if the user has never picked a Rec Room
        // install, try the common manual-install paths. Most grandmas
        // unzipped a build into Documents — auto-pick the newest match so
        // they never see the file dialog.
        if (string.IsNullOrEmpty(_state.RecRoomPath))
        {
            var detected = RecRoomPicker.Detect();
            if (detected is not null)
            {
                _state.RecRoomPath = detected;
                _state.Save();
            }
        }

        // Hydrate UI from persisted state, then fetch the manifest in the
        // background so the user can start clicking immediately even if the
        // version list is slow.
        ReflectStateInUi();
        await RefreshManifestAsync();
        AutoSelectVersionFromInstall();
    }

    /// <summary>If the user's install path resolves to a known build,
    /// pre-select the matching version in the dropdown so the field
    /// isn't a meaningless "december_2020_12_18" string. No-op if the
    /// build can't be identified — falls back to whatever the user (or
    /// the dropdown default) picks.</summary>
    private void AutoSelectVersionFromInstall()
    {
        if (_manifest is null || string.IsNullOrEmpty(_state.RecRoomPath)) return;
        var detected = RecRoomVersionDetector.Detect(_state.RecRoomPath);
        if (detected is null) return;
        var match = RecRoomVersionDetector.MatchToManifest(detected.ClientBuild, _manifest);
        if (match is null) return;
        foreach (var item in VersionSelect.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), match.VersionKey, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                _state.SelectedVersion = match.VersionKey;
                _state.Save();
                break;
            }
        }
    }

    // ── View routing ────────────────────────────────────────────────────
    private void ShowView(string name)
    {
        FirstRunView.Visibility = name == "first-run" ? Visibility.Visible : Visibility.Collapsed;
        HostView.Visibility = name == "host" ? Visibility.Visible : Visibility.Collapsed;
        JoinView.Visibility = name == "join" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = name == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private string CurrentViewName()
    {
        if (SettingsView.Visibility == Visibility.Visible) return "settings";
        if (HostView.Visibility == Visibility.Visible) return "host";
        if (JoinView.Visibility == Visibility.Visible) return "join";
        return "first-run";
    }

    private string? _previousView;

    private void ReflectStateInUi()
    {
        // Mirror persisted values into the host/join input fields so the
        // user doesn't re-type them every launch.
        var path = _state.RecRoomPath ?? "(not picked)";
        HostRecRoomPath.Text = path;
        JoinRecRoomPath.Text = path;

        PhotonAppIdInput.Text = _state.PhotonAppId;
        PhotonVoiceAppIdInput.Text = _state.PhotonVoiceAppId;
        SelectComboBoxByTag(PhotonRegionSelect, _state.PhotonRegion);
        ServerNameInput.Text = _state.ServerName;
        // Hosting mode radio — reflect persisted choice; defaults to Internet
        // since that's the install-and-play case most users want.
        if (HostModeLocal is not null && HostModeInternet is not null)
        {
            if (_state.HostingMode == HostingMode.LocalNetwork)
                HostModeLocal.IsChecked = true;
            else
                HostModeInternet.IsChecked = true;
        }

        CachePathsText.Text =
            $"State:   {AppPaths.StateFile}\n" +
            $"Servers: {AppPaths.ServersRoot}\n" +
            $"Patchers: {AppPaths.PatchersRoot}\n" +
            $"Logs:    {AppPaths.LogsRoot}";

        RefreshJoinReadiness();

        ShowView(_state.Mode switch
        {
            AppMode.Host => "host",
            AppMode.Join => "join",
            _ => "first-run",
        });
    }

    private async Task RefreshManifestAsync()
    {
        _manifest = await new VersionsManifest().FetchAsync();
        if (_manifest is null)
        {
            HostStatusText.Text = "Couldn't load versions.json from GitHub. " +
                "Check internet, then reopen.";
            return;
        }
        VersionSelect.Items.Clear();
        foreach (var v in _manifest.Branches.Where(b => b.Supported))
        {
            var item = new ComboBoxItem
            {
                Content = $"Rec Room {v.ClientBuild} ({v.Branch})",
                Tag = v.VersionKey,
            };
            VersionSelect.Items.Add(item);
            if (v.VersionKey == _state.SelectedVersion) item.IsSelected = true;
        }
        if (VersionSelect.SelectedItem is null && VersionSelect.Items.Count > 0)
            VersionSelect.SelectedIndex = 0;
    }

    private static void SelectComboBoxByTag(ComboBox cb, string tag)
    {
        foreach (var item in cb.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    // ── First-run wizard / mode picks ───────────────────────────────────
    private void OnPickHost(object sender, RoutedEventArgs e)
    {
        _state.Mode = AppMode.Host;
        _state.Save();
        ReflectStateInUi();
    }

    private void OnPickJoin(object sender, RoutedEventArgs e)
    {
        _state.Mode = AppMode.Join;
        _state.Save();
        ReflectStateInUi();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        _previousView = CurrentViewName();
        ShowView("settings");
    }

    private void OnBackFromSettings(object sender, RoutedEventArgs e)
    {
        ShowView(_previousView ?? (_state.Mode == AppMode.Join ? "join" : "host"));
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ── Shared field handlers ───────────────────────────────────────────
    private void OnPickRecRoom(object sender, RoutedEventArgs e)
    {
        var picked = RecRoomPicker.Pick(_state.RecRoomPath);
        if (picked is null) return;
        var (ok, reason) = RecRoomPicker.Validate(picked);
        if (!ok) { ShowError(reason); return; }
        _state.RecRoomPath = picked;
        _state.Save();
        ReflectStateInUi();
    }

    private void OnServerNameChanged(object sender, RoutedEventArgs e)
    {
        _state.ServerName = ServerNameInput.Text.Trim();
        _state.Save();
    }

    private void OnPhotonChanged(object sender, RoutedEventArgs e)
    {
        _state.PhotonAppId = PhotonAppIdInput.Text.Trim();
        _state.PhotonVoiceAppId = PhotonVoiceAppIdInput.Text.Trim();
        if (PhotonRegionSelect.SelectedItem is ComboBoxItem item)
            _state.PhotonRegion = item.Tag?.ToString() ?? "us";
        _state.Save();
    }

    private void OnOpenPhotonWizard(object sender, RoutedEventArgs e)
    {
        var wiz = new PhotonWizard(_state.PhotonAppId, _state.PhotonVoiceAppId, _state.PhotonRegion)
        {
            Owner = this,
        };
        if (wiz.ShowDialog() == true && wiz.Result is not null)
        {
            _state.PhotonAppId = wiz.Result.RealtimeAppId;
            _state.PhotonVoiceAppId = wiz.Result.VoiceAppId;
            _state.PhotonRegion = wiz.Result.Region;
            _state.Save();
            ReflectStateInUi();
        }
    }

    private void OnHostingModeChanged(object sender, RoutedEventArgs e)
    {
        // RadioButton.Checked fires twice on selection-change (once for
        // the un-checked, once for the new-checked). HostModeLocal/Internet
        // could be null during InitializeComponent so guard against that.
        if (HostModeLocal is null || HostModeInternet is null) return;
        _state.HostingMode = HostModeLocal.IsChecked == true
            ? HostingMode.LocalNetwork
            : HostingMode.Internet;
        _state.Save();
    }

    // ── Host flow ───────────────────────────────────────────────────────
    private async void OnHostStart(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_state.RecRoomPath))
        { ShowError("Pick your Rec Room install first."); return; }
        if (string.IsNullOrEmpty(_state.PhotonAppId))
        { ShowError("Photon AppId required (free at dashboard.photonengine.com)."); return; }

        if (_manifest is null) await RefreshManifestAsync();
        if (VersionSelect.SelectedItem is not ComboBoxItem item || item.Tag is null)
        { ShowError("Pick a version to host."); return; }
        var versionKey = item.Tag.ToString();
        var version = _manifest?.Branches.FirstOrDefault(b => b.VersionKey == versionKey);
        if (version is null)
        { ShowError($"Unknown version: {versionKey}"); return; }

        HostStartBtn.IsEnabled = false;
        HostStopBtn.Visibility = Visibility.Visible;
        JoinCodePanel.Visibility = Visibility.Collapsed;

        // Build a fresh step list so progress is visible the moment the
        // user clicks. Steps advance via StartStep/CompleteStep helpers
        // below so flow stays readable.
        _hostSteps = StartupFlow.NewHostFlow(_state.HostingMode == HostingMode.LocalNetwork);
        HostStepsList.ItemsSource = _hostSteps;
        HostStepsPanel.Visibility = Visibility.Visible;
        HostStatusText.Text = "";

        StartupStep? activeStep = null;
        HostRetryPanel.Visibility = Visibility.Collapsed;
        try
        {
            activeStep = StartStep(_hostSteps, 0);
            var serverProgress = new Progress<DownloadProgress>(p => UpdateStepProgress(activeStep, p));
            var serverDir = await _releases.EnsureServerAsync(version, serverProgress);
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 1);
            if (_state.HostingMode == HostingMode.LocalNetwork)
            {
                _hostApex = LocalNetwork.GetLanIp();
                activeStep.Detail = $"Hosting on {_hostApex}";
            }
            else
            {
                _tunnel = new Tunnel();
                activeStep.Detail = "Connecting to Cloudflare (first run downloads cloudflared, ~17 MB)";
                var publicUrl = await _tunnel.StartAsync(
                    localPort: 5005,
                    downloadProgress: new Progress<DownloadProgress>(p => UpdateStepProgress(activeStep!, p)));
                _hostApex = new Uri(publicUrl).Host;
                activeStep.Detail = $"Tunnel ready: {_hostApex}";
                activeStep.Progress = null;
            }
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 2);
            await _server.StartAsync(serverDir, _state, _hostApex);
            _state.SelectedVersion = version.VersionKey;
            _state.Save();
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 3);
            var patcherProgress = new Progress<DownloadProgress>(p => UpdateStepProgress(activeStep, p));
            var patcherDir = await _releases.EnsurePatcherAsync(version, patcherProgress);
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 4);
            var patch = await _patcher.ApplyAsync(
                patcherDir, _state.RecRoomPath!,
                _state.PhotonAppId, _state.PhotonVoiceAppId, _state.PhotonRegion, _hostApex);
            if (!patch.Ok)
            {
                FailStepWithFriendly(activeStep, ErrorTranslator.TranslateMessage(patch.Log));
                ShowHostRetry();
                return;
            }
            CompleteStep(activeStep);
            activeStep = null;

            var code = JoinCode.Encode(new JoinPayload
            {
                Host = _hostApex,
                VersionKey = version.VersionKey,
                PhotonAppId = _state.PhotonAppId,
                PhotonVoiceAppId = _state.PhotonVoiceAppId,
                PhotonRegion = _state.PhotonRegion,
                Name = _state.ServerName,
            });
            JoinCodeText.Text = code;
            JoinCodeQrImage.Source = QrCodeRenderer.Render(code);
            JoinCodePanel.Visibility = Visibility.Visible;
            HostStatusText.Text = _state.HostingMode == HostingMode.LocalNetwork
                ? $"Hosting on your local network at {_hostApex}. Share the join code with friends on the same WiFi."
                : $"Hosting at https://{_hostApex}. Share the join code below or launch Rec Room to test.";
            HostLaunchBtn.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            var friendly = ErrorTranslator.Translate(ex);
            if (activeStep is not null) FailStepWithFriendly(activeStep, friendly);
            else HostStatusText.Text = $"{friendly.Title} {friendly.Explanation}";
            ShowHostRetry();
            HostStartBtn.IsEnabled = true;
            HostStopBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowHostRetry()
    {
        HostRetryPanel.Visibility = Visibility.Visible;
    }

    private void OnHostRetry(object sender, RoutedEventArgs e)
    {
        HostRetryPanel.Visibility = Visibility.Collapsed;
        OnHostStart(sender, e);
    }

    // ── Step helpers ────────────────────────────────────────────────────
    private static StartupStep StartStep(ObservableCollection<StartupStep> steps, int index)
    {
        var s = steps[index];
        s.State = StepState.Active;
        return s;
    }

    private static void CompleteStep(StartupStep step)
    {
        step.State = StepState.Done;
        step.Progress = null;
    }

    private static void FailStepWithFriendly(StartupStep step, FriendlyError err)
    {
        step.State = StepState.Failed;
        step.Progress = null;
        step.Detail = $"{err.Title} {err.Explanation}";
    }

    private static void UpdateStepProgress(StartupStep step, DownloadProgress p)
    {
        if (p.TotalBytes > 0)
        {
            var mb = p.BytesRead / (1024.0 * 1024.0);
            var totalMb = p.TotalBytes / (1024.0 * 1024.0);
            step.Detail = $"{mb:F1} / {totalMb:F1} MB ({(int)(p.Fraction * 100)}%)";
            step.Progress = p.Fraction;
        }
        else
        {
            var mb = p.BytesRead / (1024.0 * 1024.0);
            step.Detail = $"{mb:F1} MB downloaded";
        }
    }

    private async void OnHostStop(object sender, RoutedEventArgs e)
    {
        await ShutdownServerAsync();
        HostStartBtn.IsEnabled = true;
        HostStopBtn.Visibility = Visibility.Collapsed;
        HostLaunchBtn.Visibility = Visibility.Collapsed;
        JoinCodePanel.Visibility = Visibility.Collapsed;
        HostStepsPanel.Visibility = Visibility.Collapsed;
        HostStepsList.ItemsSource = null;
        _hostSteps = null;
        HostStatusText.Text = "";
        HostRetryPanel.Visibility = Visibility.Collapsed;
    }

    private async Task ShutdownServerAsync()
    {
        try { await _server.StopAsync(); } catch { }
        if (_tunnel is not null) { try { await _tunnel.DisposeAsync(); } catch { } _tunnel = null; }
    }

    private void OnCopyJoinCode(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(JoinCodeText.Text); }
        catch (Exception ex) { ShowError("Copy failed: " + ex.Message); }
    }

    private void OnCopyJoinCodeMessage(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(_state.ServerName)
            ? "my DorkNet server"
            : _state.ServerName;
        var msg =
            $"Join {name} on DorkNet! Grab the launcher at " +
            "https://github.com/DorkSquadRR/DorkNet and paste this code:\n\n" +
            JoinCodeText.Text;
        try { Clipboard.SetText(msg); }
        catch (Exception ex) { ShowError("Copy failed: " + ex.Message); }
    }

    private void OnEmailJoinCode(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(_state.ServerName)
            ? "my DorkNet server"
            : _state.ServerName;
        // mailto: body is URL-encoded; Uri.EscapeDataString handles
        // the newlines + code text without trashing slashes or =.
        var subject = Uri.EscapeDataString($"Join {name} on DorkNet");
        var body = Uri.EscapeDataString(
            $"Hey! Want to play on {name}?\n\n" +
            "1. Download the DorkNet launcher: https://github.com/DorkSquadRR/DorkNet\n" +
            "2. Open it and pick \"Join a friend\"\n" +
            "3. Paste this code:\n\n" +
            JoinCodeText.Text);
        try { Process.Start(new ProcessStartInfo($"mailto:?subject={subject}&body={body}")
            { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Email failed: " + ex.Message); }
    }

    // ── Join flow ───────────────────────────────────────────────────────
    private void OnJoinCodeInputChanged(object sender, TextChangedEventArgs e)
        => RefreshJoinReadiness();

    private void RefreshJoinReadiness()
    {
        if (JoinApplyBtn is null) return;
        var hasCode = JoinCodeInput?.Text?.Trim().Length > 10;
        var hasPath = !string.IsNullOrEmpty(_state.RecRoomPath);
        JoinApplyBtn.IsEnabled = hasCode && hasPath;
    }

    private void OnDecodeJoinCode(object sender, RoutedEventArgs e)
    {
        var code = JoinCodeInput.Text.Trim();
        var payload = JoinCode.Decode(code);
        if (payload is null)
        {
            JoinPreviewBorder.Visibility = Visibility.Visible;
            JoinPreviewText.Text = "Invalid join code.";
            return;
        }
        JoinPreviewBorder.Visibility = Visibility.Visible;
        JoinPreviewText.Text =
            $"Connecting to {(string.IsNullOrEmpty(payload.Name) ? "(unnamed server)" : payload.Name)}\n" +
            $"Host: {payload.Host}\n" +
            $"Version: {payload.VersionKey}";
    }

    private async void OnJoinApply(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_state.RecRoomPath))
        { ShowError("Pick your Rec Room install first."); return; }
        var payload = JoinCode.Decode(JoinCodeInput.Text.Trim());
        if (payload is null)
        { ShowError("Invalid join code."); return; }
        if (_manifest is null) await RefreshManifestAsync();
        var version = _manifest?.Branches.FirstOrDefault(b => b.VersionKey == payload.VersionKey);
        if (version is null)
        { ShowError($"Host is on a version this launcher doesn't know about: {payload.VersionKey}"); return; }

        JoinApplyBtn.IsEnabled = false;
        _joinSteps = StartupFlow.NewJoinFlow();
        JoinStepsList.ItemsSource = _joinSteps;
        JoinStepsPanel.Visibility = Visibility.Visible;
        JoinStatusText.Text = "";
        JoinRetryPanel.Visibility = Visibility.Collapsed;

        StartupStep? activeStep = null;
        try
        {
            activeStep = StartStep(_joinSteps, 0);
            var progress = new Progress<DownloadProgress>(p => UpdateStepProgress(activeStep, p));
            var patcherDir = await _releases.EnsurePatcherAsync(version, progress);
            CompleteStep(activeStep);

            activeStep = StartStep(_joinSteps, 1);
            var patch = await _patcher.ApplyAsync(
                patcherDir, _state.RecRoomPath!,
                payload.PhotonAppId, payload.PhotonVoiceAppId, payload.PhotonRegion, payload.Host);
            if (!patch.Ok)
            {
                FailStepWithFriendly(activeStep, ErrorTranslator.TranslateMessage(patch.Log));
                JoinRetryPanel.Visibility = Visibility.Visible;
                return;
            }
            CompleteStep(activeStep);
            activeStep = null;

            JoinStatusText.Text = $"Patched and ready. Connecting to {payload.Host}. " +
                "Click Launch Rec Room when you're ready to play.";
            JoinLaunchBtn.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            var friendly = ErrorTranslator.Translate(ex);
            if (activeStep is not null) FailStepWithFriendly(activeStep, friendly);
            else JoinStatusText.Text = $"{friendly.Title} {friendly.Explanation}";
            JoinRetryPanel.Visibility = Visibility.Visible;
        }
        finally { RefreshJoinReadiness(); }
    }

    private void OnJoinRetry(object sender, RoutedEventArgs e)
    {
        JoinRetryPanel.Visibility = Visibility.Collapsed;
        OnJoinApply(sender, e);
    }

    private void OnOpenTroubleshooting(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://github.com/DorkSquadRR/DorkNet/blob/main/README.md#troubleshooting")
                { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError("Couldn't open help: " + ex.Message); }
    }

    private void OnLaunchRecRoom(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_state.RecRoomPath))
        { ShowError("Pick your Rec Room install first."); return; }
        if (!RecRoomLauncher.TryLaunch(_state.RecRoomPath, out var error))
            ShowError(error);
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private void ShowError(string message)
        => System.Windows.MessageBox.Show(this, message, "DorkNet",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
