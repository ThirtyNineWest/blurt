using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Blurt.App.Services;

/// <summary>Collapses an element bound to null or an empty string; shows it otherwise.</summary>
public sealed class NullToCollapsed : IValueConverter
{
    /// <summary>The shared instance XAML references.</summary>
    public static NullToCollapsed Instance { get; } = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string text && text.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Negates a bound boolean.</summary>
public sealed class Inverted : IValueConverter
{
    /// <summary>The shared instance XAML references.</summary>
    public static Inverted Instance { get; } = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Collapses an element bound to true; shows it for false.</summary>
public sealed class TrueToCollapsed : IValueConverter
{
    /// <summary>The shared instance XAML references.</summary>
    public static TrueToCollapsed Instance { get; } = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element bound to the integer zero; collapses it otherwise.</summary>
public sealed class ZeroToVisible : IValueConverter
{
    /// <summary>The shared instance XAML references.</summary>
    public static ZeroToVisible Instance { get; } = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
