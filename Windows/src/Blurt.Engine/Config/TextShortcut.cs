using System.Text.Json.Serialization;

namespace Blurt.Engine.Config;

/// <summary>
/// One spoken phrase and the text it expands to. Applied to the finished transcript on the machine,
/// after the dictation response and before the paste; nothing about a shortcut goes on the wire.
/// The id is minted once so a list row keeps its identity across a rename of its trigger.
/// </summary>
public sealed record TextShortcut
{
    /// <summary>A new shortcut with a freshly minted id.</summary>
    public TextShortcut(string trigger, string expansion)
        : this(Guid.NewGuid(), trigger, expansion)
    {
    }

    /// <summary>Full constructor, used by the JSON decoder.</summary>
    [JsonConstructor]
    public TextShortcut(Guid id, string trigger, string expansion)
    {
        Id = id;
        Trigger = trigger;
        Expansion = expansion;
    }

    /// <summary>The shortcut's identity. Fixed at creation.</summary>
    public Guid Id { get; }

    /// <summary>The spoken phrase.</summary>
    public string Trigger { get; init; }

    /// <summary>The text pasted in the phrase's place.</summary>
    public string Expansion { get; init; }
}
