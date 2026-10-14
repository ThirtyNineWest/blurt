using Blurt.Engine.Windows.Injection;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Injection;

public sealed class ClipboardSnapshotTests
{
    [TestCase(1u, TestName = "CF_TEXT is synthesized")]
    [TestCase(2u, TestName = "CF_BITMAP is a GDI handle")]
    [TestCase(3u, TestName = "CF_METAFILEPICT holds a metafile handle")]
    [TestCase(7u, TestName = "CF_OEMTEXT is synthesized")]
    [TestCase(9u, TestName = "CF_PALETTE is a GDI handle")]
    [TestCase(14u, TestName = "CF_ENHMETAFILE is a GDI handle")]
    [TestCase(16u, TestName = "CF_LOCALE is synthesized")]
    [TestCase(0x80u, TestName = "CF_OWNERDISPLAY")]
    [TestCase(0x81u, TestName = "CF_DSPTEXT")]
    [TestCase(0x82u, TestName = "CF_DSPBITMAP")]
    [TestCase(0x83u, TestName = "CF_DSPMETAFILEPICT")]
    [TestCase(0x8Eu, TestName = "CF_DSPENHMETAFILE")]
    [TestCase(0x200u, TestName = "CF_PRIVATEFIRST")]
    [TestCase(0x2FFu, TestName = "CF_PRIVATELAST")]
    [TestCase(0x300u, TestName = "CF_GDIOBJFIRST")]
    [TestCase(0x3FFu, TestName = "CF_GDIOBJLAST")]
    public void HandleBackedAndSynthesizedFormatsAreNotRestorable(uint format) =>
        ClipboardFormats.IsRestorable(format).Should().BeFalse();

    [TestCase(8u, TestName = "CF_DIB")]
    [TestCase(13u, TestName = "CF_UNICODETEXT")]
    [TestCase(15u, TestName = "CF_HDROP")]
    [TestCase(17u, TestName = "CF_DIBV5")]
    [TestCase(0x7Fu, TestName = "below CF_OWNERDISPLAY")]
    [TestCase(0x8Fu, TestName = "above CF_DSPENHMETAFILE")]
    [TestCase(0x1FFu, TestName = "below CF_PRIVATEFIRST")]
    [TestCase(0x400u, TestName = "above CF_GDIOBJLAST")]
    [TestCase(0xC000u, TestName = "first registered format")]
    [TestCase(0xFFFFu, TestName = "last registered format")]
    public void GlobalMemoryFormatsAreRestorable(uint format) =>
        ClipboardFormats.IsRestorable(format).Should().BeTrue();

    [Test]
    public void AnEmptySnapshotRestoresAnEmptyClipboard()
    {
        var snapshot = new ClipboardSnapshot([], null);

        snapshot.RestoreAction.Should().Be(ClipboardRestoreAction.Empty);
    }

    [Test]
    public void CapturedFormatsAreRestoredAsTheyWere()
    {
        var snapshot = new ClipboardSnapshot([new ClipboardItem(13, null, [0x68, 0, 0, 0])], "h");

        snapshot.RestoreAction.Should().Be(ClipboardRestoreAction.Formats);
    }

    [Test]
    public void TextWithNoCapturedFormatFallsBackToThePlainTextFloor()
    {
        var snapshot = new ClipboardSnapshot([], "only text");

        snapshot.RestoreAction.Should().Be(ClipboardRestoreAction.PlainText);
    }
}
