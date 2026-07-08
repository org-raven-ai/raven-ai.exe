# Project structure

```
raven_ai.sln
src/RavenAI/
  App.xaml(.cs)              composition root (manual wiring, no DI container)
  app.manifest              per-monitor DPI aware; Win10/11 supported OS
  Views/                    MainWindow (full-screen transparent click-through overlay), converters
  ViewModels/               MainViewModel, ChatViewModel, SettingsViewModel (MVVM)
  Services/
    ScreenCaptureProtectionService.cs   the core capture-exclusion logic
    GlobalHotkeyService.cs              RegisterHotKey + WM_HOTKEY hook
    Overlay/InteractiveModeController.cs mouse-hook + raw-input capture for interactive mode
    SecureSettingsStore.cs             DPAPI-encrypted settings persistence
    Chat/IChatProvider.cs + OpenAIChatProvider.cs   SSE streaming chat
    Voice/                 STT/TTS abstractions, NAudio capture, offline TTS
  Native/                   P/Invoke (display affinity, hotkeys, click-through, mouse hook,
                            raw input, cursor freeze)
  Models/                   RavenAISettings, ChatMessage
scripts/metrics/            repo-metrics pipeline (see docs/metrics-pipeline.md)
```
