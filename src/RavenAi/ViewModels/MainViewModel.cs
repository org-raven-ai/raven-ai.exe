using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAi.Services;

namespace RavenAi.ViewModels;

/// <summary>
/// Top-level shell view model. Holds the child view models and the capture-protection status
/// that drives the warning banner. The banner MUST be visible whenever protection is not
/// fully active, so the user never mistakenly believes they are hidden.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public ChatViewModel Chat { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private bool _isSettingsOpen;

    // Capture-protection status surfaced to the UI.
    [ObservableProperty] private bool _isProtected;
    [ObservableProperty] private bool _isFullyHidden;
    [ObservableProperty] private string _protectionStatus = "Checking screen-capture protection…";

    /// <summary>True when a warning banner should be shown (protection missing or degraded).</summary>
    public bool ShowProtectionWarning => !IsProtected || !IsFullyHidden;

    public MainViewModel(ChatViewModel chat, SettingsViewModel settings)
    {
        Chat = chat;
        Settings = settings;
        Settings.Saved += () => IsSettingsOpen = false;
    }

    /// <summary>Called by the window once protection has been applied and verified.</summary>
    public void UpdateProtectionStatus(CaptureProtectionResult result)
    {
        IsProtected = result.Success;
        IsFullyHidden = result.FullyHidden;
        ProtectionStatus = result.Message;
        OnPropertyChanged(nameof(ShowProtectionWarning));
    }

    partial void OnIsProtectedChanged(bool value) => OnPropertyChanged(nameof(ShowProtectionWarning));
    partial void OnIsFullyHiddenChanged(bool value) => OnPropertyChanged(nameof(ShowProtectionWarning));

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;
}
