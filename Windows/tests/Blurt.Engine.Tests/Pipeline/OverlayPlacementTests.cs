using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

/// <summary>
/// The overlay pill's placement rules: default bottom-center, and a persisted drag origin clamped
/// fully back onto the current screen. The clamp is the important part: a stored origin can be
/// stale (saved on a monitor that's since been unplugged) and must never leave the pill invisible
/// off-screen.
/// </summary>
public sealed class OverlayPlacementTests
{
    private static readonly Rect Screen = new(0, 0, 1000, 600);
    private static readonly Size Panel = new(200, 60);

    [Test]
    public void PanelSizeAddsTheMarginTwice()
    {
        var pill = new Size(168, 28);

        var size = OverlayPlacement.PanelSize(pill, 28);

        size.Should().Be(new Size(224, 84));
        ((size.Width - pill.Width) / 2).Should().Be(28);
        ((size.Height - pill.Height) / 2).Should().Be(28);
    }

    [Test]
    public void PanelSizeWithoutMarginLeavesThePanelExactlyPillSized()
    {
        var pill = new Size(100, 20);

        OverlayPlacement.PanelSize(pill, 0).Should().Be(pill);
    }

    [Test]
    public void PanelSizeAndOriginUseTheSameMargin()
    {
        const double margin = 28;
        var pill = new Size(168, 28);

        var size = OverlayPlacement.PanelSize(pill, margin);
        var origin = OverlayPlacement.PanelOrigin(size, Screen, null, margin);

        (origin.Y + margin).Should().Be(OverlayPlacement.DefaultBottomClearance);
        (origin.X + margin + pill.Width / 2).Should().Be(Screen.MidX);
    }

    [Test]
    public void PanelOriginMeasuresTheClearanceToThePillNotThePanel()
    {
        const double margin = 28;

        var origin = OverlayPlacement.PanelOrigin(Panel, Screen, null, margin);

        origin.Y.Should().Be(OverlayPlacement.DefaultBottomClearance - margin);
        (origin.Y + margin).Should().Be(OverlayPlacement.DefaultBottomClearance);
        origin.X.Should().Be(400);
    }

    [Test]
    public void PanelOriginStillClampsAStaleDraggedOriginBackOnScreen()
    {
        var far = new Point(5000, 5000);

        var origin = OverlayPlacement.PanelOrigin(Panel, Screen, far, 28);

        origin.Should().Be(new Point(800, 540));
    }

    [Test]
    public void NoCustomOriginIsHorizontallyCenteredBottomOffsetAboveTheBottom()
    {
        var origin = OverlayPlacement.Origin(Panel, Screen, null, 64);

        origin.Should().Be(new Point(400, 64));
    }

    [Test]
    public void TheDefaultRespectsANonZeroVisibleFrameOrigin()
    {
        var inset = new Rect(100, 50, 800, 500);

        var origin = OverlayPlacement.Origin(Panel, inset, null, 64);

        origin.Should().Be(new Point(400, 114));
    }

    [Test]
    public void AnOnScreenCustomOriginIsUsedAsIs()
    {
        var custom = new Point(300, 200);

        var origin = OverlayPlacement.Origin(Panel, Screen, custom, 64);

        origin.Should().Be(custom);
    }

    [Test]
    public void AStaleOriginPastTheTopRightClampsThePanelFullyBackOnScreen()
    {
        var origin = OverlayPlacement.Origin(Panel, Screen, new Point(2400, 1300), 64);

        origin.Should().Be(new Point(800, 540));
    }

    [Test]
    public void AStaleOriginPastTheBottomLeftClampsToTheVisibleFramesMinCorner()
    {
        var inset = new Rect(100, 50, 800, 500);

        var origin = OverlayPlacement.Origin(Panel, inset, new Point(-500, -500), 64);

        origin.Should().Be(new Point(100, 50));
    }

    [Test]
    public void ABarelyOffScreenOriginOnlyMovesAlongTheOffendingAxis()
    {
        var origin = OverlayPlacement.Origin(Panel, Screen, new Point(900, 200), 64);

        origin.Should().Be(new Point(800, 200));
    }

    [Test]
    public void APanelLargerThanTheScreenPinsDeterministicallyToTheMaxEdge()
    {
        var tiny = new Rect(0, 0, 100, 40);

        var clamped = OverlayPlacement.Clamped(new Point(50, 20), Panel, tiny);

        clamped.Should().Be(new Point(-100, -20));
    }

    [Test]
    public void TopLeftDefaultKeepsThePillTheClearanceAboveTheWorkAreaBottom()
    {
        const double margin = 28;

        var topLeft = OverlayPlacement.PanelTopLeft(Panel, Screen, null, margin);

        topLeft.Should().Be(new Point(400, 488));
        (Screen.MaxY - (topLeft.Y + Panel.Height - margin)).Should().Be(OverlayPlacement.DefaultBottomClearance);
    }

    [Test]
    public void TopLeftDefaultRespectsAnOffsetWorkArea()
    {
        var workArea = new Rect(1920, 40, 1600, 860);

        var topLeft = OverlayPlacement.PanelTopLeft(Panel, workArea, null, 28);

        topLeft.Should().Be(new Point(2620, 900 - 52 - 60));
    }

    [Test]
    public void TopLeftUsesAnOnScreenCustomOriginAsIs()
    {
        var custom = new Point(300, 200);

        var topLeft = OverlayPlacement.PanelTopLeft(Panel, Screen, custom, 28);

        topLeft.Should().Be(custom);
    }

    [Test]
    public void TopLeftClampsAStaleCustomOriginPastTheBottomRight()
    {
        var topLeft = OverlayPlacement.PanelTopLeft(Panel, Screen, new Point(5000, 5000), 28);

        topLeft.Should().Be(new Point(800, 540));
    }

    [Test]
    public void TopLeftClampsAStaleCustomOriginPastTheTopLeft()
    {
        var topLeft = OverlayPlacement.PanelTopLeft(Panel, Screen, new Point(-50, -70), 28);

        topLeft.Should().Be(new Point(0, 0));
    }
}
