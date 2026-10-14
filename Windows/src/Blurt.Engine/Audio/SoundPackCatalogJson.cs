using System.Text.Json;

namespace Blurt.Engine.Audio;

/// <summary>
/// Reads a <see cref="SoundPackCatalog"/> from the host's catalog file:
/// <c>{"defaultVoiceId": "...", "voices": [{"id": "...", "label": "...", "group": "..."}, ...]}</c>.
/// </summary>
public static class SoundPackCatalogJson
{
    /// <summary>Parses <paramref name="json"/> into a catalog, keeping the voices in file order.</summary>
    /// <exception cref="JsonException">The text is not JSON, or a required property is missing or not a string.</exception>
    public static SoundPackCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The sound pack catalog must be a JSON object.");
        }

        var defaultVoiceId = RequiredString(root, "defaultVoiceId");
        if (!root.TryGetProperty("voices", out var voicesElement) || voicesElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The sound pack catalog has no \"voices\" array.");
        }

        var voices = new List<SoundPack>();
        foreach (var entry in voicesElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Every sound pack entry must be a JSON object.");
            }

            voices.Add(new SoundPack(RequiredString(entry, "id"), RequiredString(entry, "label"), RequiredString(entry, "group")));
        }

        return new SoundPackCatalog(voices, defaultVoiceId);
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new JsonException($"The sound pack catalog is missing the string property \"{name}\".");
}
