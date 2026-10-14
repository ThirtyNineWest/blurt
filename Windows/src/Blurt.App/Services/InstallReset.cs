using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Pipeline;

namespace Blurt.App.Services;

/// <summary>
/// Hands the install back to the state a fresh download starts from: every persisted setting, the
/// stored API key, and the developer-mode logs. Every step runs even when an earlier one fails, so
/// a partial failure leaves as little behind as possible.
/// </summary>
public sealed class InstallReset(ISettingsStore settings, ApiKeyModel apiKey, DictationLog log)
{
    private const string LogCategory = "InstallReset";

    /// <summary>
    /// Clears the settings, deletes the key through <see cref="ApiKeyModel.Storage"/>, and removes the
    /// logs. Returns null when every step landed, otherwise the alert naming what survived.
    /// </summary>
    public InstallResetAlert? Run()
    {
        PersistedSettings.ResetAll(settings);
        var apiKeyCleared = apiKey.Storage.Save(string.Empty) && !apiKey.Storage.HasKey;
        apiKey.RefreshStatus();
        var logsCleared = log.RemoveStoredLogs();
        BlurtLog.Info(LogCategory, $"reset ran (key cleared: {apiKeyCleared}, logs cleared: {logsCleared})");
        return InstallResetReport.FailureAlert(apiKeyCleared, logsCleared);
    }
}
