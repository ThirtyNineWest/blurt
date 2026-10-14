namespace Blurt.Engine.Config;

/// <summary>The narrow API key storage seam hosts compose against.</summary>
public interface IApiKeyGateway
{
    /// <summary>The stored key, or null when none is saved.</summary>
    string? Current { get; }

    /// <summary>Stores <paramref name="key"/> trimmed; a blank key deletes the stored one. Returns true on success.</summary>
    bool Save(string key);

    /// <summary>Whether a non-blank key is stored, derived from <see cref="Current"/>.</summary>
    bool HasKey => Current.TrimmedNonEmpty() is not null;
}
