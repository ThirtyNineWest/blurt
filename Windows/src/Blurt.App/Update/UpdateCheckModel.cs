using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Update;

namespace Blurt.App.Update;

/// <summary>
/// Runs update checks and presents their result: on demand from the menu, and once after launch
/// when <see cref="AutomaticUpdateCheck.ShouldRun"/> allows. Download-only: the default button opens
/// the release's download URL in the browser. Call every member on the UI thread.
/// </summary>
public sealed class UpdateCheckModel : INotifyPropertyChanged
{
    private const string LogCategory = "update";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    private readonly UpdateChecker _checker;
    private readonly SemanticVersion? _currentVersion;
    private readonly LastUpdateCheckStore _lastCheck;
    private readonly Func<Window?> _owner;
    private readonly TimeProvider _clock;
    private bool _isChecking;

    /// <summary>
    /// Creates the model. <paramref name="currentVersion"/> is the running build's version, or null
    /// when it could not be read; <paramref name="owner"/> supplies the window alerts center on.
    /// </summary>
    public UpdateCheckModel(
        UpdateChecker checker,
        SemanticVersion? currentVersion,
        LastUpdateCheckStore lastCheck,
        Func<Window?> owner,
        TimeProvider? clock = null)
    {
        _checker = checker;
        _currentVersion = currentVersion;
        _lastCheck = lastCheck;
        _owner = owner;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether a check is in flight; a second request while one runs is ignored.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (_isChecking == value)
            {
                return;
            }

            _isChecking = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecking)));
        }
    }

    /// <summary>"Blurt 0.1.62", or the bare product name when the version is unknown.</summary>
    public string VersionLabel => UpdateAlertContent.AppVersionLabel(_currentVersion);

    /// <summary>The executing assembly's <c>Major.Minor.Build</c> version, or null when it does not parse.</summary>
    public static SemanticVersion? AssemblyVersion()
    {
        var version = typeof(UpdateCheckModel).Assembly.GetName().Version;
        return version is not null && SemanticVersion.TryParse($"{version.Major}.{version.Minor}.{version.Build}", out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Checks now and always presents the result: up to date, available, or the "couldn't check" alert.</summary>
    public async Task CheckNowAsync()
    {
        if (IsChecking)
        {
            return;
        }

        if (_currentVersion is not { } current)
        {
            BlurtLog.Error(LogCategory, "no parseable assembly version; cannot check for updates");
            Present(UpdateAlertContent.CheckFailed);
            return;
        }

        IsChecking = true;
        try
        {
            var result = await RunCheckAsync(current);
            Present(result is null ? UpdateAlertContent.CheckFailed : UpdateAlertContent.FromResult(result, current));
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Waits <see cref="AutomaticUpdateCheck.LaunchDelay"/>, then checks when the policy still allows
    /// it, presenting only an available update. <paramref name="isConfigured"/> is read before and
    /// after the delay.
    /// </summary>
    public async Task CheckAtLaunchAsync(Func<bool> isConfigured)
    {
        if (!IsLaunchCheckDue(isConfigured()))
        {
            return;
        }

        if (_currentVersion is not { } current)
        {
            BlurtLog.Error(LogCategory, "no parseable assembly version; skipping the launch update check");
            return;
        }

        await Task.Delay(AutomaticUpdateCheck.LaunchDelay, _clock);
        if (IsChecking || !IsLaunchCheckDue(isConfigured()))
        {
            return;
        }

        IsChecking = true;
        try
        {
            var result = await RunCheckAsync(current);
            if (result is not null && UpdateAlertContent.ForUnpromptedCheck(result, current) is { } content)
            {
                Present(content);
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool IsLaunchCheckDue(bool isConfigured) =>
        AutomaticUpdateCheck.ShouldRun(isConfigured, _lastCheck.LastCheck, _clock.GetUtcNow());

    private async Task<UpdateCheckResult?> RunCheckAsync(SemanticVersion current)
    {
        using var timeout = new CancellationTokenSource(CheckTimeout);
        try
        {
            var result = await _checker.CheckAsync(current, timeout.Token);
            _lastCheck.LastCheck = _clock.GetUtcNow();
            return result;
        }
        catch (Exception failure)
        {
            BlurtLog.Error(LogCategory, $"update check failed: {failure.GetType().Name}: {failure.Message}");
            return null;
        }
    }

    private void Present(UpdateAlertContent content)
    {
        var chosen = UpdateAlertWindow.Present(content, _owner());
        if (chosen != 0 || content.DownloadUrl is not { } url)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            BlurtLog.Error(LogCategory, $"could not open the download URL: {failure.Message}");
        }
    }
}
