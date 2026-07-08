# Building & running

## Requirements

- **Windows 10, version 2004 (build 19041)** or newer for full invisibility
  (`WDA_EXCLUDEFROMCAPTURE`). On older builds the app falls back to `WDA_MONITOR`, which shows
  the window as a **black box** in captures (content hidden, but the box is visible) and warns
  you.
- **.NET 8 Desktop Runtime** (or newer) — a framework-dependent build. The repo builds with the
  .NET SDK 8+ (developed against SDK 10).
- An **OpenAI-compatible API key** for chat/voice (offline TTS works without one).

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

## Small self-contained publish (single file, win-x64)

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
