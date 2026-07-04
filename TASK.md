# Agent Prompt — "Veil": A Screen-Capture-Invisible AI Voice/Chat Assistant (Windows, C#)

> Paste everything below into your coding agent. Rename **Veil** to whatever you like.

---

## Role & objective

You are a senior Windows desktop engineer. Build a **lightweight native Windows app in C#** called **Veil**: an AI voice + chat assistant that talks to an AI provider using a user-supplied API key.

The **single most important, non-negotiable requirement** is that the app's window must be **invisible during screen sharing / screen recording** (Google Meet, Zoom, Microsoft Teams, OBS, Windows' built-in screenshot/record tools). When the user shares their screen, Veil's window must not appear — whatever is *behind* it should be shared instead. The user must still see the window normally on their physical monitor. This is for legitimate data-loss-prevention purposes (keeping private chat content out of accidental screen shares), the same mechanism banking apps and password managers use.

Build for correctness on this feature first. Everything else is secondary.

---

## Tech stack (fixed)

- **.NET 8 (LTS) or newer**, **C#** latest language version.
- **WPF** for the UI (native, lightweight, easy Win32 interop, good for chat UIs). Do **not** use Electron, web views, WinForms, or WinUI unless you hit a blocker and explain why.
- **MVVM** via `CommunityToolkit.Mvvm`.
- `System.Net.Http` (`HttpClient`) for API calls with streaming.
- `NAudio` for microphone capture and audio playback (voice phase).
- Keep dependencies minimal. Prefer built-in .NET / Win32 over adding packages.
- Ship a **framework-dependent or trimmed self-contained** `dotnet publish` for `win-x64`; keep the binary small.

---

## The critical feature — screen-capture exclusion

Implement window exclusion with the Win32 API `SetWindowDisplayAffinity` using the `WDA_EXCLUDEFROMCAPTURE` flag (`0x00000011`). This flag tells the Desktop Window Manager to omit the window from all capture surfaces. Available on **Windows 10 version 2004 (build 19041) and later**.

Encapsulate this in a `ScreenCaptureProtectionService`. Reference implementation to adapt:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;

internal static class NativeCaptureProtection
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);

    public const uint WDA_NONE = 0x00000000;
    public const uint WDA_MONITOR = 0x00000001;            // fallback: renders as black box
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // preferred: fully hidden

    public static bool Apply(IntPtr hWnd, uint affinity, out int lastError)
    {
        bool ok = SetWindowDisplayAffinity(hWnd, affinity);
        lastError = ok ? 0 : Marshal.GetLastWin32Error();
        return ok;
    }

    public static bool Verify(IntPtr hWnd, uint expected)
        => GetWindowDisplayAffinity(hWnd, out uint current) && current == expected;
}
```

Apply it **after the HWND exists** (WPF's `OnSourceInitialized`, not the constructor):

```csharp
protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;

    if (!NativeCaptureProtection.Apply(hwnd, NativeCaptureProtection.WDA_EXCLUDEFROMCAPTURE, out int err))
    {
        // Log err. Common failure: error 8 (ERROR_NOT_ENOUGH_MEMORY) on layered windows.
        // Surface a clear warning to the user that protection could not be enabled.
    }
    // Optionally verify and re-check periodically.
}
```

### Rules you MUST follow for this feature

1. **Do NOT use `AllowsTransparency="True"`** on the protected window. That creates a `WS_EX_LAYERED` window, and on some Windows 11 builds `SetWindowDisplayAffinity` then fails with **error 8 (ERROR_NOT_ENOUGH_MEMORY)**. Use a normal **opaque** window. If custom chrome / rounded corners are wanted, use `WindowChrome` — not transparency.
2. **Guard the OS version.** If the build is below 19041, fall back to `WDA_MONITOR` and warn the user the window will appear as a black box rather than fully hidden.
3. **Verify success.** After setting, call `GetWindowDisplayAffinity` to confirm the flag stuck. Re-apply on window show if you ever hide/reshow. Log `GetLastWin32Error()` on any failure.
4. **Fail loudly, not silently.** If protection can't be enabled, show a visible warning banner in the app — the user must never *think* they're protected when they aren't.

### Known limitations to document in the README (not bugs to fix)

- Remote Desktop (RDP) disables DWM, which disables the exclusion.
- Bare VMs without GPU acceleration may not honor it.
- This defeats all normal screen-share/recording software but is **not** a security boundary against a determined attacker running kernel-level or GPU-framebuffer capture on the same machine. State this plainly.

---

## Additional features

### Window behavior
- Frameless-ish, compact, **always-on-top**, movable by dragging.
- A **global show/hide hotkey** (e.g. `Ctrl+Shift+Space`) implemented via `RegisterHotKey` / `UnregisterHotKey` (P/Invoke) with a `WM_HOTKEY` (0x0312) message hook on an `HwndSource`. The app should toggle visibility even when not focused.
- Start minimized to tray (optional) with a tray icon to quit/toggle.

### API key handling (security-sensitive)
- A **Settings** view where the user pastes their provider API key and picks a provider/model.
- **Encrypt the key at rest with DPAPI**: `System.Security.Cryptography.ProtectedData.Protect(...)` with `DataProtectionScope.CurrentUser`. Store the encrypted blob (base64) under `%APPDATA%\Veil\settings.json`.
- **Never** log, print, or write the key in plaintext anywhere. Load it into memory only when making requests.

### Chat (Phase 1 core)
- Define a provider abstraction: `IChatProvider` with a streaming method returning `IAsyncEnumerable<string>` (token deltas).
- Ship one concrete implementation for an **OpenAI-compatible Chat Completions** endpoint (configurable base URL + model), using **SSE streaming** so responses render token-by-token in the UI.
- Maintain conversation history; render a scrollable message list (user/assistant bubbles) with a text input box and Send button.
- Handle errors gracefully (invalid key, rate limit, network) with user-visible messages.

### Voice (Phase 2)
- Push-to-talk via a dedicated hotkey. Capture mic audio with **NAudio** (`WaveInEvent`, 16 kHz mono PCM → WAV).
- Define `ISpeechToText` and `ITextToSpeech` abstractions.
  - Default STT/TTS via the AI provider's audio endpoints (e.g. transcription + TTS), using the same stored API key.
  - Provide a **Windows built-in offline fallback** using `System.Speech` / WinRT `SpeechSynthesizer` where practical.
- Play TTS audio back through NAudio. Optionally support the provider's realtime/streaming voice API for low latency (mark as stretch goal).

---

## Project structure (suggested)

```
Veil/
  Veil.sln
  src/Veil/
    App.xaml / App.xaml.cs
    Views/            (MainWindow, SettingsView, ChatView)
    ViewModels/       (MainViewModel, ChatViewModel, SettingsViewModel)
    Services/
      ScreenCaptureProtectionService.cs
      GlobalHotkeyService.cs
      SecureSettingsStore.cs      (DPAPI)
      Chat/IChatProvider.cs + OpenAiChatProvider.cs
      Voice/ISpeechToText.cs, ITextToSpeech.cs, NAudioCapture.cs
    Native/           (P/Invoke declarations)
    Models/
  README.md
```

---

## Build order (do this sequentially, verify each before moving on)

1. **Scaffold** the WPF + MVVM solution with an empty opaque always-on-top window.
2. **Implement screen-capture exclusion and PROVE it works before writing any other feature.** Show a distinctive test string in the window. Verification steps:
   - Start a Google Meet / Zoom / Teams screen share, or use OBS "Display Capture", or press `Win+Shift+S`, or `Win+G` (Game Bar) — confirm the Veil window is **absent** from the capture and the content behind it shows through, while remaining visible on the real screen.
   - Also confirm `GetWindowDisplayAffinity` returns `WDA_EXCLUDEFROMCAPTURE`.
   - Do not proceed until this passes on Windows 11.
3. **Global hotkey** show/hide.
4. **Settings + DPAPI-encrypted API key** storage.
5. **Streaming chat** against the OpenAI-compatible provider.
6. **Voice** capture, STT, TTS.
7. **Polish**: tray icon, error banners, trimmed publish, README.

---

## Acceptance criteria

- [ ] Veil's window is invisible in Zoom/Meet/Teams/OBS/`Win+Shift+S` captures; content behind it is shared normally; the window is fully visible on the physical display.
- [ ] `GetWindowDisplayAffinity` confirms the flag; failures are logged with the Win32 error and surfaced to the user.
- [ ] The window is opaque (no `AllowsTransparency`), so the flag does not fail with error 8.
- [ ] Global hotkey toggles visibility even when unfocused.
- [ ] API key is stored **encrypted** (DPAPI) and never appears in plaintext in files or logs.
- [ ] Chat responses stream token-by-token; provider/model/base URL are configurable.
- [ ] Voice push-to-talk transcribes speech and speaks replies.
- [ ] `dotnet publish -c Release -r win-x64` produces a small, runnable app; README documents the OS-version floor, RDP/VM limitations, and the "not a hard security boundary" caveat.

---

## Constraints

- Keep it lightweight — resist adding heavy dependencies or a web layer.
- No telemetry; no network calls except to the configured AI provider.
- Comment the P/Invoke and the capture-protection logic thoroughly, since that's the app's whole reason to exist.

Start with step 1, then **step 2, and demonstrate the invisibility working, before anything else.**