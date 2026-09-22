# Blurt for Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A C#/.NET 10 WPF build of Blurt under `Windows/` that dictates through the same AssemblyAI request, with the same pipeline rules, on Windows 11.

**Architecture:** Three assemblies. `Blurt.Engine` (`net10.0`) is the pure pipeline behind seam interfaces, ported file-by-file from `Sources/BlurtEngine/`. `Blurt.Engine.Windows` (`net10.0-windows`) implements the seams over WASAPI, `WH_KEYBOARD_LL`, UI Automation, the Win32 clipboard, `SendInput`, and Credential Manager. `Blurt.App` (WPF) composes them and renders phases to an overlay, a tray icon, and a main/settings window.

**Tech Stack:** C# 14, .NET SDK 10.0.4xx, WPF + WinForms (`NotifyIcon`), `System.Windows.Automation`, hand-written P/Invoke and `[ComImport]` COM. Tests: NUnit 4, FluentAssertions 7.x, FakeItEasy, `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** `Windows/design/2026-09-22-windows-port-design.md` (read it first; the decisions `D1`–`D17` there are binding).

## Global Constraints

- **The Swift source is the behavior spec.** Every task names the Swift files and test suites it ports. Read them before writing C#. Port the tests' scenarios, not just their names. Where the Swift is macOS-only plumbing (AX, CGEvent, NSPasteboard), the design doc's mapping table replaces it.
- **No NuGet references in `src/` projects.** `Blurt.Engine`, `Blurt.Engine.Windows`, `Blurt.App` use only the BCL and the Windows Desktop framework. Test projects use only: `NUnit` 4.x, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` 7.x (never 8), `FakeItEasy`, `Microsoft.Extensions.TimeProvider.Testing`.
- **Build settings** (`Windows/Directory.Build.props`): `LangVersion latest`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`, `Version 0.1.56`, `EnforceCodeStyleInBuild true`. `dotnet format --verify-no-changes Windows/Blurt.sln` must pass. 4-space indent for C#, file-scoped namespaces, LF line endings.
- **Constants (exact):** sample rate `16000`; channels `1`; bytes/sample `2`; min audio `0.1` s → `MinPcmBytes = 3200`; max audio `120` s; auto-release margin `5` s → `AutoReleaseSeconds = 115`; `ContextWaitBudget = 500 ms`; paste settle `400 ms`; target activation wait `350 ms`; hold threshold `1 s`; liveness silence floor `-50 dBFS`; liveness timeout `2.5 s`; meter `20 Hz`; `STTPrompt.CharacterCap = 4096` Unicode scalars (`Rune` count); `KeytermsBoost.CharacterCap = 2048` UTF-8 bytes, `TermCap = 100`; `CleanupInstruction.CharacterCap = 2048` UTF-8 bytes; prior text `320` chars, selected text `320`, window title `120`, field label `80`; `RecentDictations.Capacity = 100`, `DisplayCapacity = 3`, `JustNowThreshold = 60 s`; `AutomaticUpdateCheck.MinimumInterval = 24 h`, `LaunchDelay = 3 s`; `SetupReadiness` poll `1 s` setting up / `5 s` ready; notice dwell `0.8 s` pasted, `1.6 s` error/noTarget; `StyleProfileStore.ProfileLimit = 4`, `NameLimit = 24`, `DefaultStyleId = "default"`, `DefaultStyleName = "Default"`, legacy profile name `"Custom"`, legacy id `UUID` bytes of ASCII `blurtstylelegacy`; `TriggerActivation` raw values `"TapOrHold"`, `"Tap"`, `"Hold"`; default trigger VK `0xA3` (`RightControl`).
- **Wire contract:** exactly as the design doc's "Wire contract" section. Never send `prompt`, `conversation_context`, `language_code(s)`, `keyterms`, `word_boost`, `llm`. Omit `stt_prompt` when empty and `keyterms_prompt` when empty. `llm_instruction` rides every request.
- **Settled decisions** in `AGENTS.md` bind this code: no streaming STT, no client-side cleanup, no local models, no keystroke-typing injector, no self-replacing updater, no filler-word clause.
- **Comments:** state what the code does or the invariant it protects, timelessly. No change provenance, no "this exists to", no references to plans or reviews. Doc comments say what a member does/guarantees/returns/throws.
- **Tests:** separate setup, action, and assertion with blank lines; no `// Arrange` comments; `FluentActions.Awaiting`/`Invoking` for exception assertions; never touch the real Credential Manager entry `blurt`/`blurt-dev` or the real `%LOCALAPPDATA%\Blurt` in tests.
- **Do not modify** anything under `Sources/`, `App/`, `Tests/`, `scripts/` (other than the new `scripts/windows/`), `.github/workflows/check.yml`, `Package.swift`, `evals/`. The macOS build is untouched.
- **Commits:** Conventional Commits (`feat(windows): …`, `test(windows): …`, `chore(windows): …`), imperative, lowercase after the colon, under 72 chars, ending with the trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Commit after each green test cycle.
- **Verification before claiming done:** `dotnet build Windows/Blurt.sln -warnaserror` and `dotnet test Windows/Blurt.sln --filter "TestCategory!=Explicit"` both green, output quoted in the report.

---

## Task 1: Solution scaffold, CI, publish script

**Files:**

- Create: `Windows/Blurt.sln`, `Windows/global.json`, `Windows/Directory.Build.props`, `Windows/Directory.Packages.props`, `Windows/.editorconfig`
- Create: `Windows/src/Blurt.Engine/Blurt.Engine.csproj`, `Windows/src/Blurt.Engine.Windows/Blurt.Engine.Windows.csproj`, `Windows/src/Blurt.App/Blurt.App.csproj` (+ minimal `App.xaml`/`App.xaml.cs`/`MainWindow.xaml`/`MainWindow.xaml.cs` that compile)
- Create: `Windows/tests/Blurt.Engine.Tests/Blurt.Engine.Tests.csproj` (+ `SmokeTests.cs`), `Windows/tests/Blurt.Engine.Windows.Tests/Blurt.Engine.Windows.Tests.csproj` (+ `SmokeTests.cs`)
- Create: `.github/workflows/check-windows.yml`, `scripts/windows/publish.ps1`, `Windows/README.md` (stub: how to build/test; Task 15 completes it)
- Modify: `.gitignore` (append a `# Windows port` block), `.editorconfig` (append C#/XAML blocks)

**Interfaces:**

- Produces: the build. `Blurt.Engine` → no references. `Blurt.Engine.Windows` → `ProjectReference Blurt.Engine`, `UseWPF true`, `TargetFramework net10.0-windows10.0.19041.0`, `AllowUnsafeBlocks true`, `Platforms x64`. `Blurt.App` → references both, `OutputType WinExe`, `UseWPF true`, `UseWindowsForms true`, `ApplicationManifest` with `dpiAware PerMonitorV2`, `RuntimeIdentifier win-x64`, `SelfContained false`, `AssemblyName Blurt`. Test projects reference their `src` twin and the packages above.

- [ ] **Step 1: Write `global.json`, `Directory.Build.props`, `Directory.Packages.props`**

```json
{ "sdk": { "version": "10.0.400", "rollForward": "latestFeature" } }
```

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <Version>0.1.56</Version>
    <Company>Blurt</Company>
    <Product>Blurt</Product>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props` pins: `NUnit` `4.*` latest stable, `NUnit3TestAdapter` latest 5.x, `Microsoft.NET.Test.Sdk` latest 17.x, `FluentAssertions` `7.*` (highest 7.x on nuget.org; **not 8**), `FakeItEasy` latest 8.x, `Microsoft.Extensions.TimeProvider.Testing` latest 9.x/10.x. Run `dotnet package search <id> --take 3` (or `dotnet add package … --prerelease false` then read the version back) to find current versions; do not guess.

- [ ] **Step 2: Create the five projects and the solution**

Use `dotnet new sln -n Blurt`, `dotnet new classlib`, `dotnet new wpf`, `dotnet new nunit`, then edit each csproj to the settings in **Interfaces**. Delete the template `Class1.cs`/`UnitTest1.cs`. Add `Windows/src/Blurt.App/app.manifest` enabling `PerMonitorV2` DPI awareness and reference it via `<ApplicationManifest>`. Add each project to the solution.

- [ ] **Step 3: Write `Windows/.editorconfig`**

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
charset = utf-8
csharp_style_namespace_declarations = file_scoped:warning
dotnet_sort_system_directives_first = true
csharp_prefer_braces = true:warning

[*.{csproj,props,targets,xaml,sln,json}]
indent_style = space
indent_size = 2
end_of_line = lf
insert_final_newline = true
```

Also append to the repo root `.editorconfig` a `[*.{cs,xaml,csproj,props}]` block with `end_of_line = lf`, `insert_final_newline = true`, `charset = utf-8` (indent sizes stay in `Windows/.editorconfig`, which is the nearer file).

- [ ] **Step 4: Smoke tests**

`Windows/tests/Blurt.Engine.Tests/SmokeTests.cs`:

```csharp
using FluentAssertions;
using NUnit.Framework;

namespace Blurt.Engine.Tests;

public sealed class SmokeTests
{
    [Test]
    public void TestProjectBuildsAndRuns()
    {
        var value = 1 + 1;

        value.Should().Be(2);
    }
}
```

Same for `Blurt.Engine.Windows.Tests`. Add `Windows/tests/Blurt.Engine.Windows.Tests/ExplicitCategory.cs`: `public static class Categories { public const string Explicit = "Explicit"; }` for the hardware smoke tests later tasks add (`[Explicit, Category(Categories.Explicit)]`).

- [ ] **Step 5: Run the build and tests**

Run: `dotnet build Windows/Blurt.sln -warnaserror` then `dotnet test Windows/Blurt.sln` then `dotnet format Windows/Blurt.sln --verify-no-changes`.
Expected: all green.

- [ ] **Step 6: `.gitignore`, CI workflow, publish script, README stub**

Append to `.gitignore`:

```gitignore
# Windows port (Windows/)
bin/
obj/
.vs/
*.user
TestResults/
Windows/publish/
```

`.github/workflows/check-windows.yml` (mirror `check.yml`'s style: `permissions: contents: read`, concurrency group, pinned SHAs):

```yaml
name: check-windows

on:
  pull_request:
    branches: [main]
    paths: ["Windows/**", ".github/workflows/check-windows.yml"]
  push:
    branches: [main]
    paths: ["Windows/**", ".github/workflows/check-windows.yml"]

concurrency:
  group: check-windows-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read

jobs:
  windows:
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
      - name: Set up .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
          global-json-file: Windows/global.json
      - name: Format
        run: dotnet format Windows/Blurt.sln --verify-no-changes
      - name: Build
        run: dotnet build Windows/Blurt.sln -c Release -warnaserror
      - name: Test
        run: dotnet test Windows/Blurt.sln -c Release --no-build --filter "TestCategory!=Explicit"
```

`scripts/windows/publish.ps1`: parameters `-Configuration Release`, `-SelfContained` switch; runs `dotnet publish Windows/src/Blurt.App/Blurt.App.csproj -c $Configuration -r win-x64 -o Windows/publish/<fd|sc>` with `-p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` when `-SelfContained`; prints the output folder. Use `$ErrorActionPreference = 'Stop'` and `Set-StrictMode -Version Latest`.

`Windows/README.md` stub: title, one paragraph on what it is, `Build`, `Test`, `Run` (`dotnet run --project Windows/src/Blurt.App`), `Publish` sections. Keep lines under 100 characters; Task 15 finishes it. Run `npx --yes prettier@3 --check .github/workflows/check-windows.yml Windows/README.md` and fix anything it reports.

- [ ] **Step 7: Commit**

```bash
git add Windows .github/workflows/check-windows.yml scripts/windows .gitignore .editorconfig
git commit -m "chore(windows): scaffold the .NET solution, CI, and publish script"
```

---

## Task 2: Core types and seams

Port the value types every other task depends on. Everything here is pure `net10.0`.

**Files:**

- Create in `Windows/src/Blurt.Engine/`: `HostIdentity.cs`, `StringNormalization.cs`, `Diagnostics/BlurtLog.cs`, `IHttpTransport.cs`, `BlurtError.cs`, `Pipeline/PipelinePhase.cs`, `Pipeline/OverlayUIState.cs`, `Pipeline/TrayStatus.cs`, `Pipeline/RecordingCueGate.cs`, `Pipeline/RecentDictations.cs`, `STT/SyncSTTLimits.cs`, `STT/TranscriptionContext.cs`, `STT/ITranscriber.cs`, `STT/AudioTooShortException.cs`, `Audio/IMicCapture.cs`, `Injection/IInjector.cs`, `FocusCapture/IFocusCapture.cs`, `FocusCapture/FocusTarget.cs`, `FocusCapture/FocusedFieldContext.cs`, `Config/DefaultsKey.cs`, `Config/ISettingsStore.cs`, `Config/InMemorySettingsStore.cs`, `Config/IApiKeyGateway.cs`, `Config/InMemoryApiKeyStore.cs`, `Permissions/MicrophoneAccessState.cs`
- Create in `Windows/tests/Blurt.Engine.Tests/`: `Stubs/StubMicCapture.cs`, `Stubs/StubTranscriber.cs`, `Stubs/StubInjector.cs`, `Stubs/StubFocusCapture.cs`, `Stubs/StubPcm.cs`, and the suites listed in Step 3.

**Swift references:** `HostIdentity.swift`, `StringNormalization.swift`, `HTTPTransport.swift`, `BlurtError.swift`, `Pipeline/PipelinePhase.swift`, `Pipeline/OverlayUIState.swift`, `Pipeline/MenuBarStatus.swift`, `Pipeline/RecordingCueGate.swift`, `Pipeline/RecentDictations.swift`, `STT/SyncSTTLimits.swift`, `STT/TranscriptionContext.swift`, `STT/TranscriberProtocol.swift`, `Audio/MicCaptureProtocol.swift`, `Injection/InjectorProtocol.swift`, `Config/APIKeyGateway.swift`; tests `BlurtErrorTests`, `PipelinePhaseTests`, `OverlayUIStateTests`, `MenuBarStatusTests`, `RecordingCueGateTests`, `RecentDictationsTests`, `SyncSTTLimitsTests`, `StringNormalizationTests`, `TranscriptionContextTests`, `HostIdentityTests`, `APIKeyGatewayTests`, `MicCaptureProtocolDefaultsTests`; stubs `Stubs/StubMicCapture.swift`, `StubTranscriber.swift`, `StubInjector.swift`, `StubPCM.swift`.

**Interfaces (produces — later tasks depend on these exact shapes):**

```csharp
namespace Blurt.Engine;
public sealed record HostIdentity(string ProductName, string Subsystem, string CredentialTarget,
    string SettingsPrefix, string LogDirectoryName, Uri ReleaseUrl)
{
    public static readonly HostIdentity Blurt = new("Blurt", "dev.alex.blurt", "blurt", "Blurt", "Blurt",
        new Uri("https://api.github.com/repos/AssemblyAI/blurt/releases/latest"));
    public static readonly HostIdentity BlurtDev = Blurt with { CredentialTarget = "blurt-dev" };
    public static HostIdentity Current { get; }          // process-wide, defaults to Blurt
    public static void Configure(HostIdentity identity);
    public string SettingsKey(string suffix) => SettingsPrefix + suffix;
}
public static class StringNormalization
{
    public static string? TrimmedNonEmpty(this string? value);
    public static string PrefixMaxUtf8Bytes(this string value, int maxBytes); // whole text elements (StringInfo), never splits a grapheme
}
public interface IHttpTransport { Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct); }
public sealed class HttpClientTransport(HttpClient client) : IHttpTransport { /* forwards with HttpCompletionOption.ResponseHeadersRead */ }

public abstract record BlurtError
{
    public sealed record ApiKeyMissing : BlurtError;
    public sealed record SttFailed(Exception Underlying) : BlurtError;
    public sealed record TargetAppLost : BlurtError;
    public sealed record AudioCaptureFailed(Exception Underlying) : BlurtError;
    public sealed record NoEditableTarget : BlurtError;
    public string Description { get; }        // the Swift errorDescription strings, "Accessibility" case omitted
    public string DiagnosticName { get; }     // "apiKeyMissing", "sttFailed", "targetAppLost", "audioCaptureFailed", "noEditableTarget"
    public bool IsSetupBlocker { get; }       // ApiKeyMissing only
    public bool IsQuietDegradation { get; }   // NoEditableTarget, TargetAppLost
    // Equality: wrapped exceptions compare by GetType() and HResult (override Equals/GetHashCode on the two wrapping records).
}
public sealed class BlurtException(BlurtError error) : Exception(error.Description) { public BlurtError Error { get; } }

namespace Blurt.Engine.Pipeline;
public abstract record PipelinePhase
{
    public static readonly PipelinePhase Idle, Connecting, Recording, Transcribing, Injecting, Cancelled, Pasted, NoTarget;
    public sealed record FailedPhase(BlurtError Error) : PipelinePhase;
    public static PipelinePhase Failed(BlurtError error) => new FailedPhase(error);
    public bool IsTerminal { get; } public bool IsCapturing { get; } public BlurtError? SetupBlocker { get; }
    public OverlayUIState OverlayState { get; } public TrayStatus TrayStatus { get; }
}
public abstract record OverlayUIState
{
    public static readonly OverlayUIState Idle, Connecting, Recording, Processing, Pasted, NoTarget;
    public sealed record ErrorState(string Message) : OverlayUIState;
    public static OverlayUIState Error(string message) => new ErrorState(message);
    public string AccessibilityLabel { get; }   // the Swift strings
    public double? NoticeDwellSeconds { get; }  // 0.8 pasted, 1.6 error/noTarget, null otherwise
}
public enum TrayStatus { Idle, Recording, Transcribing }
public static class TrayStatuses { public static string AccessibilityLabel(this TrayStatus s) /* "Blurt — idle" … */; public static string IconName(this TrayStatus s) /* "idle"/"recording"/"transcribing" */; }
public enum RecordingCue { Start, Stop }
public struct RecordingCueGate { public RecordingCue? Cue(PipelinePhase phase); }   // mutable struct, one edge bit
public sealed class RecentDictations   // mutable class mirroring the Swift struct; the session owns one and pushes copies via Snapshot()
{
    public const int Capacity = 100; public const int DisplayCapacity = 3;
    public sealed record Entry(Guid Id, string Text, DateTimeOffset Timestamp, string? Style)
    { public const double JustNowThresholdSeconds = 60; public string RelativeLabel(DateTimeOffset now); }
    public IReadOnlyList<Entry> Entries { get; } public IReadOnlyList<Entry> Displayed { get; }
    public IReadOnlyList<string> TranscriptsOldestFirst { get; }
    public void Record(string text, string? style, DateTimeOffset at);
    public RecentDictations Snapshot();
    public static double ReservedHeight(double rowHeight, double separatorThickness);
}

namespace Blurt.Engine.STT;
public static class SyncSTTLimits { SampleRate=16000; MaxAudioSeconds=120; MinAudioSeconds=0.1; MinSamples; BytesPerSample=2; ChannelCount=1; BitDepth=16; MinPcmBytes=3200; PcmBytesForSeconds(double); DurationMs(int byteCount, int rate = SampleRate); AutoReleaseMargin=5; AutoReleaseSeconds=115 }
public sealed record TranscriptionContext(string? AppName, string? WindowTitle, string? FieldLabel, string? PriorText,
    string? SelectedText, IReadOnlyList<string> RecentTranscripts, IReadOnlyList<string> KeyTerms, bool TargetIsSecure)
{ public bool IsEmpty { get; } /* same rule as Swift */ }
public interface ITranscriber { Task<string> TranscribeAsync(IAsyncEnumerable<byte[]> frames, int sampleRate, TranscriptionContext? context, CancellationToken ct); Task WarmUpAsync() => Task.CompletedTask; }
/// Thrown by a transcriber when the recording ended below SyncSTTLimits.MinPcmBytes; the session maps it to a quiet Idle.
public sealed class AudioTooShortException : Exception { public AudioTooShortException() : base("That recording was too short to transcribe.") { } }

namespace Blurt.Engine.Config;
public enum DefaultsKey { TriggerKeyCode, TriggerActivation, SoundPack, KeyTerms, DeveloperMode, EnhancedTranscripts, CustomStyle, StyleProfiles, ActiveStyleProfile, OverlayCustomOriginX, OverlayCustomOriginY, LastUpdateCheck }
public static class DefaultsKeys { public static string RawValue(this DefaultsKey key) /* the enum name */; public static string Key(this DefaultsKey key) => HostIdentity.Current.SettingsKey(key.RawValue()); public static string Key(this DefaultsKey key, HostIdentity identity); public static IReadOnlyList<DefaultsKey> All { get; } }

namespace Blurt.Engine.Permissions;
public enum MicrophoneAccessState { Granted, Denied, NoDevice, Unknown }

namespace Blurt.Engine.Audio;
public interface IMicCapture
{
    Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct);
    Task<int> StopAsync();
    Task CancelCaptureAsync() => StopAsync();          // default: stop and drop
    IAsyncEnumerable<float> Levels => AsyncEnumerable.Empty<float>();
    Task WarmUpAsync() => Task.CompletedTask;
}

namespace Blurt.Engine.FocusCapture;
public sealed record FocusTarget(nint WindowHandle, int ProcessId, string? ProcessName, string? ProcessPath);
public sealed record FocusedFieldContext(string? WindowTitle, string? FieldLabel, string? PriorText, string? SelectedText, bool IsSecure)
{ public static readonly FocusedFieldContext Empty = new(null, null, null, null, false); }
public interface IFocusCapture { FocusTarget? CaptureForeground(); FocusedFieldContext CaptureFieldContext(); }

namespace Blurt.Engine.Injection;
public interface IInjector { Task SetTargetAsync(FocusTarget? target); Task InsertAsync(string text, string? priorText, string? windowTitle, CancellationToken ct); }

namespace Blurt.Engine.Config;
public interface ISettingsStore { string? GetString(string key); int? GetInt(string key); bool? GetBool(string key); double? GetDouble(string key);
    void Set(string key, string value); void Set(string key, int value); void Set(string key, bool value); void Set(string key, double value); void Remove(string key); }
public sealed class InMemorySettingsStore : ISettingsStore { /* Dictionary<string, object> */ }
public interface IApiKeyGateway { string? Current { get; } bool Save(string key); bool HasKey => Current.TrimmedNonEmpty() is not null; }
public sealed class InMemoryApiKeyStore : IApiKeyGateway { /* Save trims; blank deletes; returns true */ }

namespace Blurt.Engine.Diagnostics;
public static class BlurtLog { public static Action<string>? Sink { get; set; } /* default Trace.WriteLine */ public static void Info(string category, string message); Warn; Error; }
```

Note on `RecentDictations`: make it a mutable class with `Snapshot()` returning a deep copy (the session pushes snapshots to the app). `Entry.RelativeLabel`: "just now" under 60 s, else `"{n} minute(s)/hour(s)/day(s) ago"` (no `RelativeDateTimeFormatter` on .NET; a small formatter with singular/plural is fine).

- [ ] **Step 1: Write the failing tests** — port each Swift suite named above (plus `DefaultsKeyTests`) into `Windows/tests/Blurt.Engine.Tests/<Folder>/<Name>Tests.cs`. Include every scenario, e.g. from `PipelinePhaseTests`: `IsTerminal` table over all nine phases; `SetupBlocker` non-null only for `Failed(ApiKeyMissing)`; from `OverlayUIStateTests`: `Injecting → Processing`, `Connecting → Connecting`, setup blocker → `Idle`, dwell seconds; from `RecordingCueGateTests`: start only on the edge into Recording, stop on the edge out, nil while repeating; from `RecentDictationsTests`: newest-first, capacity 100, displayed 3, `TranscriptsOldestFirst`, `ReservedHeight(rowHeight, sep) == 3*rowHeight + 2*sep`, relative label; from `StringNormalizationTests`: `PrefixMaxUtf8Bytes` drops a whole emoji rather than splitting it; from `TranscriptionContextTests`: `IsEmpty` is false when only `TargetIsSecure` is true.
- [ ] **Step 2: Run them** — `dotnet test Windows/tests/Blurt.Engine.Tests` → compile failures/red.
- [ ] **Step 3: Implement** every type in **Interfaces** until green.
- [ ] **Step 4: Port the stubs** (`StubMicCapture` with a `Channel<byte[]>` feed that yields `PcmToReturn` on start and completes on stop/cancel, counting calls; `StubTranscriber` returning a canned transcript or throwing, recording the frames it drained and the context it received; `StubInjector` recording inserts; `StubFocusCapture` returning configured values; `StubPcm.AboveMinimum` = `new byte[SyncSTTLimits.MinPcmBytes + 2]`, `BelowMinimum`).
- [ ] **Step 5: `dotnet format`, full build, full test, commit** — `feat(windows): port the engine's core types and seams`.

---

## Task 3: Hotkey state machine and trigger settings

**Files:** create `Windows/src/Blurt.Engine/Hotkey/TriggerKey.cs`, `TriggerActivation.cs`, `DictationKeyGate.cs`, `DictationKeyRouter.cs`, `TriggerKeyStore.cs`, `TriggerActivationStore.cs`; tests `Hotkey/TriggerKeyTests.cs`, `TriggerActivationTests.cs`, `DictationKeyGateTests.cs`, `DictationKeyRouterTests.cs`, `TriggerKeyStoreTests.cs`.

**Swift references:** `Hotkey/*.swift`; tests `TriggerKeyTests`, `TriggerActivationTests`, `DictationKeyGateTests` (port the whole scenario table with `[TestCaseSource]`), `DictationKeyRouterTests`, `TriggerKeyStoreTests`.

**Interfaces (consumes):** `DefaultsKey` / `DefaultsKeys` and `ISettingsStore` from Task 2.

**Interfaces (produces):**

```csharp
namespace Blurt.Engine.Hotkey;
public enum TriggerKey { RightControl = 0xA3, RightAlt = 0xA5, RightShift = 0xA1, RightWindows = 0x5C }
public static class TriggerKeys { public static TriggerKey FromPersisted(int code) /* default RightControl */; public static int KeyCode(this TriggerKey); public static string Label(this TriggerKey) /* "right Ctrl", "right Alt", "right Shift", "right Win" */; public static string FullName(this TriggerKey) /* "Right Control (Ctrl)", "Right Alt", "Right Shift", "Right Windows (Win)" */; public static IReadOnlyList<TriggerKey> All; }
public enum TriggerActivation { TapOrHold, Tap, Hold }
public static class TriggerActivations { RawValue ("TapOrHold"/"Tap"/"Hold"); FromPersisted(string?) default TapOrHold; Label ("Tap or hold"/"Tap"/"Hold"); Guidance (the three Swift sentences); internal static bool LatchesOnRelease(this TriggerActivation, TimeSpan held, TimeSpan holdThreshold); }
public struct DictationKeyGate
{
    public enum Action { None, Start, Stop, Cancel }
    public DictationKeyGate(TimeSpan? holdThreshold = null, TriggerActivation activation = TriggerActivation.TapOrHold);
    public TimeSpan HoldThreshold { get; } public TriggerActivation Activation { get; } public bool IsIdle { get; }
    public Action ModifierDown(TimeSpan now); public Action ModifierUp(TimeSpan now); public Action OtherKeyDown(); public void Reset();
}
public struct DictationKeyRouter
{
    public abstract record Event { public sealed record FlagsChanged(int KeyCode, bool TriggerFlagIsOn) : Event; public sealed record KeyDown(int KeyCode) : Event; }
    public DictationKeyRouter(int triggerKeyCode, TimeSpan? holdThreshold = null, TriggerActivation activation = TriggerActivation.TapOrHold);
    public int TriggerKeyCode { get; }
    public DictationKeyGate.Action Handle(Event e, TimeSpan now);
    public bool Rebind(int triggerKeyCode, TriggerActivation activation);   // returns "discarded a live recording"
    public bool RecoverFromDroppedEvents(bool triggerStillHeld);
    public bool Reset();
}
public sealed class TriggerKeyStore(ISettingsStore store) { public static string DefaultsKey => …; public TriggerKey TriggerKey { get; set; } }
public sealed class TriggerActivationStore(ISettingsStore store) { public TriggerActivation Activation { get; set; } }
```

Steps: write the tests from the Swift suites (the gate's scenario table verbatim, including the `activation` variants), run red, implement, green, `dotnet format`, commit `feat(windows): port the trigger key state machine`.

---

## Task 4: AssemblyAI transcriber and prompt builders

**Files:** create `Windows/src/Blurt.Engine/STT/STTPrompt.cs`, `KeytermsBoost.cs`, `CleanupInstruction.cs`, `DictationWireTypes.cs`, `DictationMultipart.cs`, `AssemblyAIError.cs`, `AssemblyAITranscriber.cs`, `DictationWarmUp.cs` (extension or partial), `StreamedMultipartContent.cs`, `UserAgent.cs`; tests `STT/STTPromptTests.cs`, `KeytermsBoostTests.cs`, `KeytermsWireTests.cs`, `CleanupInstructionTests.cs`, `AssemblyAITranscriberTests.cs`, `AssemblyAITranscriberMultipartTests.cs`, `ChunkedUploadTests.cs`, `UserAgentTests.cs`; stub `Stubs/FakeHttpTransport.cs`.

**Swift references:** `STT/STTPrompt.swift`, `KeytermsBoost.swift`, `CleanupInstruction.swift` (copy `text` byte-for-byte, including the paragraph breaks), `DictationWireTypes.swift`, `DictationMultipart.swift`, `AssemblyAITranscriber.swift`, `DictationWarmUp.swift`, `UserAgent.swift`; tests of the same names plus `KeytermsWireTests`, `ChunkedUploadTests`, `AssemblyAITranscriberMultipartTests`; `Stubs/FakeHTTPTransport.swift`.

**Interfaces (produces):**

```csharp
namespace Blurt.Engine.STT;
public static class STTPrompt { public const int CharacterCap = 4096; public const string TurnSeparator = "\n"; public static string Text(TranscriptionContext? context); }
public static class KeytermsBoost { public const int CharacterCap = 2048; public const int TermCap = 100; public static IReadOnlyList<string> Fitted(IEnumerable<string> terms); }
public static class CleanupInstruction { public const int CharacterCap = 2048; public static readonly string Text; public static readonly string? Sendable; public static string? Sendable(string? customStyle); public static readonly string CustomStylePreamble; public static readonly int CustomStyleBudget; }
public sealed class AssemblyAIError : Exception { public static AssemblyAIError Http(int status, string? message); public static AssemblyAIError MalformedResponse(); public int? Status { get; } public string? ServerMessage { get; } public AssemblyAIErrorKind Kind { get; } }
public enum AssemblyAIErrorKind { Http, MalformedResponse }
// The too-short floor throws Task 2's Blurt.Engine.STT.AudioTooShortException, not an AssemblyAIError.
public sealed class AssemblyAITranscriber : ITranscriber
{
    public AssemblyAITranscriber(Func<string?> apiKeyProvider, IHttpTransport transport, Uri? baseUrl = null /* https://dictation.assemblyai.com */, Func<bool>? enhancedTranscripts = null /* default true */, Func<string?>? customStyle = null /* default null */);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);
    internal static byte[] MakeConfigData(int sampleRate, string sttPrompt, IReadOnlyList<string> keytermsPrompt, string? llmInstruction);
    internal static string? ErrorMessage(byte[] body);
    internal static string Transcript(DictationResponse response, bool enhanced);
}
public static class UserAgent { public const string HeaderField = "User-Agent"; public static string Current { get; } /* Blurt/0.1.56 (Windows 10.0.26200[; dev]) */; public static string Value(string product, string? version, IReadOnlyList<string> details); }
```

Key implementation notes:

- `STTPrompt.Text`: the dedupe walk (`withoutTurnsAlreadyEnding`) and `fitted` count `Rune`s (`s.EnumerateRunes().Count()`); a single over-long turn is clipped to its **tail** in runes.
- `StreamedMultipartContent : HttpContent`: `TryComputeLength` returns `false` (chunked); `SerializeToStreamAsync` writes `DictationMultipart.ConfigHead`, `AudioPartHeader`, each frame as it arrives (counting bytes), then either the closing boundary or throws `AudioTooShortException` when fewer than `MinPcmBytes` arrived (the transcriber lets it propagate unwrapped so the session can recognize it). The transcriber POSTs with `HttpCompletionOption.ResponseHeadersRead`, reads the body, maps non-2xx to `AssemblyAIError.Http(status, ErrorMessage(body))`, decodes `DictationResponse` with `System.Text.Json` (all fields optional except `text`; a missing `text` → `MalformedResponse`), logs `session_id` and metrics through `BlurtLog`. `ApiKeyMissing` → `throw new BlurtException(new BlurtError.ApiKeyMissing())` before any request.
- `Transcript`: enhanced → `llm_response` trimmed-non-empty, else `text` (log `llm_error`); not enhanced → `text`.
- `WarmUpAsync`: `GET {baseUrl}/warm`, swallow every exception, 5 s timeout via a linked CTS.
- `FakeHttpTransport`: records requests, drains the request content into a `byte[]` (so multipart tests can assert the exact framing and the config JSON), returns canned responses in order; can be gated with a `TaskCompletionSource` to test cancellation mid-upload.
- `UserAgent.Current`: product from `HostIdentity.Current.ProductName`, version from `typeof(UserAgent).Assembly.GetName().Version` formatted `major.minor.build`, details `["Windows {Environment.OSVersion.Version.Major}.{Minor}.{Build}", "dev" (#if DEBUG)]`.

Steps: tests red → implement → green → `dotnet format` → commit `feat(windows): port the AssemblyAI dictation transcriber and prompt builders`.

---

## Task 5: Settings stores, API key flow, readiness

**Files:** create `Windows/src/Blurt.Engine/Audio/SoundPack.cs`, `SoundPackCatalog.cs`, `SoundPackStore.cs`; `Config/KeyTermsStore.cs`, `DeveloperModeStore.cs`, `EnhancedTranscriptsStore.cs`, `StyleProfileStore.cs`, `PersistedSettings.cs`, `ApiKeyValidator.cs`, `ApiKeySubmission.cs`, `ApiKeyDisplay.cs`; `Update/LastUpdateCheckStore.cs`; `Pipeline/OverlayOriginStore.cs`; `Permissions/PermissionStatus.cs`, `SetupReadiness.cs`; tests for each (`SoundPackTests`, `SoundPackStoreTests`, `KeyTermsStoreTests`, `DeveloperModeStoreTests`, `EnhancedTranscriptsStoreTests`, `StyleProfileStoreTests`, `PersistedSettingsTests`, `APIKeyValidatorTests`, `APIKeySubmissionTests`, `APIKeyDisplayTests`, `LastUpdateCheckStoreTests`, `OverlayOriginStoreTests`, `SetupReadinessTests`).

**Swift references:** the same-named files under `Sources/BlurtEngine/{Audio,Config,Update,Pipeline,Permissions}/` and their test suites. `KeyTermsStore.parse` (comma split, trim, drop blanks, case-insensitive dedupe keeping first spelling). `StyleProfileStore` including the legacy `CustomStyle` read-side migration, `Normalized`, `Active(profiles, rawId)`, `ActivateDefault`. `ApiKeyValidator` status mapping (2xx valid; 400/401/403/422 invalid; everything else unreachable; blank key invalid without a request). `ApiKeySubmission.Submit` never persists an unverified key; `FailureReport` strings verbatim (the `saveFailed` alert says "Windows Credential Manager" instead of "macOS Keychain"). `ApiKeyDisplay.Resolve(key)` masking rules.

**Interfaces (produces):** constructor shape `new XStore(ISettingsStore store)`; `PermissionStatus` = `record PermissionStatus(bool Microphone) { bool AllGranted => Microphone; bool LostGrant(PermissionStatus since); }`; `SetupReadiness.IsReady(PermissionStatus, bool hasApiKey)`, `PollInterval(bool isReady)`; `SoundPackCatalog(IReadOnlyList<SoundPack> voices, string defaultVoiceId)` with `Groups`, `Voices(group)`, `FromPersisted(string?)`, `SoundPack.None` reserved id `"none"`; `ApiKeyValidator(IHttpTransport transport, Uri? baseUrl = null)` → `Task<ApiKeyValidationResult> ValidateAsync(string key)`; `ApiKeySubmission(IApiKeyGateway store, Func<string, Task<ApiKeyValidationResult>> validate)` → `Task<ApiKeySubmissionOutcome> SubmitAsync(string key)`, `bool Save(string key)`, `FailureReport? FailureReport(outcome)`; `PersistedSettings.ResetAll(ISettingsStore store)` removes every `DefaultsKey`.

Steps: tests red → implement → green → `dotnet format` → commit `feat(windows): port the settings stores and API key flow`.

---

## Task 6: Update check and overlay geometry

**Files:** create `Windows/src/Blurt.Engine/Update/SemanticVersion.cs`, `GitHubRelease.cs`, `UpdateChecker.cs`, `AutomaticUpdateCheck.cs`, `UpdateAlertContent.cs`; `Pipeline/OverlayPlacement.cs`, `MeterBarGeometry.cs`; tests `SemanticVersionTests`, `UpdateCheckerTests`, `AutomaticUpdateCheckTests`, `UpdateAlertContentTests`, `OverlayPlacementTests`, `MeterBarGeometryTests`.

**Swift references:** `Update/*.swift`, `Pipeline/OverlayPlacement.swift`, `Pipeline/MeterBarGeometry.swift`, and their suites.

**Interfaces (produces):** `SemanticVersion.TryParse(string, out SemanticVersion)` accepting a leading `v`, `IComparable<SemanticVersion>`, equality with zero-extension; `UpdateChecker(IHttpTransport transport, Uri? releaseUrl = null)` → `Task<UpdateCheckResult> CheckAsync(SemanticVersion current)` where `UpdateCheckResult` = `UpToDate | Available(SemanticVersion Version, Uri DownloadUrl)`; per **D16** the download URL is the first asset whose lowercase name ends with `.msi` or `.exe`, or contains `win` and ends with `.zip`, else the release's `html_url` (decode it from the JSON); throw `UpdateCheckException` on malformed JSON or an unparseable tag. `AutomaticUpdateCheck.ShouldRun(bool isConfigured, DateTimeOffset? lastCheck, DateTimeOffset now)`, `LaunchDelay`. `UpdateAlertContent` strings port with "DMG" → "download" wording. `OverlayPlacement.PanelSize(pillSize, shadowMargin)`, `ClampedOrigin(...)` etc. and `MeterBarGeometry`/`MeterBarRow` as pure math with the same constants (`BarWidth`, `BarSpacing`, `BreathingOpacity(time, period, minOpacity)`, `Height(index, level, time, animated)`).

Steps: tests red → implement → green → `dotnet format` → commit `feat(windows): port the update check and overlay geometry`.

---

## Task 7: DictationSession, PressContext, DictationLog

The heart of the port. Port slowly and keep the Swift file open beside you.

**Files:** create `Windows/src/Blurt.Engine/Pipeline/DictationSession.cs` (+ `DictationSession.Commands.cs`, `DictationSession.Press.cs`, `DictationSession.Pipeline.cs` as partial classes), `PressContext.cs`, `DictationLog.cs`, `DictationLog.Errors.cs`, `DictationPipelineError.cs`; tests `Pipeline/DictationSessionTests.cs`, `DictationSessionBringUpTests.cs`, `DictationSessionContextTests.cs`, `DictationSessionGuardTests.cs`, `DictationSessionReadinessTests.cs`, `DictationSessionSubmitTests.cs`, `DictationSessionTranscriptTests.cs`, `CancelRaceTests.cs`, `ReleaseRaceTests.cs`, `HotkeyRaceTests.cs`, `ContextWaitTests.cs`, `DictationLogTests.cs`, `DictationErrorLogTests.cs`; stubs `Stubs/GatedStartMic.cs`, `Stubs/GatedStopMic.cs`, `Stubs/DictationSessionTestSupport.cs`.

**Swift references:** `Pipeline/DictationSession*.swift`, `PressContext.swift`, `DictationLog.swift`, `DictationLog+Errors.swift`; the thirteen test suites named above plus `Stubs/Gate.swift`, `GatedStartMic.swift`, `GatedStopMic.swift`, `DictationSessionTestSupport.swift`, `TestClock.swift` (use `FakeTimeProvider`).

**Interfaces:** consumes only Task 2 (seams, `AudioTooShortException` → quiet `Idle`, `BlurtException`, `RecentDictations`, `TranscriptionContext`). It must not reference Task 3–6 types: key terms, style name, readiness, and the developer-mode switch all arrive as injected `Func`s. `STTPrompt.Text`/`KeytermsBoost.Fitted` (Task 4) are needed by `DictationLog.MakeEntry`: declare a tiny internal `IPromptRenderer`-free approach instead — `DictationLog` takes `Func<TranscriptionContext?, string> renderPrompt` and `Func<IReadOnlyList<string>, IReadOnlyList<string>> fitKeyterms` in its constructor; the app passes `STTPrompt.Text` and `KeytermsBoost.Fitted`, tests pass identity functions. Produces the `DictationSession` API in the design doc, plus:

```csharp
public sealed class DictationLog
{
    public DictationLog(Func<bool> isEnabled, string directory, Func<TranscriptionContext?, string> renderPrompt, Func<IReadOnlyList<string>, IReadOnlyList<string>> fitKeyterms);   // <directory>/dictations.jsonl, errors.jsonl
    public string DictationsPath { get; } public string ErrorsPath { get; }
    public void Append(string transcript, TranscriptionContext? context);      // gated, off-thread (a single-consumer Channel), stamped before the hop
    public void AppendError(BlurtError error, TranscriptionContext? context);
    public bool RemoveStoredLogs();
    internal static JsonObject MakeEntry(string transcript, TranscriptionContext? context, DateTimeOffset now);   // keys: transcript, ts (ISO 8601 with fractional seconds), app, window, field, prior, selected, stt_prompt (omit when empty), keyterms (omit when empty) — sorted keys
    internal static JsonObject MakeErrorEntry(BlurtError error, TranscriptionContext? context, DateTimeOffset now); // kind, description, ts, app, window, field — no prior/selected/prompt
    public Task DrainAsync();   // tests await pending writes
}
```

Concurrency translation (follow exactly):

1. `Submit(command)` → if `Cancel`, call `RequestCancel()` (sets `_cancelRequested = true` under `_gate` and cancels `_inFlightPressCts` if set), then `_commands.Writer.TryWrite`. One consumer loop (`Task.Run` in the constructor) reads commands and runs `PressAsync/ReleaseAsync/CancelAsync/CancelRecordingAsync` serially.
2. `PressAsync()` chains onto the serial queue (a `Task _queueTail` guarded by `_gate`, each op awaits the previous tail); registers its `CancellationTokenSource` as the in-flight press before awaiting; clears it after if still its own.
3. `PerformPress`: guard `Phase.IsTerminal`; `readinessCheck()` → `SetPhase(Failed(blocker))`; `SetPhase(Connecting)`; fire-and-forget `transcriber.WarmUpAsync()`; start `mic.StartAsync(pressCts.Token)` **without awaiting**; `BeginContextCapture()` (capture foreground → `injector.SetTargetAsync`, read key terms + history → `PressContext`, run `focus.CaptureFieldContext()` on `Task.Run` and `press.Store(...)`); await the mic start; if `_cancelRequested || token cancelled` → `mic.CancelCaptureAsync()`, consume the request or `SetPhase(Cancelled)`; else `SetPhase(Recording)`, `StartUpload(frames)`, arm the auto-release timer (`clock.Delay(maxRecordingSeconds)` then `ReleaseAsync()`). `OperationCanceledException` from the mic → `Cancelled`; any other exception → `Failed(AudioCaptureFailed(ex))`.
4. `StartUpload(frames)`: `_upload = Task.Run(async () => { var resolved = await press.WaitAsync(ContextWaitBudget, clock, ct); return await transcriber.TranscribeAsync(frames, SampleRate, resolved ?? press.PressKnown, uploadCts.Token); })`.
5. `PerformRelease`: guard `Phase == Recording`; cancel auto-release; `SetPhase(Transcribing)`; `await mic.StopAsync()` (on throw: if cancel won → return; else `Failed(AudioCaptureFailed)`); if cancel won → return; if `bytes < MinPcmBytes` → `SetPhase(Idle)`; else `_pipeline = Task.Run(RunTranscribeInject)`.
6. `CancelAsync`: under `_gate`: if `Transcribing`/`Injecting` → cancel `_pipelineCts`, `SetPhase(Cancelled)`, return; if `Connecting` → `RequestCancel()`, `SetPhase(Cancelled)`, return; else `RequestCancel()` then enqueue `PerformCancel` (clears the request; if `Recording` → `StopAndCancel`).
7. `StopAndCancel`: cancel auto-release; `CancelUpload()`; `await mic.CancelCaptureAsync()` (log a failure, never surface it); `SetPhase(Cancelled)`.
8. `SetPhase(phase)`: under `_gate` assign, then outside the lock raise `PhaseChanged` and publish to every `PhaseStream` channel; on any terminal phase call `CancelUpload()`; on `Failed(err)` call `log.AppendError(err, CapturedContext)`.
9. `RunTranscribeInject`: `await AwaitUpload()` (null → return; cancellation-shaped → leave the phase; `AudioTooShortException` → `Idle`; `BlurtException` → `Failed(its error)`; else `Failed(SttFailed(ex))`); trimmed-empty transcript → `Idle`; `log.Append`; record into `RecentDictations` unless `CapturedContext?.TargetIsSecure == true`; `onTranscriptDelivered(trimmed, snapshot)`; `Inject(text)`: `SetPhase(Injecting)`, `injector.InsertAsync(text, CapturedContext?.PriorText, CapturedContext?.WindowTitle, pipelineCts.Token)` → `Pasted`; `BlurtException` with `IsQuietDegradation` → `NoTarget`, other `BlurtException` → `Failed(err)`, `OperationCanceledException` → leave phase, any other exception → `Failed(TargetAppLost)`.
10. `PressContext`: `PressKnown`, `Resolved`, `Store(resolved)` (value before signal), `WaitAsync(budget, clock, ct)` returns the value if it lands within the budget else `null`; second caller gets `null`.

Tests to port (all of them): the happy path (`press → connecting → recording → release → transcribing → injecting → pasted`), refused press with `ApiKeyMissing`, too-short clip idles quietly, injector `NoEditableTarget` → `NoTarget`, transcriber failure → `Failed(SttFailed)`, cancel during recording (mic `CancelCaptureAsync` called, nothing transcribed), cancel during transcribing (nothing injected, phase stays `Cancelled`), cancel racing a release inside `StopAsync` (use `GatedStopMic`), `Submit` ordering (`press` then `cancel` from the same thread never strands a recording), auto-release at 115 s via `FakeTimeProvider.Advance`, context wait budget miss sends `PressKnown` (key terms + history) and still exposes `Resolved` later, secure target not remembered, `DictationLog` entry shapes (sorted keys, omitted fields), errors log only for `Failed` phases, developer mode off writes nothing.

Steps: tests red → implement → green → `dotnet format` → commit `feat(windows): port the dictation session pipeline and developer-mode log`.

---

## Task 8: WASAPI microphone capture

**Files:** create `Windows/src/Blurt.Engine.Windows/Audio/Interop/MmDeviceApi.cs` (COM interfaces + GUIDs), `Audio/Interop/AudioClient.cs`, `Audio/WasapiMicCapture.cs`, `Audio/MicLiveness.cs`, `Audio/LevelMeter.cs`, `Audio/MicCaptureException.cs`, `Permissions/MicrophoneAccess.cs` (`public static MicrophoneAccessState Probe()`: activate + `Initialize` + `Start` + `Stop` on the default endpoint; `E_ACCESSDENIED` → `Denied`, no endpoint → `NoDevice`, success → `Granted`, anything else → `Unknown`); tests `Windows/tests/Blurt.Engine.Windows.Tests/Audio/LevelMeterTests.cs`, `MicLivenessTests.cs`, `WasapiMicCaptureSmokeTests.cs` (`[Explicit]`).

**Swift references:** `Audio/MicCapture.swift`, `MicCapture+Meter.swift`, `MicLiveness.swift`, `CaptureSessionRecorder.swift`, `MicCaptureError.swift`; tests `MicLivenessTests`, `MicCaptureLevelsTests`, `MicCaptureFormatTests`.

**Interfaces:** implements `IMicCapture`. Produces `public sealed class WasapiMicCapture : IMicCapture` with a parameterless constructor and an internal constructor taking `TimeProvider` and the liveness timeout for tests; `public static class MicLiveness { public const double SilenceFloorDb = -50; public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5); public static bool IsLive(ReadOnlySpan<byte> s16le) /* RMS above floor */; }`; `internal static class LevelMeter { public static double DbFs(ReadOnlySpan<byte> s16le); public static float Normalized(double dbFs) /* floor -50 → 0, 0 → 1 */; }`.

COM shapes to declare (`[ComImport, Guid(...), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]`, methods in vtable order, `[PreserveSig]` returning `int` HRESULTs where a failure is expected):

- `CLSID_MMDeviceEnumerator` `BCDE0395-E52F-467C-8E3D-C4579291692E`; `IMMDeviceEnumerator` `A95664D2-9614-4F35-A746-DE8DB63617E6` (`EnumAudioEndpoints`, `GetDefaultAudioEndpoint(EDataFlow, ERole, out IMMDevice)`, `GetDevice`, `RegisterEndpointNotificationCallback`, `UnregisterEndpointNotificationCallback`); `IMMDevice` `D666063F-1587-4E43-81F1-B948E807363F` (`Activate(ref Guid iid, uint clsCtx, IntPtr params, out object)`, `OpenPropertyStore`, `GetId`, `GetState`); `IAudioClient` `1CB9AD4C-DBFA-4c32-B178-C2F568A703B2` (`Initialize(shareMode, streamFlags, bufferDuration, periodicity, ref WaveFormatEx, ref Guid session)`, `GetBufferSize`, `GetStreamLatency`, `GetCurrentPadding`, `IsFormatSupported`, `GetMixFormat`, `GetDevicePeriod`, `Start`, `Stop`, `Reset`, `SetEventHandle`, `GetService(ref Guid, out object)`); `IAudioCaptureClient` `C8ADBD64-E71E-48a0-A4DE-185C395CD317` (`GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition)`, `ReleaseBuffer(uint)`, `GetNextPacketSize(out uint)`).
- Flags: `AUDCLNT_SHAREMODE_SHARED = 0`; `AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000`, `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000`, `AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000`; `AUDCLNT_BUFFERFLAGS_SILENT = 2`; `eCapture = 1`, `eConsole = 0`; `CLSCTX_ALL = 23`.
- `WAVEFORMATEX` for the request: tag `1` (PCM), channels `1`, rate `16000`, avg bytes `32000`, block align `2`, bits `16`, `cbSize 0`.
- HRESULTs to map: `E_ACCESSDENIED 0x80070005` → `MicCaptureException.AccessDenied`; `AUDCLNT_E_DEVICE_INVALIDATED 0x88890004` and `E_NOTFOUND 0x80070490` → `NoDevice`; anything else → `Failed(hresult)`.

Capture loop: a dedicated MTA thread per capture (`new Thread(...) { IsBackground = true }`, `CoInitializeEx(COINIT_MULTITHREADED)`), event-driven with a `ManualResetEvent`/`EventWaitHandle` passed to `SetEventHandle`, 100 ms buffer duration. Each packet: `GetBuffer` → copy `frames * 2` bytes (or zeros when `SILENT`) into a fresh `byte[]` → `channel.Writer.TryWrite` → `ReleaseBuffer`. Track `_bytesCaptured`. Every 50 ms push `LevelMeter.Normalized(DbFs(lastPacket))` to the levels channel. `StartAsync` starts the thread, then awaits a `TaskCompletionSource` completed by the first packet with `MicLiveness.IsLive(...)`, racing the 2.5 s timeout and `ct`; a miss tears the thread down and throws `MicCaptureException.NotLive` (the session wraps it in `AudioCaptureFailed`); cancellation throws `OperationCanceledException`. `StopAsync`/`CancelCaptureAsync` signal the thread to stop, join it, complete the frames channel, and return `_bytesCaptured`. `WarmUpAsync` activates `IAudioClient` on the default endpoint and releases it without `Initialize`.

Smoke test (`[Explicit]`): start, wait 500 ms, stop; assert `bytes >= 16000 * 2 / 2` and that at least one non-zero sample was seen. Run it once on the development machine and paste the output in the report.

Steps: pure tests red → implement helpers → green; implement the capture; run the explicit smoke test; `dotnet format`; commit `feat(windows): capture the microphone through WASAPI`.

---

## Task 9: Low-level keyboard hook

**Files:** create `Windows/src/Blurt.Engine.Windows/Hotkey/Interop/User32Hook.cs`, `Hotkey/HookEventReducer.cs`, `Hotkey/LowLevelKeyboardHook.cs`, `Hotkey/DictationKeyHook.cs`; tests `Hotkey/HookEventReducerTests.cs`.

**Swift reference:** `App/Blurt/Blurt/Hotkey/DictationKeyTap.swift` (the lifecycle and the reduction; the decisions live in `DictationKeyRouter` from Task 3).

**Interfaces (produces):**

```csharp
namespace Blurt.Engine.Windows.Hotkey;
public static class HookEventReducer
{
    public const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    public const uint LlkhfInjected = 0x10, LlkhfUp = 0x80;
    public static readonly nuint InjectedTag = 0x424C5254; // "BLRT": SendInput events carrying it are ours and ignored
    public static DictationKeyRouter.Event? Reduce(int message, int vkCode, uint flags, nuint extraInfo, int triggerKeyCode);
}
public sealed class LowLevelKeyboardHook : IDisposable   // owns the STA thread + message loop; raises RawKey(message, vk, flags, extraInfo) on that thread
public sealed class DictationKeyHook : IDisposable
{
    public DictationKeyHook(TriggerKeyStore keys, TriggerActivationStore activation, Action onStart, Action onStop, Action onCancel, Action onRecordingDiscarded, TimeProvider? clock = null);
    public bool IsInstalled { get; } public bool EnsureRunning(); public void RefreshBinding(); public void SyncAfterTerminalPhase(); public void Dispose();
}
```

`Reduce`: injected with our tag → `null`; `vk == trigger` and key-down message → `FlagsChanged(vk, true)`; `vk == trigger` and key-up → `FlagsChanged(vk, false)`; other vk key-down → `KeyDown(vk)`; other key-up → `null`. `DictationKeyHook.Handle` reads `clock.GetTimestamp()`-based elapsed `TimeSpan` and dispatches `router.Handle(...)` to the four callbacks; `RefreshBinding` and `SyncAfterTerminalPhase` mirror the Swift methods (`onRecordingDiscarded` when the router reports a discarded recording). The hook callback must return `CallNextHookEx(...)` on every path and must never block.

Steps: reducer tests red → implement → green; implement the hook and a manual check (run a tiny console harness or the app later); `dotnet format`; commit `feat(windows): drive the trigger key from a low-level keyboard hook`.

---

## Task 10: UI Automation focus capture

**Files:** create `Windows/src/Blurt.Engine.Windows/FocusCapture/Interop/User32Window.cs`, `FocusCapture/UiaFocusCapture.cs`, `FocusCapture/ChromiumHost.cs`, `FocusCapture/FieldContextRules.cs`; port the pure Swift rules into **`Blurt.Engine`**: `Windows/src/Blurt.Engine/FocusCapture/FocusCaptureRules.cs`; tests `Windows/tests/Blurt.Engine.Tests/FocusCapture/FocusCaptureRulesTests.cs`, `Windows/tests/Blurt.Engine.Windows.Tests/FocusCapture/ChromiumHostTests.cs`.

**Swift references:** `FocusCapture/FocusCapture.swift`, `FocusCapture+Pure.swift`, `FocusCapture+Editability.swift`; tests `FocusCaptureTests`, `EditableTargetTests`, `BrowserBundleIDTests`, `AXDowncastTests` (the pure parts).

**Interfaces (produces):**

```csharp
namespace Blurt.Engine.FocusCapture;   // pure, in Blurt.Engine
public static class FocusCaptureRules
{
    public static string? VisibleTextOrNil(string? text);              // all default-ignorable code points → null (Rune.GetUnicodeCategory Format/Cf plus U+200B…U+200F, U+2060…U+2064, U+FEFF)
    public static string? SelectLabel(Func<string?> placeholder, Func<string?> description, Func<string?> title, Func<string?> roleDescription);
    public static string? PriorSlice(string full, int caretUtf16, int maxChars);
    public static string? Clip(string? text, int maxChars);
    public static bool IsEditableTarget(bool isEditControlType, bool valueSettable, bool hasInsertionPoint);
    public const int MaxPriorChars = 320, MaxSelectedChars = 320, MaxWindowTitleChars = 120, MaxFieldLabelChars = 80;
}

namespace Blurt.Engine.Windows.FocusCapture;
public sealed class UiaFocusCapture : IFocusCapture { public bool HasEditableFocusedElement(); }
public static class ChromiumHost { public static bool IsOpaque(string? processPath, string? processName); internal static bool HasChromiumPak(string exeDirectory); public static readonly IReadOnlyList<string> BrowserProcessNames = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc"]; }
```

`CaptureForeground`: `GetForegroundWindow` → `GetWindowThreadProcessId` → `Process.GetProcessById` name and `MainModule?.FileName` (catch access denied → null path). `CaptureFieldContext`: `AutomationElement.FocusedElement` (wrap every UIA call in try/catch; a throw → `FocusedFieldContext.Empty with IsSecure = true` because the role was unreadable); `IsSecure = Current.IsPassword`; when secure return title/label only; window title via `GetWindowText` of the foreground window; label via `SelectLabel(Name, HelpText, ItemStatus, LocalizedControlType)`; prior text via `TextPattern`: selection[0] cloned, `MoveEndpointByRange(End, sel, Start)`, `MoveEndpointByUnit(Start, Character, -320)`, `GetText(-1)`, else `ValuePattern.Value` tail with `PriorSlice(value, value.Length, 320)`; selected text via selection[0].GetText(320). Run through `VisibleTextOrNil` and `Clip`. `HasEditableFocusedElement`: focused element null → false; `IsEditableTarget(ControlType in {Edit, Document, ComboBox}, ValuePattern present && !IsReadOnly, TextPattern present)`.

Steps: rules tests (port the Swift cases: zero-width-space read → null; caret in the middle of surrogate pairs; label priority) red → implement → green; `ChromiumHostTests` with temp directories; `dotnet format`; commit `feat(windows): read the focused field through UI Automation`.

---

## Task 11: Clipboard injector

**Files:** create in `Blurt.Engine` (pure): `Injection/InsertResolver.cs`; create in `Blurt.Engine.Windows`: `Injection/Interop/User32Clipboard.cs`, `Injection/Interop/User32Input.cs`, `Injection/IClipboardAccess.cs`, `Injection/Win32Clipboard.cs`, `Injection/ClipboardSnapshot.cs`, `Injection/ForegroundActivator.cs`, `Injection/ClipboardInjector.cs`; tests `Blurt.Engine.Tests/Injection/InsertResolverTests.cs`, `Blurt.Engine.Windows.Tests/Injection/ClipboardInjectorTests.cs` (with `FakeClipboard`), `ClipboardSnapshotTests.cs`, `Win32ClipboardSmokeTests.cs` (`[Explicit]`).

**Swift references:** `Injection/KeyInjector.swift`, `KeyInjector+Separator.swift`, `KeyInjector+SystemActions.swift`, `SystemClipboard.swift`; tests `KeyInjectorInsertTests`, `KeyInjectorFallbackTests`, `KeyInjectorLeadingSeparatorTests`, `KeyInjectorSystemActionsTests`, `SystemClipboardTests`; stub `Stubs/FakeClipboard.swift`.

**Interfaces (produces):**

```csharp
namespace Blurt.Engine.Injection;   // pure
public sealed record WindowIdentity(int Pid, string Title);
public sealed record ResolvedInsert(string Text, WindowIdentity? Window);
public static class InsertResolver
{
    public static string WithLeadingSeparator(string text, string? priorText);
    public static string? SeparatorBasis(string? priorText, string? lastInserted, bool sameWindow);
    public static ResolvedInsert Resolve(string text, string? priorText, string? windowTitle, int? targetPid, ResolvedInsert? lastInserted);
}

namespace Blurt.Engine.Windows.Injection;
public interface IClipboardAccess { void Write(string text); Action WriteAndPrepareRestore(string text); }
public sealed class Win32Clipboard : IClipboardAccess { public uint SequenceNumber { get; } public ClipboardSnapshot? Snapshot(); public void SetString(string text); public void Restore(ClipboardSnapshot saved); }
public sealed record ClipboardSnapshot(IReadOnlyList<ClipboardItem> Formats, string? PlainText);   // one entry per HGLOBAL format
public sealed record ClipboardItem(uint Format, string? RegisteredName, byte[] Data);
public static class ClipboardFormats { public static bool IsRestorable(uint format) /* false for CF_TEXT 1, CF_OEMTEXT 7, CF_LOCALE 16, CF_BITMAP 2, CF_PALETTE 9, CF_METAFILEPICT 3, CF_ENHMETAFILE 14, CF_DSP* 0x81–0x8E, CF_OWNERDISPLAY 0x80, CF_PRIVATEFIRST–LAST 0x200–0x2FF, CF_GDIOBJFIRST–LAST 0x300–0x3FF */; }
public sealed class ClipboardInjector : IInjector
{
    public ClipboardInjector(TimeSpan? pasteSettle = null);
    internal ClipboardInjector(TimeSpan pasteSettle, Func<bool> postPaste, Func<FocusTarget, Task<bool>> activateTarget, Func<bool> hasEditableTarget, Func<FocusTarget?, bool> isOpaqueApp, IClipboardAccess clipboard, TimeProvider clock);
}
```

Rules: `Win32Clipboard` opens with up to 10 retries at 10 ms; `Snapshot` enumerates formats, skips non-restorable ones and null handles, copies HGLOBAL bytes, records registered names (`GetClipboardFormatName`) for formats ≥ `0xC000` so restore re-registers them; `Restore` builds every HGLOBAL first, then `EmptyClipboard` and `SetClipboardData` each; empty snapshot → `EmptyClipboard`; nothing restorable → put `PlainText` back. `WriteAndPrepareRestore` returns a closure that compares `GetClipboardSequenceNumber()` to the value after our write and restores only when unchanged (a null snapshot means "don't restore"). `ClipboardInjector.InsertAsync` follows `KeyInjector.performInsert` step-for-step: resolve → activate (`IsWindow` false or `SetForegroundWindow` never observed within 350 ms → write the text, throw `TargetAppLost`) → `ct.ThrowIfCancellationRequested()` → editable gate (`hasEditableTarget() || isOpaqueApp(target)`, else write + `NoEditableTarget`) → `WriteAndPrepareRestore` → `postPaste()` (`SendInput` Ctrl down, V down, V up, Ctrl up with `dwExtraInfo = HookEventReducer.InjectedTag`; false → `TargetAppLost`) → record `lastInserted` → schedule the restore after the settle on a chained background task so the next insert serializes behind it. Throw `BlurtException(new BlurtError.TargetAppLost())` etc.

Steps: `InsertResolverTests` (port all separator cases) and `ClipboardInjectorTests` with `FakeClipboard` red → implement → green; explicit clipboard round-trip smoke test run once locally; `dotnet format`; commit `feat(windows): paste transcripts through the clipboard with restore`.

---

## Task 12: Credential store, JSON settings, microphone access, paths

**Files:** create `Windows/src/Blurt.Engine.Windows/Config/Interop/AdvApi32Credentials.cs`, `Config/CredentialManagerApiKeyStore.cs`, `Config/JsonSettingsStore.cs`, `Config/WindowsPaths.cs`, `Permissions/PermissionsChecker.cs`; tests `Config/JsonSettingsStoreTests.cs`, `Config/CredentialManagerApiKeyStoreTests.cs` (isolated target `blurt-test-<guid>`, deleted in teardown), `Permissions/PermissionsCheckerTests.cs` (pure mapping only).

**Swift references:** `Config/KeychainStore.swift`, `APIKeyStore.swift`, `MemoizedKeyStore.swift`, `Permissions/PermissionsChecker.swift`; tests `KeychainStoreTests`, `MemoizedKeyStoreTests`, `PermissionsCheckerTests`.

**Interfaces (produces):** `CredentialManagerApiKeyStore(string target = HostIdentity.Current.CredentialTarget) : IApiKeyGateway` (generic credential type 1, persist 2, blob UTF-16 bytes, `Save("")` deletes, memoized read like `MemoizedKeyStore`); `JsonSettingsStore(string path) : ISettingsStore` (loads on construction, writes atomically on every `Set`/`Remove`); `WindowsPaths.SettingsFile(HostIdentity)` = `%LOCALAPPDATA%\<LogDirectoryName>\settings.json`, `LogDirectory(HostIdentity)` = `%LOCALAPPDATA%\<LogDirectoryName>\Logs`; `PermissionsChecker(Func<MicrophoneAccessState> probe)` with `Check()` → `PermissionStatus(Microphone: probe() == Granted)` and `static OpenMicrophonePrivacySettings()` launching `ms-settings:privacy-microphone` (the probe itself is Task 8's `MicrophoneAccess.Probe`; the app wires it in Task 13).

Steps: tests red → implement → green → `dotnet format` → commit `feat(windows): store the API key in Credential Manager and settings as JSON`.

---

## Task 13: App shell — composition, overlay, tray, cues

**Files:** create in `Windows/src/Blurt.App/`: `App.xaml(.cs)` (single-instance mutex, `HostIdentity.Configure(BlurtDev)` under `#if DEBUG`, composition, unhandled-exception logging to `BlurtLog`), `DictationComponents.cs`, `AppCoordinator.cs`, `Overlay/OverlayWindow.xaml(.cs)`, `Overlay/OverlayViewModel.cs`, `Overlay/MeterBars.cs`, `Tray/TrayIcon.cs`, `Cues/CueSoundPlayer.cs`, `Cues/SoundPackCatalogLoader.cs`, `Resources/soundpacks.json`, `Resources/Icons/*.ico` (simple generated icons: idle/recording/transcribing), `Update/UpdateCheckModel.cs`, `Services/FileLogSink.cs`, `Services/ApiKeyModel.cs` (over `ApiKeySubmission` + `CredentialManagerApiKeyStore`, exposing `HasKey`, `SubmitAsync`, `ReadinessCheck()`); modify `Blurt.App.csproj` to link `..\..\..\App\Blurt\Blurt\Resources\Sounds\*.m4a` as `Content` with `CopyToOutputDirectory PreserveNewest` under `Resources\Sounds\%(Filename)%(Extension)`.

**Swift references:** `App/Blurt/Blurt/AppCoordinator.swift`, `DictationComposition.swift`, `Overlay/*.swift`, `MenuBar/MenuBarScene.swift`, `CueSoundPlayer.swift`, `Update/UpdateCheckModel.swift`, `SoundPackCatalog.swift`.

**Interfaces:** consumes everything above. Produces `AppCoordinator` with `Start()`, `ShowOverlayArmed()`, `HideOverlay()`, `DictationBindingChanged()`, `SoundPackChanged()`, `RecentDictations`, `TrayStatus`, `IsCapturing`, `Session`, `ApiKey` (an `ApiKeyModel` over `ApiKeySubmission`), events for the UI to observe; all UI mutation marshalled through `Dispatcher`.

Steps:

1. Generate `Resources/soundpacks.json` from `App/Blurt/Blurt/SoundPackCatalog.swift` with one sed/PowerShell pass: `[{"id":"rom1a-0","label":"Brass 1","group":"Yamaha DX7 · ROM1A"}, …]` plus `"defaultVoiceId":"rom1a-6"`; write a small `scripts/windows/generate-soundpacks.ps1` that does it so it can be re-run.
2. `CueSoundPlayer`: two `MediaPlayer`s (start/stop), resolves `Resources/Sounds/<id>-start.m4a` relative to `AppContext.BaseDirectory`, `Prime()`, `Transition(PipelinePhase)` via `RecordingCueGate`, `PackChanged()` previews the start cue.
3. `OverlayWindow`: `AllowsTransparency`, `WindowStyle None`, `Topmost`, `ShowInTaskbar false`, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` via `SetWindowLong` in `OnSourceInitialized`, `ShowActivated false`; `Show(OverlayUIState)`, `PushLevel(float)`; notices auto-hide after `NoticeDwellSeconds`; drag with the mouse and persist through `OverlayOriginStore`; default bottom-center of the primary screen using `SystemParameters.WorkArea`.
4. `TrayIcon`: `NotifyIcon` with the three icons and a context menu (Open Blurt, Settings…, Check for Updates…, Quit).
5. `AppCoordinator.Start()`: warm the mic if the probe says granted, observe `session.PhaseChanged` → `Dispatcher.BeginInvoke(Render)`, observe `mic.Levels`, wire `DictationKeyHook` to `session.Submit`, `Render` mirrors the Swift `render(_:)` (setup blocker → `OnSetupBlocked`, overlay, tray, cues, `SyncAfterTerminalPhase` on terminal phases).
6. `UpdateCheckModel`: `CheckNowAsync()`, launch check gated by `AutomaticUpdateCheck.ShouldRun` after `LaunchDelay`, result → a `MessageBox`-style dialog with Download (opens the URL) / Later, "couldn't check" caption on throw.
7. `MainWindow` stays a placeholder that shows the session phase text (Task 14 replaces it).
8. Run the app (`dotnet run --project Windows/src/Blurt.App`) and confirm it starts, the tray icon appears, and pressing right Ctrl with a saved key shows the pill (or the setup blocker path when no key). Note what you observed in the report.

Commit `feat(windows): compose the app shell with overlay, tray, and cues`.

---

## Task 14: App shell — wizard, ready view, settings

**Files:** create `Windows/src/Blurt.App/Main/MainWindow.xaml(.cs)`, `Main/WizardViewModel.cs`, `Main/Steps/ApiKeyStep.xaml`, `MicrophoneStep.xaml`, `ShortcutStep.xaml`, `SoundStep.xaml`, `Main/ReadyView.xaml`, `Main/RecentDictationsView.xaml`, `Main/StyleSwitcher.xaml`, `Settings/SettingsWindow.xaml(.cs)`, `Settings/GeneralTab.xaml`, `Settings/AdvancedTab.xaml`, `Settings/StyleProfileEditor.xaml(.cs)`, `Settings/DeveloperSection.xaml`, `Settings/ResetSection.xaml`, `Services/InstallReset.cs`.

**Swift references:** `App/Blurt/Blurt/Wizard/*.swift`, `Wizard/Steps/*.swift`, `APIKeyModel.swift`, `Config/InstallReset.swift`, `Config/APIKeyDisplay.swift`.

Behavior: the wizard advances step by step and lands on the ready view when `SetupReadiness.IsReady(PermissionsChecker.Check(), apiKey.HasKey)`; a `DispatcherTimer` polls at `SetupReadiness.PollInterval(isReady)` and drops back into the wizard on a lost grant; `ShowOverlayArmed()` on the transition into ready. API key step: masked field, Save button running `ApiKeySubmission.SubmitAsync`, inline error or alert per `FailureReport`. Ready view: `TriggerKey.FullName` how-to sentence and `TriggerActivation.Guidance`, "Listening…" while capturing, style switcher (Default + profiles, disabled while capturing), recent dictations (3 rows, `ReservedHeight`), Settings button, Cancel Dictation button while capturing. Settings: General (trigger key combo box, activation combo box, sound voice grouped combo with preview); Advanced (enhanced transcripts toggle; styles list with Add/Edit/Delete, editor with name ≤24 chars and a `{bytes}/{CharacterLimit}` counter; Check for Updates with "last checked" caption; developer mode switch + both log paths with Copy buttons; Reset — confirm dialog, then `InstallReset.Run()` = `PersistedSettings.ResetAll`, delete the API key, `DictationLog.RemoveStoredLogs`, then restart the process).

Steps: build each view against the coordinator; run the app and walk the wizard with a real API key if one is available in the environment (`ASSEMBLYAI_API_KEY`), otherwise verify the inline "rejected" path with a bogus key; `dotnet format`; commit `feat(windows): add the setup wizard, ready view, and settings window`.

---

## Task 15: Documentation, repo map, final polish

**Files:** modify `Windows/README.md` (complete: prerequisites, build, test, run, publish, how the tree maps to the Swift engine, known limitations from the design doc's follow-ups), `AGENTS.md` (add a `Windows/` row to the repository map and a short "Windows port" subsection pointing at `Windows/README.md` and the design doc; do not touch the Settled decisions table or its anchors — `scripts/check-invariants.sh --self-test` pins them).

Steps: write; run `npx --yes prettier@3 --write Windows/README.md AGENTS.md .github/workflows/check-windows.yml` then `npx --yes markdownlint-cli Windows/README.md AGENTS.md --config .markdownlint.jsonc`; run `bash scripts/check-invariants.sh --self-test` (Git Bash) to prove the anchors still hold; `dotnet format --verify-no-changes`; full build and test; commit `docs(windows): document the Windows port and map it in AGENTS.md`.

---

## Execution notes for the controller

- Tasks 1 → 2 are serial. Tasks 3, 4, 5, 6, 7 are independent of each other (each adds new files only) and may run in parallel worktrees branched from the Task 2 commit; likewise Tasks 8, 9, 10, 11, 12 depend only on Task 2. Task 13 needs 3–12 merged; Task 14 needs 13; Task 15 last.
- When merging parallel worktrees, rebuild and run the full test suite after every merge.
- The final whole-branch review runs `dotnet build -warnaserror`, the full test suite, `dotnet format --verify-no-changes`, prettier and markdownlint on the touched docs/workflows, and `scripts/check-invariants.sh --self-test`.
