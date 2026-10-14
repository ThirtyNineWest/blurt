using Blurt.Engine.Config;

namespace Blurt.Engine.Windows.Config;

/// <summary>
/// The memo in front of an <see cref="ICredentialAccess"/> target, keeping <see cref="Current"/> and
/// storage in agreement. A read that fails without saying whether a value exists (anything other than
/// "not found") is never memoized, so the next read retries storage instead of pinning "no key" for
/// the rest of the process — the same failure mode the macOS port's <c>MemoizedKeyStore</c> guards
/// against for a locked keychain or a denied ACL prompt.
/// </summary>
public sealed class CredentialManagerApiKeyStore : IApiKeyGateway
{
    private readonly string _target;
    private readonly ICredentialAccess _access;
    private readonly Lock _gate = new();
    private bool _loaded;
    private string? _cached;

    /// <summary>Reads and writes the generic credential at <paramref name="target"/> via Windows Credential Manager.</summary>
    public CredentialManagerApiKeyStore(string target)
        : this(target, new CredentialManagerCredentialAccess())
    {
    }

    /// <summary>Reads and writes <paramref name="target"/> through <paramref name="access"/>, so tests can fake storage.</summary>
    public CredentialManagerApiKeyStore(string target, ICredentialAccess access)
    {
        _target = target;
        _access = access;
    }

    /// <inheritdoc/>
    public string? Current
    {
        get
        {
            lock (_gate)
            {
                return _loaded ? _cached : ReadAndMemoize();
            }
        }
    }

    /// <inheritdoc/>
    public bool Save(string key)
    {
        var trimmed = key.TrimmedNonEmpty();

        lock (_gate)
        {
            var succeeded = trimmed is null ? _access.Delete(_target) : _access.Write(_target, trimmed);
            // Re-read rather than memoizing `trimmed` verbatim: a failed write must not clobber the
            // memo with a key that was never stored, and an unreadable read-back must leave the memo
            // unloaded rather than guess.
            ReadAndMemoize();
            return succeeded;
        }
    }

    /// <summary>Consults storage once and updates the memo. Callers hold <see cref="_gate"/>.</summary>
    private string? ReadAndMemoize()
    {
        switch (_access.Read(_target))
        {
            case CredentialReadResult.Found(var key):
                _cached = key;
                _loaded = true;
                return key;
            case CredentialReadResult.Unavailable:
                _loaded = false;
                return null;
            default:
                _cached = null;
                _loaded = true;
                return null;
        }
    }
}
