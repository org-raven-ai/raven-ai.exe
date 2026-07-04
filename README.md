# raven_ai

A lightweight native **Windows** desktop AI voice + chat assistant (WPF, .NET 8, C#) whose
window is **omitted from screen captures** — invisible during screen sharing / recording
(Google Meet, Zoom, Microsoft Teams, OBS, `Win+Shift+S`, Game Bar), while remaining fully
visible on your physical monitor.

This is a data-loss-prevention feature: it keeps private assistant content out of accidental
screen shares, using the same OS mechanism (`SetWindowDisplayAffinity` with
`WDA_EXCLUDEFROMCAPTURE`) that banking apps and password managers use.

> **⚠ Not a security boundary.** See [Limitations](#limitations-read-this).

---

## Features

- **Screen-capture exclusion** via the Win32 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`
  API, applied after the HWND exists, **verified** with `GetWindowDisplayAffinity`, and
  re-applied by a watchdog. Failures are surfaced loudly in a warning banner — you never
  *think* you're protected when you aren't.
- **Always-on-top, frameless, draggable** compact window; minimizes to a system-tray icon.
- **Global hotkeys** (work even when unfocused):
  - `Ctrl+Shift+Space` — show / hide the window
  - `Ctrl+Shift+V` — push-to-talk (start/stop voice)
- **Streaming chat** against any **OpenAI-compatible** `/chat/completions` endpoint
  (configurable base URL + model), rendered token-by-token via SSE.
- **Voice**: push-to-talk mic capture (NAudio, 16 kHz mono WAV) → transcription
  (`/audio/transcriptions`) → chat → spoken reply (`/audio/speech`), with a **Windows offline
  TTS fallback** (`System.Speech`, no network).
- **API key encrypted at rest** with Windows **DPAPI** (`CurrentUser` scope), stored as a
  base64 blob in `%APPDATA%\raven_ai\settings.json`. The plaintext key is never logged or
  written to disk.

---

## Requirements

- **Windows 10, version 2004 (build 19041)** or newer for full invisibility
  (`WDA_EXCLUDEFROMCAPTURE`). On older builds the app falls back to `WDA_MONITOR`, which shows
  the window as a **black box** in captures (content hidden, but the box is visible) and warns
  you.
- **.NET 8 Desktop Runtime** (or newer) — a framework-dependent build. The repo builds with the
  .NET SDK 8+ (developed against SDK 10).
- An **OpenAI-compatible API key** for chat/voice (offline TTS works without one).

---

## Build & run

```powershell
# from the repo root
dotnet build -c Release
dotnet run --project src\RavenAI\RavenAI.csproj
```

The executable is produced at:

```
src\RavenAI\bin\Release\net8.0-windows\raven_ai.exe
```

### Small self-contained publish (single file, win-x64)

```powershell
dotnet publish src\RavenAI\RavenAI.csproj -c Release -r win-x64 -p:PublishSelfContained=true
```

> **Antivirus note.** Because raven_ai combines screen-capture exclusion, global hotkeys, and
> DPAPI-encrypted storage, heuristic antivirus (including Microsoft Defender) may flag or
> quarantine the build output as a false positive — those three behaviors together match the
> signature used for spyware/keyloggers. If your build fails with *"Access to the path …
> raven_ai.dll is denied"*, add a folder exclusion:
>
> ```powershell
> # run as Administrator
> Add-MpPreference -ExclusionPath 'D:\romeosarkar10x_hack\raven_ai'
> ```

---

## Verifying the invisibility (the whole point)

1. Launch raven_ai — the window appears near the top-right, on top of everything.
2. Trigger any capture:
   - Press `Win+Shift+S` (Snip & Sketch), or
   - Start a Zoom / Meet / Teams **screen share**, or
   - Use OBS **Display Capture**, or
   - Press `Win+G` (Game Bar) and record.
3. In the capture, the raven_ai window is **absent** — whatever is *behind* it shows through —
   while it stays fully visible on your real monitor.
4. The title-bar status dot / banner reflects `GetWindowDisplayAffinity`; a red banner means
   protection is not fully active.

---

## Configuration

Open **Settings** (title-bar button) to set:

| Setting            | Default                       |
|--------------------|-------------------------------|
| API key            | *(stored DPAPI-encrypted)*    |
| Base URL           | `https://api.openai.com/v1`   |
| Chat model         | `gpt-4o-mini`                 |
| System prompt      | *(assistant persona)*         |
| Speech-to-text     | `whisper-1`                   |
| Text-to-speech     | `gpt-4o-mini-tts`             |
| TTS voice          | `alloy`                       |
| Offline TTS        | off (uses Windows SAPI)       |

Settings persist to `%APPDATA%\raven_ai\settings.json`. Only the encrypted key blob is stored;
never the plaintext.

---

## Project structure

```
raven_ai.sln
src/RavenAI/
  App.xaml(.cs)              composition root (manual wiring, no DI container)
  app.manifest              per-monitor DPI aware; Win10/11 supported OS
  Views/                    MainWindow (opaque, no AllowsTransparency), converters
  ViewModels/               MainViewModel, ChatViewModel, SettingsViewModel (MVVM)
  Services/
    ScreenCaptureProtectionService.cs   the core capture-exclusion logic
    GlobalHotkeyService.cs              RegisterHotKey + WM_HOTKEY hook
    SecureSettingsStore.cs             DPAPI-encrypted settings persistence
    Chat/IChatProvider.cs + OpenAIChatProvider.cs   SSE streaming chat
    Voice/                 STT/TTS abstractions, NAudio capture, offline TTS
  Native/                   P/Invoke (SetWindowDisplayAffinity, RegisterHotKey)
  Models/                   RavenAISettings, ChatMessage
```

---

## Limitations (read this)

- **Remote Desktop (RDP)** disables DWM composition, which disables the exclusion — the window
  may appear in an RDP session.
- **Bare VMs without GPU acceleration** may not honor the flag.
- **This is not a hard security boundary.** It defeats normal screen-share and recording
  software, but a determined attacker running **kernel-level capture** or reading the
  **GPU framebuffer** on the same machine can still see the window. Do not rely on it to hide
  content from a compromised host.
- Windows builds **below 19041** get the `WDA_MONITOR` black-box fallback, not true invisibility.

---

## Tech

.NET 8 (WPF) · C# · `CommunityToolkit.Mvvm` · `NAudio` · `System.Speech` · Win32 P/Invoke.
No telemetry. No network calls except to the AI provider you configure.
