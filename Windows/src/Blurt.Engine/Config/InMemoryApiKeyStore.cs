namespace Blurt.Engine.Config;

/// <summary>A thread-safe in-memory <see cref="IApiKeyGateway"/> that keeps tests away from the real credential.</summary>
public sealed class InMemoryApiKeyStore : IApiKeyGateway
{
    private readonly Lock _gate = new();
    private string? _key;

    /// <inheritdoc/>
    public string? Current
    {
        get
        {
            lock (_gate)
            {
                return _key;
            }
        }
    }

    /// <summary>Stores <paramref name="key"/> trimmed, or clears it when blank. Always returns true.</summary>
    public bool Save(string key)
    {
        lock (_gate)
        {
            _key = key.TrimmedNonEmpty();
        }

        return true;
    }
}
