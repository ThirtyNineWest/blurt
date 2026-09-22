using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blurt.Engine.Config;

/// <summary>
/// One named set of style instructions applied on top of the base cleanup instruction. The id is
/// assigned once at creation and never re-derived from the name, so renaming a profile cannot
/// silently deactivate it.
/// </summary>
public sealed record StyleProfile
{
    /// <summary>A brand-new profile, with a freshly minted id.</summary>
    public StyleProfile(string name, string instructions)
        : this(Guid.NewGuid(), name, instructions)
    {
    }

    /// <summary>Full constructor, used by the JSON decoder and by the migration's fixed-id profile.</summary>
    [JsonConstructor]
    public StyleProfile(Guid id, string name, string instructions)
    {
        Id = id;
        Name = name;
        Instructions = instructions;
    }

    /// <summary>The profile's identity. Fixed at creation.</summary>
    public Guid Id { get; }

    /// <summary>The profile's display name.</summary>
    public string Name { get; init; }

    /// <summary>The style instructions this profile appends to the base cleanup instruction.</summary>
    public string Instructions { get; init; }
}

/// <summary>
/// Storage for the user's style profiles and which one is active — possibly none of them, which
/// selects the base styling with no profile instructions appended.
///
/// Only the active profile is ever sent: the dictation API's instruction cap rejects the whole
/// request when exceeded, so concatenating every profile would fail every dictation rather than
/// degrading.
///
/// The pre-profiles single field (<see cref="LegacySettingsKey"/>) is read, never written: an
/// install that predates profiles has text there and no list at all, and that reads back as one
/// active profile named <see cref="LegacyProfileName"/> holding it.
/// </summary>
public sealed class StyleProfileStore(ISettingsStore store, int characterLimit)
{
    /// <summary>
    /// Rejects a stored profile that is missing a field (<c>RespectRequiredConstructorParameters</c>:
    /// none of <see cref="StyleProfile"/>'s constructor parameters carry a C# default) or carries an
    /// explicit JSON <c>null</c> for one (<c>RespectNullableAnnotations</c>), rather than silently
    /// binding <see langword="null"/> or <see cref="Guid.Empty"/> and failing later downstream.
    /// </summary>
    private static readonly JsonSerializerOptions DecodeOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    /// <summary>The settings key holding the JSON-encoded profile list.</summary>
    public static string SettingsKey => DefaultsKey.StyleProfiles.Key();

    /// <summary>The settings key holding the active profile's id, or the <see cref="DefaultStyleId"/> sentinel.</summary>
    public static string ActiveSettingsKey => DefaultsKey.ActiveStyleProfile.Key();

    /// <summary>The pre-profiles single style field, still read for migration and never written.</summary>
    public static string LegacySettingsKey => DefaultsKey.CustomStyle.Key();

    /// <summary>The base styling's user-facing name.</summary>
    public const string DefaultStyleName = "Default";

    /// <summary>
    /// What the active-id slot holds when the user has chosen Default: base styling, no profile
    /// appended. Distinct from an unset slot, which falls back to the first profile — that distinction
    /// is what keeps a migrated legacy profile active through an upgrade, since an upgrading user's
    /// pointer is unset rather than this sentinel.
    /// </summary>
    public const string DefaultStyleId = "default";

    /// <summary>How many profiles the user may define.</summary>
    public const int ProfileLimit = 4;

    /// <summary>The most characters a profile name may hold.</summary>
    public const int NameLimit = 24;

    /// <summary>The name the migrated legacy field takes.</summary>
    public const string LegacyProfileName = "Custom";

    private const string FallbackName = "Style";

    /// <summary>
    /// The id the migrated legacy profile carries: the ASCII bytes of "blurtstylelegacy" read as a
    /// <see cref="Guid"/>, so every read of an unmigrated install names the same profile.
    /// </summary>
    public static readonly Guid LegacyProfileId = new(Encoding.ASCII.GetBytes("blurtstylelegacy"));

    /// <summary>The most UTF-8 bytes one profile's instructions may hold.</summary>
    public int CharacterLimit { get; } = characterLimit;

    /// <summary>The user's profiles, in their own order. Normalized on the way out as well as in.</summary>
    public IReadOnlyList<StyleProfile> Profiles
    {
        get => Decode(store.GetString(SettingsKey) ?? string.Empty);
        set => store.Set(SettingsKey, JsonSerializer.Serialize(Normalized(value, CharacterLimit)));
    }

    /// <summary>Makes <paramref name="profile"/> the one the next dictation uses. Sticky until another choice is made.</summary>
    public void Activate(StyleProfile profile) => store.Set(ActiveSettingsKey, profile.Id.ToString());

    /// <summary>Makes Default — no profile appended — the choice. As sticky as <see cref="Activate"/>.</summary>
    public void ActivateDefault() => store.Set(ActiveSettingsKey, DefaultStyleId);

    /// <summary>The profile the next request's instruction comes from.</summary>
    public StyleProfile? Active => Resolve(Profiles, store.GetString(ActiveSettingsKey) ?? string.Empty);

    /// <summary>
    /// The instructions to append to the cleanup instruction, or null when there is no profile,
    /// Default is selected, or the active text is blank.
    /// </summary>
    public string? ActiveInstructions => Active?.Instructions.TrimmedNonEmpty();

    /// <summary>
    /// The profiles behind a raw slot value that has already been observed: the decode, the caps, and
    /// the pre-profiles fallback. An empty <paramref name="raw"/> means "no list stored", which defers
    /// to the legacy field; a stored value that won't decode — malformed JSON, a null element, or a
    /// profile missing or nulling a field — reads as no profiles rather than reviving the legacy text
    /// or throwing on a corrupt slot the app's launch path reads through <see cref="ActiveInstructions"/>.
    /// </summary>
    public IReadOnlyList<StyleProfile> Decode(string raw)
    {
        if (raw.Length == 0)
        {
            return LegacyProfiles();
        }

        List<StyleProfile?>? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<List<StyleProfile?>>(raw, DecodeOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (decoded is null || decoded.Any(profile => profile is null))
        {
            return [];
        }

        return Normalized(decoded.Select(profile => profile!), CharacterLimit);
    }

    /// <summary>
    /// Which of <paramref name="profiles"/> a raw active-id slot selects. <see cref="DefaultStyleId"/>
    /// selects none of them, deliberately. Otherwise it is the profile the slot names, or the first
    /// defined one when it names nothing (never set, or a profile since deleted).
    /// </summary>
    public static StyleProfile? Resolve(IReadOnlyList<StyleProfile> profiles, string rawId)
    {
        if (rawId == DefaultStyleId)
        {
            return null;
        }

        if (Guid.TryParse(rawId, out var id))
        {
            var match = profiles.FirstOrDefault(profile => profile.Id == id);
            if (match is not null)
            {
                return match;
            }
        }

        return profiles.Count > 0 ? profiles[0] : null;
    }

    /// <summary>
    /// The funnel every profile list passes through, on read and on write: at most
    /// <see cref="ProfileLimit"/> profiles, each with a trimmed non-blank name of at most
    /// <see cref="NameLimit"/> characters and instructions trimmed to <paramref name="characterLimit"/>
    /// UTF-8 bytes.
    /// </summary>
    public static IReadOnlyList<StyleProfile> Normalized(IEnumerable<StyleProfile> profiles, int characterLimit) =>
        profiles
            .Take(ProfileLimit)
            .Select(profile =>
            {
                var name = profile.Name.TrimmedNonEmpty() ?? FallbackName;
                return profile with
                {
                    Name = name.Length > NameLimit ? name[..NameLimit] : name,
                    Instructions = profile.Instructions.PrefixMaxUtf8Bytes(characterLimit),
                };
            })
            .ToList();

    private List<StyleProfile> LegacyProfiles()
    {
        var legacy = store.GetString(LegacySettingsKey).TrimmedNonEmpty();
        return legacy is null
            ? []
            : [.. Normalized([new StyleProfile(LegacyProfileId, LegacyProfileName, legacy)], CharacterLimit)];
    }
}
