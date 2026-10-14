using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Blurt.Engine.Pipeline;
using EnginePoint = Blurt.Engine.Pipeline.Point;
using EngineRect = Blurt.Engine.Pipeline.Rect;
using EngineSize = Blurt.Engine.Pipeline.Size;

namespace Blurt.App.Overlay;

/// <summary>
/// The dictation pill: a transparent, topmost, never-activated tool window. Hidden at rest, faded
/// in for a dictation, and faded out on idle; transient notices revert to idle after their dwell.
/// The user can drag it anywhere; the dragged position persists. Call every member on the UI thread.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    /// <summary>The visible pill's size.</summary>
    public static readonly EngineSize PillSize = new(138, 28);

    /// <summary>Transparent room on every side of the pill so its drop shadow is not clipped.</summary>
    public const double ShadowMargin = 28;

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const double OrbRingPeriodSeconds = 1.6;

    private static readonly EngineSize PanelSize = OverlayPlacement.PanelSize(PillSize, ShadowMargin);
    private static readonly TimeSpan AppearFade = TimeSpan.FromSeconds(0.08);
    private static readonly TimeSpan DismissFade = TimeSpan.FromSeconds(0.2);
    private static readonly TimeSpan AnimationInterval = TimeSpan.FromMilliseconds(50);

    private readonly OverlayOriginStore _originStore;
    private readonly OverlayViewModel _viewModel = new();
    private readonly DispatcherTimer _noticeTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Brush _statusBrush;
    private readonly Brush _errorBrush;
    private int _fadeGeneration;
    private bool _fadingOut;

    /// <summary>Creates the hidden pill, persisting drags through <paramref name="originStore"/>.</summary>
    public OverlayWindow(OverlayOriginStore originStore)
    {
        _originStore = originStore;
        InitializeComponent();
        Width = PanelSize.Width;
        Height = PanelSize.Height;
        DataContext = _viewModel;
        _statusBrush = (Brush)FindResource("GreenOnDark");
        _errorBrush = (Brush)FindResource("ErrorOrange");
        _noticeTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            Show(OverlayUIState.Idle);
        };
        _animationTimer = new DispatcherTimer(AnimationInterval, DispatcherPriority.Render, (_, _) => Animate(), Dispatcher);
        _animationTimer.Stop();
    }

    /// <summary>Creates the native window without showing it and returns its handle.</summary>
    public nint EnsureHandle() => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>
    /// Presents <paramref name="state"/>: idle fades the pill out; any other state shows it, and a
    /// transient notice schedules the revert to idle after its dwell. A new state supersedes a
    /// pending revert.
    /// </summary>
    public void Show(OverlayUIState state)
    {
        _noticeTimer.Stop();
        if (state == OverlayUIState.Idle)
        {
            SetVisible(false);
            return;
        }

        _viewModel.State = state;
        ApplyLayout();
        if (state.NoticeDwellSeconds is { } dwell)
        {
            Announce(state.AccessibilityLabel);
            _noticeTimer.Interval = TimeSpan.FromSeconds(dwell);
            _noticeTimer.Start();
        }

        SetVisible(true);
    }

    /// <summary>Hides the pill at once, without a fade, and cancels a pending notice revert.</summary>
    public void HidePill()
    {
        _noticeTimer.Stop();
        if (!IsVisible)
        {
            SettleContent();
            return;
        }

        Dismiss();
    }

    /// <summary>Feeds the meter the current 0…1 mic level.</summary>
    public void PushLevel(float level) => _viewModel.Level = level;

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle, (nint)((long)style | WsExToolWindow | WsExNoActivate));
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _noticeTimer.Stop();
        _animationTimer.Stop();
        base.OnClosed(e);
    }

    private static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    private void ApplyLayout()
    {
        var withOrb = _viewModel.ShowsOrb;
        Grid.SetColumn(StatusWord, withOrb ? 1 : 0);
        Grid.SetColumnSpan(StatusWord, withOrb ? 1 : 2);
        StatusWord.HorizontalAlignment = withOrb ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        StatusWord.Foreground = _viewModel.IsError ? _errorBrush : _statusBrush;
    }

    private void SetVisible(bool visible)
    {
        if (visible)
        {
            if (IsVisible && !_fadingOut)
            {
                return;
            }

            _fadingOut = false;
            var generation = ++_fadeGeneration;
            if (!IsVisible)
            {
                Reposition();
                BeginAnimation(OpacityProperty, null);
                Opacity = ReduceMotion ? 1 : 0;
                base.Show();
                _animationTimer.Start();
                Animate();
            }

            if (ReduceMotion)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                return;
            }

            var appear = new DoubleAnimation(1, AppearFade);
            appear.Completed += (_, _) =>
            {
                if (generation == _fadeGeneration)
                {
                    BeginAnimation(OpacityProperty, null);
                    Opacity = 1;
                }
            };
            BeginAnimation(OpacityProperty, appear);
            return;
        }

        if (!IsVisible || _fadingOut)
        {
            return;
        }

        if (ReduceMotion)
        {
            Dismiss();
            return;
        }

        _fadingOut = true;
        var dismissGeneration = ++_fadeGeneration;
        var fade = new DoubleAnimation(0, DismissFade);
        fade.Completed += (_, _) =>
        {
            if (dismissGeneration == _fadeGeneration)
            {
                Dismiss();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Dismiss()
    {
        ++_fadeGeneration;
        _fadingOut = false;
        _animationTimer.Stop();
        Hide();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        SettleContent();
    }

    private void SettleContent()
    {
        _viewModel.State = OverlayUIState.Idle;
        _viewModel.Level = 0;
        ApplyLayout();
    }

    private void Animate()
    {
        var time = _clock.Elapsed.TotalSeconds;
        var animated = !ReduceMotion;
        OrbRingRotation.Angle = animated ? MeterBarGeometry.RotationDegrees(time, OrbRingPeriodSeconds) : 0;
        if (_viewModel.ShowsMeter)
        {
            Meter.Redraw(time, animated);
        }
    }

    private void Reposition()
    {
        var workArea = SystemParameters.WorkArea;
        var custom = _originStore.Origin is { } origin ? new EnginePoint(origin.X, origin.Y) : (EnginePoint?)null;
        var topLeft = OverlayPlacement.PanelTopLeft(
            PanelSize,
            new EngineRect(workArea.X, workArea.Y, workArea.Width, workArea.Height),
            custom,
            ShadowMargin);
        Left = topLeft.X;
        Top = topLeft.Y;
    }

    private void OnPillMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        var before = (Left, Top);
        DragMove();
        if ((Left, Top) != before)
        {
            _originStore.Origin = (Left, Top);
        }
    }

    private void Announce(string message)
    {
        var peer = UIElementAutomationPeer.FromElement(Pill) ?? UIElementAutomationPeer.CreatePeerForElement(Pill);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            message,
            "BlurtOverlayNotice");
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint window, int index, nint value);
}
