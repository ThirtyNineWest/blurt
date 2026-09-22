using System.IO;
using System.Text.Json;
using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;

namespace Blurt.Engine.Windows.Config;

/// <summary>
/// A JSON-file-backed <see cref="ISettingsStore"/>. Loads <paramref name="path"/> once at
/// construction and writes atomically — to <c>path + ".tmp"</c>, then an overwriting
/// <see cref="File.Move(string, string, bool)"/> — on every <see cref="Set(string, string)"/> or
/// <see cref="Remove"/>.
/// </summary>
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private const string LogCategory = "JsonSettingsStore";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, JsonElement> _values = Load(path);

    /// <inheritdoc/>
    public string? GetString(string key) =>
        TryGet(key, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    /// <inheritdoc/>
    public int? GetInt(string key)
    {
        if (!TryGet(key, out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (element.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return element.TryGetDouble(out var doubleValue) ? (int)doubleValue : null;
    }

    /// <inheritdoc/>
    public bool? GetBool(string key)
    {
        if (!TryGet(key, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <inheritdoc/>
    public double? GetDouble(string key)
    {
        if (!TryGet(key, out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return element.TryGetDouble(out var value) ? value : null;
    }

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
            Persist();
        }
    }

    private bool TryGet(string key, out JsonElement element)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out element);
        }
    }

    private void Put<T>(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToElement(value);
            Persist();
        }
    }

    private static Dictionary<string, JsonElement> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            BlurtLog.Warn(LogCategory, ex.Message);
            return [];
        }
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_values));
        File.Move(tempPath, path, overwrite: true);
    }
}
