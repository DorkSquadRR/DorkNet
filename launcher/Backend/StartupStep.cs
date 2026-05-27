using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DorkNet.Launcher.Backend;

/// <summary>A single visible step in the host/join startup flow. The
/// XAML <c>ItemsControl</c> binds to a collection of these and the
/// DataTemplate switches presentation off <see cref="State"/>.
///
/// <para>This is a tiny INotifyPropertyChanged DTO on purpose — no MVVM
/// framework, no commands, no view-model layer. Code-behind owns the
/// collection and mutates fields as the flow progresses.</para></summary>
public sealed class StartupStep : INotifyPropertyChanged
{
    public StartupStep(string title) { Title = title; }

    public string Title { get; }

    private StepState _state = StepState.Pending;
    public StepState State
    {
        get => _state;
        set { if (_state == value) return; _state = value; OnChanged(); OnChanged(nameof(IsActive)); }
    }

    private string _detail = "";
    public string Detail
    {
        get => _detail;
        set { if (_detail == value) return; _detail = value; OnChanged(); OnChanged(nameof(HasDetail)); }
    }

    /// <summary>0..1 progress fraction, or null for "no progress bar"
    /// (e.g. an indeterminate phase like "starting server"). The
    /// DataTemplate hides the bar when null.</summary>
    private double? _progress;
    public double? Progress
    {
        get => _progress;
        set { if (_progress == value) return; _progress = value; OnChanged(); OnChanged(nameof(HasProgress)); OnChanged(nameof(ProgressPercent)); }
    }

    public bool HasDetail => !string.IsNullOrEmpty(_detail);
    public bool HasProgress => _progress.HasValue;
    public bool IsActive => _state == StepState.Active;

    /// <summary>0..100 for the ProgressBar Value binding.</summary>
    public double ProgressPercent => (_progress ?? 0) * 100;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
}

public enum StepState
{
    /// <summary>Hasn't started yet. Dim icon, no detail.</summary>
    Pending,
    /// <summary>In progress right now. Brand-coloured icon, detail +
    /// optional progress bar visible.</summary>
    Active,
    /// <summary>Finished successfully. Green checkmark.</summary>
    Done,
    /// <summary>Failed — error in <see cref="StartupStep.Detail"/>. Red
    /// X, the rest of the flow won't proceed.</summary>
    Failed,
}

/// <summary>Helpers for the two known flows. Centralising the step
/// names here keeps the XAML free of hardcoded strings and makes it
/// obvious what stages a user will see.</summary>
public static class StartupFlow
{
    public static ObservableCollection<StartupStep> NewHostFlow(HostingMode mode) => new()
    {
        new StartupStep("Download server binary"),
        new StartupStep(mode switch
        {
            HostingMode.LocalNetwork => "Set up local sslip.io address",
            HostingMode.RemoteWildcard => "Set up Tunnelto tunnel",
            _ => "Set up Tunnelto tunnel",
        }),
        new StartupStep("Start the server"),
        new StartupStep("Download client patcher"),
        new StartupStep("Unlock Rec Room from Steam (if needed)"),
        new StartupStep("Patch your Rec Room install"),
    };

    public static ObservableCollection<StartupStep> NewJoinFlow() => new()
    {
        new StartupStep("Download client patcher"),
        new StartupStep("Unlock Rec Room from Steam (if needed)"),
        new StartupStep("Patch your Rec Room install"),
    };
}
