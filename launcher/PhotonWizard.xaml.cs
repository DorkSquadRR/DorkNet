using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DorkNet.Launcher.Backend;

namespace DorkNet.Launcher;

/// <summary>Five-step walkthrough for first-time Photon setup. Opens
/// dashboard.photonengine.com on step 2, walks the user through app
/// creation in steps 3–4, then collects + validates AppIds on step 5.
///
/// <para>Result lives in <see cref="Result"/> when <see cref="DialogResult"/>
/// is true. Caller (MainWindow) writes it back into AppState.</para></summary>
public partial class PhotonWizard : Window
{
    public PhotonResult? Result { get; private set; }

    // Photon AppIds are GUIDs — accept with or without hyphens; user
    // gets confused if we're strict about formatting. We render the
    // canonical form back into AppState.
    private static readonly Regex AppIdRegex =
        new("^[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}$",
            RegexOptions.Compiled);

    private readonly StackPanel[] _steps;
    private int _stepIndex;

    public PhotonWizard(string? existingRealtime, string? existingVoice, string? existingRegion)
    {
        InitializeComponent();
        _steps = new[] { Step1, Step2, Step3, Step4, Step5 };

        // Pre-fill so a user re-opening the wizard sees their existing
        // values — they might just want to fix the region or swap apps.
        WizRealtime.Text = existingRealtime ?? "";
        WizVoice.Text = existingVoice ?? "";
        SelectRegion(existingRegion ?? "us");
        UpdateAppIdStatuses();
        ShowStep(0);
    }

    private void ShowStep(int index)
    {
        _stepIndex = Math.Clamp(index, 0, _steps.Length - 1);
        for (var i = 0; i < _steps.Length; i++)
            _steps[i].Visibility = i == _stepIndex ? Visibility.Visible : Visibility.Collapsed;

        StepIndicator.Text = $"STEP {_stepIndex + 1} OF {_steps.Length}";
        PrevBtn.Visibility = _stepIndex == 0 ? Visibility.Collapsed : Visibility.Visible;

        // Step 4 (Voice) is optional — surface a Skip button so the user
        // doesn't feel stuck if they don't want voice chat. Final step
        // swaps Next for a Save action.
        SkipBtn.Visibility = _stepIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = _stepIndex == _steps.Length - 1 ? "Save" : "Next →";
    }

    private void OnPrev(object sender, RoutedEventArgs e) => ShowStep(_stepIndex - 1);

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_stepIndex < _steps.Length - 1)
        {
            ShowStep(_stepIndex + 1);
            return;
        }
        TrySave();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOpenPhotonDashboard(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://dashboard.photonengine.com")
                { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open the dashboard: " + ex.Message,
                "Photon setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnAppIdChanged(object sender, TextChangedEventArgs e) => UpdateAppIdStatuses();

    private void UpdateAppIdStatuses()
    {
        var realtime = WizRealtime.Text.Trim();
        if (string.IsNullOrEmpty(realtime))
        {
            WizRealtimeStatus.Text = "32-character ID with hyphens, like 12ab34cd-56ef-78ab-90cd-12ef34ab56cd";
            WizRealtimeStatus.Foreground = (System.Windows.Media.Brush)FindResource("InkFaint");
        }
        else if (AppIdRegex.IsMatch(realtime))
        {
            WizRealtimeStatus.Text = "✓ Looks like a valid AppId.";
            WizRealtimeStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
        }
        else
        {
            WizRealtimeStatus.Text = "⚠ That doesn't look like a Photon AppId. Should be 32 hex characters (with or without hyphens).";
            WizRealtimeStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        }

        var voice = WizVoice.Text.Trim();
        if (string.IsNullOrEmpty(voice))
        {
            WizVoiceStatus.Text = "Optional — leave empty if you skipped step 4.";
            WizVoiceStatus.Foreground = (System.Windows.Media.Brush)FindResource("InkFaint");
        }
        else if (AppIdRegex.IsMatch(voice))
        {
            WizVoiceStatus.Text = "✓ Looks like a valid AppId.";
            WizVoiceStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
        }
        else
        {
            WizVoiceStatus.Text = "⚠ That doesn't look like a Photon AppId.";
            WizVoiceStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        }
    }

    private void TrySave()
    {
        var realtime = WizRealtime.Text.Trim();
        var voice = WizVoice.Text.Trim();
        if (string.IsNullOrEmpty(realtime))
        {
            MessageBox.Show(this,
                "A Realtime AppId is required — without it nobody can connect to your server. Voice is optional.",
                "Photon setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!AppIdRegex.IsMatch(realtime))
        {
            MessageBox.Show(this,
                "Your Realtime AppId doesn't look right. It should be 32 hex characters (with or without hyphens). Double-check that you copied the entire string from the Photon dashboard.",
                "Photon setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!string.IsNullOrEmpty(voice) && !AppIdRegex.IsMatch(voice))
        {
            MessageBox.Show(this,
                "Your Voice AppId doesn't look right. Either fix it or clear the field to skip voice.",
                "Photon setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var region = WizRegion.SelectedItem is ComboBoxItem item && item.Tag is not null
            ? item.Tag.ToString() ?? "us"
            : "us";

        Result = new PhotonResult(Normalise(realtime), Normalise(voice), region);
        DialogResult = true;
        Close();
    }

    private void SelectRegion(string tag)
    {
        foreach (var item in WizRegion.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    /// <summary>Render the AppId in canonical 8-4-4-4-12 form regardless
    /// of how the user pasted it — the server's Photon SDK accepts both
    /// but normalising keeps the rest of the launcher tidy.</summary>
    private static string Normalise(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return "";
        var stripped = appId.Replace("-", "").Trim();
        if (stripped.Length != 32) return appId;
        return $"{stripped[..8]}-{stripped[8..12]}-{stripped[12..16]}-{stripped[16..20]}-{stripped[20..]}";
    }
}

public sealed record PhotonResult(string RealtimeAppId, string VoiceAppId, string Region);
