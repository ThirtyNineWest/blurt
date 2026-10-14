# Blurt for Windows — design

> The sync to upstream v0.1.62 is recorded in
> [`2026-10-13-upstream-sync-design.md`](./2026-10-13-upstream-sync-design.md) (`D18`–`D30`).

Date: 2026-09-22. Status: approved for implementation (owner pre-approved the C# direction and
delegated the remaining decisions; every judgment call is recorded below as `D<n>` so it can be
reversed).

## Goal

A Windows build of Blurt with the same user-facing behavior as the macOS app: press a lone
modifier key, speak, and polished text is pasted into the focused app via one AssemblyAI dictation
request. Same wire contract, same settled decisions, same pipeline semantics. Native Windows shell.

## Non-goals for this first cut

- Feature parity on every macOS-specific nicety (see [Follow-ups](#follow-ups-explicitly-out-of-this-cut)).
- An installer, code signing, or a release pipeline. `dotnet publish` output is the deliverable.
- Changing anything under `Sources/`, `App/`, `Tests/`, or `scripts/` (the macOS build is
  untouched; `scripts/check.sh` keeps its scope).

## Language, runtime, UI

| Decision | Choice                                                                                                                       |
| -------- | ---------------------------------------------------------------------------------------------------------------------------- |
| D1       | C# 14 on .NET 10 (`net10.0` for the pure engine, `net10.0-windows` for the platform layer and app).                          |
| D2       | WPF for the shell. `System.Windows.Forms.NotifyIcon` for the tray icon (part of the Windows Desktop framework, not a NuGet). |
| D3       | x64 only, framework-dependent by default; `scripts/windows/publish.ps1` also produces a self-contained single-file build.    |

## Dependency rule (D4)

The macOS engine is dependency-free by rule. The Windows port keeps the same posture:

- `Blurt.Engine`, `Blurt.Engine.Windows`, and `Blurt.App` reference **no NuGet packages**. Only the
  .NET BCL and the Windows Desktop framework (WPF, WinForms, `System.Windows.Automation`).
- Platform APIs (WASAPI, low-level keyboard hook, Win32 clipboard, `SendInput`, Credential Manager)
  are reached through hand-written P/Invoke and `[ComImport]` COM interfaces. No NAudio, no CsWin32.
- Test projects may reference test-time packages: NUnit 4, FluentAssertions 7.x, FakeItEasy,
  `Microsoft.Extensions.TimeProvider.Testing`, and the NUnit adapter/test SDK. FluentAssertions is
  pinned below 8 (license change).

## Solution layout

```text
Windows/
  Blurt.sln
  global.json                       .NET SDK 10.0, rollForward latestFeature
  Directory.Build.props             LangVersion latest, Nullable, ImplicitUsings, TreatWarningsAsErrors,
                                    Version 0.1.56 (tracks the macOS app's marketing version)
  Directory.Packages.props          central package versions (tests only)
  README.md                         build/run/test for the Windows port
  docs/                             this design, the implementation plan
  src/
    Blurt.Engine/                   net10.0 — pure pipeline logic behind seams (no Windows APIs)
    Blurt.Engine.Windows/           net10.0-windows — the seam implementations over Win32/COM/UIA
    Blurt.App/                      net10.0-windows WPF — composition root, windows, tray, overlay, cues
  tests/
    Blurt.Engine.Tests/             NUnit ports of the pure-logic Swift suites
    Blurt.Engine.Windows.Tests/     NUnit tests for the platform layer's pure helpers + [Explicit] hardware smoke tests
scripts/windows/publish.ps1         dotnet publish wrapper
.github/workflows/check-windows.yml dotnet build/test/format on windows-latest
```

Namespaces: `Blurt.Engine.*` mirrors the Swift folders (`Pipeline`, `Hotkey`, `STT`, `Config`,
`Update`, `Audio`, `Injection`, `FocusCapture`, `Permissions`). `Blurt.Engine.Windows.*` holds the
implementations. `Blurt.App` is the WPF shell.

## Architecture

Same three-layer shape as the Swift repo, with one extra assembly boundary so the compiler enforces
"the engine has no Windows in it":

```text
Blurt.App (WPF)  ──composes──▶  Blurt.Engine (pure)  ◀──implements seams──  Blurt.Engine.Windows
```

`Blurt.App.AppCoordinator` plays the role of the Swift `AppCoordinator`: builds the session from
`WasapiMicCapture`, `AssemblyAITranscriber`, `ClipboardInjector`, wires the keyboard hook to
`DictationSession.Submit`, renders phases to the overlay/tray/main window, and plays cues.

### Pipeline semantics (unchanged)

Press → `Connecting` → mic live → `Recording` (upload opens, chunked) → release → `Transcribing` →
`Injecting` → `Pasted` | `NoTarget`, with `Failed(BlurtError)` / `Cancelled` from any stage. All of
these rules port verbatim from `Sources/BlurtEngine/Pipeline/*.swift`:

- readiness check refuses a press before capture (`ApiKeyMissing` is a setup blocker, rendered idle);
- `Connecting` is claimed before the mic is up, cues fire on the edge into `Recording`;
- auto-release at `SyncSTTLimits.AutoReleaseSeconds` (115 s);
- clips under `SyncSTTLimits.MinPcmBytes` are dropped as a silent no-op;
- one request per utterance, `config` part first, audio streamed as captured, no buffered fallback;
- context (recent dictations + prior text) is awaited at most `ContextWaitBudget` (500 ms) before
  the request opens; a miss sends the press-known half (key terms, history) without focus signals;
- cancel wins deterministically over a release racing `mic.StopAsync()`; a cancel during
  transcribe/inject tears the pipeline down and nothing is pasted;
- injector quiet degradations (`TargetAppLost`, `NoEditableTarget` thrown by the injector) become
  `NoTarget`; the transcript is already on the clipboard;
- a dictation into a secure (password) field is pasted but never remembered as history;
- developer-mode JSONL logs (`dictations.jsonl`, `errors.jsonl`) with the same line shapes.

### Wire contract (unchanged)

`POST https://dictation.assemblyai.com/v1/transcribe/live`, multipart: `config` JSON part
(`sample_rate`, `channels`, `stt_prompt` when non-empty, `keyterms_prompt` when non-empty,
`llm_instruction`), then `audio` part `audio.pcm` `audio/pcm` as raw S16LE 16 kHz mono, chunked
transfer (no `Content-Length`). `Authorization: <key>` (no Bearer). `User-Agent:
Blurt/<version> (Windows <major>.<minor>.<build>[; dev])`. Response `text` / `llm_response` /
`llm_error` / `session_id` / metrics; error bodies `{error}` or `{detail}`. `GET /warm` warm-up.
Caps: `STTPrompt` 4096 Unicode scalars (count `Rune`s, not UTF-16 units), `KeytermsBoost` 2048
UTF-8 bytes and 100 terms, `CleanupInstruction` 2048 UTF-8 bytes. The cleanup instruction text is
copied byte-for-byte from `Sources/BlurtEngine/STT/CleanupInstruction.swift`. Key validation:
`GET https://api.assemblyai.com/v2/transcript?limit=1`, 2xx valid, 400/401/403/422 invalid,
everything else unreachable.

Settled decisions in `AGENTS.md` apply to this code too: no streaming STT, no client-side cleanup,
no local models, no `language_code(s)`, no `prompt` beside `stt_prompt`, no `conversation_context`,
no widening of the context past history + prior text, no filler-word clause, no keystroke-typing
injector, no self-replacing updater.

## Seams (the C# contracts)

Defined in `Blurt.Engine`. Everything the app composes is one of these.

```csharp
namespace Blurt.Engine.Audio;
public interface IMicCapture
{
    /// Starts capture and returns the live S16LE 16 kHz mono feed. Does not return until the
    /// device is delivering non-silent frames (liveness gate) or the gate times out (throws).
    Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct);
    /// Stops capture; the feed ends. Returns bytes captured.
    Task<int> StopAsync();
    /// Stops and discards without any tail linger.
    Task CancelCaptureAsync();
    /// 0…1 meter, ~20 Hz while recording. Default: empty.
    IAsyncEnumerable<float> Levels { get; }
    /// Pay set-up cost early; never opens the device. Default: no-op.
    Task WarmUpAsync();
}

namespace Blurt.Engine.STT;
public interface ITranscriber
{
    Task<string> TranscribeAsync(IAsyncEnumerable<byte[]> frames, int sampleRate,
        TranscriptionContext? context, CancellationToken ct);
    Task WarmUpAsync(); // default no-op
}

namespace Blurt.Engine.Injection;
public interface IInjector
{
    Task SetTargetAsync(FocusTarget? target);
    /// Clipboard paste: save → write → Ctrl+V → settle → restore. Throws BlurtError.TargetAppLost /
    /// NoEditableTarget after leaving the text on the clipboard.
    Task InsertAsync(string text, string? priorText, string? windowTitle, CancellationToken ct);
}

namespace Blurt.Engine.FocusCapture;
public sealed record FocusTarget(nint WindowHandle, int ProcessId, string? ProcessName);
public sealed record FocusedFieldContext(string? WindowTitle, string? FieldLabel, string? PriorText,
    string? SelectedText, bool IsSecure);
public interface IFocusCapture
{
    FocusTarget? CaptureForeground();               // cheap
    FocusedFieldContext CaptureFieldContext();      // may block up to seconds; session bounds it
}

namespace Blurt.Engine;
public interface IHttpTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct);
}

namespace Blurt.Engine.Config;
public interface ISettingsStore   // UserDefaults analog
{
    string? GetString(string key); int? GetInt(string key); bool? GetBool(string key); double? GetDouble(string key);
    void Set(string key, string value); void Set(string key, int value); void Set(string key, bool value); void Set(string key, double value);
    void Remove(string key);
}
public interface IApiKeyGateway { string? Current { get; } bool Save(string key); bool HasKey { get; } }
```

`DictationSession` (`Blurt.Engine.Pipeline`):

```csharp
public sealed class DictationSession : IAsyncDisposable
{
    public DictationSession(IMicCapture mic, ITranscriber transcriber, IInjector injector,
        IFocusCapture focus, DictationLog log, TimeProvider? clock = null,
        double maxRecordingSeconds = SyncSTTLimits.AutoReleaseSeconds,
        Func<IReadOnlyList<string>>? keyTermsProvider = null,
        Func<string?>? styleNameProvider = null,
        Func<BlurtError?>? readinessCheck = null,
        Action<string, RecentDictations>? onTranscriptDelivered = null);
    public PipelinePhase Phase { get; }
    public event Action<PipelinePhase>? PhaseChanged;      // raised in order from one dispatcher thread
    public IAsyncEnumerable<PipelinePhase> PhaseStream(CancellationToken ct); // current phase first
    public Task PressAsync(); public Task ReleaseAsync(); public Task CancelAsync(); public Task CancelRecordingAsync();
    public void Submit(Command command);                   // FIFO, non-blocking; Cancel preempts an in-flight press
    public static readonly TimeSpan ContextWaitBudget = TimeSpan.FromMilliseconds(500);
}
public enum Command { Press, Release, Cancel, CancelRecording }
```

Concurrency model: a single consumer loop over an unbounded `Channel<QueuedCommand>` runs commands
serially (the Swift actor's serial queue). Session state is guarded by one `lock`; `SetPhase` is the
only writer of `Phase` and the only place a failure is logged. `CancelAsync`'s synchronous branches
(phase `Transcribing`/`Injecting`/`Connecting`) act under the lock without queueing, exactly as the
Swift `cancel()` does before its `enqueue`. `Submit(Cancel)` records the cancel intent and cancels
the in-flight press's `CancellationTokenSource` before enqueueing.

`PipelinePhase` and `BlurtError` are sealed record hierarchies (`PipelinePhase.Idle`,
`.Connecting`, `.Recording`, `.Transcribing`, `.Injecting`, `.Failed(BlurtError)`, `.Cancelled`,
`.Pasted`, `.NoTarget`; `BlurtError.ApiKeyMissing`, `.SttFailed(Exception)`, `.TargetAppLost`,
`.AudioCaptureFailed(Exception)`, `.NoEditableTarget`). Wrapped-exception equality compares the
exception type and `HResult`, not the message. `AccessibilityPermissionMissing` is dropped: Windows
has no such grant (D5). Projections `OverlayUIState`, `TrayStatus` (the `MenuBarStatus` analog),
`RecordingCueGate`, `SetupBlocker` port as-is.

## Windows seam mapping

| macOS                                            | Windows implementation (`Blurt.Engine.Windows`)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | Decision |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| `MicCapture` over `AVCaptureSession`             | `WasapiMicCapture`: fresh `IAudioClient` per capture on the default capture endpoint, shared mode, event-driven, `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM \| SRC_DEFAULT_QUALITY` requesting 16 kHz/mono/16-bit so no resampling code exists. Liveness gate: `StartAsync` returns once a frame arrives with RMS above `MicLiveness.SilenceFloorDb` (−115 dBFS; −50 dBFS is the meter floor, `LevelMeter.FloorDb`), capped at 2.5 s, failing closed. `Levels` = ~20 Hz dBFS→0…1 meter. `WarmUpAsync` activates and releases an `IAudioClient` without `Initialize`. No tail linger. | D6       |
| `CGEventTap` lone-modifier trigger               | `LowLevelKeyboardHook` (`WH_KEYBOARD_LL`) on a dedicated STA thread with a message loop. Reduces each event to `DictationKeyRouter.Event` (`FlagsChanged(vk, isDown)` for the trigger vk, `KeyDown(vk)` for others; injected events with our `SendInput` tag are ignored). Swallows nothing (`CallNextHookEx` always). Callback does no I/O beyond `Submit`.                                                                                                                                                                                                                  | D7       |
| `TriggerKey` right ⌘ / right ⌥                   | `TriggerKey` = `RightControl` (0xA3, default), `RightShift` (0xA1). Persisted as the VK code; an unknown code (including 0xA5 or 0x5C) decodes to `RightControl`. Right Alt and Right Win are not offered because a lone tap has a system meaning on Windows and Blurt swallows no keystrokes. Labels: "right Ctrl", "Right Control (Ctrl)", etc.                                                                                                                                                                                                                             | D8       |
| `FocusCapture` over AX                           | `UiaFocusCapture`: foreground `HWND` + PID + process name via Win32; `AutomationElement.FocusedElement` for field label (`Name` → `HelpText` → `LocalizedControlType`), prior text (TextPattern selection start, back ≤320 chars; else `ValuePattern.Value` tail), selected text (≤320), window title (`GetWindowText` of the top-level window, ≤120), `IsSecure = IsPassword`, fail-closed when the role is unreadable. Editability: ControlType Edit/Document/ComboBox, or ValuePattern not read-only, or TextPattern present.                                              | D9       |
| `isAXOpaqueApp` (Electron / browsers)            | `ChromiumHost.IsOpaque(processPath)`: `chrome_100_percent.pak` beside the exe or one directory down (Chromium, Electron), or a known browser process name (`firefox`, `chrome`, `msedge`, `brave`, `opera`, `vivaldi`, `arc`). Paste anyway on no editable signal for these.                                                                                                                                                                                                                                                                                                  | D10      |
| `KeyInjector` + `NSPasteboard` + ⌘V              | `ClipboardInjector` over `IClipboardAccess` (`Win32Clipboard`): snapshot every HGLOBAL-backed format (skip synthesized/handle-based ones and delayed-render nulls), write `CF_UNICODETEXT`, `GetClipboardSequenceNumber` as the change count, `SendInput` Ctrl+V tagged with a magic `dwExtraInfo`, 400 ms settle in a chained background task, restore only if the sequence number is unchanged. Target activation: `SetForegroundWindow` then poll ≤350 ms; `IsWindow` false → `TargetAppLost`.                                                                             | D11      |
| Keychain `APIKeyStore`                           | `CredentialManagerApiKeyStore`: generic credential, target `blurt` (release) / `blurt-dev` (debug), `CRED_PERSIST_LOCAL_MACHINE`. Tests use an isolated target name and delete it.                                                                                                                                                                                                                                                                                                                                                                                            | D12      |
| `UserDefaults`                                   | `JsonSettingsStore` at `%LOCALAPPDATA%\Blurt\settings.json` (`Blurt` prefix kept on keys). Atomic write via temp file + `File.Move(overwrite)`.                                                                                                                                                                                                                                                                                                                                                                                                                               | D13      |
| `~/Library/Logs/Blurt`                           | `%LOCALAPPDATA%\Blurt\Logs\dictations.jsonl`, `errors.jsonl`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | D13      |
| `PermissionsChecker` (mic + Accessibility)       | `PermissionStatus { Microphone }` only. `MicrophoneAccess.Probe()` initializes and starts/stops an `IAudioClient` on the default endpoint; `E_ACCESSDENIED` → denied (Settings > Privacy > Microphone, "Let desktop apps access your microphone"). Opener: `ms-settings:privacy-microphone`. No Accessibility grant exists; UIPI means elevated windows cannot be pasted into (documented limitation).                                                                                                                                                                        | D5       |
| `NSPanel` overlay                                | WPF `OverlayWindow`: `Topmost`, `ShowInTaskbar=false`, `WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`, transparent, draggable, origin persisted via `OverlayOriginStore`, default bottom-center of the primary screen.                                                                                                                                                                                                                                                                                                                                                                | D14      |
| Dock app + `MenuBarExtra`                        | Normal taskbar main window + `NotifyIcon` tray (idle / recording / transcribing icons, menu: Open, Settings, Check for Updates, Quit). Nothing depends on the tray for correctness.                                                                                                                                                                                                                                                                                                                                                                                           | D2       |
| `CueSoundPlayer` (.m4a)                          | WPF `MediaPlayer`; the `.m4a` cues are linked from `App/Blurt/Blurt/Resources/Sounds/` at build time (no copies in git). The catalog JSON is generated once from `App/Blurt/Blurt/SoundPackCatalog.swift`.                                                                                                                                                                                                                                                                                                                                                                    | D15      |
| `UpdateChecker` (DMG asset)                      | Same GitHub latest-release check. Download URL = the first asset whose name ends with `.msi`, `.exe`, or contains `win` and ends with `.zip`; **fallback to the release's `html_url`** when no Windows asset exists (the release page is still the right place to send the user). Download-only, once a day, never over an unfinished wizard.                                                                                                                                                                                                                                 | D16      |
| `OverlayPlacement`, `MeterBarGeometry`           | Port as pure math.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |          |
| `MicDeviceStore`, `AudioRouteMonitor`, BT linger | Not in this cut (default capture device only).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | D17      |

## App shell (WPF)

- **Main window.** Wizard until ready, then the ready view. Steps: API key (validate-then-save via
  `ApiKeySubmission`), Microphone (probe + "Open Privacy Settings" + "Test"), Shortcut (trigger key
  - activation picker), Sound (voice picker with preview). Ready view: how-to line with the bound
    key, listening state, style switcher (Default + up to 4 profiles), recent dictations (3 rows,
    height reserved), Settings button, "Cancel dictation" while capturing.
- **Settings window.** General: shortcut, activation, sound. Advanced: enhanced transcripts, custom
  styles (add/edit/delete, byte counter against `StyleProfileStore.CharacterLimit`), updates
  (check now, last checked), developer mode (switch + both log paths), reset install.
- **Overlay.** Pill states per `OverlayUIState`: connecting (breathing "Connecting…"), recording
  (`● REC` + meter bars), processing ("Transcribing…"), pasted / copied / error notices with
  `NoticeDwellSeconds`, then hidden.
- **Tray.** `TrayStatus` icon + menu. Quit exits the app; closing the main window hides it.
- **Startup.** Composition in `App.OnStartup`; single-instance mutex; the hook installs when the
  app becomes ready (same as the macOS tap, though no permission gates it here).

## Persistence keys

Same `DefaultsKey` raw values under the `Blurt` prefix: `TriggerKeyCode`, `TriggerActivation`,
`SoundPack`, `KeyTerms`, `DeveloperMode`, `EnhancedTranscripts`, `CustomStyle`, `StyleProfiles`,
`ActiveStyleProfile`, `OverlayCustomOriginX/Y`, `LastUpdateCheck`. `PersistedSettings.ResetAll`
sweeps the enum. Stores have setters (WPF has no `@AppStorage`; the Swift "read-only by design"
rationale was SwiftUI-specific).

## Testing

- NUnit 4 + FluentAssertions 7 + FakeItEasy, `FakeTimeProvider` for clocks. Setup, action, and
  assertion separated by blank lines; no Arrange/Act/Assert comments; `FluentActions.Awaiting` for
  exception assertions.
- The Swift suites under `Tests/BlurtEngineTests/` are the spec for the engine port. Every pure
  suite has a C# counterpart with the same scenarios (the state-machine tables, the prompt fitting,
  the wire shape, the cancel races, the readiness refusals, the projections).
- Stubs port as classes: `StubMicCapture`, `StubTranscriber`, `StubInjector`, `FakeHttpTransport`,
  `FakeClipboard`, `InMemorySettingsStore`, `InMemoryApiKeyStore`, gated mics via
  `TaskCompletionSource`.
- Platform layer: pure helpers (hook event reduction, clipboard format filtering, Chromium
  detection, level math) are unit-tested; device-touching tests are `[Explicit]` and named
  `*SmokeTests`.
- CI: `.github/workflows/check-windows.yml` on `windows-latest`: `dotnet format --verify-no-changes`,
  `dotnet build -warnaserror`, `dotnet test` excluding `Explicit`. Pinned action SHAs like the
  existing workflows. Not a required check.

## Follow-ups (explicitly out of this cut)

1. Input device selection (`MicDeviceStore`) and device-change handling.
2. Bluetooth tail linger and per-transport liveness timeouts.
3. Hook watchdog for a silently removed `WH_KEYBOARD_LL` hook.
4. Caret-anchored overlay placement via UIA `TextPattern` bounding rectangles.
5. Installer (MSIX or Inno Setup), Authenticode signing, release workflow, Windows release assets.
6. Per-monitor DPI/multi-monitor overlay placement polish.
7. UIA coverage differs by app: older Win32 controls expose no text pattern, so prior-text
   priming degrades to empty there. Measure and consider `IAccessible` fallbacks.
8. Right Alt with a swallow-own-key-up design.

## Risks

- R1 UIA prior-text coverage varies by app (see follow-up 7). The paste still works; only priming
  degrades.
- R2 Raw WASAPI COM interop is more code than a library. Mitigated by `AUTOCONVERTPCM` (no
  resampler) and an explicit hardware smoke test run on the development machine.
- R3 UIPI: `SendInput` and the hook cannot reach elevated windows from a non-elevated Blurt.
  Documented; running Blurt elevated is the workaround.
