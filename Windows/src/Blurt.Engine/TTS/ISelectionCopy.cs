namespace Blurt.Engine.TTS;

/// <summary>Reads the selection by copying it, for apps whose focused element cannot be reached directly.</summary>
public interface ISelectionCopy
{
    /// <summary>
    /// Copies the focused app's selection and returns it, cut to <paramref name="maxCharacters"/>, or
    /// null when nothing visible is selected.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<string?> CopySelectionAsync(int maxCharacters, CancellationToken ct);
}
