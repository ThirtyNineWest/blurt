using System.ComponentModel;
using Blurt.App.Services;
using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;

namespace Blurt.App.Main;

/// <summary>
/// Backs one API key entry surface (the wizard step or the Settings account editor): how the stored
/// key is presented, the commit button's state, and the validate-then-save submission. Use on the
/// UI thread.
/// </summary>
public sealed class ApiKeyEntryViewModel : ObservableObject
{
    private const string LogCategory = "ApiKeyEntry";

    private readonly ApiKeyModel _apiKey;
    private bool _draftHasText;
    private bool _isSubmitting;
    private string? _inlineError;

    /// <summary>Creates the entry over <paramref name="apiKey"/>, re-presenting the key whenever <see cref="ApiKeyModel.HasKey"/> moves.</summary>
    public ApiKeyEntryViewModel(ApiKeyModel apiKey)
    {
        _apiKey = apiKey;
        apiKey.PropertyChanged += OnApiKeyChanged;
    }

    /// <summary>Raised after a submitted key was validated and saved.</summary>
    public event Action? Saved;

    /// <summary>How the stored key is presented.</summary>
    public ApiKeyDisplay KeyDisplay => ApiKeyDisplay.Resolve(_apiKey.Current);

    /// <summary>Whether the key field holds non-blank text.</summary>
    public bool DraftHasText
    {
        get => _draftHasText;
        set
        {
            if (Set(ref _draftHasText, value))
            {
                Raise(nameof(CanSubmit));
            }
        }
    }

    /// <summary>Whether a key validation is in flight.</summary>
    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set
        {
            if (Set(ref _isSubmitting, value))
            {
                Raise(nameof(CanSubmit));
            }
        }
    }

    /// <summary>Whether the commit button is enabled: a non-blank draft and no validation in flight.</summary>
    public bool CanSubmit => DraftHasText && !IsSubmitting;

    /// <summary>A recoverable submission error shown under the field, or null.</summary>
    public string? InlineError
    {
        get => _inlineError;
        set => Set(ref _inlineError, value);
    }

    /// <summary>
    /// Validates <paramref name="key"/> and saves it only when AssemblyAI accepts it. A recoverable
    /// failure lands in <see cref="InlineError"/>; a saved key raises <see cref="Saved"/>. Returns the
    /// failure report, or null when the key was saved.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CanSubmit"/> is false.</exception>
    public async Task<ApiKeyFailureReport?> SubmitAsync(string key)
    {
        if (!CanSubmit)
        {
            throw new InvalidOperationException("A key can be submitted only while CanSubmit is true.");
        }

        IsSubmitting = true;
        InlineError = null;
        ApiKeySubmissionOutcome outcome;
        try
        {
            outcome = await _apiKey.SubmitAsync(key);
        }
        finally
        {
            IsSubmitting = false;
        }

        BlurtLog.Info(LogCategory, $"key submission: {outcome}");
        var report = ApiKeySubmission.FailureReport(outcome);
        Raise(nameof(KeyDisplay));
        if (report is ApiKeyFailureReport.InlineReport inline)
        {
            InlineError = inline.Message;
        }

        if (report is null)
        {
            Saved?.Invoke();
        }

        return report;
    }

    /// <summary>Clears the draft state and any error, for an entry surface being opened again.</summary>
    public void ResetDraft()
    {
        DraftHasText = false;
        InlineError = null;
        Raise(nameof(KeyDisplay));
    }

    private void OnApiKeyChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(KeyDisplay));
}
