namespace Blurt.Engine.Config;

/// <summary>A thread-safe in-memory <see cref="ISettingsStore"/> for tests and harnesses.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, object> _values = [];

    /// <inheritdoc/>
    public string? GetString(string key) => Get(key) as string;

    /// <inheritdoc/>
    public int? GetInt(string key) => Get(key) is int value ? value : null;

    /// <inheritdoc/>
    public bool? GetBool(string key) => Get(key) is bool value ? value : null;

    /// <inheritdoc/>
    public double? GetDouble(string key) => Get(key) is double value ? value : null;

    /// <inheritdoc/>
    public void Set(string key, string value) => Put(key, value);

    /// <inheritdoc/>
    public void Set(string key, int value) => Put(key, value);

    /// <inheritdoc/>
    public void Set(string key, bool value) => Put(key, value);

    /// <inheritdoc/>
    public void Set(string key, double value) => Put(key, value);

    /// <inheritdoc/>
    public void Remove(string key)
    {
        lock (_gate)
        {
            _values.Remove(key);
        }
    }

    private object? Get(string key)
    {
        lock (_gate)
        {
            return _values.GetValueOrDefault(key);
        }
    }

    private void Put(string key, object value)
    {
        lock (_gate)
        {
            _values[key] = value;
        }
    }
}
