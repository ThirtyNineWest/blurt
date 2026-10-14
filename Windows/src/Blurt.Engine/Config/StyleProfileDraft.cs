using System.Text;

namespace Blurt.Engine.Config;

/// <summary>
/// The style editor's rules: the caps enforced as the user types, the byte counter, when Save is
/// allowed, and how an edit merges into the stored list.
/// </summary>
public static class StyleProfileDraft
{
    /// <summary>Whether another profile may be added to a list holding <paramref name="count"/>.</summary>
    public static bool CanAdd(int count) => count < StyleProfileStore.ProfileLimit;

    /// <summary>Returns <paramref name="name"/> cut to <see cref="StyleProfileStore.NameLimit"/> characters.</summary>
    public static string ClampName(string name) =>
        name.Length > StyleProfileStore.NameLimit ? name[..StyleProfileStore.NameLimit] : name;

    /// <summary>
    /// Returns <paramref name="instructions"/> cut to <paramref name="characterLimit"/> UTF-8 bytes on
    /// a text-element boundary, so no text past the cap is ever storable.
    /// </summary>
    public static string ClampInstructions(string instructions, int characterLimit) =>
        instructions.PrefixMaxUtf8Bytes(characterLimit);

    /// <summary>The counter under the instructions field: UTF-8 bytes used over the limit, e.g. "12/2048".</summary>
    public static string Counter(string instructions, int characterLimit) =>
        $"{Encoding.UTF8.GetByteCount(instructions)}/{characterLimit}";

    /// <summary>Whether Save is allowed: both the name and the instructions hold non-blank text.</summary>
    public static bool CanSave(string name, string instructions) =>
        name.TrimmedNonEmpty() is not null && instructions.TrimmedNonEmpty() is not null;

    /// <summary>
    /// Returns <paramref name="profiles"/> with <paramref name="edited"/> replacing the profile of the
    /// same id in place, or appended when no profile carries that id.
    /// </summary>
    public static IReadOnlyList<StyleProfile> Upserted(IReadOnlyList<StyleProfile> profiles, StyleProfile edited)
    {
        var updated = profiles.ToList();
        var index = updated.FindIndex(profile => profile.Id == edited.Id);
        if (index >= 0)
        {
            updated[index] = edited;
        }
        else
        {
            updated.Add(edited);
        }

        return updated;
    }

    /// <summary>Returns <paramref name="profiles"/> without the profile carrying <paramref name="id"/>.</summary>
    public static IReadOnlyList<StyleProfile> Removed(IReadOnlyList<StyleProfile> profiles, Guid id) =>
        profiles.Where(profile => profile.Id != id).ToList();
}
