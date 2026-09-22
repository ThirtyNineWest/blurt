using Blurt.Engine.Config;

namespace Blurt.Engine.Pipeline;

/// <summary>Persists the overlay pill's user-dragged origin.</summary>
public sealed class OverlayOriginStore(ISettingsStore store)
{
    /// <summary>The settings key holding the x component.</summary>
    public static string XSettingsKey => DefaultsKey.OverlayCustomOriginX.Key();

    /// <summary>The settings key holding the y component.</summary>
    public static string YSettingsKey => DefaultsKey.OverlayCustomOriginY.Key();

    /// <summary>
    /// The dragged origin, or null when the pill has never been moved. Both components must be
    /// present: a half-written pair falls back to the default placement rather than an implied zero.
    /// </summary>
    public (double X, double Y)? Origin
    {
        get
        {
            var x = store.GetDouble(XSettingsKey);
            var y = store.GetDouble(YSettingsKey);
            return x is null || y is null ? null : (x.Value, y.Value);
        }

        set
        {
            if (value is null)
            {
                store.Remove(XSettingsKey);
                store.Remove(YSettingsKey);
                return;
            }

            store.Set(XSettingsKey, value.Value.X);
            store.Set(YSettingsKey, value.Value.Y);
        }
    }
}
