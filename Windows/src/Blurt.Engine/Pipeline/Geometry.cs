namespace Blurt.Engine.Pipeline;

/// <summary>A width and height, in device-independent pixels.</summary>
public readonly record struct Size(double Width, double Height)
{
    /// <summary>Zero width and height.</summary>
    public static readonly Size Zero = new(0, 0);
}

/// <summary>An x/y position, in device-independent pixels.</summary>
public readonly record struct Point(double X, double Y);

/// <summary>An axis-aligned rectangle, in device-independent pixels.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    /// <summary>The left edge.</summary>
    public double MinX => X;

    /// <summary>The top edge.</summary>
    public double MinY => Y;

    /// <summary>The right edge.</summary>
    public double MaxX => X + Width;

    /// <summary>The bottom edge.</summary>
    public double MaxY => Y + Height;

    /// <summary>The horizontal center.</summary>
    public double MidX => X + Width / 2;

    /// <summary>The vertical center.</summary>
    public double MidY => Y + Height / 2;
}
