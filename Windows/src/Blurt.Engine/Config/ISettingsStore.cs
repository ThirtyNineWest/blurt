namespace Blurt.Engine.Config;

/// <summary>Typed key-value persistence for the engine's settings.</summary>
public interface ISettingsStore
{
    /// <summary>Returns the string stored at <paramref name="key"/>, or null when unset or of another type.</summary>
    string? GetString(string key);

    /// <summary>Returns the integer stored at <paramref name="key"/>, or null when unset or of another type.</summary>
    int? GetInt(string key);

    /// <summary>Returns the Boolean stored at <paramref name="key"/>, or null when unset or of another type.</summary>
    bool? GetBool(string key);

    /// <summary>Returns the double stored at <paramref name="key"/>, or null when unset or of another type.</summary>
    double? GetDouble(string key);

    /// <summary>Stores <paramref name="value"/> at <paramref name="key"/>.</summary>
    void Set(string key, string value);

    /// <summary>Stores <paramref name="value"/> at <paramref name="key"/>.</summary>
    void Set(string key, int value);

    /// <summary>Stores <paramref name="value"/> at <paramref name="key"/>.</summary>
    void Set(string key, bool value);

    /// <summary>Stores <paramref name="value"/> at <paramref name="key"/>.</summary>
    void Set(string key, double value);

    /// <summary>Removes <paramref name="key"/>; a missing key is not an error.</summary>
    void Remove(string key);
}
