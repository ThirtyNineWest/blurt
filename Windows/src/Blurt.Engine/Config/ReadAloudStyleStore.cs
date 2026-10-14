using System.Globalization;

namespace Blurt.Engine.Config;

/// <summary>
/// How one read-aloud press should sound: the playback rate, and whether the text is rewritten for
/// listening before it is spoken.
/// </summary>
/// <param name="Rate">Playback speed, where 1 is the voice's natural pace.</param>
/// <param name="SkipsJargon">Whether code, file paths, links, email addresses and long numbers are left out.</param>
public sealed record ReadAloudStyle(double Rate, bool SkipsJargon)
{
    /// <summary>Read-aloud untouched: natural pace, the selection verbatim.</summary>
    public static readonly ReadAloudStyle Standard = new(1, false);
}

/// <summary>
/// Persists how read-aloud sounds: its playback speed and whether it skips jargon, two independent
/// settings. Untouched, it reads at natural pace with nothing skipped (<see cref="ReadAloudStyle.Standard"/>).
/// </summary>
public sealed class ReadAloudStyleStore(ISettingsStore store)
{
    /// <summary>What an unset speed reads as.</summary>
    public const double DefaultSpeed = 1;

    /// <summary>What an unset skip-jargon switch reads as.</summary>
    public const bool DefaultSkipsJargon = false;

    /// <summary>The speeds the settings picker offers, in ascending order.</summary>
    public static readonly IReadOnlyList<double> SpeedChoices = [1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];

    /// <summary>The settings key holding the speed.</summary>
    public static string SpeedSettingsKey => DefaultsKey.ReadAloudSpeed.Key();

    /// <summary>The settings key holding the skip-jargon switch.</summary>
    public static string SkipsJargonSettingsKey => DefaultsKey.ReadAloudSkipsJargon.Key();

    /// <summary>
    /// The stored speed snapped to <see cref="SpeedChoices"/>. A missing or non-numeric slot reads as
    /// <see cref="DefaultSpeed"/>. The setter stores the value as given.
    /// </summary>
    public double Speed
    {
        get => store.GetDouble(SpeedSettingsKey) is { } stored ? NearestChoice(stored) : DefaultSpeed;
        set => store.Set(SpeedSettingsKey, value);
    }

    /// <summary>Whether the selection is rewritten for listening before it is read.</summary>
    public bool SkipsJargon
    {
        get => store.GetBool(SkipsJargonSettingsKey) ?? DefaultSkipsJargon;
        set => store.Set(SkipsJargonSettingsKey, value);
    }

    /// <summary>The style the next press reads with.</summary>
    public ReadAloudStyle Style => new(Speed, SkipsJargon);

    /// <summary>Returns the entry of <see cref="SpeedChoices"/> closest to <paramref name="speed"/>, or <see cref="DefaultSpeed"/> when it is not finite.</summary>
    public static double NearestChoice(double speed) =>
        double.IsFinite(speed) ? SpeedChoices.MinBy(choice => Math.Abs(choice - speed)) : DefaultSpeed;

    /// <summary>Returns <paramref name="speed"/> with trailing zeros dropped and the decimal mark of <paramref name="culture"/>, followed by the multiplication sign.</summary>
    public static string SpeedLabel(double speed, CultureInfo culture) =>
        speed.ToString("0.##", culture) + "×";
}
