using System.Windows;
using System.Windows.Interop;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Windows.FocusCapture;

namespace Blurt.App.Services;

/// <summary>
/// Registers every WPF window this process shows with <see cref="UiaFocusCapture.IgnoreWindow"/>,
/// so a dictation started while one of Blurt's own windows is in front never pastes into it. A class
/// handler on <see cref="FrameworkElement.LoadedEvent"/> covers windows created later; windows
/// already open when <see cref="Install"/> runs are registered at once. Each window is forgotten when
/// it closes. Use on the UI thread.
/// </summary>
public static class OwnWindowFocusExclusion
{
    private const string LogCategory = "FocusExclusion";

    private static readonly HashSet<nint> Registered = [];
    private static UiaFocusCapture? s_focus;

    /// <summary>
    /// Starts excluding this process's windows from <paramref name="focus"/>. Call once, before the
    /// first window is created; a second call is ignored.
    /// </summary>
    public static void Install(UiaFocusCapture focus)
    {
        if (s_focus is not null)
        {
            return;
        }

        s_focus = focus;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        foreach (Window window in Application.Current.Windows)
        {
            Register(window);
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            Register(window);
        }
    }

    private static void Register(Window window)
    {
        if (s_focus is not { } focus)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (!Registered.Add(handle))
        {
            return;
        }

        focus.IgnoreWindow(handle);
        BlurtLog.Info(LogCategory, $"ignoring {window.GetType().Name} (0x{handle:X}) for focus capture");
        window.Closed += (_, _) =>
        {
            focus.ForgetWindow(handle);
            Registered.Remove(handle);
        };
    }
}
