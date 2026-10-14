using Blurt.Engine.Windows.Injection;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Injection;

public sealed class SelectionCopyTests
{
    private static SelectionCopy Copy(FakeCopyClipboard clipboard, string? answering) =>
        new(clipboard, clipboard.AnswerCopy(answering), TimeSpan.FromMilliseconds(50), TimeProvider.System);

    [Test]
    public async Task ReturnsWhatTheCopyCopiedAndPutsTheUsersClipboardBack()
    {
        var clipboard = new FakeCopyClipboard();

        var text = await Copy(clipboard, "  The pre-push gate blocked it.  ").CopySelectionAsync(100, CancellationToken.None);

        text.Should().Be("The pre-push gate blocked it.");
        clipboard.Text.Should().Be("user's clipboard");
    }

    [Test]
    public async Task NothingCopiedLeavesTheClipboardUntouched()
    {
        var clipboard = new FakeCopyClipboard();

        var text = await Copy(clipboard, null).CopySelectionAsync(100, CancellationToken.None);

        text.Should().BeNull();
        clipboard.RestoreCount.Should().Be(0);
    }

    [Test]
    public async Task AnUnreadableClipboardIsNeverCopiedOver()
    {
        var clipboard = new FakeCopyClipboard(readable: false);
        var posted = false;
        var copy = new SelectionCopy(clipboard, () => posted = true, TimeSpan.FromMilliseconds(50), TimeProvider.System);

        var text = await copy.CopySelectionAsync(100, CancellationToken.None);

        text.Should().BeNull();
        posted.Should().BeFalse();
    }

    [Test]
    public async Task TheCopiedTextIsCapped()
    {
        var clipboard = new FakeCopyClipboard();

        var text = await Copy(clipboard, "abcdefgh").CopySelectionAsync(3, CancellationToken.None);

        text.Should().Be("abc");
    }

    [Test]
    public async Task AClearBeforeTheWriteIsNotMistakenForAnEmptyCopy()
    {
        var clipboard = new FakeCopyClipboard();
        Task? lateWrite = null;
        var copy = new SelectionCopy(
            clipboard,
            () =>
            {
                clipboard.Clear();
                lateWrite = WriteLaterAsync(clipboard, "late copy");
                return true;
            },
            TimeSpan.FromMilliseconds(500),
            TimeProvider.System);

        var text = await copy.CopySelectionAsync(100, CancellationToken.None);
        await lateWrite!;

        text.Should().Be("late copy");
        clipboard.Text.Should().Be("user's clipboard");
    }

    [Test]
    public async Task ACopyOfOnlyZeroWidthCharactersReadsAsNothingSelected()
    {
        var clipboard = new FakeCopyClipboard();

        var text = await Copy(clipboard, "\u200B").CopySelectionAsync(100, CancellationToken.None);

        text.Should().BeNull();
    }

    private static async Task WriteLaterAsync(FakeCopyClipboard clipboard, string text)
    {
        await Task.Delay(30);
        clipboard.Write(text);
    }
}
