using System.ComponentModel;
using System.Globalization;
using Blurt.App.Services;
using Blurt.App.Update;
using Blurt.Engine.Config;
using Blurt.Engine.Pipeline;
using Blurt.Engine.Update;

namespace Blurt.App.Settings;

/// <summary>
/// Backs the Settings window: the shared pickers and styles, the update check with its "last
/// checked" caption, and the developer-mode switch with the two log paths. Use on the UI thread.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly DeveloperModeStore _developerMode;
    private readonly LastUpdateCheckStore _lastUpdateCheck;

    /// <summary>Creates the view model over the shared view models and stores.</summary>
    public SettingsViewModel(
        SettingsStores settings,
        DictationSettingsViewModel dictation,
        StylesViewModel styles,
        UpdateCheckModel updates,
        DictationLog log)
    {
        _developerMode = settings.DeveloperMode;
        _lastUpdateCheck = settings.LastUpdateCheck;
        Dictation = dictation;
        Styles = styles;
        Updates = updates;
        DictationsPath = log.DictationsPath;
        ErrorsPath = log.ErrorsPath;
        updates.PropertyChanged += OnUpdatesChanged;
    }

    /// <summary>The trigger key, activation, and cue voice pickers.</summary>
    public DictationSettingsViewModel Dictation { get; }

    /// <summary>The custom styles and the enhanced-transcripts switch.</summary>
    public StylesViewModel Styles { get; }

    /// <summary>The update check.</summary>
    public UpdateCheckModel Updates { get; }

    /// <summary>When an update check last completed, in local time.</summary>
    public string LastCheckedCaption =>
        LastUpdateCheckCaption.Text(_lastUpdateCheck.LastCheck?.ToLocalTime(), CultureInfo.CurrentCulture);

    /// <summary>Whether developer mode is on, which writes the dictation logs.</summary>
    public bool DeveloperMode
    {
        get => _developerMode.IsEnabled;
        set
        {
            if (value == _developerMode.IsEnabled)
            {
                return;
            }

            _developerMode.IsEnabled = value;
            Raise(nameof(DeveloperMode));
        }
    }

    /// <summary>Where completed dictations are logged.</summary>
    public string DictationsPath { get; }

    /// <summary>Where failed dictations are logged.</summary>
    public string ErrorsPath { get; }

    /// <summary>Re-reads every store-backed value, for a window being shown again.</summary>
    public void Reload()
    {
        Styles.Reload();
        Raise(nameof(LastCheckedCaption), nameof(DeveloperMode));
    }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateCheckModel.IsChecking))
        {
            Raise(nameof(LastCheckedCaption));
        }
    }
}
