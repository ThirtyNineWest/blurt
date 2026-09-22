using System.Globalization;
using Blurt.Engine.Config;

namespace Blurt.Engine.Update;

/// <summary>
/// Persists when an update check last completed, so the automatic launch check can be throttled
/// instead of fetching GitHub on every launch.
/// </summary>
public sealed class LastUpdateCheckStore(ISettingsStore store)
{
    /// <summary>The settings key holding the stamp, an ISO 8601 string.</summary>
    public static string SettingsKey => DefaultsKey.LastUpdateCheck.Key();

    /// <summary>When a check last produced a result, or null if none ever has.</summary>
    public DateTimeOffset? LastCheck
    {
        get
        {
            var raw = store.GetString(SettingsKey);
            return raw is null ? null : DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        set
        {
            if (value is null)
            {
                store.Remove(SettingsKey);
                return;
            }

            store.Set(SettingsKey, value.Value.ToString("o", CultureInfo.InvariantCulture));
        }
    }
}
