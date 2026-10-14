# Blurt for Windows

A C#/.NET 10 WPF port of Blurt, the AssemblyAI dictation app, for Windows 11. It dictates
through the same AssemblyAI request and pipeline rules as the macOS app, adapted to WASAPI,
`WH_KEYBOARD_LL`, UI Automation, and the Win32 clipboard. See
[`Windows/design/2026-09-22-windows-port-design.md`](./design/2026-09-22-windows-port-design.md)
for the full design and the decisions (`D1`–`D17`) behind it, and
[`Windows/design/2026-10-13-upstream-sync-design.md`](./design/2026-10-13-upstream-sync-design.md)
for the sync to upstream v0.1.62 (`D18`–`D30`).

## Prerequisites

- Windows 10 19041 (20H1) or later, or Windows 11.
- .NET SDK 10.0.400 or later in the 10.0.4xx band (see [`global.json`](./global.json),
  `rollForward: latestFeature`). No Visual Studio required — the SDK alone builds, tests, and
  publishes.

## Build

```powershell
dotnet build Windows/Blurt.sln -warnaserror
```

## Test

```powershell
dotnet test Windows/Blurt.sln --filter "TestCategory!=Explicit"
```

The excluded `Explicit` tests are hardware/clipboard smoke tests (`*SmokeTests`) that touch the
real microphone or the real clipboard. Run them deliberately, one at a time, when you need to
verify the platform layer against real hardware:

```powershell
dotnet test Windows/Blurt.sln --filter "Category=Explicit"
```

## Format

```powershell
dotnet format Windows/Blurt.sln --verify-no-changes
```

## Run

```powershell
dotnet run --project Windows/src/Blurt.App
```

`dotnet run --no-build` does not work here: the app builds to `bin\x64\<Configuration>\...` (the
`Platforms=x64` project setting), not the `bin\<Configuration>\...` path `dotnet run` expects
without a fresh build. Omit `--no-build`, or run the built executable directly (e.g.
`Windows\src\Blurt.App\bin\x64\Debug\net10.0-windows10.0.19041.0\Blurt.exe`).

## Publish

```powershell
scripts/windows/publish.ps1 [-Configuration Release] [-SelfContained]
```

Framework-dependent builds land in `Windows/publish/fd`; self-contained, single-file builds land
in `Windows/publish/sc`.

## Debug vs. release identity

A Debug build runs under a separate identity from Release, so the two never collide on settings,
credentials, or logs:

| Identity | App data directory          | Credential Manager target | User-Agent suffix |
| -------- | --------------------------- | ------------------------- | ----------------- |
| Debug    | `%LOCALAPPDATA%\Blurt-dev\` | `blurt-dev`               | `; dev`           |
| Release  | `%LOCALAPPDATA%\Blurt\`     | `blurt`                   | (none)            |

Under the app data directory: `settings.json` holds persisted settings, and `Logs\blurt.log` is
the app's own diagnostic log. Developer mode (a setting toggled in the app) additionally writes
`Logs\dictations.jsonl` and `Logs\errors.jsonl`. Each identity holds a single instance: launching
a second copy of the same build exits immediately instead of opening a second window.

## Permissions

Windows has no Accessibility grant to request — there is no macOS-style permission prompt. The
one permission that matters is the microphone: Blurt reads consent from the Capability Access
Manager consent store, so "Let desktop apps access your microphone" (in Settings, under Privacy
& security, Microphone) must be on. The app links straight to that page when the mic probe fails.

Elevated (Run as administrator) windows cannot be pasted into from a non-elevated Blurt — User
Interface Privilege Isolation (UIPI) blocks both the low-level keyboard hook's view of an elevated
foreground window and `SendInput`'s paste keystrokes into it. Run Blurt elevated if you need to
dictate into an elevated app.

## Trigger key

The dictation trigger is a single modifier key, tapped or held. Two keys are offered (persisted
as the Windows virtual-key code):

| Key                  | Virtual-key code |
| -------------------- | ---------------- |
| Right Ctrl (default) | `0xA3`           |
| Right Shift          | `0xA1`           |

Activation modes: **Tap or hold** (default) — a short press latches recording until pressed again,
a press held past the hold threshold (1 s) records only while held; **Tap** — always latches;
**Hold** — always push-to-talk.

Right Alt and Right Win are not offered because a lone tap has a system meaning on Windows (Right
Win opens the Start menu, a lone Alt tap enters menu-bar mode in Win32 apps) and Blurt swallows no
keystrokes.

## What's new in the v0.1.62 sync

The port tracks upstream v0.1.62 (`Directory.Build.props` `Version`). The design is in
[`Windows/design/2026-10-13-upstream-sync-design.md`](./design/2026-10-13-upstream-sync-design.md).

- **Text shortcuts.** Settings → Vocabulary holds a list of phrases and their replacements,
  expanded in the pasted text. The expansion never goes to AssemblyAI: the service still hears and
  returns the spoken phrase.
- **Key terms box added.** Settings → Vocabulary has a new key terms box, beside the shortcuts.
- **Separator fix.** Dictating twice into the same window no longer adds the separator space when
  you typed or clicked in between. Blurt notices your input through its keyboard hook and a new
  mouse hook, and ignores its own pastes.
- **Experimental read-aloud.** Settings → Advanced → Experimental reads the selected text aloud,
  off by default. While it is on you can hold the trigger key to ask about the selection, pick a
  speed from 1× to 3×, and skip code, links, and long numbers. Speech comes from AssemblyAI's
  streaming text-to-speech WebSocket, whose endpoint is undocumented and may change. The jargon
  skip and the answer to a spoken question send the selection to AssemblyAI's LLM Gateway.
- **Ctrl+Shift+C hazard.** With Right Shift as the held trigger, the Ctrl+C that read-aloud sends
  to copy a selection reaches the app as Ctrl+Shift+C. Prefer Right Ctrl while read-aloud is on.
- **Footer and tray links.** The ready view footer reads "Powered by AssemblyAI · Report a bug",
  and the tray menu adds Report a Bug and Share Blurt on LinkedIn. The bug link opens this fork's
  issues, `https://github.com/ThirtyNineWest/blurt/issues`.
- **Tray hint.** The tray menu's first line follows the activation setting, for example "Hold
  right Ctrl to dictate and paste".

## How the tree maps to the Swift engine

The Swift package under `Sources/BlurtEngine/` is the behavior spec; its Swift Testing suites
under `Tests/BlurtEngineTests/` are the scenario source the C# tests port from — same state-machine
tables, same prompt fitting, same wire shape, same cancel races.

| Swift                                                                     | C# / Windows                                                                                                       |
| ------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `Sources/BlurtEngine/Audio/`                                              | `Windows/src/Blurt.Engine/Audio/`                                                                                  |
| `Sources/BlurtEngine/Config/`                                             | `Windows/src/Blurt.Engine/Config/`                                                                                 |
| `Sources/BlurtEngine/FocusCapture/`                                       | `Windows/src/Blurt.Engine/FocusCapture/`                                                                           |
| `Sources/BlurtEngine/Hotkey/`                                             | `Windows/src/Blurt.Engine/Hotkey/`                                                                                 |
| `Sources/BlurtEngine/Injection/`                                          | `Windows/src/Blurt.Engine/Injection/`                                                                              |
| `Sources/BlurtEngine/Permissions/`                                        | `Windows/src/Blurt.Engine/Permissions/`                                                                            |
| `Sources/BlurtEngine/Pipeline/`                                           | `Windows/src/Blurt.Engine/Pipeline/`                                                                               |
| `Sources/BlurtEngine/STT/`                                                | `Windows/src/Blurt.Engine/STT/`                                                                                    |
| `Sources/BlurtEngine/TTS/`                                                | `Windows/src/Blurt.Engine/TTS/`                                                                                    |
| `Sources/BlurtEngine/Update/`                                             | `Windows/src/Blurt.Engine/Update/`                                                                                 |
| AVFoundation, `CGEventTap`, AX, `NSPasteboard`, Keychain (platform seams) | `Windows/src/Blurt.Engine.Windows/` (WASAPI, `WH_KEYBOARD_LL`, UI Automation, Win32 clipboard, Credential Manager) |
| `App/Blurt/Blurt/`                                                        | `Windows/src/Blurt.App/`                                                                                           |

`Blurt.Engine` is pure pipeline logic behind the same seam interfaces the Swift engine defines
(`IMicCapture`, `ITranscriber`, `IInjector`, `IFocusCapture`, `ISettingsStore`, `IApiKeyGateway`)
and references no Windows APIs. `Blurt.Engine.Windows` implements those seams over Win32/COM/UIA.
`Blurt.App` is the WPF composition root, windows, tray, and overlay — the analog of `App/Blurt/`.

## Known limitations

This first cut trades a few macOS niceties and adds a few Windows-specific rough edges, all
tracked for later work:

- No input device picker; capture always uses the default recording device
  (`MicDeviceStore`/`AudioRouteMonitor` and Bluetooth tail-linger handling are not ported).
- No hook watchdog: if `WH_KEYBOARD_LL` is silently removed (e.g. by a slow callback getting
  unhooked by the OS), Blurt does not detect or reinstall it.
- UIA prior-text coverage varies by app. Older Win32 controls that expose no `TextPattern` or
  `ValuePattern` degrade to no prior-text priming. UWP apps hosted behind `ApplicationFrameHost`
  are skipped, for the same reason. The paste itself still works either way; only priming degrades.
- Native `MessageBox` dialogs are not excluded from focus capture the way Blurt's own windows are,
  so a stray system dialog can be captured as the "focused" target.
- A physically held Shift or Alt key combines with the paste chord (Ctrl+V), which can send an
  unintended Ctrl+Shift+V or Ctrl+Alt+V to the target app instead of a plain paste.
- `SetForegroundWindow` can be refused by Windows (e.g. the target process doesn't have input
  focus permission at that moment). When that happens the transcript is left on the clipboard and
  the dictation reports "no target" rather than silently failing.
- Every dictation's transcript passes through the system clipboard, so it also lands in Windows'
  Win+V clipboard history if the user has that turned on.
- Raw WASAPI COM interop is more code than a capture library would be, mitigated by requesting
  `AUTOCONVERTPCM` (no hand-written resampler) and an `[Explicit]` hardware smoke test.
- No caret-anchored overlay placement (UIA `TextPattern` bounding rectangles) and no per-monitor
  DPI/multi-monitor placement polish yet; the overlay defaults to bottom-center of the primary
  screen.
- Read-aloud (experimental) reads a selection through UI Automation. When UI Automation cannot
  reach a focused element at all, read-aloud falls back to sending Ctrl+C, so that app needs a
  working Ctrl+C; a bare caret or a password field never triggers the fallback. An app that copies
  the current line on an empty Ctrl+C would read that line aloud.
- The streaming text-to-speech endpoint read-aloud uses is undocumented by AssemblyAI and may
  change without notice.
- No installer (MSIX or Inno Setup), no code signing, and no release workflow yet —
  `scripts/windows/publish.ps1` output is the deliverable for this cut.

**Note for maintainers (affects the macOS engine too):** while porting the cancel race, a bug
surfaced in `Sources/BlurtEngine/Pipeline/DictationSession+Commands.swift`. In the
`.transcribing`/`.injecting` branch of `cancel()`, a cancel submitted through `submit(.cancel)`
sets the cancel-requested flag and tears the pipeline down, but that early-return branch never
clears the flag — only `performCancel()` does, and this branch returns before reaching it. The
flag then stays set, so the _next_ press reads it as an already-requested cancel and cancels
itself. The Windows port's `DictationSession` (`Windows/src/Blurt.Engine/Pipeline/DictationSession.Commands.cs`)
clears the flag in the equivalent branch.

## CI

[`.github/workflows/check-windows.yml`](../.github/workflows/check-windows.yml) runs
`dotnet format --verify-no-changes`, `dotnet build -warnaserror`, and the non-`Explicit` test
suite on `windows-latest`, triggered by pushes and pull requests that touch `Windows/**`. It is
**not** a required check.

`scripts/check.sh` (the macOS gate) does not build or test the Windows tree, but its prettier,
markdownlint, and actionlint steps run repo-wide, so they do lint this file, the design docs under
`Windows/design/`, and `check-windows.yml` itself.
