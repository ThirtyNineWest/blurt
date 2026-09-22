using System.ComponentModel;
using Blurt.Engine;
using Blurt.Engine.Config;

namespace Blurt.App.Services;

/// <summary>
/// The API key surface the UI binds to: the stored key, an observable <see cref="HasKey"/>, and the
/// validate-then-save flow. <see cref="ApiKeySubmission"/> owns the invariant that an unverified
/// key never persists.
/// </summary>
public sealed class ApiKeyModel : INotifyPropertyChanged
{
    private readonly IApiKeyGateway _keyStore;
    private readonly ApiKeySubmission _submission;

    /// <summary>Creates the model over <paramref name="keyStore"/>, validating submitted keys with <paramref name="validate"/>.</summary>
    public ApiKeyModel(IApiKeyGateway keyStore, Func<string, Task<ApiKeyValidationResult>> validate)
    {
        _keyStore = keyStore;
        _submission = new ApiKeySubmission(keyStore, validate);
        HasKey = keyStore.HasKey;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether a key is stored, as of the last save, submission, or <see cref="RefreshStatus"/>.</summary>
    public bool HasKey { get; private set; }

    /// <summary>The stored key, read from the store on every access.</summary>
    public string? Current => _keyStore.Current;

    /// <summary>The storage seam the key is read from and written to.</summary>
    public IApiKeyGateway Storage => _keyStore;

    /// <summary>
    /// The session's per-press readiness check: <see cref="BlurtError.ApiKeyMissing"/> when no key is
    /// stored, otherwise null. Reads the store on every call, so a key saved elsewhere counts at once.
    /// </summary>
    public Func<BlurtError?> ReadinessCheck()
    {
        var keyStore = _keyStore;
        return () => keyStore.HasKey ? null : new BlurtError.ApiKeyMissing();
    }

    /// <summary>Saves <paramref name="key"/> without validation. Returns whether a key is readable back afterwards.</summary>
    public bool Save(string key)
    {
        var saved = _submission.Save(key);
        RefreshStatus();
        return saved;
    }

    /// <summary>
    /// Validates <paramref name="key"/> against AssemblyAI and saves it only when accepted. A
    /// cancellation of <paramref name="ct"/> before the save leaves the stored key untouched.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the save.</exception>
    public async Task<ApiKeySubmissionOutcome> SubmitAsync(string key, CancellationToken ct = default)
    {
        var outcome = await _submission.SubmitAsync(key, ct);
        RefreshStatus();
        return outcome;
    }

    /// <summary>Re-reads the store and raises <see cref="PropertyChanged"/> when <see cref="HasKey"/> moved.</summary>
    public void RefreshStatus()
    {
        var hasKey = _keyStore.HasKey;
        if (hasKey == HasKey)
        {
            return;
        }

        HasKey = hasKey;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasKey)));
    }
}
