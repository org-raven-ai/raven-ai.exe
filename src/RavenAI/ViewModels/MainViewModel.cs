using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Services;

namespace RavenAI.ViewModels;

/// <summary>
/// Top-level shell view model. Holds the child view models and the capture-protection status
/// that drives the warning banner. The banner MUST be visible whenever protection is not
/// fully active, so the user never mistakenly believes they are hidden.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public ChatViewModel Chat { get; }
    public SettingsViewModel Settings { get; }
    public SpeechViewModel Speech { get; }
    public LogViewModel Log { get; }

    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isLogOpen;

    /// <summary>
    /// True while interactive mode is on: the fullscreen overlay is capturing mouse input and
    /// driving the fake cursor, so the floating panel can be clicked/typed into. False means the
    /// overlay is fully click-through and every key/click passes to the app underneath.
    /// </summary>
    [ObservableProperty] private bool _isInteractive;

    // Capture-protection status surfaced to the UI.
    [ObservableProperty] private bool _isProtected;
    [ObservableProperty] private bool _isFullyHidden;
    [ObservableProperty] private string _protectionStatus = "Checking screen-capture protection…";

    /// <summary>True when a warning banner should be shown (protection missing or degraded).</summary>
    public bool ShowProtectionWarning => !IsProtected || !IsFullyHidden;

    /// <summary>True when the chat messages are visible (neither Settings nor Logs overlay is open).</summary>
    public bool IsChatOpen => !IsSettingsOpen && !IsLogOpen;

    public MainViewModel(ChatViewModel chat, SettingsViewModel settings, SpeechViewModel speech, LogViewModel log)
    {
        Chat = chat;
        Settings = settings;
        Speech = speech;
        Log = log;
        Settings.Saved += () => IsSettingsOpen = false;

        // The staging window's Send button routes its batched transcript into the chat. Close any
        // Settings/Logs overlay first so the streaming reply is visible in the chat card.
        Speech.SendToChat = async text =>
        {
            IsSettingsOpen = false;
            IsLogOpen = false;
            await Chat.SubmitExternalAsync(text);
        };
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
    partial void OnIsSettingsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsChatOpen));
        if (value) Settings.EnsureModelsLoaded(); // populate the model dropdowns on first open
    }
    partial void OnIsLogOpenChanged(bool value) => OnPropertyChanged(nameof(IsChatOpen));

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        if (IsSettingsOpen) IsLogOpen = false; // the two overlays are mutually exclusive
    }

    [RelayCommand]
    private void ToggleLog()
    {
        IsLogOpen = !IsLogOpen;
        if (IsLogOpen) IsSettingsOpen = false; // the two overlays are mutually exclusive
    }
}
