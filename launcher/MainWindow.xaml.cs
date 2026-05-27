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
    private readonly Updater _updater = new();
    private IAsyncDisposable? _tunnel;
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

        // Update check runs last, async fire-and-forget. Failures are
        // swallowed inside Updater.CheckAsync — the launcher must never
        // block on a slow GitHub roundtrip.
        SidebarVersionText.Text = $"DORKNET LAUNCHER · v{Updater.CurrentVersion}";
        _ = CheckForUpdatesAsync();
    }

    private UpdateInfo? _pendingUpdate;

    private async Task CheckForUpdatesAsync()
    {
        var info = await _updater.CheckAsync();
        if (info is null) return;
        _pendingUpdate = info;
        UpdateBannerVersionText.Text = $"v{Updater.CurrentVersion} → v{info.Version}";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;
        UpdateInstallBtn.IsEnabled = false;
        UpdateBannerTitle.Text = "Downloading…";
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                var mb = p.BytesRead / (1024.0 * 1024.0);
                UpdateBannerVersionText.Text = p.TotalBytes > 0
                    ? $"{mb:F1} / {p.TotalBytes / (1024.0 * 1024.0):F1} MB"
                    : $"{mb:F1} MB";
            });
            var script = await _updater.StageUpdateAsync(_pendingUpdate, progress);
            UpdateBannerTitle.Text = "Restarting…";

            // Launch the swap script detached, then bail out so it can
            // overwrite the running exe.
            await ShutdownServerAsync();
            Process.Start(new ProcessStartInfo("cmd", $"/c \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateBannerTitle.Text = "Update failed";
            UpdateBannerVersionText.Text = ex.Message.Length > 80
                ? ex.Message.Substring(0, 80) + "…" : ex.Message;
            UpdateInstallBtn.IsEnabled = true;
        }
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
        WelcomeView.Visibility = name == "welcome" ? Visibility.Visible : Visibility.Collapsed;
        SetupView.Visibility = name == "setup" ? Visibility.Visible : Visibility.Collapsed;
        HostView.Visibility = name == "host" ? Visibility.Visible : Visibility.Collapsed;
        JoinView.Visibility = name == "join" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = name == "settings" ? Visibility.Visible : Visibility.Collapsed;

        // Sidebar is part of the in-product chrome — hide it for the
        // hero-style Welcome and the dedicated Setup wizard.
        if (MainNav is not null)
            MainNav.Visibility = (name == "host" || name == "join" || name == "settings")
                ? Visibility.Visible : Visibility.Collapsed;

        if (NavHostBtn is not null) NavHostBtn.IsChecked = name == "host";
        if (NavJoinBtn is not null) NavJoinBtn.IsChecked = name == "join";
        if (NavSettingsBtn is not null) NavSettingsBtn.IsChecked = name == "settings";

        if (name == "setup") RenderSetupStep();
    }

    private string CurrentViewName()
    {
        if (SettingsView.Visibility == Visibility.Visible) return "settings";
        if (HostView.Visibility == Visibility.Visible) return "host";
        if (JoinView.Visibility == Visibility.Visible) return "join";
        if (SetupView.Visibility == Visibility.Visible) return "setup";
        return "welcome";
    }

    private string? _previousView;

    /// <summary>Pick the right initial view based on persisted state.
    /// True first launch: welcome -> setup -> host/join. Subsequent
    /// launches skip the wizard since SetupComplete is true.</summary>
    private string PickRoutingTarget()
    {
        if (!_state.WelcomeSeen) return "welcome";
        if (_state.Mode == AppMode.Unset || !_state.SetupComplete) return "setup";
        return _state.Mode == AppMode.Join ? "join" : "host";
    }

    private void ReflectStateInUi()
    {
        // Mirror persisted values into the host/join + setup input fields
        // so the user doesn't re-type them every launch (or every step
        // back/forward in the wizard).
        var path = _state.RecRoomPath ?? "(not picked)";
        HostRecRoomPath.Text = path;
        JoinRecRoomPath.Text = path;
        SetupRecRoomPath.Text = path;

        PhotonAppIdInput.Text = _state.PhotonAppId;
        PhotonVoiceAppIdInput.Text = _state.PhotonVoiceAppId;
        SetupPhotonAppId.Text = _state.PhotonAppId;
        SetupPhotonVoiceAppId.Text = _state.PhotonVoiceAppId;
        SelectComboBoxByTag(PhotonRegionSelect, _state.PhotonRegion);
        ServerNameInput.Text = _state.ServerName;
        SetupServerName.Text = _state.ServerName;
        EnsureRemoteWildcardApex();
        RemoteWildcardInput.Text = _state.RemoteWildcardApex;
        SetupRemoteWildcardInput.Text = _state.RemoteWildcardApex;
        if (HostScreenModeChk is not null)
            HostScreenModeChk.IsChecked = _state.LaunchInScreenMode;
        if (JoinScreenModeChk is not null)
            JoinScreenModeChk.IsChecked = _state.LaunchInScreenMode;

        // Hosting mode radios — reflect persisted choice on both the
        // main HostView and the setup wizard's matching step.
        if (HostModeLocal is not null && HostModeInternet is not null && HostModeRemote is not null)
        {
            HostModeInternet.IsChecked = _state.HostingMode == HostingMode.Internet;
            HostModeLocal.IsChecked = _state.HostingMode == HostingMode.LocalNetwork;
            HostModeRemote.IsChecked = _state.HostingMode == HostingMode.RemoteWildcard;
        }
        if (SetupHostLocal is not null && SetupHostInternet is not null && SetupHostRemote is not null)
        {
            SetupHostInternet.IsChecked = _state.HostingMode == HostingMode.Internet;
            SetupHostLocal.IsChecked = _state.HostingMode == HostingMode.LocalNetwork;
            SetupHostRemote.IsChecked = _state.HostingMode == HostingMode.RemoteWildcard;
        }

        // Setup step 2 banner — surface the auto-detected install so the
        // user knows the launcher already did the work.
        if (!string.IsNullOrEmpty(_state.RecRoomPath))
        {
            SetupDetectedBanner.Visibility = Visibility.Visible;
            SetupDetectedPath.Text = _state.RecRoomPath;
        }
        else
        {
            SetupDetectedBanner.Visibility = Visibility.Collapsed;
        }

        UpdateSetupPhotonStatus();
        UpdateRemoteWildcardVisibility();

        CachePathsText.Text =
            $"State:   {AppPaths.StateFile}\n" +
            $"Servers: {AppPaths.ServersRoot}\n" +
            $"Patchers: {AppPaths.PatchersRoot}\n" +
            $"Logs:    {AppPaths.LogsRoot}";

        var overrides = LocalOverrides.DescribeActive();
        if (!string.IsNullOrEmpty(overrides))
        {
            LocalOverridesPanel.Visibility = Visibility.Visible;
            LocalOverridesText.Text = overrides;
        }
        else
        {
            LocalOverridesPanel.Visibility = Visibility.Collapsed;
        }

        RefreshJoinReadiness();
        ShowView(PickRoutingTarget());
    }

    private async Task RefreshManifestAsync()
    {
        _manifest = await new VersionsManifest().FetchAsync();
        VersionSelect.Items.Clear();
        if (_manifest is null)
        {
            // Empty placeholder so the user sees *something* in the
            // dropdown instead of a silent blank — helps when the public
            // repo is unreachable (e.g. private repo + no env override).
            var placeholder = new ComboBoxItem
            {
                Content = "(no versions — check internet or set DORKNET_LOCAL_MANIFEST)",
                IsEnabled = false,
                Tag = null,
            };
            VersionSelect.Items.Add(placeholder);
            placeholder.IsSelected = true;
            HostStatusText.Text = "Couldn't load versions.json. " +
                "Either the repo isn't reachable, or DORKNET_LOCAL_MANIFEST " +
                "points at a missing/invalid file — check Settings.";
            return;
        }
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

    // ── Welcome + setup wizard ──────────────────────────────────────────
    private int _setupStep = 1; // 1=mode, 2=install, 3=server, 4=photon, 5=done

    private void OnWelcomeContinue(object sender, RoutedEventArgs e)
    {
        _state.WelcomeSeen = true;
        _state.Save();
        _setupStep = 1;
        ShowView("setup");
    }

    private void OnSetupPickHost(object sender, RoutedEventArgs e)
    {
        _state.Mode = AppMode.Host;
        _state.Save();
        _setupStep = 2;
        RenderSetupStep();
    }

    private void OnSetupPickJoin(object sender, RoutedEventArgs e)
    {
        _state.Mode = AppMode.Join;
        _state.Save();
        // Join mode only needs the install path — Photon/server config
        // come from the host's join code, not the joiner.
        _setupStep = 2;
        RenderSetupStep();
    }

    private void OnSetupBack(object sender, RoutedEventArgs e)
    {
        if (_setupStep <= 1) return;
        _setupStep--;
        // For Join mode, skip the host-only steps (3 + 4) when walking back
        // — there's nothing to show there.
        if (_state.Mode == AppMode.Join && _setupStep is 3 or 4)
            _setupStep = 2;
        RenderSetupStep();
    }

    private void OnSetupNext(object sender, RoutedEventArgs e)
    {
        // Validate before advancing — surface a friendly nudge if the
        // step's prereq isn't met.
        switch (_setupStep)
        {
            case 2 when string.IsNullOrEmpty(_state.RecRoomPath):
                ShowError("Pick your Rec Room install folder first.");
                return;
            case 3 when string.IsNullOrWhiteSpace(_state.ServerName):
                ShowError("Give your server a name first.");
                return;
            case 4 when string.IsNullOrWhiteSpace(_state.PhotonAppId):
                ShowError("A Realtime Photon AppId is required. Use the walkthrough button if you don't have one yet.");
                return;
        }

        _setupStep++;
        // Join mode skips host-only steps (3 = server name, 4 = Photon).
        if (_state.Mode == AppMode.Join && _setupStep is 3 or 4)
            _setupStep = 5;

        if (_setupStep > 5)
        {
            _state.SetupComplete = true;
            _state.Save();
            ShowView(_state.Mode == AppMode.Join ? "join" : "host");
            return;
        }
        RenderSetupStep();
    }

    private void RenderSetupStep()
    {
        SetupStep1.Visibility = _setupStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        SetupStep2.Visibility = _setupStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        SetupStep3.Visibility = _setupStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        SetupStep4.Visibility = _setupStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        SetupStepDone.Visibility = _setupStep == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Step indicator chip and totals — Join skips host-only steps so
        // the "of N" denominator changes between modes. Join's step 5
        // (done) is the third visible step, since 3 + 4 are skipped.
        var total = _state.Mode == AppMode.Join ? 3 : 5;
        var displayed = (_state.Mode == AppMode.Join && _setupStep == 5) ? 3 : _setupStep;
        SetupStepIndicator.Text = $"STEP {displayed} OF {total}";

        // Drive the horizontal stepper bars + numbered labels under each.
        // A bar is "filled" when its step is the current step or behind it.
        UpdateSetupStepperVisuals();

        // Step 1 (mode pick) has no Back / Next — the cards self-advance.
        SetupBackBtn.Visibility = _setupStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        SetupNextBtn.Visibility = _setupStep == 1 ? Visibility.Collapsed : Visibility.Visible;
        SetupNextLabel.Text = _setupStep == 5 ? "Finish" : "Next";

        // Refresh the install banner + Photon status each render — the
        // user may have hit "Pick…" mid-wizard and we want the new path
        // reflected immediately.
        var path = _state.RecRoomPath ?? "(not picked)";
        SetupRecRoomPath.Text = path;
        SetupDetectedBanner.Visibility = string.IsNullOrEmpty(_state.RecRoomPath)
            ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrEmpty(_state.RecRoomPath)) SetupDetectedPath.Text = _state.RecRoomPath;
        UpdateSetupPhotonStatus();

        // Re-sync the hosting-mode radios + base-host visibility every
        // time we render. The Checked event during ReflectStateInUi
        // doesn't always fire (XAML default IsChecked="True" on the
        // first radio can race the initial sync), so re-asserting here
        // guarantees step 3 shows the right state when the user lands
        // on it via Next or Re-run setup.
        if (SetupHostInternet is not null && SetupHostRemote is not null && SetupHostLocal is not null)
        {
            SetupHostInternet.IsChecked = _state.HostingMode == HostingMode.Internet;
            SetupHostRemote.IsChecked = _state.HostingMode == HostingMode.RemoteWildcard;
            SetupHostLocal.IsChecked = _state.HostingMode == HostingMode.LocalNetwork;
        }
        UpdateRemoteWildcardVisibility();
    }

    /// <summary>Repaint the 5-segment stepper rail to reflect the current
    /// step. Filled bars trail behind the active step; the active bar
    /// itself is brand-coloured. This is the visual replacement for the
    /// old single chip.</summary>
    private void UpdateSetupStepperVisuals()
    {
        var brand = (System.Windows.Media.Brush)FindResource("Brand");
        var dim = (System.Windows.Media.Brush)FindResource("Border");
        var bars = new[] { StepBar1, StepBar2, StepBar3, StepBar4, StepBar5 };
        for (int i = 0; i < bars.Length; i++)
            bars[i].Background = (i + 1) <= _setupStep ? brand : dim;
    }

    private void UpdateSetupPhotonStatus()
    {
        if (SetupPhotonStatus is null) return;
        if (string.IsNullOrWhiteSpace(_state.PhotonAppId))
        {
            SetupPhotonStatus.Text = "Realtime AppId required before you can host.";
            SetupPhotonStatus.Foreground = (System.Windows.Media.Brush)FindResource("InkFaint");
        }
        else
        {
            SetupPhotonStatus.Text = string.IsNullOrWhiteSpace(_state.PhotonVoiceAppId)
                ? "✓ Realtime AppId set. (No voice — players can use Discord.)"
                : "✓ Realtime + Voice AppIds set.";
            SetupPhotonStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
        }
    }

    private void OnSetupServerNameChanged(object sender, RoutedEventArgs e)
    {
        _state.ServerName = SetupServerName.Text.Trim();
        ServerNameInput.Text = _state.ServerName;
        _state.Save();
    }

    private void OnSetupHostingModeChanged(object sender, RoutedEventArgs e)
    {
        if (SetupHostLocal is null || SetupHostInternet is null || SetupHostRemote is null) return;
        _state.HostingMode = SetupHostRemote.IsChecked == true
            ? HostingMode.RemoteWildcard
            : SetupHostLocal.IsChecked == true
                ? HostingMode.LocalNetwork
                : HostingMode.Internet;
        _state.Save();
        // Mirror to main HostView radios.
        if (HostModeLocal is not null && HostModeInternet is not null && HostModeRemote is not null)
        {
            HostModeInternet.IsChecked = _state.HostingMode == HostingMode.Internet;
            HostModeLocal.IsChecked = _state.HostingMode == HostingMode.LocalNetwork;
            HostModeRemote.IsChecked = _state.HostingMode == HostingMode.RemoteWildcard;
        }
        UpdateRemoteWildcardVisibility();
    }

    /// <summary>The Tunnelto base host field only matters when the user
    /// picks the wildcard-base option. Plain Tunnelto auto-generates it;
    /// LAN doesn't use it at all. Hide it everywhere else.</summary>
    private void UpdateRemoteWildcardVisibility()
    {
        var show = _state.HostingMode == HostingMode.RemoteWildcard
            ? Visibility.Visible : Visibility.Collapsed;
        if (SetupRemoteWildcardSection is not null)
            SetupRemoteWildcardSection.Visibility = show;
        if (HostRemoteWildcardSection is not null)
            HostRemoteWildcardSection.Visibility = show;
    }

    private void OnSetupPhotonChanged(object sender, RoutedEventArgs e)
    {
        _state.PhotonAppId = SetupPhotonAppId.Text.Trim();
        _state.PhotonVoiceAppId = SetupPhotonVoiceAppId.Text.Trim();
        _state.Save();
        // Mirror into the canonical Photon section so re-opening HostView
        // doesn't show stale values.
        PhotonAppIdInput.Text = _state.PhotonAppId;
        PhotonVoiceAppIdInput.Text = _state.PhotonVoiceAppId;
        UpdateSetupPhotonStatus();
    }

    private void OnRerunSetup(object sender, RoutedEventArgs e)
    {
        _state.SetupComplete = false;
        _state.Save();
        _setupStep = 1;
        ShowView("setup");
    }

    // ── Legacy mode-pick handlers used by Settings -> Switch mode ──────
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

    private void OnRemoteWildcardChanged(object sender, RoutedEventArgs e)
    {
        _state.RemoteWildcardApex = TunneltoTunnel.NormalizeHost((sender as TextBox)?.Text ?? "");
        RemoteWildcardInput.Text = _state.RemoteWildcardApex;
        SetupRemoteWildcardInput.Text = _state.RemoteWildcardApex;
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
        // the un-checked, once for the new-checked). HostMode* controls
        // could be null during InitializeComponent so guard against that.
        if (HostModeLocal is null || HostModeInternet is null || HostModeRemote is null) return;
        _state.HostingMode = HostModeRemote.IsChecked == true
            ? HostingMode.RemoteWildcard
            : HostModeLocal.IsChecked == true
                ? HostingMode.LocalNetwork
                : HostingMode.Internet;
        _state.Save();
        UpdateRemoteWildcardVisibility();
    }

    // ── Host flow ───────────────────────────────────────────────────────
    /// <summary>Reflect the running state on the sidebar status pill.
    /// "Idle"/grey when nothing's running, "Starting"/amber while the
    /// host steps are in flight, "Live"/mint once the server is up,
    /// "Failed"/red on error.</summary>
    private void SetSidebarStatus(string label, string colorKey)
    {
        if (SidebarStatusText is null || SidebarStatusDot is null) return;
        SidebarStatusText.Text = label;
        SidebarStatusDot.Fill = (System.Windows.Media.Brush)FindResource(colorKey);
    }

    private async void OnHostStart(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_state.RecRoomPath))
        { ShowError("Pick your Rec Room install first."); return; }
        if (string.IsNullOrEmpty(_state.PhotonAppId))
        { ShowError("Photon AppId required (free at dashboard.photonengine.com)."); return; }
        if (_state.HostingMode != HostingMode.LocalNetwork)
        {
            var typed = TunneltoTunnel.NormalizeHost(RemoteWildcardInput.Text);
            _state.RemoteWildcardApex = string.IsNullOrWhiteSpace(typed)
                ? TunneltoTunnel.GenerateBaseHost()
                : typed;
            RemoteWildcardInput.Text = _state.RemoteWildcardApex;
            SetupRemoteWildcardInput.Text = _state.RemoteWildcardApex;
            _state.Save();
        }

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
        SetSidebarStatus("Starting…", "Sunshine");

        // Build a fresh step list so progress is visible the moment the
        // user clicks. Steps advance via StartStep/CompleteStep helpers
        // below so flow stays readable.
        _hostSteps = StartupFlow.NewHostFlow(_state.HostingMode);
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
                var lan = LocalNetwork.GetLanAddress();
                _hostApex = lan.Host;
                activeStep.Detail = $"LAN address {lan.Ip} via {_hostApex}";
            }
            else if (_state.HostingMode == HostingMode.RemoteWildcard)
            {
                var tunnel = new TunneltoTunnel();
                _tunnel = tunnel;
                activeStep.Detail = $"Connecting Tunnelto for {_state.RemoteWildcardApex}";
                var publicUrl = await tunnel.StartAsync(
                    _state.RemoteWildcardApex,
                    ServerProcess.DefaultLocalPort);
                var publicHost = new Uri(publicUrl).Host;
                _hostApex = TunneltoTunnel.NormalizeHost(_state.RemoteWildcardApex);
                activeStep.Detail = $"Tunnel ready: {publicHost}; services under {_hostApex}";
            }
            else
            {
                var tunnel = new TunneltoTunnel();
                _tunnel = tunnel;
                activeStep.Detail = $"Connecting Tunnelto for {_state.RemoteWildcardApex}";
                var publicUrl = await tunnel.StartAsync(
                    _state.RemoteWildcardApex,
                    ServerProcess.DefaultLocalPort);
                var publicHost = new Uri(publicUrl).Host;
                _hostApex = TunneltoTunnel.NormalizeHost(_state.RemoteWildcardApex);
                activeStep.Detail = $"Tunnel ready: {publicHost}; services under {_hostApex}";
                activeStep.Progress = null;
            }
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 2);
            await _server.StartAsync(
                serverDir, _state, _hostApex,
                hostingMode: _state.HostingMode);
            _state.SelectedVersion = version.VersionKey;
            _state.Save();
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 3);
            var patcherProgress = new Progress<DownloadProgress>(p => UpdateStepProgress(activeStep, p));
            var patcherDir = await _releases.EnsurePatcherAsync(version, patcherProgress);
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 4);
            var stripProgress = new Progress<DownloadProgress>(p => UpdateStepProgress(activeStep, p));
            var stripResult = await SteamDrmStripper.StripIfNeededAsync(
                _state.RecRoomPath!, stripProgress);
            activeStep.Detail = stripResult switch
            {
                StripResult.Stripped => "Steam DRM removed; backup saved next to the exe.",
                StripResult.AlreadyUnpacked => "Already unwrapped — skipped.",
                _ => "Not a Steam build — skipped.",
            };
            CompleteStep(activeStep);

            activeStep = StartStep(_hostSteps, 5);
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
            HostAdminBtn.Visibility = Visibility.Visible;
            SetSidebarStatus("Live", "Ok");
        }
        catch (Exception ex)
        {
            var friendly = ErrorTranslator.Translate(ex);
            if (activeStep is not null) FailStepWithFriendly(activeStep, friendly);
            else HostStatusText.Text = $"{friendly.Title} {friendly.Explanation}";
            ShowHostRetry();
            HostStartBtn.IsEnabled = true;
            HostStopBtn.Visibility = Visibility.Collapsed;
            SetSidebarStatus("Failed", "Danger");
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
        HostAdminBtn.Visibility = Visibility.Collapsed;
        JoinCodePanel.Visibility = Visibility.Collapsed;
        HostStepsPanel.Visibility = Visibility.Collapsed;
        HostStepsList.ItemsSource = null;
        _hostSteps = null;
        HostStatusText.Text = "";
        HostRetryPanel.Visibility = Visibility.Collapsed;
        SetSidebarStatus("Idle", "InkFaint");
    }

    /// <summary>Open the server's admin web panel in the user's default
    /// browser. The admin SPA is published at <c>admin.&lt;apex&gt;</c>
    /// over the same scheme the server uses (https for tunnel modes,
    /// http for LAN). First account created on a fresh server is
    /// auto-promoted; subsequent admins are added from inside the panel.</summary>
    private void OnOpenAdminPanel(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_hostApex))
        {
            ShowError("Server isn't running. Start hosting first, then the admin panel becomes reachable.");
            return;
        }
        var scheme = _state.HostingMode == HostingMode.LocalNetwork ? "http" : "https";
        var url = $"{scheme}://admin.{_hostApex}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't open {url}: {ex.Message}");
        }
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
            var stripProgress = new Progress<DownloadProgress>(p => UpdateStepProgress(activeStep, p));
            var stripResult = await SteamDrmStripper.StripIfNeededAsync(
                _state.RecRoomPath!, stripProgress);
            activeStep.Detail = stripResult switch
            {
                StripResult.Stripped => "Steam DRM removed; backup saved next to the exe.",
                StripResult.AlreadyUnpacked => "Already unwrapped — skipped.",
                _ => "Not a Steam build — skipped.",
            };
            CompleteStep(activeStep);

            activeStep = StartStep(_joinSteps, 2);
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

    private void OnScreenModeChanged(object sender, RoutedEventArgs e)
    {
        var enabled = (sender as CheckBox)?.IsChecked == true;
        _state.LaunchInScreenMode = enabled;
        if (HostScreenModeChk is not null && !ReferenceEquals(sender, HostScreenModeChk))
            HostScreenModeChk.IsChecked = enabled;
        if (JoinScreenModeChk is not null && !ReferenceEquals(sender, JoinScreenModeChk))
            JoinScreenModeChk.IsChecked = enabled;
        _state.Save();
    }

    private void EnsureRemoteWildcardApex()
    {
        if (!string.IsNullOrWhiteSpace(_state.RemoteWildcardApex)) return;
        _state.RemoteWildcardApex = TunneltoTunnel.GenerateBaseHost();
        _state.Save();
    }

    private void OnLaunchRecRoom(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_state.RecRoomPath))
        { ShowError("Pick your Rec Room install first."); return; }
        if (!RecRoomLauncher.TryLaunch(_state.RecRoomPath, _state.LaunchInScreenMode, out var error))
            ShowError(error);
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private void ShowError(string message)
        => System.Windows.MessageBox.Show(this, message, "DorkNet",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
