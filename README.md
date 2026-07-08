# raven_ai

A lightweight native **Windows** desktop AI voice + chat assistant (WPF, .NET 8, C#) whose
window is **omitted from screen captures** — invisible during screen sharing / recording
(Google Meet, Zoom, Microsoft Teams, OBS, `Win+Shift+S`, Game Bar), while remaining fully
visible on your physical monitor.

This is a data-loss-prevention feature: it keeps private assistant content out of accidental
screen shares, using the same OS mechanism (`SetWindowDisplayAffinity` with
`WDA_EXCLUDEFROMCAPTURE`) that banking apps and password managers use.

> **⚠ Not a security boundary.** See [Limitations](docs/limitations.md).

---

## Features

- **Screen-capture exclusion** via the Win32 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`
  API, applied after the HWND exists, **verified** with `GetWindowDisplayAffinity`, and
  re-applied by a watchdog. Failures are surfaced loudly in a warning banner — you never
  *think* you're protected when you aren't.
- **Full-screen transparent overlay.** The window spans the whole virtual desktop and is
  **click-through at all times** — every click and keystroke falls through to the app underneath.
  The assistant UI lives in an always-on-top floating card you can reposition.
- **Interactive mode** (`Ctrl+Shift+I`). Enter it to work the assistant *without* giving the
  overlay OS focus: a global low-level mouse hook (`WH_MOUSE_LL`) **swallows all mouse input** and
  **freezes the real pointer** (`ClipCursor`), while the app paints its own **fake cursor** driven
  by raw-input deltas and does its own hit-testing — click buttons, focus/type the chat box, drag
  the card, and scroll. `Esc` or `Ctrl+Shift+I` again exits and returns focus to the previous app.
- **Global hotkeys** (work even when unfocused):
  - `Ctrl+Shift+Space` — show / hide the overlay
  - `Ctrl+Shift+V` — push-to-talk (start/stop voice)
  - `Ctrl+Shift+I` — toggle interactive mode (capture the mouse / release it)
- **Streaming chat** against any **OpenAI-compatible** `/chat/completions` endpoint
  (configurable base URL + model), rendered token-by-token via SSE.
- **Voice**: push-to-talk mic capture (NAudio, 16 kHz mono WAV) → transcription
  (`/audio/transcriptions`) → chat → spoken reply (`/audio/speech`), with a **Windows offline
  TTS fallback** (`System.Speech`, no network).
- **API key encrypted at rest** with Windows **DPAPI** (`CurrentUser` scope), stored as a
  base64 blob in `%APPDATA%\raven_ai\settings.json`. The plaintext key is never logged or
  written to disk.

---

## Quick start

```powershell
dotnet build -c Release
dotnet run --project src\RavenAI\RavenAI.csproj
```

Needs Windows 10 build 19041+ and the .NET 8 Desktop Runtime — see
[Building & running](docs/building.md) for details, publishing, and the antivirus note.

---

## Repo metrics

Updated automatically on every push to `main` — see
[the metrics pipeline](docs/metrics-pipeline.md) for how.

![Lines of code over time](https://raw.githubusercontent.com/org-raven-ai/raven-ai.exe/metrics/metrics-loc.svg)

![Lines of code by language](https://raw.githubusercontent.com/org-raven-ai/raven-ai.exe/metrics/metrics-loc-by-language.svg)

![Commits and merged PRs over time](https://raw.githubusercontent.com/org-raven-ai/raven-ai.exe/metrics/metrics-activity.svg)

![Commits per day](https://raw.githubusercontent.com/org-raven-ai/raven-ai.exe/metrics/metrics-commits-per-day.svg)

![Code churn per day](https://raw.githubusercontent.com/org-raven-ai/raven-ai.exe/metrics/metrics-churn-per-day.svg)

![Merged PRs per week](https://raw.githubusercontent.com/org-raven-ai/raven-ai.exe/metrics/metrics-prs-per-week.svg)

---

## Documentation

| Doc | Contents |
|---|---|
| [Building & running](docs/building.md) | requirements, build/run/publish, antivirus note |
| [Verifying the invisibility](docs/verifying-invisibility.md) | how to confirm captures can't see the window |
| [Configuration](docs/configuration.md) | settings reference and where they persist |
| [Project structure](docs/project-structure.md) | solution layout and key services |
| [Limitations](docs/limitations.md) | RDP/VM caveats, security-boundary notes, interactive-mode quirks |
| [Metrics pipeline](docs/metrics-pipeline.md) | how the charts above are generated |

---

## Tech

.NET 8 (WPF) · C# · `CommunityToolkit.Mvvm` · `NAudio` · `System.Speech` · Win32 P/Invoke.
No telemetry. No network calls except to the AI provider you configure.
