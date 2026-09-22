namespace Blurt.Engine.Audio;

/// <summary>
/// A host's set of selectable cue voices: the picker's sections, the lookup behind a persisted id,
/// and which voice an unset setting means.
/// </summary>
public sealed class SoundPackCatalog
{
    private readonly SoundPack _defaultPack;
    private readonly IReadOnlyDictionary<string, SoundPack> _byId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SoundPack>> _voicesByGroup;

    /// <summary>
    /// Builds a catalog from <paramref name="voices"/>, resolving <paramref name="defaultVoiceId"/> to
    /// the voice an unset setting means, falling back to <see cref="SoundPack.None"/> when nothing in
    /// <paramref name="voices"/> carries that id. A voice claiming the reserved id <c>"none"</c> is
    /// dropped before the groups and lookups are built, so it can never take the silent pack's slot.
    /// </summary>
    public SoundPackCatalog(IReadOnlyList<SoundPack> voices, string defaultVoiceId)
    {
        var real = voices.Where(voice => voice.Id != SoundPack.None.Id).ToList();

        var groups = new List<string>();
        var seenGroups = new HashSet<string>();
        var byId = new Dictionary<string, SoundPack> { [SoundPack.None.Id] = SoundPack.None };
        var byGroup = new Dictionary<string, List<SoundPack>>();

        foreach (var voice in real)
        {
            byId[voice.Id] = voice;
            if (voice.IsSilent)
            {
                continue;
            }

            if (seenGroups.Add(voice.Group))
            {
                groups.Add(voice.Group);
            }

            if (!byGroup.TryGetValue(voice.Group, out var list))
            {
                list = [];
                byGroup[voice.Group] = list;
            }

            list.Add(voice);
        }

        Groups = groups;
        _byId = byId;
        _voicesByGroup = byGroup.ToDictionary(entry => entry.Key, IReadOnlyList<SoundPack> (entry) => entry.Value);
        _defaultPack = byId.GetValueOrDefault(defaultVoiceId, SoundPack.None);
    }

    /// <summary>Distinct group names, in catalog order — the picker's sections.</summary>
    public IReadOnlyList<string> Groups { get; }

    /// <summary>The voices belonging to one group, in catalog order, or empty for a group nobody declared.</summary>
    public IReadOnlyList<SoundPack> Voices(string group) => _voicesByGroup.GetValueOrDefault(group, []);

    /// <summary>
    /// Decodes a persisted pack id, falling back to the catalog's default voice when the id is unset
    /// or names no voice in this catalog. <c>"none"</c> always resolves to <see cref="SoundPack.None"/>.
    /// </summary>
    public SoundPack FromPersisted(string? id) => id is not null && _byId.TryGetValue(id, out var pack) ? pack : _defaultPack;
}
