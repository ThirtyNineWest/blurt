using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Blurt.App.Services;
using Blurt.App.Settings;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Pipeline;

namespace Blurt.App.Main;

/// <summary>
/// One row of the ready view's recent list: the transcript, its style, a relative time, and the
/// acknowledgement of a copy. Create and use on the UI thread.
/// </summary>
public sealed class RecentRow : ObservableObject, IDisposable
{
    private const string LogCategory = "RecentDictations";

    private readonly Dispatcher _dispatcher;
    private readonly CopyFeedback _copyFeedback = new();
    private string _relativeLabel;
    private bool _showsCopyFeedback;
    private string _copyFeedbackLabel = "";

    /// <summary>Creates a row for <paramref name="entry"/> labelled as of <paramref name="now"/>.</summary>
    public RecentRow(RecentDictations.Entry entry, DateTimeOffset now, bool showsSeparator)
    {
        Entry = entry;
        ShowsSeparator = showsSeparator;
        _relativeLabel = entry.RelativeLabel(now);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _copyFeedback.Changed += state => _dispatcher.BeginInvoke(() => ShowCopyFeedback(state));
    }

    /// <summary>The dictation this row shows.</summary>
    public RecentDictations.Entry Entry { get; }

    /// <summary>The transcript.</summary>
    public string Text => Entry.Text;

    /// <summary>The custom style's name, or null for the base styling.</summary>
    public string? Style => Entry.Style;

    /// <summary>Whether a separator follows this row: every row but the last.</summary>
    public bool ShowsSeparator { get; }

    /// <summary>"just now", "3 minutes ago", ….</summary>
    public string RelativeLabel
    {
        get => _relativeLabel;
        private set => Set(ref _relativeLabel, value);
    }

    /// <summary>Whether the trailing slot shows the copy acknowledgement instead of the style, time, and Copy button.</summary>
    public bool ShowsCopyFeedback
    {
        get => _showsCopyFeedback;
        private set => Set(ref _showsCopyFeedback, value);
    }

    /// <summary>
    /// "Copied" or "Couldn't copy" for the latest copy. Kept after the acknowledgement clears, so the
    /// trailing slot's width holds while it fades.
    /// </summary>
    public string CopyFeedbackLabel
    {
        get => _copyFeedbackLabel;
        private set => Set(ref _copyFeedbackLabel, value);
    }

    /// <summary>Re-renders <see cref="RelativeLabel"/> as of <paramref name="now"/>.</summary>
    public void Relabel(DateTimeOffset now) => RelativeLabel = Entry.RelativeLabel(now);

    /// <summary>
    /// Writes the transcript to the clipboard and acknowledges it for <see cref="CopyFeedback.CopiedDwell"/>;
    /// a clipboard held by another process is logged and acknowledged as "Couldn't copy". Never throws.
    /// </summary>
    public void Copy()
    {
        bool copied;
        try
        {
            // A dictation's deferred clipboard restore runs only while the clipboard sequence number
            // is still the one its paste wrote, so this write is never overwritten by a pending restore.
            Clipboard.SetText(Text);
            copied = true;
        }
        catch (ExternalException ex)
        {
            BlurtLog.Warn(LogCategory, $"copy failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            copied = false;
        }

        _copyFeedback.Report(copied);
    }

    /// <summary>Stops the pending acknowledgement timer.</summary>
    public void Dispose() => _copyFeedback.Dispose();

    private void ShowCopyFeedback(CopyFeedbackState state)
    {
        if (CopyFeedback.Label(state) is { } label)
        {
            CopyFeedbackLabel = label;
        }

        ShowsCopyFeedback = state != CopyFeedbackState.None;
    }
}

/// <summary>
/// Backs the ready view: the shortcut readout, the listening face while recording, the style
/// switcher's enabled state, and the recent dictations with relative times refreshed every
/// <see cref="RecentDictations.Entry.LabelRefreshInterval"/>. Use on the UI thread.
/// </summary>
public sealed class ReadyViewModel : ObservableObject
{
    /// <summary>The height of one recent row.</summary>
    public const double RowHeight = 28;

    /// <summary>The height of the separator between recent rows.</summary>
    public const double SeparatorThickness = 1;

    private readonly AppCoordinator _coordinator;
    private readonly DispatcherTimer _labelTimer;

    /// <summary>Creates the view model observing <paramref name="coordinator"/> and the shared pickers.</summary>
    public ReadyViewModel(AppCoordinator coordinator, DictationSettingsViewModel dictation, StylesViewModel styles)
    {
        _coordinator = coordinator;
        Dictation = dictation;
        Styles = styles;
        _labelTimer = new DispatcherTimer { Interval = RecentDictations.Entry.LabelRefreshInterval };
        _labelTimer.Tick += (_, _) => RelabelRecent();
        _labelTimer.Start();
        coordinator.PropertyChanged += OnCoordinatorChanged;
        dictation.PropertyChanged += OnDictationChanged;
        styles.PropertyChanged += OnStylesChanged;
        RebuildRecent();
    }

    /// <summary>The shared shortcut pickers the readout names.</summary>
    public DictationSettingsViewModel Dictation { get; }

    /// <summary>The shared style profiles the switcher shows.</summary>
    public StylesViewModel Styles { get; }

    /// <summary>The headline split around the key name.</summary>
    public ReadoutSentence Headline => ReadyReadout.Headline(Dictation.TriggerKey, Dictation.Activation);

    /// <summary>The line under the headline.</summary>
    public string Subtitle => ReadyReadout.Subtitle(Dictation.Activation);

    /// <summary>
    /// Whether audio is being captured. The mic bring-up is excluded: nothing is recorded yet, so
    /// claiming "Listening" then would invite speech that is lost.
    /// </summary>
    public bool IsListening => _coordinator.TrayStatus == TrayStatus.Recording;

    /// <summary>The line under "Listening…".</summary>
    public string ListeningSubtitle => ReadyReadout.ListeningSubtitle(Styles.ActiveName);

    /// <summary>Whether a dictation is being brought up or captured, which shows Cancel Dictation.</summary>
    public bool IsCapturing => _coordinator.IsCapturing;

    /// <summary>Whether the style switcher accepts a change: not mid-capture, when the request's style is already fixed.</summary>
    public bool CanSwitchStyle => !_coordinator.IsCapturing;

    /// <summary>The recent dictations shown, newest first.</summary>
    public ObservableCollection<RecentRow> Recent { get; } = [];

    /// <summary>Whether any dictation is shown; the empty state shows otherwise.</summary>
    public bool HasRecent => Recent.Count > 0;

    /// <summary>The recent list's fixed height, reserved for a full list so the window never jumps.</summary>
    public static double RecentHeight => RecentDictations.ReservedHeight(RowHeight, SeparatorThickness);

    /// <summary>Cancels the in-flight dictation.</summary>
    public void CancelDictation() => _coordinator.Session.Submit(Command.Cancel);

    private void OnCoordinatorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppCoordinator.TrayStatus):
                Raise(nameof(IsListening));
                break;
            case nameof(AppCoordinator.IsCapturing):
                Raise(nameof(IsCapturing), nameof(CanSwitchStyle));
                break;
            case nameof(AppCoordinator.RecentDictations):
                RebuildRecent();
                break;
            default:
                break;
        }
    }

    private void OnDictationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DictationSettingsViewModel.TriggerKey) or nameof(DictationSettingsViewModel.Activation))
        {
            Raise(nameof(Headline), nameof(Subtitle));
        }
    }

    private void OnStylesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StylesViewModel.ActiveName))
        {
            Raise(nameof(ListeningSubtitle));
        }
    }

    private void RebuildRecent()
    {
        var now = DateTimeOffset.Now;
        var entries = _coordinator.RecentDictations.Displayed;
        foreach (var row in Recent)
        {
            row.Dispose();
        }

        Recent.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            Recent.Add(new RecentRow(entries[i], now, showsSeparator: i < entries.Count - 1));
        }

        Raise(nameof(HasRecent));
    }

    private void RelabelRecent()
    {
        var now = DateTimeOffset.Now;
        foreach (var row in Recent)
        {
            row.Relabel(now);
        }
    }
}
