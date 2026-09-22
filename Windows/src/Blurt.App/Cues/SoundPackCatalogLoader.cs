using System.IO;
using System.Text.Json;
using Blurt.Engine.Audio;
using Blurt.Engine.Diagnostics;

namespace Blurt.App.Cues;

/// <summary>Loads the cue voice catalog shipped as <c>Resources\soundpacks.json</c> beside the executable.</summary>
public static class SoundPackCatalogLoader
{
    private const string LogCategory = "SoundPackCatalogLoader";

    /// <summary>The catalog file's path in the output directory.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Resources", "soundpacks.json");

    /// <summary>
    /// Reads and parses the catalog at <paramref name="path"/>. A missing or malformed file logs and
    /// yields an empty catalog whose only pack is <see cref="SoundPack.None"/>, so the app runs silent.
    /// </summary>
    public static SoundPackCatalog Load(string path)
    {
        try
        {
            return SoundPackCatalogJson.Parse(File.ReadAllText(path));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            BlurtLog.Error(LogCategory, $"sound pack catalog unreadable at {path}: {failure.Message}");
            return new SoundPackCatalog([], SoundPack.None.Id);
        }
    }
}
