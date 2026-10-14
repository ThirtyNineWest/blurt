# Blurt for Windows: upstream sync to v0.1.62

Brings the Windows port (branch `WindowsPort`, forked at upstream `v0.1.56`, commit `8d78dde`) up to
the behavior of upstream `AssemblyAI/blurt` `v0.1.62` (commit `479b0bd`). This document continues the
decision log of [`2026-09-22-windows-port-design.md`](./2026-09-22-windows-port-design.md); decisions
here are numbered `D18` onward and are binding on the plan in
[`2026-10-13-upstream-sync-plan.md`](./2026-10-13-upstream-sync-plan.md).

## What upstream changed, and what this sync does with it

| Upstream change (PR)                                                   | Windows                                                                        |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| Text shortcuts: spoken phrase → saved text before the paste (#200)     | Ported (engine + Settings).                                                    |
| Drop the same-window separator after the user types or clicks (#206)   | Ported, with a Windows source for "has the user typed or clicked" (`D21`).     |
| Experimental read-selected-text-aloud over streaming TTS (#221)        | Ported (engine + WASAPI playback + UIA selection read + Ctrl+C fallback).      |
| Read-aloud work mode: speed 1×–3×, skip jargon, hold-to-ask (#225)     | Ported. Speed is a pure C# time stretch (`D23`).                               |
| "Report a bug" footer link (#204), share link (#197)                   | Ported; the bug link points at the fork (`D27`).                               |
| Menu-bar hint says tap or hold per the activation setting (v0.1.60)    | Ported to the tray hint (`D28`).                                               |
| `TriggerActivation.startVerb/finishHint/stopHint` wording (#209, #225) | Ported (the Experimental footer needs them).                                   |
| Style menu not updating after Settings edits (#219)                    | Not needed: the WPF app shares one `StylesViewModel`, which reloads on save.   |
| Electron apps with a renamed framework (#205)                          | Not needed: Windows detects Chromium by `chrome_100_percent.pak`, not by name. |
| `fn` trigger key, `GlobeKeyOverride`, stale-`fn` migration (#209)      | Not ported: macOS-only key.                                                    |
| Sparkle auto-update, release pipeline, hand-written release notes      | Not ported: the Windows engine keeps its GitHub-API update checker (`D18`).    |
| Intel Mac build (#199), iPhone app and engine fencing (#223)           | Not ported: platform work. The Swift seam renames there are not mirrored.      |

## Decisions

- **D18 Scope and the update checker.** Everything user-visible in the table above ports. The macOS
  engine deleted `Update/` when it switched to Sparkle; the Windows engine keeps `Update/`
  unchanged, because Sparkle has no Windows build and the "no update code in the engine" rule is
  about not running two updaters beside Sparkle. The port's version becomes `0.1.62`
  (`Directory.Build.props`), so the User-Agent and the update check name the behavior shipped.
- **D19 Text shortcuts.** `TextShortcut`, `TextShortcutStore`, and `TextShortcutExpander` port
  one-to-one from `Sources/BlurtEngine/{Config/TextShortcutStore,Pipeline/TextShortcutExpander}.swift`.
  The Windows store has a settable `Shortcuts` property (no `@AppStorage`). Caps are measured in
  UTF-16 code units (`string.Length`), the same unit `StyleProfileStore.NameLimit` uses. Settings
  gets a **Vocabulary** tab holding a Key Terms box (the Windows app had `KeyTermsStore` but no
  control for it) and the Text Shortcuts list, matching upstream's pane layout. The list is a
  `ListView` with Phrase/Replacement columns and Add…/Edit…/Remove buttons; double-click edits;
  Delete removes without confirmation (upstream's rule). The editor is a modal `Window` in the
  shape of `StyleProfileEditor`.
- **D20 Nothing about a shortcut goes on the wire.** The ring keeps both `Text` (what was pasted,
  shown and copied in Recent) and `Spoken` (the service's transcript); `stt_prompt` history is
  `SpokenOldestFirst`. `TranscriptionContext` carries the shortcut list, read only when there is
  prior text, and `STTPrompt.Text` scrubs expansions back to their triggers before the prior chunk
  is fitted. `DictationLog` keeps the raw transcript and the raw prior text.
- **D21 "Has the user typed or clicked since the last paste."** macOS reads HID idle counters that
  synthetic events do not move. On Windows `GetLastInputInfo` is moved by `SendInput` and by the
  trigger key itself, so it cannot answer the question. Instead a `UserInputClock`
  (`Blurt.Engine.Windows`) is touched by (a) the existing `WH_KEYBOARD_LL` hook for every key-down
  that is neither injected (`InjectedTag`) nor the trigger key, and (b) a new `WH_MOUSE_LL` hook for
  non-injected button-downs. `ClipboardInjector` remembers when it pasted and drops the same-window
  continuity when the clock is newer than that paste. Both hooks share one thread class
  (`LowLevelHook`), and the mouse hook installs beside the keyboard hook in `ShowOverlayArmed`.
- **D22 Read-aloud architecture.** Engine (`Blurt.Engine/TTS`): `SpeechSegmenter`,
  `AssemblyAISpeechSynthesizer` over an `ISpeechSocket` seam (production: `ClientWebSocket`, BCL),
  `ReadAloudLLM` over `IHttpTransport`, `SelectionSpeaker` over an `IPcmSink` factory and an
  `ISelectionCopy` seam, `SelectionSpeechRouting` (the v0.1.62 state machine with asking), and the
  stores `SelectionSpeechStore`, `SelectionAskStore`, `ReadAloudStyleStore`. Platform
  (`Blurt.Engine.Windows`): `WasapiPcmPlayer` (default render endpoint, shared mode, 24 kHz mono
  16-bit requested with `AUTOCONVERTPCM`), `UiaFocusCapture.CaptureSelectedText` (UIA
  `TextPattern.GetSelection`), `SelectionCopy` (Ctrl+C through `SendInput`, read `CF_UNICODETEXT`,
  restore gated on the clipboard sequence number), `LowLevelMouseHook`. App: `SelectionSpeechRouter`
  between `DictationKeyHook` and `DictationSession`. The one WebSocket in the Windows tree is the
  TTS client and the one LLM Gateway client is `ReadAloudLLM`; neither touches a dictation.
- **D23 Speed.** AVFoundation's renderer changes rate with pitch held; WASAPI has no equivalent. The
  Windows player applies a pure C# WSOLA time stretch (`PcmTimeStretch`, in the engine, unit-tested)
  to each chunk before queueing it; rate `1` is a byte-identical passthrough. `PCMSchedule` is not
  ported: a WASAPI render queue is contiguous and takes no presentation timestamps.
- **D24 Hand-back press.** `Command.PressHandingBack` runs the same dictation whose transcript ends
  the run as the terminal phase `PipelinePhase.HandedBack(transcript)` (the spoken text, before any
  shortcut expansion), never pasted, remembered, or delivered. It projects as idle on the pill and
  the tray and logs the outcome line `handed back`.
- **D25 The gate reports a latch.** `DictationKeyGate.Action.Latch` replaces `None` on the release
  that latches a tap; `DictationKeyHook` gains `onLatch` and a silent `ResetGate()`.
- **D26 Experimental section.** Settings → Advanced gets an **Experimental** card: "Read selected
  text aloud" (off by default); while on, "Hold to ask about the selection" (on by default),
  a Speed picker over `ReadAloudStyleStore.SpeedChoices` (`1×`, `1.25×`, `1.5×`, `1.75×`, `2×`,
  `2.5×`, `3×`), and "Skip code, links, and long numbers" (off by default). The footer copy is
  upstream's, with Windows key names, and names the LLM Gateway whenever a setting sends text to it.
- **D27 Links.** The ready view footer reads "Powered by AssemblyAI · Report a bug". The bug link
  opens `https://github.com/ThirtyNineWest/blurt/issues`, because the Windows port lives in that
  fork; change `BlurtLinks.ReportBug` if the port moves. "Share Blurt on LinkedIn" and "Report a
  Bug" join the tray menu, the Windows analog of upstream's Help menu.
- **D28 Tray hint.** The disabled hint line reads `"{activation label} {key label} to dictate and
paste"`, e.g. "Hold right Ctrl to dictate and paste".
- **D29 Hazards accepted while experimental.** (1) A Ctrl+C fallback posted while Right Shift is the
  held trigger reaches the app as Ctrl+Shift+C; same class as the paste chord's known limitation.
  (2) A UIA-opaque app that copies the current line on an empty Ctrl+C would read that line aloud;
  the fallback runs only when UIA cannot reach a focused element, never on a bare caret. (3) The
  TTS endpoint is undocumented; the feature is off by default. (4) With skip-jargon or asking on,
  the selection (and the spoken request) go to AssemblyAI's LLM Gateway; the footer says so.
- **D30 Threading.** `SelectionSpeechRouter` guards its routing state with one lock and performs
  actions outside it, so a hook-thread callback never blocks: forwarding is `Session.Submit`,
  the selection read and speech run on tasks, and gate resets take the hook's own router lock,
  which the hook has already released when it invokes a callback.

## Constants (exact)

`TextShortcutStore.ShortcutLimit = 200`, `TriggerLimit = 60`, `ExpansionLimit = 2000`;
`TextShortcutExpander.MinimumClippedOverlap = 4`; `SelectionSpeaker.MaxCharacters = 4000`;
`AssemblyAISpeechSynthesizer.SampleRate = 24000`, `Voice = "jane"`, endpoint
`wss://streaming-tts.assemblyai.com/v1/ws/`, receive timeout `10 s`; `ReadAloudLLM.Model =
"qwen3.5-4b-32k-fast"`, endpoint `https://llm-gateway.assemblyai.com/v1/chat/completions`,
`RewriteMaxTokens = 4096`, `AnswerMaxTokens = 1024`, `RewriteTimeout = 10 s`, `AnswerTimeout = 20 s`,
plausibility slack `max(80, input.Length / 4)`; `SelectionSpeechRouting.AskHoldDelay = 350 ms`;
`SelectionCopy` timeout `100 ms`, poll `10 ms`; `ReadAloudStyleStore.SpeedChoices = [1, 1.25, 1.5,
1.75, 2, 2.5, 3]`, default speed `1`, skip-jargon default `false`; `SelectionAskStore` default
`true`; `SelectionSpeechStore` default `false`; `PcmTimeStretch` frame `480` samples (20 ms at
24 kHz), synthesis hop `240`, search `±120`; `WasapiPcmPlayer` drain slack `2 s`.

## Settled decisions that still bind

No streaming STT (the TTS socket speaks, it never transcribes). No client-side cleanup pass
(`ReadAloudLLM` rewrites a selection for listening or answers a request about one; it never sees a
dictation that is pasted). No NuGet in `src/`. No `InternalsVisibleTo`. Async is awaited. Comments
state behavior and invariants only.
