using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class ApiKeyDisplayTests
{
    private const string RealKey = "0123456789abcdef0123456789abcdef";

    [Test]
    public void NoKeyReadsAsNotConnected()
    {
        ApiKeyDisplay.Resolve(null).Should().Be(ApiKeyDisplay.NotConnected);
        ApiKeyDisplay.Resolve("").Should().Be(ApiKeyDisplay.NotConnected);
    }

    [Test]
    public void AWhitespaceOnlyKeyIsNoKeyAtAll()
    {
        ApiKeyDisplay.Resolve("   \n\t ").Should().Be(ApiKeyDisplay.NotConnected);
    }

    [Test]
    public void AStoredKeyShowsOnlyItsLastFourCharacters()
    {
        var display = ApiKeyDisplay.Resolve(RealKey);

        display.Should().Be(new ApiKeyDisplay.ConnectedDisplay("••••cdef"));
        display.StatusText.Should().Be("••••cdef");
        display.IsConnected.Should().BeTrue();
        display.RendersIdentifier.Should().BeTrue();
    }

    [Test]
    public void TheMaskNeverRevealsMoreThanTheTailLength()
    {
        var display = ApiKeyDisplay.Resolve(RealKey);
        var text = display.StatusText;
        var hidden = RealKey[..^ApiKeyDisplay.RevealedTailLength];

        text.Should().NotContain(hidden);
        text.Should().EndWith(RealKey[^ApiKeyDisplay.RevealedTailLength..]);
    }

    [TestCase("k")]
    [TestCase("ab")]
    [TestCase("sk-1")]
    [TestCase("sk-1234")]
    public void AKeyTooShortToMaskIsNeverPartiallyRevealed(string shortKey)
    {
        var display = ApiKeyDisplay.Resolve(shortKey);

        display.Should().Be(new ApiKeyDisplay.ConnectedDisplay(null));
        display.StatusText.Should().Be("Connected");
        display.IsConnected.Should().BeTrue();
        display.RendersIdentifier.Should().BeFalse();
        display.StatusText.Should().NotContain(shortKey);
    }

    [Test]
    public void TheNotConnectedRowReadsAsProseNotAsAnIdentifier()
    {
        ApiKeyDisplay.NotConnected.RendersIdentifier.Should().BeFalse();
    }

    [Test]
    public void TheThresholdKeepsAtLeastHalfOfAnyMaskedKeyHidden()
    {
        ApiKeyDisplay.MinimumLengthToMask.Should().Be(ApiKeyDisplay.RevealedTailLength * 2);

        var atThreshold = new string('x', ApiKeyDisplay.MinimumLengthToMask);
        ApiKeyDisplay.Resolve(atThreshold).RendersIdentifier.Should().BeTrue();
        ApiKeyDisplay.Resolve(atThreshold[..^1]).RendersIdentifier.Should().BeFalse();
    }

    [Test]
    public void TheKeyIsTrimmedBeforeMaskingSoTheTailIsRealCharacters()
    {
        var display = ApiKeyDisplay.Resolve($"  {RealKey}\n");

        display.Should().Be(new ApiKeyDisplay.ConnectedDisplay("••••cdef"));
    }

    [Test]
    public void VoiceOverSpellsTheStateOutInsteadOfSpeakingTheBullets()
    {
        ApiKeyDisplay.Resolve(null).AccessibilityLabel.Should().Be("Not connected");
        ApiKeyDisplay.Resolve(RealKey).AccessibilityLabel.Should().Be("Connected, key ending cdef");
        ApiKeyDisplay.Resolve("sk-1").AccessibilityLabel.Should().Be("Connected");
    }

    [Test]
    public void TheAccessibilityLabelNeverSpeaksTheMaskCharacters()
    {
        ApiKeyDisplay.Resolve(RealKey).AccessibilityLabel.Should().NotContain("•");
    }

    [Test]
    public void ControlTitlesSwitchBetweenFirstConnectAndRotateWording()
    {
        var empty = ApiKeyDisplay.Resolve(null);
        var stored = ApiKeyDisplay.Resolve(RealKey);

        empty.EditButtonTitle.Should().Be("Connect…");
        stored.EditButtonTitle.Should().Be("Change…");
        empty.CommitButtonTitle.Should().Be("Connect");
        stored.CommitButtonTitle.Should().Be("Update");
        empty.CommitButtonTitle.Should().NotEndWith("…");
        stored.CommitButtonTitle.Should().NotEndWith("…");
    }

    [Test]
    public void TheSheetsRationaleExplainsTheFirstConnectThenTheRotation()
    {
        ApiKeyDisplay.Resolve(null).Rationale.Should().Contain("needs an AssemblyAI API key");
        ApiKeyDisplay.Resolve(RealKey).Rationale.Should().Contain("replace");
    }
}
