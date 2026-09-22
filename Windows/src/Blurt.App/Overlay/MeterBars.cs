using System.Windows;
using System.Windows.Media;
using Blurt.Engine.Pipeline;
using EngineSize = Blurt.Engine.Pipeline.Size;

namespace Blurt.App.Overlay;

/// <summary>
/// The pill's live level meter: a centered row of rounded bars growing from the vertical middle,
/// shaped by <see cref="MeterBarRow"/>. The row is resolved once per size; the owner redraws the
/// bars with <see cref="Redraw"/> on its animation tick.
/// </summary>
public sealed class MeterBars : FrameworkElement
{
    /// <summary>The 0…1 mic level the bars track.</summary>
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(float), typeof(MeterBars), new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The bar color.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MeterBars), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private MeterBarRow? _row;
    private System.Windows.Size _rowSize;
    private double _time;
    private bool _animated;

    /// <summary>The 0…1 mic level the bars track.</summary>
    public float Level
    {
        get => (float)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>The bar color.</summary>
    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Redraws at <paramref name="time"/> seconds; <paramref name="animated"/> false drops the idle wave.</summary>
    public void Redraw(double time, bool animated)
    {
        _time = time;
        _animated = animated;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        if (_row is null || size != _rowSize)
        {
            _row = new MeterBarRow(new EngineSize(size.Width, size.Height));
            _rowSize = size;
        }

        var rowWidth = _row.Count * MeterBarGeometry.BarWidth + (_row.Count - 1) * MeterBarGeometry.BarSpacing;
        var x = (size.Width - rowWidth) / 2;
        var radius = MeterBarGeometry.BarWidth / 2;
        for (var index = 0; index < _row.Count; index++)
        {
            var height = _row.Height(index, Level, _time, _animated);
            var bar = new System.Windows.Rect(x, (size.Height - height) / 2, MeterBarGeometry.BarWidth, height);
            drawingContext.DrawRoundedRectangle(Fill, null, bar, radius, radius);
            x += MeterBarGeometry.BarWidth + MeterBarGeometry.BarSpacing;
        }
    }
}
