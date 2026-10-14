using System.Runtime.InteropServices;
using Blurt.Engine.Windows.Injection;
using FluentAssertions;
using WpfClipboard = System.Windows.Clipboard;

namespace Blurt.Engine.Windows.Tests.Injection;

[Explicit("Rewrites the real clipboard and puts the original text back.")]
[Category(Categories.Explicit)]
[Apartment(ApartmentState.STA)]
public sealed class Win32ClipboardSmokeTests
{
    private const string Marker = "blurt-clipboard-smoke-marker";

    [Test]
    public void ASnapshotRestoresThePlainTextItReplaced()
    {
        string? backup = null;
        string[] formatsBefore;
        try
        {
            if (WpfClipboard.ContainsImage() || WpfClipboard.ContainsFileDropList() || WpfClipboard.ContainsAudio()
                || !WpfClipboard.ContainsText())
            {
                Assert.Ignore("The clipboard holds something other than plain text.");
            }

            backup = WpfClipboard.GetText();
            formatsBefore = WpfClipboard.GetDataObject()?.GetFormats() ?? [];
        }
        catch (ExternalException exception)
        {
            Assert.Ignore($"The clipboard could not be read for a backup: {exception.Message}");
            return;
        }

        var clipboard = new Win32Clipboard();
        var restored = false;
        try
        {
            var snapshot = clipboard.Snapshot();
            TestContext.Out.WriteLine($"formats before: {string.Join(", ", formatsBefore)}");
            TestContext.Out.WriteLine(
                $"snapshot: {snapshot?.Formats.Count} formats [{string.Join(", ", snapshot?.Formats.Select(f => $"{f.Format:X}{(f.RegisteredName is null ? "" : $" {f.RegisteredName}")} {f.Data.Length}B") ?? [])}]");
            snapshot.Should().NotBeNull();

            clipboard.SetString(Marker);
            var sequenceAfterWrite = clipboard.SequenceNumber;
            var duringPaste = WpfClipboard.GetText();
            var sequenceAfterRead = clipboard.SequenceNumber;
            clipboard.Restore(snapshot!);
            var after = WpfClipboard.GetText();
            var formatsAfter = WpfClipboard.GetDataObject()?.GetFormats() ?? [];
            restored = after == backup;
            TestContext.Out.WriteLine($"sequence after write {sequenceAfterWrite}, after read {sequenceAfterRead}");
            TestContext.Out.WriteLine($"formats after: {string.Join(", ", formatsAfter)}");
            TestContext.Out.WriteLine($"text restored: {restored} (length {after.Length})");

            duringPaste.Should().Be(Marker);
            sequenceAfterRead.Should().Be(sequenceAfterWrite);
            after.Should().Be(backup);
            formatsAfter.Should().BeEquivalentTo(formatsBefore);
        }
        finally
        {
            if (!restored && backup is not null)
            {
                WpfClipboard.SetText(backup);
                TestContext.Out.WriteLine("round trip failed; the backup text was put back");
            }
        }
    }
}
