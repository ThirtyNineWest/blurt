namespace Blurt.Engine.Windows.Injection;

/// <summary>The two clipboard operations a paste performs.</summary>
public interface IClipboardAccess
{
    /// <summary>Replaces the clipboard contents with <paramref name="text"/>.</summary>
    void Write(string text);

    /// <summary>
    /// Replaces the clipboard contents with <paramref name="text"/> and returns an action that puts the previous
    /// contents back, but only when nothing else has written to the clipboard since this write.
    /// </summary>
    Action WriteAndPrepareRestore(string text);
}
