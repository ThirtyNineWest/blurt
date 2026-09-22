using System.Globalization;
using Blurt.Engine.Update;
using FluentAssertions;

namespace Blurt.Engine.Tests.Update;

public sealed class LastUpdateCheckCaptionTests
{
    [Test]
    public void NoCompletedCheckSaysSo()
    {
        LastUpdateCheckCaption.Text(null, CultureInfo.InvariantCulture).Should().Be("Not checked yet");
    }

    [Test]
    public void ACompletedCheckShowsItsShortDateAndTime()
    {
        var stamp = new DateTimeOffset(2026, 9, 22, 14, 5, 0, TimeSpan.FromHours(-7));

        var caption = LastUpdateCheckCaption.Text(stamp, CultureInfo.InvariantCulture);

        caption.Should().Be("Last checked 09/22/2026 14:05");
    }
}
