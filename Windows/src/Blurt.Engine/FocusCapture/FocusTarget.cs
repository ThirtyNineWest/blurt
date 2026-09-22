namespace Blurt.Engine.FocusCapture;

/// <summary>The foreground window captured at press time, which the paste returns to.</summary>
/// <param name="WindowHandle">The top-level window handle.</param>
/// <param name="ProcessId">The owning process id.</param>
/// <param name="ProcessName">The owning process's display name, when readable.</param>
/// <param name="ProcessPath">The owning process's executable path, when readable.</param>
public sealed record FocusTarget(nint WindowHandle, int ProcessId, string? ProcessName, string? ProcessPath);
