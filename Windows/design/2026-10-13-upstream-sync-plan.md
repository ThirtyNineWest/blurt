# Upstream Sync (v0.1.56 → v0.1.62) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the Windows port up to upstream v0.1.62 behavior: text shortcuts, the same-window separator fix, experimental read-aloud with hold-to-ask, speed and skip-jargon, the footer and tray links, and the activation-aware tray hint.

**Architecture:** Engine logic ports file-for-file from the Swift under `Sources/BlurtEngine/` at `upstream/main` (commit `479b0bd`) into `Windows/src/Blurt.Engine`, behind seams (`ISpeechSocket`, `IPcmSink`, `ISelectionReader`, `ISelectionCopy`, `IHttpTransport`). `Windows/src/Blurt.Engine.Windows` implements the seams over WASAPI render, UI Automation, the Win32 clipboard, `SendInput`, and a `WH_MOUSE_LL` hook. `Windows/src/Blurt.App` composes them and adds the Vocabulary tab, the Experimental section, and the links.

**Tech Stack:** C# 14, .NET SDK 10.0.4xx, WPF, `System.Net.WebSockets.ClientWebSocket` (BCL), `System.Windows.Automation`, hand-written P/Invoke and `[ComImport]` COM. Tests: NUnit 4, FluentAssertions 7, FakeItEasy, `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** `Windows/design/2026-10-13-upstream-sync-design.md` (decisions `D18`–`D30` are binding) on top of `Windows/design/2026-09-22-windows-port-design.md`.

## Global Constraints

- **The Swift at `upstream/main` is the behavior spec.** Read the named Swift file with `git show upstream/main:<path>` before writing its C# twin, and port every `@Test` of the named suite as a `[Test]` with the same inputs and expected values. The remote `upstream` already exists (`https://github.com/AssemblyAI/blurt.git`).
- **No NuGet references in `src/` projects.** Test projects use only the packages already in `Windows/Directory.Packages.props`.
- **Build settings are unchanged:** `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `dotnet format --verify-no-changes Windows/Blurt.sln` must pass. File-scoped namespaces, 4-space indent, LF endings. Every public member has an XML doc comment that says what it does, guarantees, returns, and throws.
- **No `InternalsVisibleTo`.** A member a test needs is `public`.
- **Async is awaited.** No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`.
- **Constants (exact):** `TextShortcutStore.ShortcutLimit = 200`, `TriggerLimit = 60`, `ExpansionLimit = 2000`; `TextShortcutExpander.MinimumClippedOverlap = 4`; `SelectionSpeaker.MaxCharacters = 4000`; `AssemblyAISpeechSynthesizer.SampleRate = 24000`, `Voice = "jane"`, endpoint `wss://streaming-tts.assemblyai.com/v1/ws/`, receive timeout `10 s`; `ReadAloudLLM.Model = "qwen3.5-4b-32k-fast"`, endpoint `https://llm-gateway.assemblyai.com/v1/chat/completions`, `RewriteMaxTokens = 4096`, `AnswerMaxTokens = 1024`, `RewriteTimeout = 10 s`, `AnswerTimeout = 20 s`; `SelectionSpeechRouting.AskHoldDelay = 350 ms`; `SelectionCopy` timeout `100 ms`, poll `10 ms`; `ReadAloudStyleStore.SpeedChoices = [1, 1.25, 1.5, 1.75, 2, 2.5, 3]`; `SelectionAskStore` default `true`; `SelectionSpeechStore` default `false`; `ReadAloudStyleStore` defaults speed `1`, skip `false`; `PcmTimeStretch` frame `480`, hop `240`, search `120`; `WasapiPcmPlayer` drain slack `2 s`; version `0.1.62`.
- **Settled decisions bind:** no streaming STT (the only socket is the TTS client), no client-side cleanup pass (the only gateway client is `ReadAloudLLM`, which never sees a pasted dictation), no keystroke-typing injector, no self-replacing updater.
- **Comments** state what the code does or the invariant it protects, timelessly. No change provenance, no "this exists to", no references to plans, PRs, or reviews.
- **Tests:** blank lines separate setup, action, and assertion; no `// Arrange` comments; `FluentActions.Awaiting`/`Invoking` for exception assertions; never touch the real Credential Manager, the real `%LOCALAPPDATA%\Blurt`, the real clipboard, or the real audio devices outside `[Explicit]` smoke tests; never send keystrokes or activate another process's window.
- **Do not modify** anything under `Sources/`, `App/`, `Tests/`, `scripts/` (other than `scripts/windows/`), `.github/workflows/check.yml`, `Package.swift`, `evals/`, `release-notes/`.
- **Commits:** Conventional Commits (`feat(windows): …`, `fix(windows): …`, `test(windows): …`, `docs(windows): …`), imperative, lowercase after the colon, under 72 chars, ending with the trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Commit after each green test cycle.
- **Verification before claiming done:** `dotnet build Windows/Blurt.sln -warnaserror`, `dotnet test Windows/Blurt.sln --filter "TestCategory!=Explicit"`, and `dotnet format Windows/Blurt.sln --verify-no-changes` all green, output quoted in the report.

## Review Focus

1. **A trigger inside a longer word** ("personal email" in "impersonal emails") must not expand; a trigger split by a sentence break ("work. Email") must not expand. Pinned in Task 1 (`WholeWords`, `NeverMatchesAcrossASentenceOrClauseBreak`).
2. **The trigger key and Blurt's own injected keystrokes must never count as user input**, or the same-window separator is permanently off. Pinned in Task 4 (`UserInputClockTests`, `DictationKeyHookTests.TheTriggerKeyAndInjectedKeysDoNotTouchTheInputClock`).
3. **A read-aloud press with no API key** must fail before any socket opens, and the gate must come back to idle. Pinned in Task 7 (`AMissingKeyFailsBeforeAnySocketOpens`) and Task 6 (`ANaturalEndClearsTheGateSilently`).
4. **Speed at rate 1 is a byte-identical passthrough; at rate 2 a 440 Hz tone stays 440 Hz** (pitch held) and the output is about half as long. Pinned in Task 9.
5. **A stalled gateway or TTS server must not hold the press forever.** Rewrite 10 s, answer 20 s, socket idle 10 s. Pinned in Task 7 (`ASilentServerTimesOut`) and Task 8 (`AStalledRewriteTimesOut`).

---

### Task 1: Text shortcut model, store, draft rules, and expander (engine)

**Files:**

- Create: `Windows/src/Blurt.Engine/Config/TextShortcut.cs`, `Windows/src/Blurt.Engine/Config/TextShortcutStore.cs`, `Windows/src/Blurt.Engine/Config/TextShortcutDraft.cs`, `Windows/src/Blurt.Engine/Pipeline/TextShortcutExpander.cs`
- Modify: `Windows/src/Blurt.Engine/Config/DefaultsKey.cs` (append five members)
- Test: `Windows/tests/Blurt.Engine.Tests/Config/TextShortcutStoreTests.cs`, `Windows/tests/Blurt.Engine.Tests/Config/TextShortcutDraftTests.cs`, `Windows/tests/Blurt.Engine.Tests/Pipeline/TextShortcutExpanderTests.cs`; modify `Windows/tests/Blurt.Engine.Tests/Config/DefaultsKeyTests.cs` (`RawValuesAreTheOnDiskNames`), `Windows/tests/Blurt.Engine.Tests/Config/PersistedSettingsTests.cs` (add the store's key line)

**Swift spec:** `Sources/BlurtEngine/Config/TextShortcutStore.swift`, `Sources/BlurtEngine/Pipeline/TextShortcutExpander.swift`, `Tests/BlurtEngineTests/TextShortcutTests.swift` (suites `TextShortcutExpander`, `TextShortcutStore`).

**Interfaces:**

- Produces: `public sealed record TextShortcut(Guid Id, string Trigger, string Expansion)` with ctor `TextShortcut(string trigger, string expansion)` minting a `Guid`; `public sealed class TextShortcutStore(ISettingsStore store)` with `static string SettingsKey`, `const int ShortcutLimit = 200, TriggerLimit = 60, ExpansionLimit = 2000`, `IReadOnlyList<TextShortcut> Shortcuts { get; set; }`, `IReadOnlyList<TextShortcut> Decode(string raw)`, `static string MatchKey(string trigger)`, `static IReadOnlyList<TextShortcut> Normalized(IEnumerable<TextShortcut>)`; `public static class TextShortcutExpander` with `string Expand(string text, IReadOnlyList<TextShortcut> shortcuts)`, `string RedactingExpansions(string text, IReadOnlyList<TextShortcut> shortcuts)`, `IReadOnlyList<string> Words(string trigger)`, `const int MinimumClippedOverlap = 4`; `public static class TextShortcutDraft` with `bool CanSave(string trigger, string expansion, IReadOnlySet<string> otherKeys)`, `bool DuplicatesAnother(string trigger, IReadOnlySet<string> otherKeys)`, `string ClampTrigger(string)`, `string ClampExpansion(string)`; `DefaultsKey.TextShortcuts`, `DefaultsKey.SelectionSpeech`, `DefaultsKey.ReadAloudSpeed`, `DefaultsKey.ReadAloudSkipsJargon`, `DefaultsKey.SelectionAsk`.

- [ ] **Step 1: Append the five keys to `DefaultsKey`** (after `LastUpdateCheck`, in this order) and update `DefaultsKeyTests.RawValuesAreTheOnDiskNames` to end with `"LastUpdateCheck", "TextShortcuts", "SelectionSpeech", "ReadAloudSpeed", "ReadAloudSkipsJargon", "SelectionAsk"`.

```csharp
    /// <summary>The JSON list of spoken-phrase → text expansions.</summary>
    TextShortcuts,

    /// <summary>The experimental read-selection-aloud switch.</summary>
    SelectionSpeech,

    /// <summary>Read-aloud's playback speed.</summary>
    ReadAloudSpeed,

    /// <summary>Whether read-aloud rewrites the text for listening first.</summary>
    ReadAloudSkipsJargon,

    /// <summary>Whether a hold over a selection asks about it.</summary>
    SelectionAsk,
```

- [ ] **Step 2: Write the failing expander tests** (`TextShortcutExpanderTests.cs`). Port every case of the Swift `TextShortcutExpander` suite:

```csharp
using Blurt.Engine.Config;
using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class TextShortcutExpanderTests
{
    private static readonly TextShortcut Email = new("personal email", "me@example.com");
    private static readonly TextShortcut Linktree = new("personal link tree", "https://linktr.ee/someone");
    private static readonly TextShortcut Cal = new("personal cal.com", "https://cal.com/someone");

    [Test]
    public void ReplacesATriggerMidSentenceIgnoringCase()
    {
        TextShortcutExpander.Expand("You can reach me at Personal Email anytime.", [Email])
            .Should().Be("You can reach me at me@example.com anytime.");
    }

    [Test]
    public void ADictationThatIsOnlyTheTriggerPastesTheBareExpansion()
    {
        TextShortcutExpander.Expand("Personal email.", [Email]).Should().Be("me@example.com");
        TextShortcutExpander.Expand(" personal email ", [Email]).Should().Be(" me@example.com ");
    }

    [Test]
    public void SeparatorsBetweenWordsAreLoose()
    {
        TextShortcutExpander.Expand("my personal Linktree is here", [Linktree]).Should().Be("my https://linktr.ee/someone is here");
        TextShortcutExpander.Expand("book at personal Cal.com.", [Cal]).Should().Be("book at https://cal.com/someone.");
        TextShortcutExpander.Expand("send it to personal-email", [Email]).Should().Be("send it to me@example.com");
    }

    [TestCase("I finished my work. Email me later.")]
    [TestCase("at work, email is slow")]
    [TestCase("work! Email")]
    public void NeverMatchesAcrossASentenceOrClauseBreak(string text)
    {
        var work = new TextShortcut("work email", "me@work.example");

        TextShortcutExpander.Expand(text, [work]).Should().Be(text);
    }

    [Test]
    public void APeriodInsideAPhraseIsNotABreak()
    {
        var work = new TextShortcut("work email", "me@work.example");

        TextShortcutExpander.Expand("my work.email", [work]).Should().Be("my me@work.example");
    }

    [Test]
    public void ATriggerWithAnApostropheOrOtherSymbolMatchesAsSpelled()
    {
        var mom = new TextShortcut("mom's address", "1 Main St");
        var rnd = new TextShortcut("R&D budget", "$2M");

        TextShortcutExpander.Expand("send to mom's address", [mom]).Should().Be("send to 1 Main St");
        TextShortcutExpander.Expand("send to Mom’s address", [mom]).Should().Be("send to 1 Main St");
        TextShortcutExpander.Expand("the R&D budget", [rnd]).Should().Be("the $2M");
    }

    [Test]
    public void RedactingPutsPastedExpansionsBackToTheirTriggers()
    {
        TextShortcutExpander.RedactingExpansions("Mail me@example.com.", [Email]).Should().Be("Mail personal email.");
        TextShortcutExpander.RedactingExpansions("Mail me@example.com ", [Email]).Should().Be("Mail personal email ");
        TextShortcutExpander.RedactingExpansions("nothing here", [Email]).Should().Be("nothing here");
    }

    [Test]
    public void RedactingCatchesAnExpansionWhoseHeadTheCaptureClipped()
    {
        TextShortcutExpander.RedactingExpansions("ample.com and more", [Email]).Should().Be("personal email and more");
        TextShortcutExpander.RedactingExpansions("com and more", [Email]).Should().Be("com and more");
    }

    [Test]
    public void WholeWords()
    {
        const string text = "That was impersonal emails, honestly.";

        TextShortcutExpander.Expand(text, [Email]).Should().Be(text);
    }

    [Test]
    public void ReplacesEveryOccurrenceAndTheLongestTriggerWins()
    {
        var work = new TextShortcut("personal email work", "me@work.example");

        TextShortcutExpander.Expand("personal email work or personal email", [Email, work])
            .Should().Be("me@work.example or me@example.com");
    }

    [Test]
    public void AnExpansionIsInsertedLiterallyAndNeverReExpanded()
    {
        var tricky = new TextShortcut("price tag", "$1 \\0 personal email");

        TextShortcutExpander.Expand("the price tag", [tricky, Email]).Should().Be("the $1 \\0 personal email");
    }

    [Test]
    public void NoShortcutsOrAPunctuationOnlyTriggerLeavesTheTextAlone()
    {
        var junk = new TextShortcut("...", "x");

        TextShortcutExpander.Expand("hello", []).Should().Be("hello");
        TextShortcutExpander.Expand("wait...", [junk]).Should().Be("wait...");
    }

    [Test]
    public void WordsAreTheLetterAndDigitRuns()
    {
        TextShortcutExpander.Words("personal cal.com").Should().Equal("personal", "cal", "com");
        TextShortcutExpander.Words("Mom’s address").Should().Equal("Mom", "s", "address");
        TextShortcutExpander.Words("...").Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail** (compile error: types missing).

Run: `dotnet test Windows/tests/Blurt.Engine.Tests --filter "FullyQualifiedName~TextShortcutExpanderTests"`
Expected: build failure naming `TextShortcut`/`TextShortcutExpander`.

- [ ] **Step 4: Write `TextShortcut.cs` and `TextShortcutExpander.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Blurt.Engine.Config;

/// <summary>
/// One spoken phrase and the text it expands to. Applied to the finished transcript on the machine,
/// after the dictation response and before the paste; nothing about a shortcut goes on the wire.
/// The id is minted once so a list row keeps its identity across a rename of its trigger.
/// </summary>
public sealed record TextShortcut
{
    /// <summary>A new shortcut with a freshly minted id.</summary>
    public TextShortcut(string trigger, string expansion)
        : this(Guid.NewGuid(), trigger, expansion)
    {
    }

    /// <summary>Full constructor, used by the JSON decoder.</summary>
    [JsonConstructor]
    public TextShortcut(Guid id, string trigger, string expansion)
    {
        Id = id;
        Trigger = trigger;
        Expansion = expansion;
    }

    /// <summary>The shortcut's identity. Fixed at creation.</summary>
    public Guid Id { get; }

    /// <summary>The spoken phrase.</summary>
    public string Trigger { get; init; }

    /// <summary>The text pasted in the phrase's place.</summary>
    public string Expansion { get; init; }
}
```

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Blurt.Engine.Config;

namespace Blurt.Engine.Pipeline;

/// <summary>
/// Replaces each spoken shortcut trigger in a finished transcript with its expansion. Pure and
/// local: it runs on the text the dictation API returned, between the response and the paste.
/// Matching is case-insensitive; the separators between a trigger's words are loose (any run of
/// non-letter/digit characters, or none), except sentence punctuation followed by whitespace, which
/// ends the match; a trigger never matches inside a longer word. One pass, longest trigger first,
/// so an expansion is never itself re-expanded.
/// </summary>
public static class TextShortcutExpander
{
    /// <summary>
    /// The fewest characters of an expansion's tail <see cref="RedactingExpansions"/> treats as a
    /// clipped copy at the very start of the text. Below this an overlap is as likely to be
    /// coincidence (a shared "com" or ".") as a clip.
    /// </summary>
    public const int MinimumClippedOverlap = 4;

    private const string WordClass = @"\p{L}\p{M}\p{N}";
    private const string SentencePunctuation = ".,!?;:";
    private const string SeparatorRun = @"(?:[^\p{L}\p{M}\p{N}.,!?;:]|[.,!?;:](?!\s))*";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// Returns <paramref name="text"/> with every trigger in <paramref name="shortcuts"/> replaced by
    /// its expansion. A text that is only a trigger, surrounding whitespace and trailing sentence
    /// punctuation aside, becomes exactly the expansion (the whitespace kept). Shortcuts whose
    /// trigger holds no letters or digits never match.
    /// </summary>
    public static string Expand(string text, IReadOnlyList<TextShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(shortcuts);
        var ordered = shortcuts
            .Select(shortcut => (Shortcut: shortcut, Pattern: Pattern(shortcut.Trigger)))
            .Where(pair => pair.Pattern is not null)
            .OrderByDescending(pair => pair.Pattern!.Length)
            .ToList();
        if (ordered.Count == 0)
        {
            return text;
        }

        var alternation = string.Join("|", ordered.Select(pair => $"({pair.Pattern})"));
        var lone = new Regex($"^(\\s*)(?:{alternation})[{SentencePunctuation}]*(\\s*)$", Options).Match(text);
        if (lone.Success && ShortcutIndex(lone, groupOffset: 2, ordered.Count) is { } loneIndex)
        {
            return lone.Groups[1].Value + ordered[loneIndex].Shortcut.Expansion + lone.Groups[ordered.Count + 2].Value;
        }

        var inline = new Regex($"(?<![{WordClass}])(?:{alternation})(?![{WordClass}])", Options);
        var result = new StringBuilder();
        var cursor = 0;
        foreach (Match match in inline.Matches(text))
        {
            if (ShortcutIndex(match, groupOffset: 1, ordered.Count) is not { } index)
            {
                continue;
            }

            result.Append(text, cursor, match.Index - cursor);
            result.Append(ordered[index].Shortcut.Expansion);
            cursor = match.Index + match.Length;
        }

        result.Append(text, cursor, text.Length - cursor);
        return result.ToString();
    }

    /// <summary>The trigger's letter/digit runs (letters, marks, and digits), the only part of it the matcher reads.</summary>
    public static IReadOnlyList<string> Words(string trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var rune in trigger.EnumerateRunes())
        {
            if (IsWordRune(rune))
            {
                current.Append(rune.ToString());
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>
    /// Returns <paramref name="text"/> with every expansion in it put back to its trigger, literally
    /// and case-sensitively, longest expansion first. An expansion's tail of at least
    /// <see cref="MinimumClippedOverlap"/> characters at the very start of the text counts too, since
    /// the capture reads only the last few hundred characters before the caret.
    /// </summary>
    public static string RedactingExpansions(string text, IReadOnlyList<TextShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(shortcuts);
        var result = text;
        foreach (var shortcut in shortcuts.OrderByDescending(shortcut => shortcut.Expansion.Length))
        {
            var expansion = shortcut.Expansion;
            if (expansion.Length == 0)
            {
                continue;
            }

            result = result.Replace(expansion, shortcut.Trigger, StringComparison.Ordinal);
            var longest = Math.Min(expansion.Length - 1, result.Length);
            for (var length = longest; length >= MinimumClippedOverlap; length--)
            {
                if (result.StartsWith(expansion.AsSpan(expansion.Length - length), StringComparison.Ordinal))
                {
                    result = shortcut.Trigger + result[length..];
                    break;
                }
            }
        }

        return result;
    }

    private static bool IsWordRune(Rune rune) =>
        Rune.IsLetterOrDigit(rune)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>The regex body for one trigger, or null for a trigger with no letters or digits.</summary>
    private static string? Pattern(string trigger)
    {
        var words = Words(trigger);
        return words.Count == 0 ? null : string.Join(SeparatorRun, words.Select(Regex.Escape));
    }

    /// <summary>Which of the <paramref name="count"/> trigger groups, numbered from <paramref name="groupOffset"/>, took part in <paramref name="match"/>.</summary>
    private static int? ShortcutIndex(Match match, int groupOffset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (match.Groups[i + groupOffset].Success)
            {
                return i;
            }
        }

        return null;
    }
}
```

- [ ] **Step 5: Run the expander tests to verify they pass**

Run: `dotnet test Windows/tests/Blurt.Engine.Tests --filter "FullyQualifiedName~TextShortcutExpanderTests"`
Expected: all green.

- [ ] **Step 6: Write the failing store and draft tests** (`TextShortcutStoreTests.cs`, `TextShortcutDraftTests.cs`)

```csharp
using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class TextShortcutStoreTests
{
    [Test]
    public void AnUntouchedInstallHasNoShortcuts()
    {
        new TextShortcutStore(new InMemorySettingsStore()).Shortcuts.Should().BeEmpty();
    }

    [Test]
    public void ShortcutsRoundTripInOrder()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());
        var written = new List<TextShortcut>
        {
            new("personal email", "me@example.com"),
            new("personal github", "https://github.com/me"),
        };

        store.Shortcuts = written;

        store.Shortcuts.Should().Equal(written);
    }

    [Test]
    public void NormalizationTrimsDropsBlanksAndDedupesCaseInsensitively()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts =
        [
            new("  personal email ", " me@example.com\n"),
            new("Personal Email", "other@example.com"),
            new("   ", "orphan"),
            new("empty", "  "),
        ];

        store.Shortcuts.Select(s => s.Trigger).Should().Equal("personal email");
        store.Shortcuts.Select(s => s.Expansion).Should().Equal("me@example.com");
    }

    [Test]
    public void DedupeTreatsTriggersThatDifferOnlyInSeparatorsAsOnePhrase()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts =
        [
            new("personal email", "a@example.com"),
            new("Personal-Email", "b@example.com"),
            new("personalemail", "c@example.com"),
            new("...", "unmatchable"),
        ];

        store.Shortcuts.Select(s => s.Expansion).Should().Equal("a@example.com");
        TextShortcutStore.MatchKey("Cal.com").Should().Be(TextShortcutStore.MatchKey("cal com"));
    }

    [Test]
    public void CapsAreAppliedOnTheWayIn()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts = [new(new string('a', 70), new string('b', 2500))];

        store.Shortcuts.Single().Trigger.Should().HaveLength(TextShortcutStore.TriggerLimit);
        store.Shortcuts.Single().Expansion.Should().HaveLength(TextShortcutStore.ExpansionLimit);
    }

    [Test]
    public void AtMostShortcutLimitEntriesAreKept()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts = Enumerable.Range(0, TextShortcutStore.ShortcutLimit + 5).Select(i => new TextShortcut($"phrase {i}", "x")).ToList();

        store.Shortcuts.Should().HaveCount(TextShortcutStore.ShortcutLimit);
    }

    [Test]
    public void AnUndecodableSlotReadsAsNoShortcuts()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(TextShortcutStore.SettingsKey, "not json");

        new TextShortcutStore(settings).Shortcuts.Should().BeEmpty();
    }

    [Test]
    public void TheStoreWritesUnderItsRosterKey()
    {
        TextShortcutStore.SettingsKey.Should().Be(DefaultsKey.TextShortcuts.Key());
    }
}
```

```csharp
using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class TextShortcutDraftTests
{
    private static readonly IReadOnlySet<string> Others = new HashSet<string> { TextShortcutStore.MatchKey("personal email") };

    [TestCase("work email", "me@work.example", true)]
    [TestCase("   ", "me@work.example", false)]
    [TestCase("...", "me@work.example", false)]
    [TestCase("work email", "   ", false)]
    [TestCase("Personal-Email", "other@example.com", false)]
    public void CanSaveNeedsAMatchablePhraseAReplacementAndNoDuplicate(string trigger, string expansion, bool expected)
    {
        TextShortcutDraft.CanSave(trigger, expansion, Others).Should().Be(expected);
    }

    [Test]
    public void DuplicatesAnotherComparesByMatchKey()
    {
        TextShortcutDraft.DuplicatesAnother("personalemail", Others).Should().BeTrue();
        TextShortcutDraft.DuplicatesAnother("work email", Others).Should().BeFalse();
        TextShortcutDraft.DuplicatesAnother("...", Others).Should().BeFalse();
    }

    [Test]
    public void ClampsToTheStoresCaps()
    {
        TextShortcutDraft.ClampTrigger(new string('a', 100)).Should().HaveLength(TextShortcutStore.TriggerLimit);
        TextShortcutDraft.ClampExpansion(new string('b', 3000)).Should().HaveLength(TextShortcutStore.ExpansionLimit);
        TextShortcutDraft.ClampTrigger("short").Should().Be("short");
    }
}
```

- [ ] **Step 7: Write `TextShortcutStore.cs` and `TextShortcutDraft.cs`**

```csharp
using System.Text.Json;
using Blurt.Engine.Pipeline;

namespace Blurt.Engine.Config;

/// <summary>
/// Storage for the user's text shortcuts, as one JSON list. The pipeline reads
/// <see cref="Shortcuts"/> at every transcript, so an edit applies to the next dictation.
/// </summary>
public sealed class TextShortcutStore(ISettingsStore store)
{
    /// <summary>How many shortcuts the user may define.</summary>
    public const int ShortcutLimit = 200;

    /// <summary>The most characters a trigger may hold.</summary>
    public const int TriggerLimit = 60;

    /// <summary>The most characters an expansion may hold.</summary>
    public const int ExpansionLimit = 2000;

    private static readonly JsonSerializerOptions DecodeOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    /// <summary>The settings key holding the JSON-encoded list.</summary>
    public static string SettingsKey => DefaultsKey.TextShortcuts.Key();

    /// <summary>
    /// The user's shortcuts, in their own order, normalized on the way out as well as in. Reads an
    /// unset or undecodable slot as no shortcuts.
    /// </summary>
    public IReadOnlyList<TextShortcut> Shortcuts
    {
        get => Decode(store.GetString(SettingsKey) ?? string.Empty);
        set => store.Set(SettingsKey, JsonSerializer.Serialize(Normalized(value)));
    }

    /// <summary>The shortcuts behind a raw slot value: the same decode and normalization the getter uses.</summary>
    public IReadOnlyList<TextShortcut> Decode(string raw)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        List<TextShortcut?>? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<List<TextShortcut?>>(raw, DecodeOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (decoded is null || decoded.Any(shortcut => shortcut is null))
        {
            return [];
        }

        return Normalized(decoded.Select(shortcut => shortcut!));
    }

    /// <summary>
    /// The identity two triggers share when the expander cannot tell them apart: letters and digits
    /// only, lowercased, separators dropped. Empty for a trigger with no letters or digits.
    /// </summary>
    public static string MatchKey(string trigger) =>
        string.Concat(TextShortcutExpander.Words(trigger)).ToLowerInvariant();

    /// <summary>
    /// The funnel every list passes through, on read and on write: trimmed fields within their caps,
    /// entries with a blank or unmatchable trigger or a blank expansion dropped, triggers deduplicated
    /// by <see cref="MatchKey"/> (first wins), at most <see cref="ShortcutLimit"/> kept. Idempotent.
    /// </summary>
    public static IReadOnlyList<TextShortcut> Normalized(IEnumerable<TextShortcut> shortcuts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TextShortcut>();
        foreach (var shortcut in shortcuts)
        {
            if (result.Count >= ShortcutLimit)
            {
                break;
            }

            var trigger = shortcut.Trigger.TrimmedNonEmpty();
            var expansion = shortcut.Expansion.TrimmedNonEmpty();
            if (trigger is null || expansion is null)
            {
                continue;
            }

            trigger = TextShortcutDraft.ClampTrigger(trigger);
            expansion = TextShortcutDraft.ClampExpansion(expansion);
            var key = MatchKey(trigger);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            result.Add(new TextShortcut(shortcut.Id, trigger, expansion));
        }

        return result;
    }
}
```

```csharp
namespace Blurt.Engine.Config;

/// <summary>The pure rules of the text shortcut editor: what can be saved, and the caps applied as the user types.</summary>
public static class TextShortcutDraft
{
    /// <summary>
    /// Whether the editor may save: the trigger has letters or digits, the expansion is non-blank,
    /// and no other shortcut (by <see cref="TextShortcutStore.MatchKey"/> in <paramref name="otherKeys"/>)
    /// already uses the phrase.
    /// </summary>
    public static bool CanSave(string trigger, string expansion, IReadOnlySet<string> otherKeys) =>
        TextShortcutStore.MatchKey(trigger).Length > 0
        && expansion.TrimmedNonEmpty() is not null
        && !DuplicatesAnother(trigger, otherKeys);

    /// <summary>Whether another shortcut's match key equals <paramref name="trigger"/>'s; an unmatchable trigger duplicates nothing.</summary>
    public static bool DuplicatesAnother(string trigger, IReadOnlySet<string> otherKeys)
    {
        var key = TextShortcutStore.MatchKey(trigger);
        return key.Length > 0 && otherKeys.Contains(key);
    }

    /// <summary>Returns <paramref name="trigger"/> cut to <see cref="TextShortcutStore.TriggerLimit"/> characters.</summary>
    public static string ClampTrigger(string trigger) =>
        trigger.Length > TextShortcutStore.TriggerLimit ? trigger[..TextShortcutStore.TriggerLimit] : trigger;

    /// <summary>Returns <paramref name="expansion"/> cut to <see cref="TextShortcutStore.ExpansionLimit"/> characters.</summary>
    public static string ClampExpansion(string expansion) =>
        expansion.Length > TextShortcutStore.ExpansionLimit ? expansion[..TextShortcutStore.ExpansionLimit] : expansion;
}
```

- [ ] **Step 8: Add the roster line to `PersistedSettingsTests.EveryStoreInThisTaskWritesUnderARosterKey`**: `TextShortcutStore.SettingsKey.Should().Be(DefaultsKey.TextShortcuts.Key());`

- [ ] **Step 9: Run the engine tests and format**

Run: `dotnet test Windows/tests/Blurt.Engine.Tests` then `dotnet format Windows/Blurt.sln --verify-no-changes`
Expected: all green, no format diffs.

- [ ] **Step 10: Commit**

```bash
git add Windows/src/Blurt.Engine/Config Windows/src/Blurt.Engine/Pipeline/TextShortcutExpander.cs Windows/tests/Blurt.Engine.Tests/Config Windows/tests/Blurt.Engine.Tests/Pipeline/TextShortcutExpanderTests.cs
git commit -m "feat(windows): text shortcut store, draft rules, and expander"
```

---

### Task 2: Shortcuts in the pipeline: spoken history, scrubbed prompt, expansion before the paste

**Files:**

- Modify: `Windows/src/Blurt.Engine/Pipeline/RecentDictations.cs`, `Windows/src/Blurt.Engine/STT/TranscriptionContext.cs`, `Windows/src/Blurt.Engine/STT/STTPrompt.cs`, `Windows/src/Blurt.Engine/Pipeline/DictationSession.cs`, `Windows/src/Blurt.Engine/Pipeline/DictationSession.Press.cs`, `Windows/src/Blurt.Engine/Pipeline/DictationSession.Pipeline.cs`
- Modify tests: `Windows/tests/Blurt.Engine.Tests/Stubs/DictationSessionTestSupport.cs` (`SessionHarness` gains `textShortcuts` and `textShortcutsProvider`), `Windows/tests/Blurt.Engine.Tests/Stubs/TranscriberTestSupport.cs` (`Context(...)` gains `textShortcuts`), `Windows/tests/Blurt.Engine.Tests/Pipeline/RecentDictationsTests.cs`, `Windows/tests/Blurt.Engine.Tests/STT/STTPromptTests.cs`, `Windows/tests/Blurt.Engine.Tests/STT/TranscriptionContextTests.cs`, every `new TranscriptionContext(` call site (13 across `src` and `tests`)
- Create test: `Windows/tests/Blurt.Engine.Tests/Pipeline/DictationSessionTextShortcutTests.cs`

**Swift spec:** at `upstream/main`: `Sources/BlurtEngine/Pipeline/RecentDictations.swift`, `STT/STTPrompt.swift`, `STT/TranscriptionContext.swift`, `Pipeline/DictationSession+Pipeline.swift`, `Pipeline/DictationSession+Press.swift`; `Tests/BlurtEngineTests/TextShortcutTests.swift` suite `DictationSession text shortcuts`; `RecentDictationsTests.swift` (`spokenOldestFirst`).

**Interfaces:**

- Consumes: `TextShortcut`, `TextShortcutExpander` (Task 1).
- Produces: `RecentDictations.Entry(Guid Id, string Text, string Spoken, DateTimeOffset Timestamp, string? Style)`; `RecentDictations.Record(string text, string? spoken, string? style, DateTimeOffset at)` (null `spoken` means same as `text`); `RecentDictations.SpokenOldestFirst` (replaces `TranscriptsOldestFirst`; `ReservedHeight` stays); `TranscriptionContext(string? AppName, string? WindowTitle, string? FieldLabel, string? PriorText, string? SelectedText, IReadOnlyList<string> RecentTranscripts, IReadOnlyList<string> KeyTerms, IReadOnlyList<TextShortcut> TextShortcuts, bool TargetIsSecure)`; `DictationSession` ctor gains `Func<IReadOnlyList<TextShortcut>>? textShortcutsProvider = null` right after `styleNameProvider`.

- [ ] **Step 1: Write the failing session tests** (`DictationSessionTextShortcutTests.cs`)

```csharp
using Blurt.Engine.Config;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionTextShortcutTests
{
    private static readonly TextShortcut Email = new("personal email", "me@example.com");

    [Test]
    public async Task ThePastedTextAndTheDeliveredTranscriptAreExpandedButTheLogIsNot()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Mail personal email." },
            textShortcuts: [Email]);

        await harness.DictateAsync();

        harness.Injector.Inserted.Should().Equal("Mail me@example.com.");
        harness.Deliveries.Select(d => d.Text).Should().Equal("Mail me@example.com.");
        var logged = (await harness.Log.TranscriptsAsync()).Should().ContainSingle().Subject;
        ((string?)logged["transcript"]).Should().Be("Mail personal email.");
    }

    [Test]
    public async Task TheNextRequestsHistoryCarriesTheSpokenTextNotTheExpansion()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Mail personal email." },
            textShortcuts: [Email]);

        await harness.DictateAsync();
        await harness.DictateAsync();

        harness.Transcriber.ReceivedContexts[^1]!.RecentTranscripts.Should().Equal("Mail personal email.");
        harness.Deliveries[^1].Recent.Entries[0].Text.Should().Be("Mail me@example.com.");
        harness.Deliveries[^1].Recent.Entries[0].Spoken.Should().Be("Mail personal email.");
    }

    [Test]
    public async Task TheTextBeforeTheCaretGoesOutWithExpansionsPutBackToTriggers()
    {
        var focus = new StubFocusCapture
        {
            FieldContext = new FocusedFieldContext(null, null, "Mail me@example.com.", null, IsSecure: false),
        };
        await using var harness = new SessionHarness(focus: focus, textShortcuts: [Email]);

        await harness.DictateAsync();

        var context = harness.Transcriber.ReceivedContexts[^1]!;
        context.PriorText.Should().Be("Mail me@example.com.");
        context.TextShortcuts.Should().Equal(Email);
        STTPrompt.Text(context).Should().Be("Mail personal email.");
    }

    [Test]
    public async Task ShortcutsAreReadForTheContextOnlyWhenThereIsPriorTextToScrub()
    {
        var reads = 0;
        await using var harness = new SessionHarness(textShortcutsProvider: () =>
        {
            reads++;
            return [Email];
        });

        await harness.DictateAsync();

        // One read for the expansion before the paste, none for the context: the field had no prior text.
        reads.Should().Be(1);
        harness.Transcriber.ReceivedContexts.Should().Equal([null]);
    }
}
```

- [ ] **Step 2: Extend the harness and the context helper**

In `SessionHarness` add parameters `IReadOnlyList<TextShortcut>? textShortcuts = null` and `Func<IReadOnlyList<TextShortcut>>? textShortcutsProvider = null`; pass `textShortcutsProvider ?? (() => textShortcuts ?? [])` as the session's `textShortcutsProvider` argument (after `styleNameProvider`). In `TranscriberTestSupport.Context` add `TextShortcut[]? textShortcuts = null` and pass `textShortcuts ?? []` before `false`.

- [ ] **Step 3: Run the new suite to verify it fails**

Run: `dotnet test Windows/tests/Blurt.Engine.Tests --filter "FullyQualifiedName~DictationSessionTextShortcutTests"`
Expected: build failure (no `Spoken`, no `TextShortcuts`, no `textShortcuts` parameter).

- [ ] **Step 4: Update `RecentDictations`**

```csharp
    /// <summary>Every remembered transcript as spoken, oldest first, which is the order the request context wants.</summary>
    public IReadOnlyList<string> SpokenOldestFirst => _entries.AsEnumerable().Reverse().Select(e => e.Spoken).ToList();

    /// <summary>
    /// Records <paramref name="text"/> (what was pasted) made at <paramref name="at"/> with the custom
    /// style name <paramref name="style"/> at the front, dropping the oldest entries beyond
    /// <see cref="Capacity"/>. <paramref name="spoken"/> is the transcript before text shortcuts
    /// changed it; null means the two are the same.
    /// </summary>
    public void Record(string text, string? spoken, string? style, DateTimeOffset at)
    {
        _entries.Insert(0, new Entry(Guid.NewGuid(), text, spoken ?? text, at, style));
        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
    }
```

`Entry` becomes `public sealed record Entry(Guid Id, string Text, string Spoken, DateTimeOffset Timestamp, string? Style)` with `<param name="Text">What was pasted, text shortcuts expanded; what the Recent row shows and copies.</param>` and `<param name="Spoken">What the service returned, before any text shortcut was expanded. This, not Text, rides the next request's history.</param>`. Remove `TranscriptsOldestFirst`.

Update `RecentDictationsTests`: every `Record(x, null, t)` becomes `Record(x, null, null, t)`; rename the oldest-first test to `ProjectsTheSpokenHistoryOldestFirstForTheRequestContext` asserting `SpokenOldestFirst`; add:

```csharp
    [Test]
    public void AnEntryKeepsTheSpokenTextBesideThePastedOne()
    {
        var recent = new RecentDictations();

        recent.Record("Mail me@example.com.", "Mail personal email.", null, Epoch);

        recent.Entries[0].Text.Should().Be("Mail me@example.com.");
        recent.Entries[0].Spoken.Should().Be("Mail personal email.");
        recent.SpokenOldestFirst.Should().Equal("Mail personal email.");
    }
```

- [ ] **Step 5: Update `TranscriptionContext`**

Add the positional parameter `IReadOnlyList<TextShortcut> TextShortcuts` between `KeyTerms` and `TargetIsSecure`, documented as `<param name="TextShortcuts">The user's text shortcuts. Not sent: <see cref="STTPrompt"/> uses them to put a pasted expansion in <paramref name="PriorText"/> back to its trigger. Empty when there is no prior text to scrub.</param>`. `IsEmpty` is unchanged. `Equals` adds `&& TextShortcuts.SequenceEqual(other.TextShortcuts)`; `GetHashCode` adds each shortcut. Update every `new TranscriptionContext(` call site (`DictationSession.Press.cs` x2, tests) to pass `[]` or the shortcuts. Add to `TranscriptionContextTests`: a context with shortcuts and nothing else is still `IsEmpty`, and two contexts differing only in shortcuts are not equal.

- [ ] **Step 6: Update `STTPrompt.Text`**

```csharp
        var recent = context.RecentTranscripts.Select(t => t.TrimmedNonEmpty()).OfType<string>().ToList();
        // Scrubbed here, where the wire string is built, rather than at capture: the log's prior
        // text and the paste separator read the field text as it was.
        var scrubbed = context.PriorText is null
            ? null
            : TextShortcutExpander.RedactingExpansions(context.PriorText, context.TextShortcuts);
        if (scrubbed.TrimmedNonEmpty() is not { } prior)
        {
            return Fitted(recent);
        }
```

Add to `STTPromptTests`:

```csharp
    [Test]
    public void APastedExpansionInThePriorTextGoesOutAsItsTrigger()
    {
        var email = new TextShortcut("personal email", "me@example.com");
        var context = Context(priorText: "Mail me@example.com.", textShortcuts: [email]);

        STTPrompt.Text(context).Should().Be("Mail personal email.");
    }

    [Test]
    public void ScrubbingHappensBeforeTheHistoryDedupe()
    {
        var email = new TextShortcut("personal email", "me@example.com");
        var context = Context(priorText: "Mail me@example.com.", recentTranscripts: ["Mail personal email."], textShortcuts: [email]);

        STTPrompt.Text(context).Should().Be("Mail personal email.");
    }
```

- [ ] **Step 7: Update `DictationSession`**

Field `private readonly Func<IReadOnlyList<TextShortcut>> _textShortcutsProvider;` set from the new ctor parameter (`?? (static () => [])`), plus:

```csharp
    /// <summary>The user's shortcuts, or none when the provider throws (logged, never onto the dictation path).</summary>
    private IReadOnlyList<TextShortcut> ReadShortcuts()
    {
        try
        {
            return _textShortcutsProvider();
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"textShortcutsProvider threw {ex.GetType().Name}");
            return [];
        }
    }
```

In `DictationSession.Press.cs`: `BeginContextCaptureAsync` reads `recent = _recent.SpokenOldestFirst`; the press-known context passes `[]` for shortcuts; `ReadFieldContext` builds the resolved context with `field.PriorText is null ? [] : ReadShortcuts()`; the fail-closed branch passes `[]`.

In `DictationSession.Pipeline.cs` `TranscribeAndInjectAsync`, replace the trimming block with:

```csharp
        // The log keeps what the service returned; the paste and the Recent list get the expanded
        // text, and the ring also keeps the spoken version.
        var expanded = TextShortcutExpander.Expand(text, ReadShortcuts());
        var spoken = text.TrimmedNonEmpty();
        var trimmed = expanded.TrimmedNonEmpty();
        if (spoken is null || trimmed is null)
        {
            TryAdvance(run, PipelinePhase.Transcribing, PipelinePhase.Idle);
            return;
        }
```

Record with `_recent.Record(trimmed, spoken, style, now);`, keep `_log.Append(text, context);`, deliver `trimmed`, and `await InjectAsync(run, expanded, context);`. `_delivered` keeps `text.Length`.

- [ ] **Step 8: Run the whole engine suite and format**

Run: `dotnet test Windows/tests/Blurt.Engine.Tests` and `dotnet format Windows/Blurt.sln --verify-no-changes`
Expected: green; the new suite's four tests pass; no other suite regressed.

- [ ] **Step 9: Commit**

```bash
git add Windows/src/Blurt.Engine Windows/tests/Blurt.Engine.Tests
git commit -m "feat(windows): expand text shortcuts before the paste, keep the wire spoken"
```

---

### Task 3: Vocabulary tab: key terms and the text shortcuts list (app)

**Files:**

- Create: `Windows/src/Blurt.App/Settings/VocabularyTab.xaml`, `VocabularyTab.xaml.cs`, `Windows/src/Blurt.App/Settings/VocabularyViewModel.cs`, `Windows/src/Blurt.App/Settings/TextShortcutEditor.xaml`, `TextShortcutEditor.xaml.cs`
- Modify: `Windows/src/Blurt.App/Services/SettingsStores.cs` (add `TextShortcuts`), `Windows/src/Blurt.App/Services/Converters.cs` (add `ZeroToVisible`), `Windows/src/Blurt.App/Settings/SettingsViewModel.cs` (add `Vocabulary`), `Windows/src/Blurt.App/Settings/SettingsWindow.xaml` (`VocabularyItem` tab between General and Advanced), `Windows/src/Blurt.App/App.xaml.cs` (construct `VocabularyViewModel`), `Windows/src/Blurt.App/AppCoordinator.cs` (pass `textShortcutsProvider: () => settings.TextShortcuts.Shortcuts`)

**Interfaces:**

- Consumes: `TextShortcutStore`, `TextShortcutDraft`, `KeyTermsStore.Raw`.
- Produces: `VocabularyViewModel(SettingsStores)` with `string KeyTerms { get; set; }`, `ObservableCollection<TextShortcut> Shortcuts`, `TextShortcut? Selected { get; set; }`, `bool CanAdd`, `bool CanEditOrRemove`, `IReadOnlySet<string> OtherKeys(Guid id)`, `void Save(TextShortcut edited)`, `void Delete(Guid id)`, `void Reload()`; `TextShortcutEditor(TextShortcut shortcut, bool isExisting, IReadOnlySet<string> otherKeys)` window with `TextShortcut? Result` and `bool Deleted`.

- [ ] **Step 1: `VocabularyViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using Blurt.App.Services;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The Vocabulary tab: the key terms string and the text shortcuts list. Every write goes through
/// its store, then the view model re-reads the store. Use on the UI thread.
/// </summary>
public sealed class VocabularyViewModel : ObservableObject
{
    private readonly KeyTermsStore _keyTerms;
    private readonly TextShortcutStore _shortcuts;
    private TextShortcut? _selected;

    /// <summary>Creates the view model over the key terms and text shortcut stores in <paramref name="settings"/>.</summary>
    public VocabularyViewModel(SettingsStores settings)
    {
        _keyTerms = settings.KeyTerms;
        _shortcuts = settings.TextShortcuts;
        Reload();
    }

    /// <summary>The raw comma-separated key terms as typed; blank clears them.</summary>
    public string KeyTerms
    {
        get => _keyTerms.Raw ?? string.Empty;
        set
        {
            if (value == KeyTerms)
            {
                return;
            }

            _keyTerms.Raw = value;
            Raise(nameof(KeyTerms));
        }
    }

    /// <summary>The stored shortcuts, in the user's order.</summary>
    public ObservableCollection<TextShortcut> Shortcuts { get; } = [];

    /// <summary>The list's selected row, or null.</summary>
    public TextShortcut? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            _selected = value;
            Raise(nameof(Selected), nameof(CanEditOrRemove));
        }
    }

    /// <summary>Whether another shortcut may be added.</summary>
    public bool CanAdd => Shortcuts.Count < TextShortcutStore.ShortcutLimit;

    /// <summary>Whether a row is selected.</summary>
    public bool CanEditOrRemove => Selected is not null;

    /// <summary>The match keys of every stored shortcut other than <paramref name="id"/>, for the editor's duplicate check.</summary>
    public IReadOnlySet<string> OtherKeys(Guid id) =>
        _shortcuts.Shortcuts.Where(s => s.Id != id).Select(s => TextShortcutStore.MatchKey(s.Trigger)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Writes <paramref name="edited"/> into the stored list, replacing the shortcut with its id or appending it.</summary>
    public void Save(TextShortcut edited)
    {
        var updated = _shortcuts.Shortcuts.ToList();
        var index = updated.FindIndex(s => s.Id == edited.Id);
        if (index >= 0)
        {
            updated[index] = edited;
        }
        else
        {
            updated.Add(edited);
        }

        _shortcuts.Shortcuts = updated;
        Reload();
        Selected = Shortcuts.FirstOrDefault(s => s.Id == edited.Id);
    }

    /// <summary>Removes the shortcut carrying <paramref name="id"/>.</summary>
    public void Delete(Guid id)
    {
        _shortcuts.Shortcuts = _shortcuts.Shortcuts.Where(s => s.Id != id).ToList();
        Reload();
    }

    /// <summary>Re-reads both stores. A selection naming a removed row is cleared.</summary>
    public void Reload()
    {
        var selectedId = Selected?.Id;
        Shortcuts.Clear();
        foreach (var shortcut in _shortcuts.Shortcuts)
        {
            Shortcuts.Add(shortcut);
        }

        Selected = Shortcuts.FirstOrDefault(s => s.Id == selectedId);
        Raise(nameof(KeyTerms), nameof(CanAdd));
    }
}
```

- [ ] **Step 2: `VocabularyTab.xaml`** (the Key Terms card, then the Text Shortcuts card)

```xml
<UserControl x:Class="Blurt.App.Settings.VocabularyTab"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:svc="clr-namespace:Blurt.App.Services">
  <ScrollViewer VerticalScrollBarVisibility="Auto">
    <StackPanel Margin="20" DataContext="{Binding Vocabulary}">
      <TextBlock Style="{StaticResource SectionHeader}" Text="Key Terms" />
      <Border Style="{StaticResource Card}">
        <TextBox Padding="4,3"
                 AutomationProperties.Name="Key terms"
                 Text="{Binding KeyTerms, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" />
      </Border>
      <TextBlock Style="{StaticResource Footer}"
                 Margin="0,-8,0,20"
                 Text="Names and jargon Blurt should spell your way, separated by commas." />

      <TextBlock Style="{StaticResource SectionHeader}" Text="Text Shortcuts" />
      <Border Style="{StaticResource Card}">
        <StackPanel>
          <Grid>
            <ListView x:Name="ShortcutList"
                      Height="160"
                      AutomationProperties.Name="Text shortcuts"
                      ItemsSource="{Binding Shortcuts, Mode=OneWay}"
                      SelectedItem="{Binding Selected, Mode=TwoWay}"
                      MouseDoubleClick="OnListDoubleClick"
                      KeyDown="OnListKeyDown">
              <ListView.View>
                <GridView>
                  <GridViewColumn Width="150" Header="Phrase" DisplayMemberBinding="{Binding Trigger, Mode=OneWay}" />
                  <GridViewColumn Width="260" Header="Replacement" DisplayMemberBinding="{Binding Expansion, Mode=OneWay}" />
                </GridView>
              </ListView.View>
            </ListView>
            <TextBlock HorizontalAlignment="Center"
                       VerticalAlignment="Center"
                       Foreground="{StaticResource Secondary}"
                       IsHitTestVisible="False"
                       Text="No shortcuts"
                       Visibility="{Binding Shortcuts.Count, Mode=OneWay, Converter={x:Static svc:ZeroToVisible.Instance}}" />
          </Grid>
          <StackPanel Margin="0,8,0,0" Orientation="Horizontal">
            <Button Padding="10,2" Content="Add…" IsEnabled="{Binding CanAdd, Mode=OneWay}" Click="OnAddClick" />
            <Button Margin="8,0,0,0" Padding="10,2" Content="Edit…" IsEnabled="{Binding CanEditOrRemove, Mode=OneWay}" Click="OnEditClick" />
            <Button Margin="8,0,0,0" Padding="10,2" Content="Remove" IsEnabled="{Binding CanEditOrRemove, Mode=OneWay}" Click="OnRemoveClick" />
          </StackPanel>
        </StackPanel>
      </Border>
      <TextBlock Style="{StaticResource Footer}"
                 Margin="0,-8,0,0"
                 Text="Say a phrase while dictating and it's replaced with your saved text. For example, “personal email” becomes your address." />
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

`ZeroToVisible` in `Converters.cs`: an `IValueConverter` singleton (`public static ZeroToVisible Instance { get; } = new();`) returning `Visibility.Visible` when the value is the integer `0` and `Visibility.Collapsed` otherwise; `ConvertBack` throws `NotSupportedException`. Follow the shape of the converters already in that file.

- [ ] **Step 3: `VocabularyTab.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The Vocabulary tab: key terms and the text shortcuts list with its editor. Double-click or
/// Enter edits the selected row; Delete removes it without confirmation. Binds to a
/// <see cref="SettingsViewModel"/>.
/// </summary>
public partial class VocabularyTab : UserControl
{
    /// <summary>Creates the tab.</summary>
    public VocabularyTab()
    {
        InitializeComponent();
    }

    private VocabularyViewModel? Model => (DataContext as SettingsViewModel)?.Vocabulary;

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (Model is { CanAdd: true } model)
        {
            OpenEditor(model, new TextShortcut(string.Empty, string.Empty), isExisting: false);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e) => EditSelected();

    private void OnRemoveClick(object sender, RoutedEventArgs e) => RemoveSelected();

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                EditSelected();
                e.Handled = true;
                break;
            case Key.Delete:
                RemoveSelected();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private void EditSelected()
    {
        if (Model is { Selected: { } selected } model)
        {
            OpenEditor(model, selected, isExisting: true);
        }
    }

    private void RemoveSelected()
    {
        if (Model is { Selected: { } selected } model)
        {
            model.Delete(selected.Id);
        }
    }

    private void OpenEditor(VocabularyViewModel model, TextShortcut shortcut, bool isExisting)
    {
        var editor = new TextShortcutEditor(shortcut, isExisting, model.OtherKeys(shortcut.Id)) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        if (editor.Deleted)
        {
            model.Delete(shortcut.Id);
        }
        else if (editor.Result is { } edited)
        {
            model.Save(edited);
        }
    }
}
```

- [ ] **Step 4: `TextShortcutEditor.xaml` and its code-behind**

Same window shape as `StyleProfileEditor.xaml` (Width 440, `SizeToContent="Height"`, `NoResize`, `ShowInTaskbar="False"`, `CenterOwner`): title "Text Shortcut", subtitle "When you say the phrase, Blurt pastes the replacement instead.", a "Phrase" `TextBox x:Name="PhraseField"` (`MaxLength` set in code to `TextShortcutStore.TriggerLimit`, `AutomationProperties.Name="Phrase"`, `TextChanged="OnFieldChanged"`), a red caption `TextBlock x:Name="DuplicateWarning"` with text "Another shortcut already uses this phrase." (`Foreground="{StaticResource ErrorText}"`, `FontSize="11"`), a "Replacement" `TextBox x:Name="ReplacementField"` (`AcceptsReturn="True"`, `MinLines="1"`, `MaxLines="6"`, `TextWrapping="Wrap"`, `AutomationProperties.Name="Replacement"`, `TextChanged="OnReplacementChanged"`), then the Delete / Cancel / Save row exactly as the style editor's. Code-behind:

```csharp
using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The modal text shortcut editor. The phrase is capped at <see cref="TextShortcutStore.TriggerLimit"/>
/// and the replacement at <see cref="TextShortcutStore.ExpansionLimit"/> characters as the user
/// types; Save needs a matchable phrase, a replacement, and no other shortcut on the same phrase.
/// Delete appears only for a stored shortcut.
/// </summary>
public partial class TextShortcutEditor : Window
{
    private readonly TextShortcut _shortcut;
    private readonly IReadOnlySet<string> _otherKeys;
    private bool _clamping;

    /// <summary>Creates the editor for <paramref name="shortcut"/>; <paramref name="otherKeys"/> are the other shortcuts' match keys.</summary>
    public TextShortcutEditor(TextShortcut shortcut, bool isExisting, IReadOnlySet<string> otherKeys)
    {
        InitializeComponent();
        _shortcut = shortcut;
        _otherKeys = otherKeys;
        DeleteButton.Visibility = isExisting ? Visibility.Visible : Visibility.Collapsed;
        PhraseField.MaxLength = TextShortcutStore.TriggerLimit;
        PhraseField.Text = shortcut.Trigger;
        ReplacementField.Text = shortcut.Expansion;
        Refresh();
        Loaded += (_, _) => PhraseField.Focus();
    }

    /// <summary>The edited shortcut after Save, with the original id; null otherwise.</summary>
    public TextShortcut? Result { get; private set; }

    /// <summary>Whether the user chose Delete.</summary>
    public bool Deleted { get; private set; }

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnReplacementChanged(object sender, TextChangedEventArgs e)
    {
        if (_clamping)
        {
            return;
        }

        var clamped = TextShortcutDraft.ClampExpansion(ReplacementField.Text);
        if (clamped != ReplacementField.Text)
        {
            _clamping = true;
            var caret = Math.Min(ReplacementField.CaretIndex, clamped.Length);
            ReplacementField.Text = clamped;
            ReplacementField.CaretIndex = caret;
            _clamping = false;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (SaveButton is null || DuplicateWarning is null)
        {
            return;
        }

        DuplicateWarning.Visibility = TextShortcutDraft.DuplicatesAnother(PhraseField.Text, _otherKeys) ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = TextShortcutDraft.CanSave(PhraseField.Text, ReplacementField.Text, _otherKeys);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TextShortcutDraft.CanSave(PhraseField.Text, ReplacementField.Text, _otherKeys))
        {
            return;
        }

        Result = _shortcut with { Trigger = PhraseField.Text, Expansion = ReplacementField.Text };
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Deleted = true;
        DialogResult = true;
    }
}
```

- [ ] **Step 5: Wire the tab and the provider**

`SettingsStores`: `TextShortcuts = new TextShortcutStore(store);` plus `public TextShortcutStore TextShortcuts { get; }` documented "The spoken phrases expanded into saved text." `SettingsViewModel`: ctor takes `VocabularyViewModel vocabulary`, exposes `public VocabularyViewModel Vocabulary { get; }`, and `Reload()` also calls `Vocabulary.Reload()`. `SettingsWindow.xaml`: `<TabItem x:Name="VocabularyItem" Header="Vocabulary"><settings:VocabularyTab /></TabItem>` between the two existing tabs; update the class doc comment to name the three tabs. `App.xaml.cs`: `var vocabulary = new VocabularyViewModel(settings);` passed into `SettingsViewModel`. `AppCoordinator`: `textShortcutsProvider: () => settings.TextShortcuts.Shortcuts` in the `DictationSession` construction.

- [ ] **Step 6: Build, format, and check the tab by hand**

Run: `dotnet build Windows/Blurt.sln -warnaserror` and `dotnet format Windows/Blurt.sln --verify-no-changes`, then `dotnet run --project Windows/src/Blurt.App`; open Settings → Vocabulary, add a shortcut "test phrase" → "expanded", edit it, remove it, type key terms and switch tabs; quit from the tray. Confirm `%LOCALAPPDATA%\Blurt-dev\settings.json` holds `BlurtTextShortcuts` and `BlurtKeyTerms`. Do not dictate, and do not leave the app running.

- [ ] **Step 7: Commit**

```bash
git add Windows/src/Blurt.App
git commit -m "feat(windows): vocabulary tab with key terms and text shortcuts"
```

---

### Task 4: Drop the same-window separator after the user types or clicks

**Files:**

- Create: `Windows/src/Blurt.Engine.Windows/Hotkey/UserInputClock.cs`, `Windows/src/Blurt.Engine.Windows/Hotkey/LowLevelHook.cs`, `Windows/src/Blurt.Engine.Windows/Hotkey/LowLevelMouseHook.cs`
- Modify: `Windows/src/Blurt.Engine.Windows/Hotkey/LowLevelKeyboardHook.cs` (becomes a thin subclass of `LowLevelHook`), `Windows/src/Blurt.Engine.Windows/Hotkey/Interop/User32Hook.cs` (`WhMouseLl = 14`, `MsLlHookStruct`), `Windows/src/Blurt.Engine.Windows/Hotkey/HookEventReducer.cs` (`IsHardwareKeyDown`), `Windows/src/Blurt.Engine.Windows/Hotkey/DictationKeyHook.cs` (`onHardwareKeyDown`), `Windows/src/Blurt.Engine.Windows/Injection/ClipboardInjector.cs` (`timeSinceUserInput` seam, timestamped `_lastInserted`), `Windows/src/Blurt.App/DictationComponents.cs` (`UserInput`), `Windows/src/Blurt.App/AppCoordinator.cs` (feed the clock, install the mouse hook)
- Test: create `Windows/tests/Blurt.Engine.Windows.Tests/Hotkey/UserInputClockTests.cs`; modify `HookEventReducerTests.cs`, `DictationKeyHookTests.cs`, `Injection/ClipboardInjectorTests.cs`

**Swift spec:** `Sources/BlurtEngine/Injection/KeyInjector.swift` (`lastInserted`, `secondsSinceUserInput`) and `KeyInjector+SystemActions.swift` (`secondsSinceHardwareInput`) at `upstream/main`; `Tests/BlurtEngineTests/KeyInjectorInsertTests.swift` (`opaqueEditorUserInputDropsSeparator`).

**Interfaces:**

- Produces: `public sealed class UserInputClock(TimeProvider? clock = null)` with `void Touch()` and `TimeSpan TimeSinceInput` (`TimeSpan.MaxValue` before the first touch); `HookEventReducer.IsHardwareKeyDown(int message, int vkCode, nuint extraInfo, int triggerKeyCode)`; `DictationKeyHook` ctor gains `Action? onHardwareKeyDown = null` as its last parameter before `clock`; `LowLevelMouseHook : LowLevelHook` raising `event Action<int, uint, nuint>? RawMouse` (message, flags, extraInfo) for button-down messages only; `ClipboardInjector` seam ctor gains `Func<TimeSpan> timeSinceUserInput` after `isOpaqueApp`; the production ctor becomes `ClipboardInjector(UiaFocusCapture focus, UserInputClock userInput, TimeSpan? pasteSettle = null)`; `DictationComponents` gains `UserInputClock UserInput`.

- [ ] **Step 1: Write the failing injector test** (append to `ClipboardInjectorTests`)

```csharp
    [Test]
    public async Task TypingOrClickingAfterAPasteDropsTheSameWindowSeparator()
    {
        // In an editor whose caret is unreadable, pressing Enter between two dictations keeps the
        // window title but moves the caret off the last paste, so a leading space there is stray.
        var harness = new Harness(null, TimeSpan.Zero) { TimeSinceUserInput = TimeSpan.Zero };
        await harness.Injector.SetTargetAsync(Target);

        await harness.Injector.InsertAsync("First.", null, "Doc - Google Docs", CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await harness.Injector.InsertAsync("Second.", null, "Doc - Google Docs", CancellationToken.None);

        harness.Pasted.Should().Equal("First.", "Second.");
    }

    [Test]
    public async Task InputOlderThanTheLastPasteKeepsTheSameWindowSeparator()
    {
        var harness = new Harness(null, TimeSpan.Zero) { TimeSinceUserInput = TimeSpan.FromMinutes(5) };
        await harness.Injector.SetTargetAsync(Target);

        await harness.Injector.InsertAsync("First.", null, "notes.txt — Editor", CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        await harness.Injector.InsertAsync("Second.", null, "notes.txt — Editor", CancellationToken.None);

        harness.Pasted.Should().Equal("First.", " Second.");
    }
```

In `Harness`, add `public TimeSpan TimeSinceUserInput { get; set; } = TimeSpan.MaxValue;` and pass `() => TimeSinceUserInput` to the injector after `IsOpaque`.

- [ ] **Step 2: Run to verify it fails** (compile error on the ctor).

- [ ] **Step 3: Implement the injector seam**

Field `private readonly Func<TimeSpan> _timeSinceUserInput;` and `private (ResolvedInsert Resolved, long At)? _lastInserted;`. In `PerformInsertAsync`, compute continuity under the lock:

```csharp
        ResolvedInsert? continuity = null;
        lock (_gate)
        {
            target = _target;
            // The last paste still precedes the caret only if the user has not typed or clicked
            // since it; otherwise it is forgotten, as a window change would be.
            if (_lastInserted is { } last && _timeSinceUserInput() >= _clock.GetElapsedTime(last.At))
            {
                continuity = last.Resolved;
            }
        }

        var resolved = InsertResolver.Resolve(text, priorText, windowTitle, target?.ProcessId, continuity);
```

and after the paste posts: `_lastInserted = (resolved, _clock.GetTimestamp());`. The production ctor passes `() => userInput.TimeSinceInput`. Document the parameter: "How long since the user last pressed a key or clicked; `TimeSpan.MaxValue` when never."

- [ ] **Step 4: Run the injector suite** (`--filter "FullyQualifiedName~ClipboardInjectorTests"`): green, including the existing `ASecondDictationIntoTheSameWindowGetsASeparatingSpace` (its harness default is `TimeSpan.MaxValue`).

- [ ] **Step 5: Write the failing clock and reducer tests**

```csharp
using Blurt.Engine.Windows.Hotkey;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Blurt.Engine.Windows.Tests.Hotkey;

public sealed class UserInputClockTests
{
    [Test]
    public void BeforeAnyInputTheElapsedTimeIsInfinite()
    {
        new UserInputClock(new FakeTimeProvider()).TimeSinceInput.Should().Be(TimeSpan.MaxValue);
    }

    [Test]
    public void ATouchStartsTheClockAndLaterTouchesRestartIt()
    {
        var time = new FakeTimeProvider();
        var clock = new UserInputClock(time);

        clock.Touch();
        time.Advance(TimeSpan.FromSeconds(3));
        var afterFirst = clock.TimeSinceInput;
        clock.Touch();
        var afterSecond = clock.TimeSinceInput;

        afterFirst.Should().Be(TimeSpan.FromSeconds(3));
        afterSecond.Should().Be(TimeSpan.Zero);
    }
}
```

Append to `HookEventReducerTests`:

```csharp
    [TestCase(HookEventReducer.WmKeyDown, 0x41, 0u, true)]
    [TestCase(HookEventReducer.WmSysKeyDown, 0x41, 0u, true)]
    [TestCase(HookEventReducer.WmKeyUp, 0x41, 0u, false)]
    [TestCase(HookEventReducer.WmKeyDown, 0xA3, 0u, false)]
    public void HardwareKeyDownIsADownOfAnyKeyButTheTriggerThatBlurtDidNotInject(int message, int vk, uint unused, bool expected)
    {
        HookEventReducer.IsHardwareKeyDown(message, vk, 0, triggerKeyCode: 0xA3).Should().Be(expected);
    }

    [Test]
    public void AnInjectedKeyDownIsNotHardwareInput()
    {
        HookEventReducer.IsHardwareKeyDown(HookEventReducer.WmKeyDown, 0x56, HookEventReducer.InjectedTag, triggerKeyCode: 0xA3).Should().BeFalse();
    }
```

Append to `DictationKeyHookTests` (the `CreateHook` helper gains `onHardwareKeyDown: () => _calls.Add("input")`):

```csharp
    [Test]
    public void TheTriggerKeyAndInjectedKeysDoNotTouchTheInputClock()
    {
        _hook.HandleRawKey(HookEventReducer.WmKeyDown, RightControl, ControlScanCode, 0, 0);
        _hook.HandleRawKey(HookEventReducer.WmKeyUp, RightControl, ControlScanCode, HookEventReducer.LlkhfUp, 0);
        _hook.HandleRawKey(HookEventReducer.WmKeyDown, KeyV, 0, HookEventReducer.LlkhfInjected, HookEventReducer.InjectedTag);

        _calls.Should().NotContain("input");
    }

    [Test]
    public void AnOrdinaryKeyDownTouchesTheInputClockBeforeItIsRouted()
    {
        _hook.HandleRawKey(HookEventReducer.WmKeyDown, KeyC, 0, 0, 0);

        _calls.Should().Equal("input");
    }
```

- [ ] **Step 6: Implement the clock, the reducer rule, and the hook callback**

```csharp
namespace Blurt.Engine.Windows.Hotkey;

/// <summary>
/// When the user last pressed a key or clicked, as the low-level hooks report it. Synthetic input
/// never reaches it: the keyboard hook skips Blurt's own tagged keystrokes and the trigger key, and
/// the mouse hook skips injected buttons. Thread-safe.
/// </summary>
public sealed class UserInputClock(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long _lastInput;
    private int _touched;

    /// <summary>How long since the last hardware key-down or button-down, or <see cref="TimeSpan.MaxValue"/> before any.</summary>
    public TimeSpan TimeSinceInput =>
        Volatile.Read(ref _touched) == 0 ? TimeSpan.MaxValue : _clock.GetElapsedTime(Volatile.Read(ref _lastInput));

    /// <summary>Records that hardware input just happened.</summary>
    public void Touch()
    {
        Volatile.Write(ref _lastInput, _clock.GetTimestamp());
        Volatile.Write(ref _touched, 1);
    }
}
```

`HookEventReducer.IsHardwareKeyDown`: returns `extraInfo != InjectedTag && vkCode != triggerKeyCode && message is WmKeyDown or WmSysKeyDown`. In `DictationKeyHook.HandleRawKey`, before the lock: `if (_onHardwareKeyDown is { } touch && HookEventReducer.IsHardwareKeyDown(message, vkCode, extraInfo, triggerKeyCode)) touch();` where `triggerKeyCode` is read under `_routerGate` (read `_router.TriggerKeyCode` inside the existing lock and call `touch()` after releasing it; keep the callback outside the lock).

- [ ] **Step 7: Extract `LowLevelHook` and add the mouse hook**

Move the thread, message loop, install, and dispose logic of `LowLevelKeyboardHook` into `public abstract class LowLevelHook(int idHook, string threadName) : IDisposable` with `protected abstract void OnHook(nint wParam, nint lParam)` called for `nCode >= 0` inside the same try/catch. `LowLevelKeyboardHook : LowLevelHook` passes `User32Hook.WhKeyboardLl` and overrides `OnHook` to raise `RawKey` as today. `LowLevelMouseHook : LowLevelHook` passes `User32Hook.WhMouseLl` (`14`), reads `MsLlHookStruct { Point Pt; uint MouseData; uint Flags; uint Time; nuint ExtraInfo; }` (`Point { int X; int Y; }`), and raises `RawMouse(message, flags, extraInfo)` only for `WM_LBUTTONDOWN 0x0201`, `WM_RBUTTONDOWN 0x0204`, `WM_MBUTTONDOWN 0x0207`, `WM_XBUTTONDOWN 0x020B`; every other message returns at once. `LLMHF_INJECTED` is `0x01`: the handler the app installs ignores events with that flag. Keep every existing `LowLevelKeyboardHook` test green.

- [ ] **Step 8: Wire the app**

`DictationComponents` gains `UserInputClock UserInput` (constructed in `Production`, passed to `ClipboardInjector`). `AppCoordinator.Start` passes `onHardwareKeyDown: _components.UserInput.Touch` to `DictationKeyHook`; `ShowOverlayArmed` installs a `LowLevelMouseHook` (field `_mouseHook`) whose `RawMouse` handler calls `Touch()` when `(flags & 0x01) == 0`; `DisposeAsync` disposes it; `RetryKeyHookInstallIfNeeded` also retries the mouse hook when it is not installed.

- [ ] **Step 9: Run both test projects and format**

Run: `dotnet test Windows/Blurt.sln --filter "TestCategory!=Explicit"` and `dotnet format Windows/Blurt.sln --verify-no-changes`
Expected: green.

- [ ] **Step 10: Commit**

```bash
git add Windows/src Windows/tests
git commit -m "fix(windows): drop the same-window separator after the user types or clicks"
```

---

### Task 5: Gate latch, hand-back press, and activation wording (engine + hook)

**Files:**

- Modify: `Windows/src/Blurt.Engine/Hotkey/DictationKeyGate.cs`, `Windows/src/Blurt.Engine/Hotkey/TriggerActivation.cs`, `Windows/src/Blurt.Engine/Pipeline/PipelinePhase.cs`, `Windows/src/Blurt.Engine/Pipeline/DictationSession.Commands.cs`, `Windows/src/Blurt.Engine/Pipeline/DictationSession.cs`, `Windows/src/Blurt.Engine/Pipeline/DictationSession.Press.cs`, `Windows/src/Blurt.Engine/Pipeline/DictationSession.Pipeline.cs`, `Windows/src/Blurt.Engine.Windows/Hotkey/DictationKeyHook.cs`, `Windows/src/Blurt.App/AppCoordinator.cs` (pass an empty `onLatch` for now)
- Tests: modify `Windows/tests/Blurt.Engine.Tests/Hotkey/DictationKeyGateTests.cs`, `DictationKeyRouterTests.cs`, `TriggerActivationTests.cs`, `Pipeline/PipelinePhaseTests.cs`, `Windows/tests/Blurt.Engine.Tests/Stubs/DictationSessionTestSupport.cs` (`DictateAsync(bool handingBack = false)`), `Windows/tests/Blurt.Engine.Windows.Tests/Hotkey/DictationKeyHookTests.cs`; create `Windows/tests/Blurt.Engine.Tests/Pipeline/DictationSessionHandBackTests.cs`

**Swift spec:** at `upstream/main`: `Sources/BlurtEngine/Hotkey/DictationKeyGate.swift`, `Hotkey/TriggerActivation.swift`, `Pipeline/PipelinePhase.swift`, `Pipeline/DictationSession+Commands.swift`, `Pipeline/DictationSession+Pipeline.swift`; `Tests/BlurtEngineTests/DictationSessionHandBackTests.swift`, `DictationKeyGateTests.swift`, `TriggerActivationTests.swift`.

**Interfaces:**

- Produces: `DictationKeyGate.Action.Latch`; `TriggerActivations.StartVerb()`, `FinishHint()`, `StopHint()`; `PipelinePhase.HandedBack(string transcript)` returning a `PipelinePhase.HandedBackPhase(string Transcript)` (public sealed record); `Command.PressHandingBack`; `DictationSession.PressAsync(bool handingBack = false)`; `DictationKeyHook` ctor gains `Action onLatch` after `onCancel`; `DictationKeyHook.ResetGate()`.

- [ ] **Step 1: Gate and router tests.** In `DictationKeyGateTests`, every `Up(..., DictationKeyGate.Action.None)` that is a release *latching from idle* (a tap under tap-or-hold, any release under tap-only, and the first release in "quick tap latches on") becomes `DictationKeyGate.Action.Latch`; a release over an already-latched recording stays `Stop`, and a release in hold-only stays `Stop`. In `DictationKeyRouterTests`, every line marked `// latched` becomes `.Latch`. Run them and confirm they fail.

- [ ] **Step 2: Implement `Latch`.** Add `Latch` to the enum ("A release that latched the recording on: a tap. Dictation keeps recording; read-aloud's router reads it as the moment a tap is known.") and return it from the latching branch of `ModifierUp`. `DictationKeyHook.Invoke` adds `case DictationKeyGate.Action.Latch: _onLatch(); break;` with the new ctor parameter; add `public void ResetGate()` that takes `_routerGate`, calls `_router.Reset()`, and reports nothing (document: "Clears the gate after a read-aloud that ended on its own; no recording is discarded, so nothing is reported."). Update `DictationKeyHookTests.CreateHook` to pass `() => _calls.Add("latch")` and add:

```csharp
    [Test]
    public void ATapReportsALatchAndAHoldDoesNot()
    {
        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(Up(), At(0.2));
        _hook.Dispatch(Down(), At(3));
        _hook.Dispatch(Up(), At(3.2));
        _hook.Dispatch(Down(), At(5));
        _hook.Dispatch(Up(), At(6.5));

        _calls.Should().Equal("start", "latch", "stop", "start", "stop");
    }

    [Test]
    public void ResetGateClearsALatchWithoutReportingADiscard()
    {
        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(Up(), At(0.2));

        _hook.ResetGate();
        _hook.Dispatch(Down(), At(3));

        _calls.Should().Equal("start", "latch", "start");
    }
```

`AppCoordinator` passes `onLatch: static () => { }` until Task 12 wires the router.

- [ ] **Step 3: Activation wording.** Add to `TriggerActivations`:

```csharp
    /// <summary>The verb for pressing the key to start, lowercase for mid-sentence use: "tap" for the tap modes, "hold" for hold-only.</summary>
    public static string StartVerb(this TriggerActivation activation) => activation == TriggerActivation.Hold ? "hold" : "tap";

    /// <summary>How to finish a dictation that is recording, naming only the gestures this mode stops on.</summary>
    public static string FinishHint(this TriggerActivation activation) => $"{StopGesture(activation)} to finish.";

    /// <summary>How to stop something the trigger started that is not a dictation, such as a read-aloud.</summary>
    public static string StopHint(this TriggerActivation activation) => $"{StopGesture(activation)} to stop.";

    private static string StopGesture(TriggerActivation activation) => activation switch
    {
        TriggerActivation.TapOrHold => "Tap again or release",
        TriggerActivation.Tap => "Tap again",
        TriggerActivation.Hold => "Release",
        _ => throw new ArgumentOutOfRangeException(nameof(activation), activation, null),
    };
```

Tests (append to `TriggerActivationTests`): `StartVerb` is `"tap"`, `"tap"`, `"hold"`; `FinishHint` is `"Tap again or release to finish."`, `"Tap again to finish."`, `"Release to finish."`; `StopHint` ends with `" to stop."` and the three are distinct.

- [ ] **Step 4: Write the failing hand-back tests** (`DictationSessionHandBackTests.cs`)

```csharp
using Blurt.Engine.Config;
using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionHandBackTests
{
    [Test]
    public async Task TheTranscriptComesBackAsHandedBackAndNothingIsPasted()
    {
        await using var harness = SessionHarness.WithTranscript("Give me a high-level summary.");

        var phase = await harness.DictateAsync(handingBack: true);

        phase.Should().Be(PipelinePhase.HandedBack("Give me a high-level summary."));
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task ARequestIsNeverRememberedAsADictationNorReportedAsOne()
    {
        await using var harness = SessionHarness.WithTranscript("What does this mean?");

        await harness.DictateAsync(handingBack: true);
        await harness.DictateAsync();

        harness.Transcriber.ReceivedContexts[^1].Should().BeNull();
        harness.Deliveries.Should().ContainSingle().Which.Text.Should().Be("What does this mean?");
    }

    [Test]
    public async Task TheSpokenWordsComeBackAsSaidWithoutTextShortcutsExpanded()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Email it to my personal email" },
            textShortcuts: [new TextShortcut("personal email", "me@example.com")]);

        var phase = await harness.DictateAsync(handingBack: true);

        phase.Should().Be(PipelinePhase.HandedBack("Email it to my personal email"));
    }

    [Test]
    public async Task NothingSaidEndsInIdle()
    {
        await using var harness = SessionHarness.WithTranscript("   ");

        var phase = await harness.DictateAsync(handingBack: true);

        phase.Should().Be(PipelinePhase.Idle);
    }

    [Test]
    public async Task TheNextOrdinaryPressPastesAgain()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        await harness.DictateAsync(handingBack: true);
        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("Hello world.");
    }

    [Test]
    public async Task SubmittingPressHandingBackIsTheSamePressThroughTheCommandFeed()
    {
        await using var harness = SessionHarness.WithTranscript("Summarize this.");
        var phases = harness.Session.PhaseStream(CancellationToken.None);

        harness.Session.Submit(Command.PressHandingBack);
        await harness.Session.WaitForPhaseAsync(PipelinePhase.Recording);
        harness.Session.Submit(Command.Release);
        var seen = await phases.CollectUntilAsync(p => p is PipelinePhase.HandedBackPhase);

        seen[^1].Should().Be(PipelinePhase.HandedBack("Summarize this."));
    }

    [Test]
    public void AHandBackPhaseIsTerminalHidesThePillAndReadsAsIdleOnTheTray()
    {
        var phase = PipelinePhase.HandedBack("x");

        phase.IsTerminal.Should().BeTrue();
        phase.IsCapturing.Should().BeFalse();
        phase.OverlayState.Should().Be(OverlayUIState.Idle);
        phase.TrayStatus.Should().Be(TrayStatus.Idle);
        phase.SetupBlocker.Should().BeNull();
        phase.ToString().Should().Be("handedBack");
        PipelinePhase.HandedBack("x").Should().Be(PipelinePhase.HandedBack("x"));
    }
}
```

`SessionHarness.DictateAsync(bool handingBack = false)` calls `Session.PressAsync(handingBack)`.

- [ ] **Step 5: Implement the hand-back press**

`PipelinePhase`: `public static PipelinePhase HandedBack(string transcript) => new HandedBackPhase(transcript);` and

```csharp
    /// <summary>
    /// Transcription succeeded and the press asked for the text back instead of a paste
    /// (<see cref="Command.PressHandingBack"/>). <paramref name="Transcript"/> is the spoken text,
    /// before any text shortcut expands it. Terminal; presents as idle, since what happens next is the host's.
    /// </summary>
    public sealed record HandedBackPhase(string Transcript) : PipelinePhase
    {
        public override bool IsTerminal => true;
        public override bool IsCapturing => false;
        public override OverlayUIState OverlayState => OverlayUIState.Idle;
        public override TrayStatus TrayStatus => TrayStatus.Idle;
        private protected override string CaseName => "handedBack";
    }
```

(with `/// <inheritdoc/>` on each override). `Command` gains `PressHandingBack` ("`DictationSession.PressAsync(bool)` with `handingBack` true: the same dictation, whose transcript ends the run as `PipelinePhase.HandedBack` rather than being pasted."); `ConsumeCommandsAsync` maps it to `PressAsync(handingBack: true)`. `PressAsync(bool handingBack = false)` passes the flag to `PerformPressAsync(pressToken, handingBack)`, which sets `_handsBackTranscript = handingBack` inside its first `lock (_gate)`. In `TranscribeAndInjectAsync`, inside the existing lock after the live check:

```csharp
            context = _pressContext?.Resolved;
            handsBack = _handsBackTranscript;
            if (handsBack)
            {
                _delivered = new DeliveredTranscript(text.Length, context?.AppName);
            }
            else
            {
                if (context?.TargetIsSecure != true)
                {
                    _recent.Record(trimmed, spoken, style, now);
                }

                snapshot = _recent.Snapshot();
                _delivered = new DeliveredTranscript(text.Length, context?.AppName);
            }
```

then after the lock: `_log.Append(text, context); if (handsBack) { TryAdvance(run, PipelinePhase.Transcribing, PipelinePhase.HandedBack(spoken)); return; }` before the delivery callback. `OutcomeLine` adds `PipelinePhase.HandedBackPhase => "handed back",`. Add to `PipelinePhaseTests` whatever enumeration of all phases exists there (terminal set, `ToString` names) with the new phase.

- [ ] **Step 6: Run both test projects and format** → green.

- [ ] **Step 7: Commit**

```bash
git add Windows/src Windows/tests
git commit -m "feat(windows): latch action, hand-back press, and activation wording"
```

---

### Task 6: Read-aloud stores and the routing state machine (engine)

**Files:**

- Create: `Windows/src/Blurt.Engine/Config/SelectionSpeechStore.cs`, `SelectionAskStore.cs`, `ReadAloudStyleStore.cs` (also holds `ReadAloudStyle`), `Windows/src/Blurt.Engine/Hotkey/SelectionSpeechRouting.cs` (also holds `RoutingAction`)
- Test: `Windows/tests/Blurt.Engine.Tests/Config/ReadAloudStoresTests.cs`, `Windows/tests/Blurt.Engine.Tests/Hotkey/SelectionSpeechRoutingTests.cs`; modify `PersistedSettingsTests.cs` (roster lines for the three stores)

**Swift spec:** at `upstream/main`: `Sources/BlurtEngine/Config/SelectionSpeechStore.swift`, `SelectionAskStore.swift`, `ReadAloudStyleStore.swift`, `Sources/BlurtEngine/Hotkey/SelectionSpeechRouting.swift`; `Tests/BlurtEngineTests/SelectionSpeechRoutingTests.swift` (both suites), `SelectionSpeechStoreTests.swift`, `SelectionAskStoreTests.swift`, `ReadAloudStyleStoreTests.swift`.

**Interfaces:**

- Consumes: `Command.PressHandingBack`, `PipelinePhase.HandedBackPhase` (Task 5).
- Produces: `SelectionSpeechStore(ISettingsStore)` with `static string SettingsKey`, `bool IsEnabled { get; set; }` (unset reads false); `SelectionAskStore(ISettingsStore)` with `SettingsKey`, `const bool DefaultValue = true`, `bool IsEnabled { get; set; }` (unset reads true); `ReadAloudStyle(double Rate, bool SkipsJargon)` record with `static ReadAloudStyle Standard = new(1, false)`; `ReadAloudStyleStore(ISettingsStore)` with `SpeedSettingsKey`, `SkipsJargonSettingsKey`, `const double DefaultSpeed = 1`, `const bool DefaultSkipsJargon = false`, `static IReadOnlyList<double> SpeedChoices`, `double Speed { get; set; }` (snapped on read), `bool SkipsJargon { get; set; }`, `ReadAloudStyle Style`, `static double NearestChoice(double)`, `static string SpeedLabel(double, CultureInfo)`; `abstract record RoutingAction` with `Forward(Command Command)`, `ReadSelection`, `StartHoldTimer`, `Speak(string Text)`, `Answer(string Request, string Selection)`, `StopSpeech`, `SyncGateAfterSession`, `ResetGate` (parameterless ones are singletons exposed as `RoutingAction.ReadSelection` etc.); `sealed class SelectionSpeechRouting` with `static readonly TimeSpan AskHoldDelay = 350 ms`, `IReadOnlyList<RoutingAction> Submit(Command command, bool readAloudEnabled, bool askEnabled = false)`, `KeyLatched()`, `HoldElapsed()`, `SelectionResolved(string? selection)`, `SpeechFinished()`, `SessionReachedTerminalPhase(PipelinePhase phase)`.

- [ ] **Step 1: Store tests** (`ReadAloudStoresTests.cs`): port the three Swift store suites against `InMemorySettingsStore`: `SelectionSpeechStore` defaults off and reads back a write; `SelectionAskStore` defaults on and reads back `false`/`true`; `ReadAloudStyleStore` untouched is `ReadAloudStyle.Standard`, speed and skip are independent, speed snaps (`9 → 3`, `0.1 → 1`, `1.6 → 1.5`, `1.65 → 1.75`, `double.NaN → 1`, a string in the slot → 1), `SpeedChoices` contains the default and is sorted, `SpeedLabel(2, en-US) == "2×"`, `SpeedLabel(1.5, en-US) == "1.5×"`, `SpeedLabel(1.25, en-US) == "1.25×"`, `SpeedLabel(1.5, de-DE) == "1,5×"`. Add the roster lines to `PersistedSettingsTests`.

- [ ] **Step 2: Implement the three stores** in the shape of `DeveloperModeStore` (settable; `GetBool(key) ?? default`, `GetDouble(key)`). `SpeedLabel`: `speed.ToString("0.##", culture) + "×"`. `NearestChoice`: `double.IsFinite(speed) ? SpeedChoices.MinBy(c => Math.Abs(c - speed)) : DefaultSpeed`. `Style => new(Speed, SkipsJargon)`.

- [ ] **Step 3: Routing tests** (`SelectionSpeechRoutingTests.cs`). Port every `@Test` of both Swift suites. The first suite in C#:

```csharp
using Blurt.Engine.Hotkey;
using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

public sealed class SelectionSpeechRoutingTests
{
    [Test]
    public void WithTheSwitchOffEveryCommandGoesStraightToTheSession()
    {
        var routing = new SelectionSpeechRouting();

        foreach (var command in new[] { Command.Press, Command.Release, Command.Cancel, Command.CancelRecording })
        {
            routing.Submit(command, readAloudEnabled: false).Should().Equal(RoutingAction.Forward(command));
        }
    }

    [Test]
    public void APressWithNothingSelectedDictatesReplayingWhatArrivedDuringTheReadInOrder()
    {
        var routing = new SelectionSpeechRouting();

        routing.Submit(Command.Press, readAloudEnabled: true).Should().Equal(RoutingAction.ReadSelection);
        routing.Submit(Command.Release, readAloudEnabled: true).Should().BeEmpty();
        routing.SelectionResolved(null).Should().Equal(RoutingAction.Forward(Command.Press), RoutingAction.Forward(Command.Release));
        routing.Submit(Command.Press, readAloudEnabled: true).Should().Equal(RoutingAction.ReadSelection);
    }

    [Test]
    public void APressOverASelectionSpeaksItAndTheNextKeyCommandStopsIt()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);

        routing.SelectionResolved("Hello there.").Should().Equal(new RoutingAction.Speak("Hello there."));
        routing.Submit(Command.Release, readAloudEnabled: true).Should().Equal(RoutingAction.StopSpeech);
        routing.SpeechFinished().Should().BeEmpty();
    }

    [Test]
    public void AComboCancelDuringTheReadDropsThePress()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.Submit(Command.Cancel, readAloudEnabled: true);

        routing.SelectionResolved("Hello there.").Should().BeEmpty();
        routing.Submit(Command.Press, readAloudEnabled: false).Should().Equal(RoutingAction.Forward(Command.Press));
    }

    [Test]
    public void ARecoveryResetWhileSpeakingStopsTheReadAndStillReachesTheSession()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.SelectionResolved("Hello there.");

        routing.Submit(Command.CancelRecording, readAloudEnabled: true)
            .Should().Equal(RoutingAction.StopSpeech, RoutingAction.Forward(Command.CancelRecording));
    }

    [Test]
    public void ANaturalEndClearsTheGateSilently()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.SelectionResolved("Hello there.");

        routing.SpeechFinished().Should().Equal(RoutingAction.ResetGate);
        routing.Submit(Command.Press, readAloudEnabled: false).Should().Equal(RoutingAction.Forward(Command.Press));
    }

    [Test]
    public void ADictationsTerminalPhaseSyncsTheGateOnlyWhileCommandsGoToTheSession()
    {
        var routing = new SelectionSpeechRouting();

        routing.SessionReachedTerminalPhase(PipelinePhase.Pasted).Should().Equal(RoutingAction.SyncGateAfterSession);
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.SessionReachedTerminalPhase(PipelinePhase.Pasted).Should().BeEmpty();
        routing.SelectionResolved("Hello there.");
        routing.SessionReachedTerminalPhase(PipelinePhase.Pasted).Should().BeEmpty();
    }

    [Test]
    public void APressThatLandsDuringASlowReadAndIsDroppedClearsTheGateItLatched()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.Submit(Command.Cancel, readAloudEnabled: true);
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.KeyLatched();

        routing.SelectionResolved("Hello there.").Should().Equal(RoutingAction.ResetGate);

        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.Submit(Command.Release, readAloudEnabled: true);
        routing.SelectionResolved("Hello there.").Should().BeEmpty();
    }

    [Test]
    public void AStaleSelectionResultAfterThePressResolvedIsIgnored()
    {
        new SelectionSpeechRouting().SelectionResolved("Hello there.").Should().BeEmpty();
    }
}
```

The second suite, `SelectionSpeechRoutingAskTests`, ports each case of the Swift `SelectionSpeechRouting asking` suite with helpers `Selected()` (press with `askEnabled: true`, then `SelectionResolved("The quarterly report.")`) and `Asking()` (`Selected()` then `HoldElapsed()`); expected sequences are exactly the Swift ones with `.forward(.pressHandingBack)` → `RoutingAction.Forward(Command.PressHandingBack)`, `.answer(request:selection:)` → `new RoutingAction.Answer(request, selection)`, `.handedBack(text)` → `PipelinePhase.HandedBack(text)`, `.failed(.sttFailed(...))` → `PipelinePhase.Failed(new BlurtError.SttFailed(new TimeoutException()))`, `.failed(.audioCaptureFailed(...))` → `PipelinePhase.Failed(new BlurtError.AudioCaptureFailed(new InvalidOperationException()))`, `.cancelled` → `PipelinePhase.Cancelled`, `.idle` → `PipelinePhase.Idle`.

- [ ] **Step 4: Implement `RoutingAction` and `SelectionSpeechRouting`**

```csharp
using Blurt.Engine.Pipeline;

namespace Blurt.Engine.Hotkey;

/// <summary>One action <see cref="SelectionSpeechRouting"/> asks the host to carry out.</summary>
public abstract record RoutingAction
{
    /// <summary>Start the selection read; its result comes back through <see cref="SelectionSpeechRouting.SelectionResolved"/>.</summary>
    public static readonly RoutingAction ReadSelection = new ReadSelectionAction();

    /// <summary>Call <see cref="SelectionSpeechRouting.HoldElapsed"/> after <see cref="SelectionSpeechRouting.AskHoldDelay"/>, unless a newer press restarts the timer.</summary>
    public static readonly RoutingAction StartHoldTimer = new StartHoldTimerAction();

    /// <summary>Stop the speech that is playing.</summary>
    public static readonly RoutingAction StopSpeech = new StopSpeechAction();

    /// <summary>A dictation ended without a key event: the hook's terminal-phase sync.</summary>
    public static readonly RoutingAction SyncGateAfterSession = new SyncGateAfterSessionAction();

    /// <summary>A read ended on its own: clear the gate with no recording discarded.</summary>
    public static readonly RoutingAction ResetGate = new ResetGateAction();

    private RoutingAction()
    {
    }

    /// <summary>Hand <paramref name="command"/> to the dictation session.</summary>
    public static RoutingAction Forward(Command command) => new ForwardAction(command);

    /// <summary>Hand <paramref name="Command"/> to the dictation session.</summary>
    public sealed record ForwardAction(Command Command) : RoutingAction;

    /// <summary>Read <paramref name="Text"/> aloud.</summary>
    public sealed record Speak(string Text) : RoutingAction;

    /// <summary>Answer the spoken <paramref name="Request"/> about <paramref name="Selection"/>, aloud.</summary>
    public sealed record Answer(string Request, string Selection) : RoutingAction;

    private sealed record ReadSelectionAction : RoutingAction;

    private sealed record StartHoldTimerAction : RoutingAction;

    private sealed record StopSpeechAction : RoutingAction;

    private sealed record SyncGateAfterSessionAction : RoutingAction;

    private sealed record ResetGateAction : RoutingAction;
}
```

```csharp
using Blurt.Engine.Pipeline;

namespace Blurt.Engine.Hotkey;

/// <summary>
/// Decides where each trigger command goes while the read-aloud switch is on: to the dictation
/// session, to reading the selection aloud, or to asking about the selection. A pure state machine;
/// the host carries out the actions it returns. A press first waits in the deciding state for the
/// selection read, holding the commands that arrive meanwhile and replaying them in order. Speech
/// follows the gate's tap/hold semantics: a tap latches the gate so speech runs until the next tap,
/// a hold speaks until the release, a combo cancels. With asking on, a hold over a selection asks
/// about it instead (the mic opens through the session's hand-back press once the key has been down
/// <see cref="AskHoldDelay"/>), and a tap still reads. This owns every "the gate is stale" decision.
/// Not thread-safe; the host serializes calls.
/// </summary>
public sealed class SelectionSpeechRouting
{
    /// <summary>How long the key must stay down over a selection to count as a hold.</summary>
    public static readonly TimeSpan AskHoldDelay = TimeSpan.FromMilliseconds(350);

    private enum Mode
    {
        Passthrough,
        Deciding,
        Selected,
        Asking,
        Transcribing,
        Speaking,
    }

    private readonly List<Command> _pending = [];
    private Mode _mode = Mode.Passthrough;
    private bool _asks;
    private bool _latched;
    private bool _held;
    private string _selection = string.Empty;

    /// <summary>Routes <paramref name="command"/>; returns the actions to carry out, in order.</summary>
    public IReadOnlyList<RoutingAction> Submit(Command command, bool readAloudEnabled, bool askEnabled = false)
    {
        switch (_mode)
        {
            case Mode.Passthrough:
                if (command != Command.Press || !readAloudEnabled)
                {
                    return [RoutingAction.Forward(command)];
                }

                _mode = Mode.Deciding;
                _pending.Clear();
                _asks = askEnabled;
                _latched = false;
                _held = false;
                return askEnabled ? [RoutingAction.ReadSelection, RoutingAction.StartHoldTimer] : [RoutingAction.ReadSelection];
            case Mode.Deciding:
                _pending.Add(command);
                return [];
            case Mode.Selected:
                // Let go before it was a hold, or a combo over the selection: nothing was asked for.
                _mode = Mode.Passthrough;
                return command == Command.CancelRecording ? [RoutingAction.Forward(command)] : [];
            case Mode.Asking:
                return Asking(command);
            case Mode.Transcribing:
                return Transcribing(command);
            default:
                _mode = Mode.Passthrough;
                return command switch
                {
                    // A recovery reset still reaches the session, which can only cancel a live recording.
                    Command.CancelRecording => [RoutingAction.StopSpeech, RoutingAction.Forward(command)],
                    // A press reaches speech only when the gate was idle under it; its key-up would
                    // latch a recording nobody started, so the gate is cleared now.
                    Command.Press or Command.PressHandingBack => [RoutingAction.StopSpeech, RoutingAction.ResetGate],
                    _ => [RoutingAction.StopSpeech],
                };
        }
    }

    /// <summary>The gate latched: the key came up quickly enough to be a tap.</summary>
    public IReadOnlyList<RoutingAction> KeyLatched()
    {
        switch (_mode)
        {
            case Mode.Deciding:
                _latched = true;
                return [];
            case Mode.Selected:
                _mode = Mode.Speaking;
                return [new RoutingAction.Speak(_selection)];
            case Mode.Asking:
                // Let go before the gate's own hold threshold. Letting go ends the request, and the
                // latch holds no recording the user knows about.
                _mode = Mode.Transcribing;
                return [RoutingAction.Forward(Command.Release), RoutingAction.ResetGate];
            default:
                return [];
        }
    }

    /// <summary><see cref="AskHoldDelay"/> passed since the press.</summary>
    public IReadOnlyList<RoutingAction> HoldElapsed()
    {
        switch (_mode)
        {
            case Mode.Deciding when _asks:
                _held = true;
                return [];
            case Mode.Selected:
                _mode = Mode.Asking;
                return [RoutingAction.Forward(Command.PressHandingBack)];
            default:
                return [];
        }
    }

    /// <summary>The selection read returned; null means nothing is selected, so the press dictates.</summary>
    public IReadOnlyList<RoutingAction> SelectionResolved(string? selection)
    {
        if (_mode != Mode.Deciding)
        {
            return [];
        }

        if (selection is null)
        {
            _mode = Mode.Passthrough;
            var replay = new List<RoutingAction> { RoutingAction.Forward(Command.Press) };
            replay.AddRange(_pending.Select(RoutingAction.Forward));
            return replay;
        }

        // The press was over before the read returned. A later press among the held commands still
        // holds the gate, and nothing will run for it, so the gate is cleared or the next press is swallowed.
        if (_pending.Count > 0)
        {
            _mode = Mode.Passthrough;
            return _pending[^1] == Command.Press ? [RoutingAction.ResetGate] : [];
        }

        _selection = selection;
        if (!_asks || _latched)
        {
            _mode = Mode.Speaking;
            return [new RoutingAction.Speak(selection)];
        }

        if (_held)
        {
            _mode = Mode.Asking;
            return [RoutingAction.Forward(Command.PressHandingBack)];
        }

        _mode = Mode.Selected;
        return [];
    }

    /// <summary>The read ended on its own, or failed. A read the host stopped itself must not be reported.</summary>
    public IReadOnlyList<RoutingAction> SpeechFinished()
    {
        if (_mode != Mode.Speaking)
        {
            return [];
        }

        _mode = Mode.Passthrough;
        return [RoutingAction.ResetGate];
    }

    /// <summary>
    /// A dictation reached a terminal phase. While commands go to the session, the gate is this
    /// router's to sync. While a read or a decision owns it, an earlier dictation's end changes
    /// nothing. A request's end is what an ask waits for: its transcript, nothing said, or a failure.
    /// </summary>
    public IReadOnlyList<RoutingAction> SessionReachedTerminalPhase(PipelinePhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        switch (_mode)
        {
            case Mode.Passthrough:
                return [RoutingAction.SyncGateAfterSession];
            case Mode.Asking:
            case Mode.Transcribing:
                var stillDown = _mode == Mode.Asking;
                if (phase is PipelinePhase.HandedBackPhase handedBack)
                {
                    _mode = Mode.Speaking;
                    // Handed back with the key still down means the recording cap released it; the
                    // eventual key-up would otherwise stop the answer.
                    return stillDown
                        ? [new RoutingAction.Answer(handedBack.Transcript, _selection), RoutingAction.ResetGate]
                        : [new RoutingAction.Answer(handedBack.Transcript, _selection)];
                }

                if (phase == PipelinePhase.Idle && !stillDown)
                {
                    _mode = Mode.Speaking;
                    return [new RoutingAction.Speak(_selection)];
                }

                _mode = Mode.Passthrough;
                return [RoutingAction.SyncGateAfterSession];
            default:
                return [];
        }
    }

    private IReadOnlyList<RoutingAction> Asking(Command command)
    {
        switch (command)
        {
            case Command.Release:
                _mode = Mode.Transcribing;
                return [RoutingAction.Forward(Command.Release)];
            case Command.Cancel:
            case Command.CancelRecording:
                _mode = Mode.Passthrough;
                return [RoutingAction.Forward(command)];
            default:
                // The key is down for the whole request, so the gate cannot press again.
                return [];
        }
    }

    private IReadOnlyList<RoutingAction> Transcribing(Command command)
    {
        switch (command)
        {
            case Command.Press:
            case Command.PressHandingBack:
                // A press while the request is transcribing drops it; the gate is armed for this
                // press and its key-up would latch a recording nobody started.
                _mode = Mode.Passthrough;
                return [RoutingAction.Forward(Command.Cancel), RoutingAction.ResetGate];
            case Command.Cancel:
                _mode = Mode.Passthrough;
                return [RoutingAction.Forward(Command.Cancel)];
            default:
                // Nothing is recording, so the session treats a release or a recovery reset as a no-op.
                return [RoutingAction.Forward(command)];
        }
    }
}
```

- [ ] **Step 5: Run the engine tests and format** → green (both routing suites, the store suite, the roster).

- [ ] **Step 6: Commit**

```bash
git add Windows/src/Blurt.Engine Windows/tests/Blurt.Engine.Tests
git commit -m "feat(windows): read-aloud stores and the selection speech routing"
```

---

### Task 7: Speech segmenter and the streaming TTS client (engine)

**Files:**

- Create: `Windows/src/Blurt.Engine/TTS/SpeechSegmenter.cs`, `Windows/src/Blurt.Engine/TTS/ISpeechSocket.cs`, `Windows/src/Blurt.Engine/TTS/ClientWebSocketSpeechSocket.cs`, `Windows/src/Blurt.Engine/TTS/AssemblyAISpeechSynthesizer.cs`, `Windows/src/Blurt.Engine/TTS/SpeechSynthesisException.cs`
- Test: `Windows/tests/Blurt.Engine.Tests/TTS/SpeechSegmenterTests.cs`, `Windows/tests/Blurt.Engine.Tests/TTS/AssemblyAISpeechSynthesizerTests.cs`, `Windows/tests/Blurt.Engine.Tests/Stubs/FakeSpeechSocket.cs`

**Swift spec:** at `upstream/main`: `Sources/BlurtEngine/TTS/SpeechSegmenter.swift`, `TTS/AssemblyAISpeechSynthesizer.swift`; `Tests/BlurtEngineTests/SpeechSegmenterTests.swift`, `AssemblyAISpeechSynthesizerTests.swift` (suites `wire format` and `exchange`; skip the `SelectionSpeaker` suite, which is Task 10), `Tests/BlurtEngineTests/Stubs/FakeSpeechSocket.swift`.

**Interfaces:**

- Produces: `public static class SpeechSegmenter` with `const int MinWords = 2`, `IReadOnlyList<string> Segments(string text, int maxCharacters = 400)`, `IReadOnlyList<string> MergingShort(IReadOnlyList<string>)`, `IReadOnlyList<string> Split(string segment, int maxCharacters)`; `public interface ISpeechSocket { Task SendAsync(string text, CancellationToken ct); Task<string?> ReceiveAsync(CancellationToken ct); void Close(bool normally); }` (`ReceiveAsync` returns null once the socket is closed); `public delegate Task<ISpeechSocket> SpeechSocketConnector(Uri url, string apiKey, CancellationToken ct)`; `public sealed class AssemblyAISpeechSynthesizer(Func<string?> apiKeyProvider, SpeechSocketConnector connect, TimeSpan? receiveTimeout = null)` with `const int SampleRate = 24000`, `const string Voice = "jane"`, `static readonly Uri Endpoint`, `static Uri Url`, `IAsyncEnumerable<byte[]> Synthesize(string text, CancellationToken ct)`, `static string Command(string type, string? text = null)`, `static SpeechFrame Frame(string message)`; `public abstract record SpeechFrame` with `Audio(byte[] Pcm)`, `FlushDone`, `Error(string Message)`, `Ignored`; `public sealed class SpeechSynthesisException(SpeechSynthesisFailure failure)` with `abstract record SpeechSynthesisFailure { TimedOut; Server(string Message); }` and a `Message` of `"Text-to-speech stopped responding."` or `"Text-to-speech failed: {message}"`; `ClientWebSocketSpeechSocket.ConnectAsync` as the production `SpeechSocketConnector`.

- [ ] **Step 1: Segmenter tests** (port every Swift case): sentences `"It works. Does it? Yes it does!"` → three; curly closer `"She said “you uploaded me.” The clock ticks."` → two; decimals `"Pi is 3.14 in v1.2 today."` → one; abbreviations `"Dr. Smith is in. Go."` → one; short tail `"That went well. Thanks."` → one, `"Hi."` → one; line breaks `"Release notes here\n\nFixed the paste bug"` → two; blank `"  \n "` → empty; runaway `("alpha beta gamma delta epsilon", 12)` → `["alpha beta", "gamma delta", "epsilon"]` all ≤ 12; hard cut `Split("abcdefghij", 4)` → `["abcd", "efgh", "ij"]`.

- [ ] **Step 2: Implement `SpeechSegmenter`**

```csharp
using System.Text.RegularExpressions;

namespace Blurt.Engine.TTS;

/// <summary>
/// Cuts text into the segments the synthesizer flushes one at a time. Cuts fall at sentence ends
/// only (terminal punctuation plus any closing quotes or brackets, followed by whitespace or the
/// end), or at line breaks; never at commas. A segment needs at least <see cref="MinWords"/> words,
/// so a one-word "sentence" (an abbreviation) joins the sentence after it. <c>maxCharacters</c>
/// only splits a runaway sentence.
/// </summary>
public static partial class SpeechSegmenter
{
    /// <summary>The fewest words a segment may hold.</summary>
    public const int MinWords = 2;

    [GeneratedRegex(@"[.!?…][""'’”)\]]*(?=\s|$)|\n")]
    private static partial Regex Boundary();

    /// <summary>The segments of <paramref name="text"/>, trimmed, in order; empty for blank text.</summary>
    public static IReadOnlyList<string> Segments(string text, int maxCharacters = 400)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sentences = new List<string>();
        var start = 0;
        foreach (Match match in Boundary().Matches(text))
        {
            var end = match.Index + match.Length;
            AppendTrimmed(text[start..end], sentences);
            start = end;
        }

        AppendTrimmed(text[start..], sentences);
        return MergingShort(sentences).SelectMany(segment => Split(segment, maxCharacters)).ToList();
    }

    /// <summary>Joins any sentence shorter than <see cref="MinWords"/> onto the sentence after it; a short sentence at the very end joins the one before it.</summary>
    public static IReadOnlyList<string> MergingShort(IReadOnlyList<string> sentences)
    {
        var merged = new List<string>();
        var pending = string.Empty;
        foreach (var sentence in sentences)
        {
            pending = pending.Length == 0 ? sentence : pending + " " + sentence;
            if (WordCount(pending) >= MinWords)
            {
                merged.Add(pending);
                pending = string.Empty;
            }
        }

        if (pending.Length > 0)
        {
            if (merged.Count > 0)
            {
                merged[^1] = merged[^1] + " " + pending;
            }
            else
            {
                merged.Add(pending);
            }
        }

        return merged;
    }

    /// <summary>Splits <paramref name="segment"/> at the last whitespace at or before <paramref name="maxCharacters"/>; a run with no whitespace in that span is cut hard at the limit.</summary>
    public static IReadOnlyList<string> Split(string segment, int maxCharacters)
    {
        var pieces = new List<string>();
        var rest = segment.AsSpan();
        while (rest.Length > maxCharacters)
        {
            var window = rest[..maxCharacters];
            var cut = LastWhitespace(window);
            if (cut < 0)
            {
                cut = maxCharacters;
            }

            AppendTrimmed(rest[..cut].ToString(), pieces);
            rest = rest[cut..].TrimStart();
        }

        AppendTrimmed(rest.ToString(), pieces);
        return pieces;
    }

    private static int LastWhitespace(ReadOnlySpan<char> span)
    {
        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static void AppendTrimmed(string piece, List<string> into)
    {
        if (piece.TrimmedNonEmpty() is { } trimmed)
        {
            into.Add(trimmed);
        }
    }

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
```

- [ ] **Step 3: The fake socket** (`Stubs/FakeSpeechSocket.cs`): plays the service's side. `FakeSpeechSocket(Func<string, IReadOnlyList<string>> respond)`; `SendAsync` throws `WebSocketException` once closed, records the text in `Sent`, and queues each frame `respond(text)` returns onto an unbounded `Channel<string>`; `ReceiveAsync` reads the channel and returns null when it completes; `Close(bool normally)` records `ClosedNormally` (first call wins) and completes the channel; `Drop()` completes the channel without recording a close; `static string Audio(byte[] bytes)` builds `{"type":"Audio","audio":"<base64>"}`; `static string FlushDone`; `static Func<string, IReadOnlyList<string>> Service()` answers nothing for `Generate` and, for each `Flush`, one audio chunk `[index, 0]` (first flush index 1) then `FlushDone`.

- [ ] **Step 4: Synthesizer tests.** Port the Swift `wire format` suite (`Url` string equals `wss://streaming-tts.assemblyai.com/v1/ws/?voice=jane&sample_rate=24000`; `Command("Generate", "Hi.")` is `{"text":"Hi.","type":"Generate"}` and `Command("Flush")` is `{"type":"Flush"}`; Audio/FlushDone/Error frames with numeric and string `error_code`, blank reason → `unknown error`; Begin/WordBoundaries/empty audio/`not json`/`[1]` → `Ignored`; a missing key throws `BlurtException` whose `Error` is `ApiKeyMissing` before the connector is called; blank text yields no chunks and never connects) and the `exchange` suite (happy path: chunks `[1,0]`, `[2,0]`; `Sent` is Generate/Flush/Generate/Flush/Terminate with the two sentences; `ClosedNormally == true`; the connector received the raw key and `Url`; an in-band Error frame throws `SpeechSynthesisException` with `Server("(1008) Unauthorized: Invalid API key")` and `ClosedNormally == false`; a dropped connection throws and closes abruptly; a silent server with `receiveTimeout` 50 ms throws `TimedOut` and closes abruptly; noise frames do not count as audio or acks; cancelling the consumer closes the socket abruptly). Name the timeout test `ASilentServerTimesOut` and the key test `AMissingKeyFailsBeforeAnySocketOpens`.

- [ ] **Step 5: Implement the synthesizer, the frame types, the exception, and the production socket**

Key points for `AssemblyAISpeechSynthesizer.Synthesize`: segments via `SpeechSegmenter.Segments`; no segments → yield nothing; no key → throw `new BlurtException(new BlurtError.ApiKeyMissing())`; connect once; run the exchange on a task that writes chunks into a bounded-unlimited `Channel<byte[]>` and completes it (with the exception on failure); `await foreach` the channel with `ct`; a cancellation registration calls `socket.Close(false)`. The exchange: a sender task that, per segment, checks the token, sends `Command("Generate", segment)` then the cached `Flush` command; a receive loop that calls `socket.ReceiveAsync(ct).WaitAsync(receiveTimeout, ct)` and maps `TimeoutException` to `Close(false)` + `SpeechSynthesisException(TimedOut)`, a null message to `WebSocketException("The socket closed before every segment was acknowledged.")`, `Audio` to a channel write, `FlushDone` to `acknowledged++`, `Error` to `Close(false)` + `SpeechSynthesisException(Server(msg))`; once every segment is acknowledged, cancel the sender's token, send `Terminate` inside `try/catch (Exception)`, `Close(true)`. Any failure closes the socket abruptly before propagating. `Command` serializes `record Command([property: JsonPropertyName("text")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text, [property: JsonPropertyName("type")] string Type)` with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. `Frame(string)` parses with `JsonDocument`, leniently: anything that is not an object with a string `type` is `Ignored`.

`ClientWebSocketSpeechSocket`: `public static async Task<ISpeechSocket> ConnectAsync(Uri url, string apiKey, CancellationToken ct)` creates a `ClientWebSocket`, sets `Options.SetRequestHeader("Authorization", apiKey)` and `Options.SetRequestHeader(UserAgent.HeaderField, UserAgent.Current)`, awaits `ConnectAsync(url, ct)`. `SendAsync` sends one text message; `ReceiveAsync` accumulates until `EndOfMessage` into a `MemoryStream`, decodes UTF-8 for both text and binary message types, returns null on `Close`/aborted; `Close(normally)` aborts for abrupt, and for normal starts `CloseOutputAsync(NormalClosure)` observed by `TaskObserver`-style logging (the engine has no `TaskObserver`; use a continuation that logs via `BlurtLog.Warn`), then disposes.

- [ ] **Step 6: Run the engine tests and format** → green.

- [ ] **Step 7: Commit**

```bash
git add Windows/src/Blurt.Engine/TTS Windows/tests/Blurt.Engine.Tests
git commit -m "feat(windows): speech segmenter and the streaming TTS client"
```

---

### Task 8: The read-aloud LLM Gateway client (engine)

**Files:**

- Create: `Windows/src/Blurt.Engine/TTS/ReadAloudLLM.cs`, `Windows/src/Blurt.Engine/TTS/ReadAloudLLMException.cs`
- Test: `Windows/tests/Blurt.Engine.Tests/TTS/ReadAloudLLMTests.cs`

**Swift spec:** at `upstream/main`: `Sources/BlurtEngine/TTS/ReadAloudLLM.swift`; `Tests/BlurtEngineTests/ReadAloudLLMTests.swift`.

**Interfaces:**

- Consumes: `IHttpTransport`, `FakeHttpTransport`, `UserAgent`.
- Produces: `public sealed class ReadAloudLLM(Func<string?> apiKeyProvider, IHttpTransport transport)` with `static readonly Uri Endpoint`, `const string Model = "qwen3.5-4b-32k-fast"`, `const int RewriteMaxTokens = 4096`, `AnswerMaxTokens = 1024`, `static readonly TimeSpan RewriteTimeout = 10 s`, `AnswerTimeout = 20 s`, `const string RewriteInstruction`, `AnswerInstruction` (the Swift texts verbatim, including the line breaks the Swift multi-line literal produces), `Task<string> RewriteForListeningAsync(string text, CancellationToken ct)`, `Task<string> AnswerAsync(string request, string selection, CancellationToken ct)`, `static string AnswerPrompt(string request, string selection)`, `static bool Plausible(string output, string input)`, `static LLMReply Reply(byte[] body)` with `record LLMReply(string Text, bool Truncated)`; `ReadAloudLLMException(ReadAloudLLMFailure)` with `abstract record ReadAloudLLMFailure { Status(int Code); Empty; Truncated; Implausible; TimedOut; }`.

- [ ] **Step 1: Tests.** Port every Swift case. The transport is `FakeHttpTransport`; a completion body helper `Completion(string? content, string? finishReason = "stop")` builds `{"choices":[{"message":{"content":...},"finish_reason":"..."}]}` (`content` null → JSON null). Assert on the recorded request: `Message.RequestUri == ReadAloudLLM.Endpoint`, method POST, `Header("Authorization") == "raw-key"`, `ContentType.MediaType == "application/json"`, `Header(UserAgent.HeaderField) == UserAgent.Current`; parse `Body` with `JsonDocument` and check `model`, `max_tokens`, `temperature == 0`, the two messages, and that the object's keys are exactly `max_tokens, messages, model, temperature`. Add `AStalledRewriteTimesOut`: a transport with `Hold` set and a `ReadAloudLLM` constructed with the test-only timeouts parameter (`rewriteTimeout: 50 ms`) throws `ReadAloudLLMException` with `TimedOut` and the caller's token is not cancelled.

- [ ] **Step 2: Implement.** Constructor overload `ReadAloudLLM(Func<string?> apiKeyProvider, IHttpTransport transport, TimeSpan? rewriteTimeout = null, TimeSpan? answerTimeout = null)`. `CompleteAsync(body, timeout, ct)`: key missing → `BlurtException(ApiKeyMissing)`; `HttpRequestMessage` POST with `StringContent(json, UTF8, "application/json")`, headers `Authorization` raw and `User-Agent`; linked `CancellationTokenSource` with `CancelAfter(timeout)`; on `OperationCanceledException` when `!ct.IsCancellationRequested` throw `TimedOut`; non-2xx → `Status(code)`; read the body and `Reply` it: first choice's trimmed content or `Empty`; `Truncated` when `finish_reason == "length"`. `RewriteForListeningAsync` throws `Truncated` for a truncated reply and `Implausible` when `!Plausible(reply, text)`; `AnswerAsync` returns the text as is. `Plausible(output, input) => output.Length <= input.Length + Math.Max(80, input.Length / 4)`. Body serialization: `{"model":..., "messages":[{"role":"system","content":...},{"role":"user","content":...}], "max_tokens":..., "temperature":0}` with `UnsafeRelaxedJsonEscaping`.

- [ ] **Step 3: Run the engine tests and format** → green.

- [ ] **Step 4: Commit**

```bash
git add Windows/src/Blurt.Engine/TTS Windows/tests/Blurt.Engine.Tests/TTS
git commit -m "feat(windows): read-aloud rewrite and answer over the llm gateway"
```

---

### Task 9: Pitch-preserving time stretch (engine)

**Files:**

- Create: `Windows/src/Blurt.Engine/TTS/PcmTimeStretch.cs`
- Test: `Windows/tests/Blurt.Engine.Tests/TTS/PcmTimeStretchTests.cs`

**Interfaces:**

- Produces: `public sealed class PcmTimeStretch(double rate, int frameSize = 480, int synthesisHop = 240, int searchRange = 120)` over 16-bit little-endian mono PCM with `byte[] Process(byte[] pcm)` (returns the output produced so far, possibly empty) and `byte[] Flush()` (the tail; the instance is then reset), `double Rate`. Rate `1` is a passthrough: `Process` returns its input unchanged and `Flush` returns an empty array.

- [ ] **Step 1: Tests**

```csharp
using Blurt.Engine.TTS;
using FluentAssertions;

namespace Blurt.Engine.Tests.TTS;

public sealed class PcmTimeStretchTests
{
    private const int SampleRate = 24_000;

    [Test]
    public void RateOneIsAByteIdenticalPassthrough()
    {
        var stretch = new PcmTimeStretch(rate: 1);
        var input = Tone(440, seconds: 0.5);

        stretch.Process(input).Should().Equal(input);
        stretch.Flush().Should().BeEmpty();
    }

    [Test]
    public void RateTwoHalvesTheDurationWithinOneFrame()
    {
        var stretch = new PcmTimeStretch(rate: 2);
        var input = Tone(440, seconds: 2);

        var output = Concat(stretch.Process(input), stretch.Flush());

        output.Length.Should().BeCloseTo(input.Length / 2, delta: 480 * 2 * 2);
    }

    [Test]
    public void RateTwoKeepsThePitch()
    {
        var stretch = new PcmTimeStretch(rate: 2);
        var input = Tone(440, seconds: 2);

        var output = Concat(stretch.Process(input), stretch.Flush());

        ZeroCrossingsPerSecond(output).Should().BeInRange(440 * 2 * 0.9, 440 * 2 * 1.1);
    }

    [Test]
    public void ChunkedInputProducesTheSameLengthAsOneShot()
    {
        var input = Tone(300, seconds: 1.5);
        var oneShot = new PcmTimeStretch(rate: 1.5);
        var chunked = new PcmTimeStretch(rate: 1.5);

        var whole = Concat(oneShot.Process(input), oneShot.Flush());
        var pieces = new List<byte[]>();
        foreach (var chunk in input.Chunk(1_000))
        {
            pieces.Add(chunked.Process(chunk));
        }

        pieces.Add(chunked.Flush());

        Concat(pieces.ToArray()).Length.Should().BeCloseTo(whole.Length, delta: 480 * 2);
    }

    [Test]
    public void SilenceStaysSilent()
    {
        var stretch = new PcmTimeStretch(rate: 2);

        var output = Concat(stretch.Process(new byte[24_000 * 2]), stretch.Flush());

        output.Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void ARateAtOrBelowZeroIsRefused()
    {
        FluentActions.Invoking(() => new PcmTimeStretch(rate: 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new PcmTimeStretch(rate: double.NaN)).Should().Throw<ArgumentOutOfRangeException>();
    }

    private static byte[] Tone(double hz, double seconds)
    {
        var samples = (int)(SampleRate * seconds);
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * hz * i / SampleRate) * 12_000);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static double ZeroCrossingsPerSecond(byte[] pcm)
    {
        var samples = pcm.Length / 2;
        var crossings = 0;
        var previous = BitConverter.ToInt16(pcm, 0);
        for (var i = 1; i < samples; i++)
        {
            var current = BitConverter.ToInt16(pcm, i * 2);
            if ((previous < 0 && current >= 0) || (previous >= 0 && current < 0))
            {
                crossings++;
            }

            previous = current;
        }

        // Two crossings per cycle.
        return crossings / 2.0 / (samples / (double)SampleRate);
    }
}
```

- [ ] **Step 2: Implement WSOLA**

```csharp
namespace Blurt.Engine.TTS;

/// <summary>
/// Changes the playback speed of 16-bit little-endian mono PCM while holding its pitch
/// (waveform-similarity overlap-add). Each synthesis frame of <c>frameSize</c> samples is taken
/// from the input near the nominal analysis position (which advances <c>synthesisHop × rate</c>
/// per frame), at the offset within <c>±searchRange</c> whose waveform best continues the previous
/// frame, and overlap-added with a Hann crossfade over <c>synthesisHop</c> samples. Streaming:
/// input not yet consumed is kept across calls. Rate 1 passes bytes through unchanged.
/// </summary>
public sealed class PcmTimeStretch
{
    private readonly int _frameSize;
    private readonly int _hop;
    private readonly int _search;
    private readonly float[] _window;
    private readonly List<short> _input = [];
    private short[]? _tail;
    private double _position;

    /// <summary>Creates a stretcher at <paramref name="rate"/> (1 is natural pace, 2 is twice as fast).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is not a positive finite number, or the geometry is not <c>searchRange ≤ synthesisHop &lt; frameSize</c>.</exception>
    public PcmTimeStretch(double rate, int frameSize = 480, int synthesisHop = 240, int searchRange = 120)
    {
        if (!double.IsFinite(rate) || rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The rate must be a positive finite number.");
        }

        if (synthesisHop <= 0 || synthesisHop >= frameSize || searchRange < 0 || searchRange > synthesisHop)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSize), "Expected searchRange <= synthesisHop < frameSize.");
        }

        Rate = rate;
        _frameSize = frameSize;
        _hop = synthesisHop;
        _search = searchRange;
        _window = new float[synthesisHop];
        for (var i = 0; i < synthesisHop; i++)
        {
            _window[i] = (float)(0.5 - (0.5 * Math.Cos(Math.PI * (i + 0.5) / synthesisHop)));
        }
    }

    /// <summary>The playback rate.</summary>
    public double Rate { get; }

    /// <summary>Feeds <paramref name="pcm"/> and returns every output sample that can be produced so far.</summary>
    public byte[] Process(byte[] pcm)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        if (Rate == 1)
        {
            return pcm;
        }

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            _input.Add((short)(pcm[i] | (pcm[i + 1] << 8)));
        }

        var output = new List<short>();
        while (true)
        {
            var nominal = (int)Math.Round(_position);
            var lowest = Math.Max(0, nominal - _search);
            var highest = nominal + _search;
            if (highest + _frameSize > _input.Count)
            {
                break;
            }

            var start = _tail is null ? nominal : BestOffset(lowest, highest);
            if (_tail is null)
            {
                for (var i = 0; i < _hop; i++)
                {
                    output.Add(_input[start + i]);
                }
            }
            else
            {
                for (var i = 0; i < _hop; i++)
                {
                    var mixed = (_tail[i] * (1 - _window[i])) + (_input[start + i] * _window[i]);
                    output.Add((short)Math.Clamp(Math.Round(mixed), short.MinValue, short.MaxValue));
                }
            }

            _tail = new short[_frameSize - _hop];
            _input.CopyTo(start + _hop, _tail, 0, _tail.Length);
            _position += _hop * Rate;
        }

        var consumed = Math.Max(0, (int)Math.Round(_position) - _search);
        if (consumed > 0)
        {
            _input.RemoveRange(0, Math.Min(consumed, _input.Count));
            _position -= Math.Min(consumed, _input.Count + consumed);
        }

        return ToBytes(output);
    }

    /// <summary>Returns the overlap tail and resets the stretcher.</summary>
    public byte[] Flush()
    {
        var tail = _tail is null ? [] : ToBytes(_tail);
        _tail = null;
        _input.Clear();
        _position = 0;
        return tail;
    }

    /// <summary>The start offset in <c>[lowest, highest]</c> whose first <c>hop</c> samples correlate best with the tail.</summary>
    private int BestOffset(int lowest, int highest)
    {
        var best = lowest;
        var bestScore = double.NegativeInfinity;
        for (var offset = lowest; offset <= highest; offset++)
        {
            double score = 0;
            for (var i = 0; i < _hop; i++)
            {
                score += (double)_tail![i] * _input[offset + i];
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = offset;
            }
        }

        return best;
    }

    private static byte[] ToBytes(IReadOnlyList<short> samples)
    {
        var bytes = new byte[samples.Count * 2];
        for (var i = 0; i < samples.Count; i++)
        {
            bytes[i * 2] = (byte)samples[i];
            bytes[(i * 2) + 1] = (byte)(samples[i] >> 8);
        }

        return bytes;
    }
}
```

The `consumed` bookkeeping above must keep `_position` and `_input` consistent: after removing `consumed` samples from the front, subtract exactly `consumed` from `_position`. Write it as `_input.RemoveRange(0, consumed); _position -= consumed;` guarded by `consumed <= _input.Count`. Tune only if a test fails; the tests pin the behavior, not the arithmetic.

- [ ] **Step 3: Run the stretch tests and format** → green.

- [ ] **Step 4: Commit**

```bash
git add Windows/src/Blurt.Engine/TTS/PcmTimeStretch.cs Windows/tests/Blurt.Engine.Tests/TTS/PcmTimeStretchTests.cs
git commit -m "feat(windows): pitch-preserving time stretch for read-aloud speed"
```

---

### Task 10: The selection speaker and its seams (engine)

**Files:**

- Create: `Windows/src/Blurt.Engine/TTS/IPcmSink.cs`, `Windows/src/Blurt.Engine/TTS/ISelectionCopy.cs`, `Windows/src/Blurt.Engine/FocusCapture/SelectionRead.cs`, `Windows/src/Blurt.Engine/FocusCapture/ISelectionReader.cs`, `Windows/src/Blurt.Engine/TTS/SelectionSpeaker.cs`
- Test: `Windows/tests/Blurt.Engine.Tests/TTS/SelectionSpeakerTests.cs`, `Windows/tests/Blurt.Engine.Tests/Stubs/RecordingSink.cs`

**Swift spec:** at `upstream/main`: `Sources/BlurtEngine/TTS/SelectionSpeaker.swift`; `Tests/BlurtEngineTests/AssemblyAISpeechSynthesizerTests.swift` suite `SelectionSpeaker`; `SelectionCopyTests.swift` case `resolveRule`.

**Interfaces:**

- Consumes: `AssemblyAISpeechSynthesizer`, `FakeSpeechSocket` (Task 7), `ReadAloudLLM` (Task 8), `ReadAloudStyle` (Task 6).
- Produces: `public interface IPcmSink { void Enqueue(byte[] pcm); Task DrainAsync(CancellationToken ct); void Stop(); }` (`DrainAsync` returns once everything enqueued has played or `Stop` was called; `Stop` silences at once, idempotent); `public abstract record SelectionRead` with `Text(string Value)`, `None` (a readable focused element with no selection, or a secure field: dictate), `Unreadable` (no focused element could be reached: only here may the copy fallback run); `public interface ISelectionReader { SelectionRead CaptureSelectedText(int maxChars); }`; `public interface ISelectionCopy { Task<string?> CopySelectionAsync(int maxCharacters, CancellationToken ct); }`; `public sealed class SelectionSpeaker(AssemblyAISpeechSynthesizer synthesizer, ReadAloudLLM llm, Func<double, IPcmSink> makeSink)` with `const int MaxCharacters = 4000`, `static Task<string?> ResolveAsync(SelectionRead read, ISelectionCopy copy, CancellationToken ct)`, `static async Task<string?> FocusedSelectionAsync(ISelectionReader reader, ISelectionCopy copy, CancellationToken ct)` (runs the reader on a dedicated thread via `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)`, then resolves), `Task SpeakAsync(string text, ReadAloudStyle style, CancellationToken ct)`, `Task AnswerAsync(string request, string selection, ReadAloudStyle style, CancellationToken ct)`.

- [ ] **Step 1: `RecordingSink`** records `Chunks`, `Drained`, `Stopped` (thread-safe). **Tests**, porting the Swift suite: plays every chunk in order then drains; a synthesis failure (Error frame `boom`) stops the sink, leaves it undrained, and surfaces `SpeechSynthesisException`; cancelling the speaking task stops playback; the standard style reads verbatim at rate 1 with no gateway call (count `FakeHttpTransport.Requests`); `ReadAloudStyle(2, true)` speaks the gateway's rewrite at rate 2 (the `makeSink` factory records the rate); a 503 rewrite falls back to reading the selection verbatim; `AnswerAsync` speaks the gateway's answer at the style's rate; a failed answer throws `ReadAloudLLMException(Status(503))` and sends nothing on the socket; a cancellation during the rewrite (transport `Hold`) plays nothing and opens no socket (`connects == 0`); `ResolveAsync` returns the AX text for `Text`, null for `None` without calling the copy, and the copy's result for `Unreadable`. Name the missing-key case `AReadWithNoKeyOpensNoSocket` (the synthesizer's `BlurtException` surfaces, `connects == 0`).

- [ ] **Step 2: Implement `SelectionSpeaker`**

```csharp
    /// <summary>Reads <paramref name="text"/> in <paramref name="style"/>: rewritten for listening first when it skips jargon, then played at its rate.</summary>
    public async Task SpeakAsync(string text, ReadAloudStyle style, CancellationToken ct)
    {
        var spoken = style.SkipsJargon ? await ForListeningAsync(text, ct).ConfigureAwait(false) : text;
        // A stop that landed while the rewrite was on its way back: nothing plays, no socket opens.
        ct.ThrowIfCancellationRequested();
        var sink = _makeSink(style.Rate);
        using var stopOnCancel = ct.Register(sink.Stop);
        try
        {
            await foreach (var chunk in _synthesizer.Synthesize(spoken, ct).ConfigureAwait(false))
            {
                sink.Enqueue(chunk);
            }
        }
        catch (Exception)
        {
            sink.Stop();
            throw;
        }

        await sink.DrainAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Answers <paramref name="request"/> about <paramref name="selection"/> and reads the answer at <paramref name="style"/>'s rate; the answer is written to be heard, so it skips the rewrite. No fallback.</summary>
    public async Task AnswerAsync(string request, string selection, ReadAloudStyle style, CancellationToken ct)
    {
        var reply = await _llm.AnswerAsync(request, selection, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await SpeakAsync(reply, new ReadAloudStyle(style.Rate, SkipsJargon: false), ct).ConfigureAwait(false);
    }

    /// <summary>The rewrite, or <paramref name="text"/> itself when the rewrite fails; a cancellation is the one failure that does not fall back.</summary>
    private async Task<string> ForListeningAsync(string text, CancellationToken ct)
    {
        try
        {
            return await _llm.RewriteForListeningAsync(text, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            BlurtLog.Error(LogCategory, $"read-aloud rewrite failed, reading verbatim: {ex.GetType().Name}: {ex.Message}");
            return text;
        }
    }
```

- [ ] **Step 3: Run the engine tests and format** → green.

- [ ] **Step 4: Commit**

```bash
git add Windows/src/Blurt.Engine Windows/tests/Blurt.Engine.Tests
git commit -m "feat(windows): selection speaker over the sink, reader, and copy seams"
```

---

### Task 11: Platform seams: WASAPI playback, UIA selection read, Ctrl+C copy fallback

**Files:**

- Create: `Windows/src/Blurt.Engine.Windows/Audio/Interop/AudioRenderClient.cs`, `Windows/src/Blurt.Engine.Windows/Audio/WasapiPcmPlayer.cs`, `Windows/src/Blurt.Engine.Windows/Injection/ICopyClipboard.cs`, `Windows/src/Blurt.Engine.Windows/Injection/SelectionCopy.cs`
- Modify: `Windows/src/Blurt.Engine.Windows/Audio/Interop/AudioClient.cs` (`RenderEndpoint.ActivateDefault`), `Windows/src/Blurt.Engine.Windows/FocusCapture/UiaFocusCapture.cs` (implements `ISelectionReader`), `Windows/src/Blurt.Engine.Windows/FocusCapture/FieldContextRules.cs` (pure selection rule), `Windows/src/Blurt.Engine.Windows/Injection/Win32Clipboard.cs` (implements `ICopyClipboard`), `Windows/src/Blurt.Engine.Windows/Injection/Interop/User32Input.cs` (`CtrlCInputs`, `PostCtrlC`)
- Test: create `Windows/tests/Blurt.Engine.Windows.Tests/Audio/WasapiPcmPlayerSmokeTests.cs` (`[Explicit]`), `Windows/tests/Blurt.Engine.Windows.Tests/Injection/SelectionCopyTests.cs`, `Windows/tests/Blurt.Engine.Windows.Tests/Injection/FakeCopyClipboard.cs`; modify `FocusCapture/FieldContextRulesTests.cs`, `Injection/User32InputTests.cs`

**Swift spec:** at `upstream/main`: `Sources/BlurtEngine/TTS/SelectionCopy.swift`, `TTS/StreamingPCMPlayer.swift` (behavior of `drain`/`stop` only), `FocusCapture/FocusCapture.swift` (`captureSelectedText`); `Tests/BlurtEngineTests/SelectionCopyTests.swift`.

**Interfaces:**

- Consumes: `IPcmSink`, `ISelectionReader`, `SelectionRead`, `ISelectionCopy`, `PcmTimeStretch` (Tasks 9–10), `ClipboardSnapshot`, `HookEventReducer.InjectedTag`.
- Produces: `public sealed class WasapiPcmPlayer(int sampleRate, double rate = 1) : IPcmSink`; `public interface ICopyClipboard { uint SequenceNumber { get; } ClipboardSnapshot? Snapshot(); string? CurrentString(); void Restore(ClipboardSnapshot saved, uint ifSequenceIs); }`; `public sealed class SelectionCopy(ICopyClipboard clipboard, Func<bool> postCopy, TimeSpan? timeout = null, TimeProvider? clock = null) : ISelectionCopy` with a production ctor `SelectionCopy()` over `Win32Clipboard` and `User32Input.PostCtrlC`; `UiaFocusCapture.CaptureSelectedText(int maxChars)`; `FieldContextRules.ClassifySelection(bool elementReachable, bool? isPassword, string? selected, int maxChars)` returning a `SelectionRead`; `User32Input.CtrlCInputs()`, `PostCtrlC()`.

- [ ] **Step 1: `User32Input`**: add `VkC = 0x43`, `CtrlCInputs()` (Ctrl down, C down, C up, Ctrl up, each tagged `InjectedTag`), `PostCtrlC()`; extend `User32InputTests` with the four-event shape and the tag on every event, mirroring the Ctrl+V test.

- [ ] **Step 2: The pure selection rule and its tests.** In `FieldContextRules`:

```csharp
    /// <summary>
    /// What a selection read found. No reachable element is <see cref="SelectionRead.Unreadable"/>
    /// (the one case the copy fallback may run on); an unreadable password flag or a password field
    /// is <see cref="SelectionRead.None"/>; otherwise the selection, visible text only, cut to
    /// <paramref name="maxChars"/>, or <see cref="SelectionRead.None"/> when there is none.
    /// </summary>
    public static SelectionRead ClassifySelection(bool elementReachable, bool? isPassword, string? selected, int maxChars)
```

with `SelectionRead.Text` only when `FocusCaptureRules.VisibleTextOrNull(selected)` (reuse the existing visible-text rule, adding one if the port names it differently) is non-null, prefixed to `maxChars`. Tests: unreachable → `Unreadable`; `isPassword` null or true → `None`; empty/whitespace/zero-width-only selection → `None`; `"  hello  "` with `maxChars` 3 → `Text("hel")`.

`UiaFocusCapture.CaptureSelectedText(int maxChars)`: `FocusedElement()` null → `ClassifySelection(false, ...)`; read `IsPassword` through `Read`; read the selection through `TextPattern.GetSelection()` (`[0].GetText(maxChars)`; no pattern or no range → null); return the classification. Document: "Call off the UI thread; every read is a cross-process round trip."

- [ ] **Step 3: `ICopyClipboard` on `Win32Clipboard`**: `SequenceNumber` exists; add `CurrentString()` (open, `ReadPlainText`, close; null when the clipboard cannot be opened) and `Restore(ClipboardSnapshot saved, uint ifSequenceIs)` (`if (SequenceNumber != ifSequenceIs) return; Restore(saved);`).

- [ ] **Step 4: `SelectionCopy` tests** (`FakeCopyClipboard`: `SequenceNumber`, `Text`, `RestoreCount`, `Readable`; `AnswerCopy(string? copied)` returns a `Func<bool>` that writes `copied` and bumps the sequence when non-null; a `Clear()` that nulls the text and bumps the sequence). Port every Swift case (use `TimeProvider.System` and a 50 ms timeout; the polls are real 10 ms waits, as in the Swift tests): copies and restores (text trimmed, user clipboard back); nothing copied → null and no restore; unreadable clipboard → null and `postCopy` never called; capped to `maxCharacters`; a clear followed 30 ms later by a write (use a timer on the fake clock, or have `postCopy` clear now and a `Task` write after the first poll) returns the late copy and restores; zero-width-only copy → null.

- [ ] **Step 5: Implement `SelectionCopy`**

```csharp
    /// <summary>
    /// The copied text, up to <paramref name="maxCharacters"/>, or null when the copy produced
    /// nothing readable. Polls every 10 ms up to the timeout for a sequence change that comes with
    /// a string (an app may clear the clipboard and write it a moment later). The clipboard is
    /// restored whenever the copy changed it and nothing wrote after the copy.
    /// </summary>
    public async Task<string?> CopySelectionAsync(int maxCharacters, CancellationToken ct)
    {
        var saved = _clipboard.Snapshot();
        if (saved is null)
        {
            return null;
        }

        var before = _clipboard.SequenceNumber;
        if (!_postCopy())
        {
            return null;
        }

        var deadline = _clock.GetTimestamp();
        string? copied = null;
        var seen = before;
        do
        {
            await Task.Delay(PollInterval, _clock, ct).ConfigureAwait(false);
            var sequence = _clipboard.SequenceNumber;
            if (sequence != before)
            {
                var text = _clipboard.CurrentString();
                // A write between the two reads would pair the string with a stale sequence, and the
                // restore would then skip; poll again instead.
                if (_clipboard.SequenceNumber == sequence)
                {
                    seen = sequence;
                    copied = text;
                }
            }
        }
        while (copied is null && _clock.GetElapsedTime(deadline) < _timeout);

        if (seen == before)
        {
            return null;
        }

        _clipboard.Restore(saved, ifSequenceIs: seen);
        return FocusCaptureRules.VisibleTextOrNull(copied?.TrimmedNonEmpty()) is { } visible
            ? (visible.Length > maxCharacters ? visible[..maxCharacters] : visible)
            : null;
    }
```

Run on `Task.Run` from the caller (the clipboard calls open and close the clipboard on the calling thread).

- [ ] **Step 6: `IAudioRenderClient` interop and `WasapiPcmPlayer`**

Add to `AudioClient.cs`: `[ComImport][Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")] internal interface IAudioRenderClient { int GetBuffer(uint frames, out IntPtr data); int ReleaseBuffer(uint written, uint flags); }` and `RenderEndpoint.ActivateDefault(out IAudioClient?)` (same as `CaptureEndpoint` with `EDataFlow.Render`). `WaveFormatEx.RenderRequest(int sampleRate)` for mono 16-bit at that rate.

`WasapiPcmPlayer`: on construction, a `PcmTimeStretch` when `rate != 1`; a `BlockingCollection<byte[]>` queue; a dedicated MTA thread started on the first `Enqueue` that activates the default render endpoint, initializes shared mode with `StreamFlagsEventCallback | StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality`, 100 ms buffer, `SetEventHandle`, gets `IAudioRenderClient`, starts, and loops: wait the event, `GetCurrentPadding`, write up to `bufferFrames - padding` frames from the current chunk (carrying partial chunks), pad with `BufferFlagsSilent` when the queue is empty, and exits when stopped or when the queue is complete and the padding reaches 0. `Enqueue(pcm)`: after `Stop` a no-op; passes through the stretch; adds to the queue. `DrainAsync(ct)`: completes adding, flushes the stretch tail onto the queue first, then waits for the render thread to exit, bounded by `queued seconds + 2 s` (a `TaskCompletionSource` the thread completes, raced against `Task.Delay`), then `Stop()`. `Stop()`: idempotent; sets a stopped flag, completes the queue, and lets the thread exit after `IAudioClient.Stop` and `Reset`. Every COM failure is logged under `"PcmPlayer"` and ends playback; never throws onto the caller. A missing render device (no endpoint) completes `DrainAsync` at once.

`WasapiPcmPlayerSmokeTests` (`[Explicit, Category("Explicit")]`): enqueue 0.5 s of a 440 Hz tone at rate 1, `DrainAsync` completes within 3 s; at rate 2 the same tone drains within 2 s.

- [ ] **Step 7: Run the Windows test project (non-explicit), run the smoke test once on this machine, format** → green; quote the smoke test output.

- [ ] **Step 8: Commit**

```bash
git add Windows/src/Blurt.Engine.Windows Windows/tests/Blurt.Engine.Windows.Tests
git commit -m "feat(windows): wasapi playback, uia selection read, and the copy fallback"
```

---

### Task 12: Wire read-aloud into the app and add the Experimental section

**Files:**

- Create: `Windows/src/Blurt.App/Services/SelectionSpeechRouter.cs`, `Windows/src/Blurt.App/Settings/ExperimentalViewModel.cs`, `Windows/src/Blurt.App/Settings/ExperimentalSection.xaml`, `ExperimentalSection.xaml.cs`
- Modify: `Windows/src/Blurt.App/DictationComponents.cs` (`Speaker`, `SelectionReader`, `SelectionCopy`), `Windows/src/Blurt.App/AppCoordinator.cs` (router between hook and session), `Windows/src/Blurt.App/Services/SettingsStores.cs` (three stores), `Windows/src/Blurt.App/Settings/SettingsViewModel.cs` (`Experimental`), `Windows/src/Blurt.App/Settings/AdvancedTab.xaml` (section after Updates, before Developer), `Windows/src/Blurt.App/App.xaml.cs`

**Interfaces:**

- Consumes: everything from Tasks 5–11.
- Produces: `SelectionSpeechRouter(DictationSession session, SettingsStores settings, SelectionSpeaker speaker, Func<CancellationToken, Task<string?>> readSelection)` with `Submit(Command)`, `KeyLatched()`, `SessionReachedTerminalPhase(PipelinePhase)`, `Action SyncGateAfterSession { get; set; }`, `Action ResetGate { get; set; }`, `IAsyncDisposable`; `ExperimentalViewModel(SettingsStores, DictationSettingsViewModel)` with `bool ReadAloud`, `bool Ask`, `double Speed`, `IReadOnlyList<Choice<double>> SpeedChoices`, `bool SkipsJargon`, `string HowToUse`, `string AskNote`, `string SkipNote`, `bool ShowsReadOptions`, `bool ShowsAskNote`, `bool ShowsSkipNote`, `void Reload()`.

- [ ] **Step 1: `SelectionSpeechRouter`**

```csharp
using System.Media;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Hotkey;
using Blurt.Engine.Pipeline;
using Blurt.Engine.TTS;

namespace Blurt.App.Services;

/// <summary>
/// Sits between the trigger hook and the dictation session. With the read-aloud switch off every
/// command goes straight to <see cref="DictationSession.Submit"/>; with it on, a press over selected
/// text reads it aloud, and a hold with asking on records a request about it and reads the answer.
/// The decisions are <see cref="SelectionSpeechRouting"/>'s; this class carries out its actions.
/// Thread-safe: the routing state sits behind one lock and every action runs outside it, so a
/// hook-thread callback never blocks.
/// </summary>
public sealed class SelectionSpeechRouter : IAsyncDisposable
{
    private const string LogCategory = "SelectionSpeech";

    private readonly DictationSession _session;
    private readonly SettingsStores _settings;
    private readonly SelectionSpeaker _speaker;
    private readonly Func<CancellationToken, Task<string?>> _readSelection;
    private readonly Lock _gate = new();
    private readonly SelectionSpeechRouting _routing = new();
    private CancellationTokenSource? _speech;
    private CancellationTokenSource? _holdTimer;
    private int _generation;

    public SelectionSpeechRouter(
        DictationSession session, SettingsStores settings, SelectionSpeaker speaker, Func<CancellationToken, Task<string?>> readSelection)
    {
        _session = session;
        _settings = settings;
        _speaker = speaker;
        _readSelection = readSelection;
    }

    /// <summary>The hook's terminal-phase sync: a dictation ended without a key event.</summary>
    public Action SyncGateAfterSession { get; set; } = static () => { };

    /// <summary>The hook's silent gate reset: a read ended without a key event.</summary>
    public Action ResetGate { get; set; } = static () => { };

    /// <summary>Routes one trigger command.</summary>
    public void Submit(Command command)
    {
        var readAloud = _settings.SelectionSpeech.IsEnabled;
        var ask = _settings.SelectionAsk.IsEnabled;
        Perform(Route(routing => routing.Submit(command, readAloud, ask)));
    }

    /// <summary>The hook's latch: the press was a tap.</summary>
    public void KeyLatched() => Perform(Route(routing => routing.KeyLatched()));

    /// <summary>A dictation reached a terminal phase.</summary>
    public void SessionReachedTerminalPhase(PipelinePhase phase) => Perform(Route(routing => routing.SessionReachedTerminalPhase(phase)));

    private IReadOnlyList<RoutingAction> Route(Func<SelectionSpeechRouting, IReadOnlyList<RoutingAction>> decide)
    {
        lock (_gate)
        {
            return decide(_routing);
        }
    }

    private void Perform(IReadOnlyList<RoutingAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case RoutingAction.ForwardAction forward:
                    _session.Submit(forward.Command);
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.ReadSelection):
                    TaskObserver.Observe(ReadSelectionAsync(), LogCategory, "selection read");
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.StartHoldTimer):
                    StartHoldTimer();
                    break;
                case RoutingAction.Speak speak:
                    var style = _settings.ReadAloudStyle.Style;
                    Run(ct => _speaker.SpeakAsync(speak.Text, style, ct));
                    break;
                case RoutingAction.Answer answer:
                    var answerStyle = _settings.ReadAloudStyle.Style;
                    Run(ct => _speaker.AnswerAsync(answer.Request, answer.Selection, answerStyle, ct));
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.StopSpeech):
                    StopSpeech();
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.SyncGateAfterSession):
                    SyncGateAfterSession();
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.ResetGate):
                    ResetGate();
                    break;
                default:
                    break;
            }
        }
    }

    private async Task ReadSelectionAsync()
    {
        string? selection;
        try
        {
            selection = await _readSelection(CancellationToken.None);
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"selection read failed: {ex.GetType().Name}");
            selection = null;
        }

        Perform(Route(routing => routing.SelectionResolved(selection)));
    }

    /// <summary>One timer at a time: a newer press cancels the older one's, so a timer left over from a quick tap cannot mark the next press as a hold.</summary>
    private void StartHoldTimer()
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _holdTimer;
            _holdTimer = cts;
        }

        previous?.Cancel();
        previous?.Dispose();
        TaskObserver.Observe(HoldAsync(cts.Token), LogCategory, "hold timer");
    }

    private async Task HoldAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SelectionSpeechRouting.AskHoldDelay, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Perform(Route(routing => routing.HoldElapsed()));
    }

    /// <summary>Runs one read or answer as the current speech; only a speech that ends on its own reports back.</summary>
    private void Run(Func<CancellationToken, Task> work)
    {
        var cts = new CancellationTokenSource();
        int generation;
        lock (_gate)
        {
            _speech?.Cancel();
            _speech = cts;
            generation = ++_generation;
        }

        TaskObserver.Observe(SpeakAsync(work, cts, generation), LogCategory, "read-aloud");
    }

    private async Task SpeakAsync(Func<CancellationToken, Task> work, CancellationTokenSource cts, int generation)
    {
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"read-aloud failed: {ex.GetType().Name}: {ex.Message}");
            SystemSounds.Beep.Play();
        }

        IReadOnlyList<RoutingAction> actions;
        lock (_gate)
        {
            // A read this router stopped was cancelled, and a newer one may already be playing.
            if (cts.IsCancellationRequested || generation != _generation)
            {
                return;
            }

            _speech = null;
            actions = _routing.SpeechFinished();
        }

        Perform(actions);
    }

    private void StopSpeech()
    {
        CancellationTokenSource? speech;
        lock (_gate)
        {
            speech = _speech;
            _speech = null;
        }

        speech?.Cancel();
    }

    /// <summary>Stops any speech and hold timer.</summary>
    public ValueTask DisposeAsync()
    {
        StopSpeech();
        CancellationTokenSource? hold;
        lock (_gate)
        {
            hold = _holdTimer;
            _holdTimer = null;
        }

        hold?.Cancel();
        return ValueTask.CompletedTask;
    }
}
```

`SettingsStores` gains `SelectionSpeech`, `SelectionAsk`, `ReadAloudStyle` (the three stores from Task 6). Since `RoutingAction.ReadSelection` and the other singletons are `static readonly` fields of abstract type, matching by `ReferenceEquals` in the switch is what the code above does; if `EnforceCodeStyleInBuild` objects to `case var _ when`, use an `if/else if` chain instead.

- [ ] **Step 2: Composition.** `DictationComponents` gains `SelectionSpeaker Speaker` and `Func<CancellationToken, Task<string?>> ReadSelection`; `Production` builds `new SelectionSpeaker(new AssemblyAISpeechSynthesizer(() => keyStore.Current, ClientWebSocketSpeechSocket.ConnectAsync), new ReadAloudLLM(() => keyStore.Current, transport), rate => new WasapiPcmPlayer(AssemblyAISpeechSynthesizer.SampleRate, rate))` and `ct => SelectionSpeaker.FocusedSelectionAsync(focus, new SelectionCopy(), ct)` (the `SelectionCopy` production ctor over `Win32Clipboard` and `User32Input.PostCtrlC`). `AppCoordinator.Start` builds `_speechRouter = new SelectionSpeechRouter(Session, _settings, _components.Speaker, _components.ReadSelection)`, points the hook's callbacks at `router.Submit(Command.Press/Release/Cancel/CancelRecording)` and `onLatch: router.KeyLatched`, sets `router.SyncGateAfterSession = () => _keyHook?.SyncAfterTerminalPhase()` and `router.ResetGate = () => _keyHook?.ResetGate()`, and `Render` replaces `_keyHook?.SyncAfterTerminalPhase()` with `_speechRouter?.SessionReachedTerminalPhase(phase)`. `DisposeAsync` disposes the router after the hook.

- [ ] **Step 3: `ExperimentalViewModel`**

Reads and writes the three stores; `SpeedChoices = ReadAloudStyleStore.SpeedChoices.Select(c => new Choice<double>(c, ReadAloudStyleStore.SpeedLabel(c, CultureInfo.CurrentCulture)))`; `Speed` getter returns `_style.Speed` (already snapped); setters raise the dependent properties (`ShowsReadOptions`, `ShowsAskNote`, `ShowsSkipNote`, `HowToUse`). Subscribes to `DictationSettingsViewModel.PropertyChanged` for `TriggerKey`/`Activation` and raises `HowToUse`, `AskNote`. Copy:

```csharp
    /// <summary>How to use read-aloud under the current key and activation mode, ending with the dictation fallback sentence.</summary>
    public string HowToUse
    {
        get
        {
            var key = _dictation.TriggerKey.Label();
            var activation = _dictation.Activation;
            var line = !Ask
                ? $"Select text and {activation.StartVerb()} {key} to hear it. {activation.StopHint()}"
                : activation == TriggerActivation.Hold
                    ? $"Select text and hold {key} without speaking to hear it. Press again to stop."
                    : $"Select text and tap {key} to hear it. Tap again to stop.";
            return $"{line} With nothing selected, the key dictates as usual.";
        }
    }

    public string AskNote =>
        $"Hold {_dictation.TriggerKey.Label()} over a selection and say what you want, like “summarize this,” to hear the answer. "
        + "Asking sends the selection and what you said to AssemblyAI’s LLM Gateway.";

    public string SkipNote =>
        "To skip code, links, and long numbers, Blurt sends the selection to AssemblyAI’s LLM Gateway before reading it.";

    public bool ShowsReadOptions => ReadAloud;

    public bool ShowsAskNote => ReadAloud && Ask;

    public bool ShowsSkipNote => ReadAloud && SkipsJargon;
```

- [ ] **Step 4: `ExperimentalSection.xaml`** (a `UserControl` whose `DataContext` is `{Binding Experimental}`): header "Experimental"; a card with `CheckBox` "Read selected text aloud" (`IsChecked="{Binding ReadAloud, Mode=TwoWay}"`), then a `StackPanel` visible with `ShowsReadOptions` holding `CheckBox` "Hold to ask about the selection" (`Ask`), a `DockPanel` row "Speed" with a `ComboBox` (`ItemsSource="{Binding SpeedChoices, Mode=OneTime}"`, `DisplayMemberPath="Label"`, `SelectedValuePath="Value"`, `SelectedValue="{Binding Speed, Mode=TwoWay}"`, `AutomationProperties.Name="Read-aloud speed"`), and `CheckBox` "Skip code, links, and long numbers" (`SkipsJargon`); then three `Footer` text blocks: `HowToUse` always, `AskNote` visible with `ShowsAskNote`, `SkipNote` visible with `ShowsSkipNote`. Insert `<settings:ExperimentalSection Margin="0,4,0,4" />` in `AdvancedTab.xaml` after the Updates card and before `DeveloperSection`. `SettingsViewModel` gains `Experimental` (constructed in `App.xaml.cs` from `settings` and `dictation`) and `Reload()` calls `Experimental.Reload()`.

- [ ] **Step 5: Build, format, run the non-explicit suites, and check by hand** (with the API key already saved on this machine): turn on "Read selected text aloud", select text in Notepad, tap Right Ctrl: speech plays and the next tap stops it; with nothing selected a tap still dictates; turn on asking, hold Right Ctrl over a selection, say "summarize this", release: the answer plays; set speed 2×: faster, same pitch; turn the switch off; quit from the tray. Report exactly what happened, including any beep (a beep means a logged failure in `%LOCALAPPDATA%\Blurt-dev\Logs\blurt.log`; quote the line).

- [ ] **Step 6: Commit**

```bash
git add Windows/src/Blurt.App
git commit -m "feat(windows): experimental read-aloud with hold-to-ask, speed, and skip jargon"
```

---

### Task 13: Links, tray hint, version, and docs

**Files:**

- Create: `Windows/src/Blurt.App/Services/BlurtLinks.cs`
- Modify: `Windows/src/Blurt.App/Main/ReadyView.xaml` and `.xaml.cs` (footer), `Windows/src/Blurt.App/Tray/TrayIcon.cs` (activation-aware hint, two link items), `Windows/src/Blurt.App/App.xaml.cs` (tray ctor and link handlers), `Windows/Directory.Build.props` (`Version` `0.1.62`), `Windows/README.md`, `AGENTS.md` (the "Windows port" paragraph gains one sentence naming the sync design doc), `Windows/design/2026-09-22-windows-port-design.md` (a one-line pointer at the top to the sync doc)
- Test: any `UserAgentTests` or version test that pins `0.1.56` is updated to `0.1.62`.

**Interfaces:**

- Produces: `public static class BlurtLinks { static readonly Uri PoweredBy; static readonly Uri ReportBug; static readonly Uri ShareOnLinkedIn; static void Open(Uri url, string logCategory); }` with `ReportBug = https://github.com/ThirtyNineWest/blurt/issues` and `ShareOnLinkedIn = https://www.linkedin.com/sharing/share-offsite/?url=https%3A%2F%2Fwww.assemblyai.com%2Fblurt`; `TrayIcon(TriggerKeyStore, TriggerActivationStore)` with events `ReportBugRequested`, `ShareRequested`.

- [ ] **Step 1: `BlurtLinks`** with `Open` carrying the `Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })` and the `Win32Exception or InvalidOperationException` catch that logs under `logCategory`; `ReadyView.OnPoweredByClick` calls it. Footer XAML becomes:

```xml
    <TextBlock Margin="0,20,0,0" HorizontalAlignment="Center" FontSize="11">
      <Run Foreground="{StaticResource Secondary}" Text="Powered by " /><Hyperlink Foreground="{StaticResource Accent}" Click="OnPoweredByClick">AssemblyAI</Hyperlink><Run Foreground="{StaticResource Secondary}" Text=" · " /><Hyperlink Foreground="{StaticResource Accent}" Click="OnReportBugClick">Report a bug</Hyperlink>
    </TextBlock>
```

- [ ] **Step 2: Tray.** `RefreshHint` becomes `$"{_activation.Activation.Label()} {_triggerKey.TriggerKey.Label()} to dictate and paste"`. Add menu items "Report a Bug" and "Share Blurt on LinkedIn" after "Settings…", raising the two events; `App.Compose` passes `settings.TriggerActivation` and opens the links through `BlurtLinks.Open`.

- [ ] **Step 3: Version and docs.** `Directory.Build.props` `Version` → `0.1.62`. `Windows/README.md`: add a "What's new in the v0.1.62 sync" section (text shortcuts in Settings → Vocabulary; the same-window separator fix; Experimental read-aloud with hold-to-ask, speed, skip jargon, naming the LLM Gateway and the undocumented TTS endpoint; the Ctrl+Shift+C hazard with Right Shift; the fork issues link), point the design list at both design docs, and bump the test count sentence if one exists. `AGENTS.md` Windows paragraph: append "The sync to upstream v0.1.62 is documented in `Windows/design/2026-10-13-upstream-sync-design.md`." Add the pointer line to the 2026-09-22 design doc. Run `npx prettier --check Windows/README.md Windows/design AGENTS.md` and `npx markdownlint-cli2 Windows/README.md Windows/design/*.md AGENTS.md` if those tools resolve on this machine; otherwise note it for CI.

- [ ] **Step 4: Full verification**

Run: `dotnet build Windows/Blurt.sln -warnaserror`, `dotnet test Windows/Blurt.sln --filter "TestCategory!=Explicit"`, `dotnet format Windows/Blurt.sln --verify-no-changes`.
Expected: green; quote the test totals.

- [ ] **Step 5: Commit**

```bash
git add Windows AGENTS.md
git commit -m "feat(windows): footer and tray links, activation-aware hint, version 0.1.62"
```
