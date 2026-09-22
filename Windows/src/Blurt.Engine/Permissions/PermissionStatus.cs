namespace Blurt.Engine.Permissions;

/// <summary>The permissions dictation needs, and whether Blurt currently holds them.</summary>
/// <param name="Microphone">Whether microphone capture is permitted.</param>
public sealed record PermissionStatus(bool Microphone)
{
    /// <summary>Whether every required permission is granted.</summary>
    public bool AllGranted => Microphone;

    /// <summary>
    /// True when <paramref name="since"/> held every permission and this status no longer does —
    /// i.e. a permission was revoked after setup had completed.
    /// </summary>
    public bool LostGrant(PermissionStatus since) => since.AllGranted && !AllGranted;
}
