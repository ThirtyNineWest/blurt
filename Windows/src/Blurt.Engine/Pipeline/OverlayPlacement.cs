namespace Blurt.Engine.Pipeline;

/// <summary>
/// Where the overlay pill's panel goes on screen — pure geometry, owned here so the placement rules
/// are unit-tested. The shell feeds in the screen's visible frame and the persisted drag origin.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>
    /// Default clearance between the bottom of the visible pill and the bottom of the screen's
    /// visible frame. Measured to the pill, not the panel: the panel is deliberately larger so the
    /// drop shadow isn't clipped, so a caller placing the panel must back this off by that
    /// transparent inset — <see cref="PanelOrigin"/> does that here, where it's unit-tested.
    /// </summary>
    public const double DefaultBottomClearance = 80;

    /// <summary>
    /// The panel size that holds a <paramref name="pillSize"/> pill with <paramref name="shadowMargin"/>
    /// of transparent room on all four sides, so the pill's drop shadow renders its full falloff
    /// instead of being clipped by the window's content rect.
    /// </summary>
    public static Size PanelSize(Size pillSize, double shadowMargin) =>
        new(pillSize.Width + shadowMargin * 2, pillSize.Height + shadowMargin * 2);

    /// <summary>
    /// Resolves the panel origin from what the shell owns — the screen's visible frame, the
    /// persisted drag origin, and the panel's transparent shadow inset — keeping the clearance
    /// policy and the pill/panel conversion here.
    /// </summary>
    public static Point PanelOrigin(Size panelSize, Rect visibleFrame, Point? customOrigin, double shadowMargin) =>
        Origin(panelSize, visibleFrame, customOrigin, DefaultBottomClearance - shadowMargin);

    /// <summary>
    /// Resolves the panel's origin: the user's dragged <paramref name="customOrigin"/> clamped
    /// fully back onto the current screen, or — when the pill has never been moved — the default
    /// placement, horizontally centered with its bottom edge <paramref name="bottomOffset"/> above
    /// the visible frame's bottom. Clamping matters because a stored origin can be stale (saved on
    /// a monitor that has since been unplugged, or near an edge of a larger screen): it must never
    /// leave the pill stranded off-screen and invisible.
    /// </summary>
    public static Point Origin(Size panelSize, Rect visibleFrame, Point? customOrigin, double bottomOffset)
    {
        if (customOrigin is not { } origin)
        {
            return new Point(visibleFrame.MidX - panelSize.Width / 2, visibleFrame.MinY + bottomOffset);
        }

        return Clamped(origin, panelSize, visibleFrame);
    }

    /// <summary>
    /// Clamps <paramref name="origin"/> so a <paramref name="size"/>-sized panel lies entirely
    /// inside <paramref name="rect"/>. In the degenerate case of a panel larger than
    /// <paramref name="rect"/> the max edge wins: the panel's top/right edge pins to the rect's and
    /// the overflow spills past the min edge — deterministic rather than oscillating between the
    /// two constraints.
    /// </summary>
    public static Point Clamped(Point origin, Size size, Rect rect)
    {
        var maxX = rect.MaxX - size.Width;
        var maxY = rect.MaxY - size.Height;
        return new Point(
            Math.Min(Math.Max(origin.X, rect.MinX), maxX),
            Math.Min(Math.Max(origin.Y, rect.MinY), maxY));
    }
}
