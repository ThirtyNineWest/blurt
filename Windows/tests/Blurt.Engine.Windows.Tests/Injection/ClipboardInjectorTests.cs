using Blurt.Engine.FocusCapture;
using Blurt.Engine.Windows.Injection;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Blurt.Engine.Windows.Tests.Injection;

public sealed class ClipboardInjectorTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);
    private static readonly FocusTarget Target = new(0x1234, 501, "Code", @"C:\Apps\Code\Code.exe");

    [Test]
    public async Task APasteRestoresTheUsersClipboardOnlyAfterTheSettle()
    {
        var harness = new Harness("user-clipboard");

        await harness.Injector.InsertAsync("dictated text", null, null, CancellationToken.None);
        harness.Clock.Advance(Settle - TimeSpan.FromMilliseconds(1));
        var beforeSettle = harness.Clipboard.Text;
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await harness.Injector.PendingSettle;

        harness.Pasted.Should().Equal("dictated text");
        beforeSettle.Should().Be("dictated text");
        harness.Clipboard.Text.Should().Be("user-clipboard");
    }

    [Test]
    public async Task InsertReturnsBeforeTheDeferredRestoreRuns()
    {
        var harness = new Harness("user-clipboard");

        await harness.Injector.InsertAsync("dictated text", null, null, CancellationToken.None);

        harness.Injector.PendingSettle.IsCompleted.Should().BeFalse();
        harness.Clipboard.Text.Should().Be("dictated text");
    }

    [Test]
    public async Task ACapturedTargetIsActivatedBeforeThePaste()
    {
        var harness = new Harness(null);
        await harness.Injector.SetTargetAsync(Target);

        await harness.Injector.InsertAsync("dictated text", null, null, CancellationToken.None);

        harness.Activated.Should().Equal(Target);
        harness.Pasted.Should().Equal("dictated text");
    }

    [Test]
    public async Task NoCapturedTargetPastesWithoutActivating()
    {
        var harness = new Harness(null);

        await harness.Injector.InsertAsync("dictated text", null, null, CancellationToken.None);

        harness.Activated.Should().BeEmpty();
        harness.Pasted.Should().Equal("dictated text");
    }

    [Test]
    public async Task ALostTargetLeavesTheSeparatedTextOnTheClipboardAndThrows()
    {
        var harness = new Harness("user-clipboard") { ActivationResult = false };
        await harness.Injector.SetTargetAsync(Target);

        await FluentActions.Awaiting(() => harness.Injector.InsertAsync("Second.", "First.", null, CancellationToken.None))
            .Should().ThrowAsync<BlurtException>().Where(e => e.Error is BlurtError.TargetAppLost);

        harness.Pasted.Should().BeEmpty();
        harness.Clipboard.Text.Should().Be(" Second.");
    }

    [Test]
    public async Task NoEditableTargetInANonOpaqueAppLeavesTheTextOnTheClipboardAndThrows()
    {
        var harness = new Harness("user-clipboard") { Editable = false };
        await harness.Injector.SetTargetAsync(Target);

        await FluentActions.Awaiting(() => harness.Injector.InsertAsync("dictated text", null, null, CancellationToken.None))
            .Should().ThrowAsync<BlurtException>().Where(e => e.Error is BlurtError.NoEditableTarget);

        harness.Pasted.Should().BeEmpty();
        harness.Clipboard.Text.Should().Be("dictated text");
    }

    [Test]
    public async Task AnOpaqueAppIsPastedIntoWithoutAnEditableSignal()
    {
        var harness = new Harness("user-clipboard") { Editable = false, Opaque = true };
        await harness.Injector.SetTargetAsync(Target);

        await harness.Injector.InsertAsync("dictated text", null, null, CancellationToken.None);
        harness.Clock.Advance(Settle);
        await harness.Injector.PendingSettle;

        harness.OpaqueQueries.Should().Equal(Target);
        harness.Pasted.Should().Equal("dictated text");
        harness.Clipboard.Text.Should().Be("user-clipboard");
    }

    [Test]
    public async Task AFailedPasteKeystrokeThrowsTargetLostAndKeepsTheTextOnTheClipboard()
    {
        var harness = new Harness("user-clipboard") { PostResult = false };

        await FluentActions.Awaiting(() => harness.Injector.InsertAsync("dictated text", null, null, CancellationToken.None))
            .Should().ThrowAsync<BlurtException>().Where(e => e.Error is BlurtError.TargetAppLost);
        harness.Clock.Advance(Settle);
        await harness.Injector.PendingSettle;

        harness.Clipboard.Text.Should().Be("dictated text");
    }

    [Test]
    public async Task ASecondInsertWaitsForTheFirstRestoreBeforeSnapshotting()
    {
        var harness = new Harness("user-clipboard");

        await harness.Injector.InsertAsync("first", null, null, CancellationToken.None);
        var second = harness.Injector.InsertAsync("second", null, null, CancellationToken.None);
        var finishedBeforeFirstSettle = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200))) == second;
        var pastedBeforeFirstSettle = harness.Pasted;
        harness.Clock.Advance(Settle);
        await second;
        harness.Clock.Advance(Settle);
        await harness.Injector.PendingSettle;

        finishedBeforeFirstSettle.Should().BeFalse();
        pastedBeforeFirstSettle.Should().Equal("first");
        harness.Pasted.Should().Equal("first", "second");
        harness.Clipboard.Text.Should().Be("user-clipboard");
    }

    [Test]
    public async Task ACopyDuringTheSettleIsNotRestoredOver()
    {
        var harness = new Harness("user-clipboard");

        await harness.Injector.InsertAsync("dictated", null, null, CancellationToken.None);
        harness.Clipboard.ExternalWrite("copied-mid-paste");
        harness.Clock.Advance(Settle);
        await harness.Injector.PendingSettle;

        harness.Clipboard.Text.Should().Be("copied-mid-paste");
    }

    [Test]
    public async Task ACancelledInsertPostsNothingAndLeavesTheClipboardAlone()
    {
        var harness = new Harness("user-clipboard");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => harness.Injector.InsertAsync("text", null, null, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        harness.Pasted.Should().BeEmpty();
        harness.Clipboard.Text.Should().Be("user-clipboard");
    }

    [Test]
    public async Task ACancelLandingDuringActivationAbortsBeforeThePaste()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = new Harness("user-clipboard")
        {
            Activate = _ =>
            {
                cancellation.Cancel();
                return Task.FromResult(true);
            },
        };
        await harness.Injector.SetTargetAsync(Target);

        await FluentActions.Awaiting(() => harness.Injector.InsertAsync("text", null, null, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        harness.Pasted.Should().BeEmpty();
        harness.Clipboard.Text.Should().Be("user-clipboard");
    }

    [Test]
    public async Task ASecondDictationIntoTheSameWindowGetsASeparatingSpace()
    {
        var harness = new Harness(null, TimeSpan.Zero);
        await harness.Injector.SetTargetAsync(Target);

        await harness.Injector.InsertAsync("First.", null, "notes.txt — Editor", CancellationToken.None);
        await harness.Injector.InsertAsync("Second.", null, "notes.txt — Editor", CancellationToken.None);

        harness.Pasted.Should().Equal("First.", " Second.");
    }

    [Test]
    public async Task AThrownInsertDoesNotBlockTheNextOne()
    {
        var harness = new Harness("user-clipboard") { Editable = false };

        await FluentActions.Awaiting(() => harness.Injector.InsertAsync("first", null, null, CancellationToken.None))
            .Should().ThrowAsync<BlurtException>().Where(e => e.Error is BlurtError.NoEditableTarget);
        harness.Editable = true;
        await harness.Injector.InsertAsync("second", null, null, CancellationToken.None);
        harness.Clock.Advance(Settle);
        await harness.Injector.PendingSettle;

        harness.Pasted.Should().Equal("second");
        harness.Clipboard.Text.Should().Be("first");
    }

    [Test]
    public async Task EmptyTextPostsNothing()
    {
        var harness = new Harness("user-clipboard");

        await harness.Injector.InsertAsync(string.Empty, null, null, CancellationToken.None);

        harness.Pasted.Should().BeEmpty();
        harness.Clipboard.Text.Should().Be("user-clipboard");
    }

    /// <summary>A <see cref="ClipboardInjector"/> over a <see cref="FakeClipboard"/> whose seams record their calls.</summary>
    private sealed class Harness
    {
        private readonly Lock _gate = new();
        private readonly List<string?> _pasted = [];
        private readonly List<FocusTarget> _activated = [];
        private readonly List<FocusTarget?> _opaqueQueries = [];

        public Harness(string? clipboardText, TimeSpan? settle = null)
        {
            Clipboard = new FakeClipboard(clipboardText);
            Injector = new ClipboardInjector(
                settle ?? Settle,
                PostPaste,
                ActivateTarget,
                () => Editable,
                IsOpaque,
                Clipboard,
                Clock);
        }

        public FakeClipboard Clipboard { get; }

        public FakeTimeProvider Clock { get; } = new();

        public ClipboardInjector Injector { get; }

        public bool PostResult { get; init; } = true;

        public bool ActivationResult { get; init; } = true;

        public Func<FocusTarget, Task<bool>>? Activate { get; init; }

        public bool Editable { get; set; } = true;

        public bool Opaque { get; init; }

        /// <summary>The clipboard contents at each posted paste keystroke.</summary>
        public IReadOnlyList<string?> Pasted
        {
            get
            {
                lock (_gate)
                {
                    return [.. _pasted];
                }
            }
        }

        public IReadOnlyList<FocusTarget> Activated
        {
            get
            {
                lock (_gate)
                {
                    return [.. _activated];
                }
            }
        }

        public IReadOnlyList<FocusTarget?> OpaqueQueries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _opaqueQueries];
                }
            }
        }

        private bool PostPaste()
        {
            lock (_gate)
            {
                _pasted.Add(Clipboard.Text);
            }

            return PostResult;
        }

        private Task<bool> ActivateTarget(FocusTarget target)
        {
            lock (_gate)
            {
                _activated.Add(target);
            }

            return Activate?.Invoke(target) ?? Task.FromResult(ActivationResult);
        }

        private bool IsOpaque(FocusTarget? target)
        {
            lock (_gate)
            {
                _opaqueQueries.Add(target);
            }

            return Opaque;
        }
    }
}
