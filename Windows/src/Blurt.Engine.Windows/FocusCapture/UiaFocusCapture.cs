using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.Windows.FocusCapture.Interop;

namespace Blurt.Engine.Windows.FocusCapture;

/// <summary>
/// Reads the foreground window through Win32 and the focused field through UI Automation. Every UI Automation call
/// is a cross-process round trip; call these members off the UI thread of any window they may read.
/// </summary>
public sealed class UiaFocusCapture : IFocusCapture, ISelectionReader
{
    private const string LogCategory = "FocusCapture";

    private ImmutableHashSet<nint> _ignoredWindows = [];

    /// <summary>Registers a window of this app that <see cref="CaptureForeground"/> never returns. Thread-safe.</summary>
    public void IgnoreWindow(nint handle) => ImmutableInterlocked.Update(ref _ignoredWindows, static (set, h) => set.Add(h), handle);

    /// <summary>Removes a window registered with <see cref="IgnoreWindow"/>. Thread-safe.</summary>
    public void ForgetWindow(nint handle) => ImmutableInterlocked.Update(ref _ignoredWindows, static (set, h) => set.Remove(h), handle);

    /// <summary>Returns whether <paramref name="foreground"/> is zero or one of the <paramref name="ignored"/> windows.</summary>
    public static bool IsOwnWindow(nint foreground, IReadOnlySet<nint> ignored)
    {
        ArgumentNullException.ThrowIfNull(ignored);
        return foreground == 0 || ignored.Contains(foreground);
    }

    /// <summary>
    /// Returns the foreground window with its process id, name, and executable path, or null when there is no
    /// foreground window, it is a window registered with <see cref="IgnoreWindow"/>, or its process id is unreadable. The name and path are
    /// null when the process has exited or denies access.
    /// </summary>
    public FocusTarget? CaptureForeground()
    {
        var window = User32Window.GetForegroundWindow();
        if (IsOwnWindow(window, Volatile.Read(ref _ignoredWindows)))
        {
            return null;
        }

        var processId = User32Window.ProcessIdOf(window);
        if (processId == 0)
        {
            return null;
        }

        string? name = null;
        string? path = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
            path = process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            BlurtLog.Info(LogCategory, $"process {processId} unreadable: {exception.GetType().Name}");
        }

        return new FocusTarget(window, processId, name, path);
    }

    /// <summary>
    /// Returns the focused field's window title, label, prior text, and selection. Returns
    /// <see cref="FocusedFieldContext.Empty"/> when no element is focused, and <see cref="FocusedFieldContext.Empty"/>
    /// marked secure when the focused element, its role, or its password flag cannot be read. Never reads the contents of a password
    /// field or of an element owned by a process other than the foreground window's.
    /// </summary>
    public FocusedFieldContext CaptureFieldContext() =>
        FieldContextRules.FromFocusedElement(static () => AutomationElement.FocusedElement, CaptureFrom);

    private static FocusedFieldContext CaptureFrom(AutomationElement element)
    {
        var isPassword = Read(() =>
        {
            _ = element.Current.ControlType;
            return (bool?)element.Current.IsPassword;
        });
        var foreground = User32Window.GetForegroundWindow();
        var foregroundProcess = User32Window.ProcessIdOf(foreground);
        var elementProcess = Read(() => (int?)element.Current.ProcessId);
        var sameProcess = foregroundProcess != 0 && elementProcess == foregroundProcess;

        return FieldContextRules.Compose(
            isPassword,
            sameProcess,
            () => User32Window.TitleOf(foreground),
            () => FocusCaptureRules.SelectLabel(
                () => Read(() => element.Current.Name),
                () => Read(() => element.Current.HelpText),
                () => Read(() => element.Current.ItemStatus),
                () => Read(() => element.Current.LocalizedControlType)),
            () => ReadText(element));
    }

    /// <summary>
    /// Returns whether the focused element accepts pasted text: an Edit, Document, or ComboBox control type, a
    /// writable value, or a text pattern. Returns false when no element is focused.
    /// </summary>
    public bool HasEditableFocusedElement()
    {
        var element = FocusedElement();
        if (element is null)
        {
            return false;
        }

        var controlType = Read(() => element.Current.ControlType);
        var isEditControlType = controlType == ControlType.Edit
            || controlType == ControlType.Document
            || controlType == ControlType.ComboBox;
        var valueSettable = Read(() =>
            element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
            && pattern is ValuePattern value
            && !value.Current.IsReadOnly);
        var hasInsertionPoint = Read(() => element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)
            && pattern is TextPattern);

        return FocusCaptureRules.IsEditableTarget(isEditControlType, valueSettable, hasInsertionPoint);
    }

    /// <summary>
    /// Returns the focused element's selection, trimmed and cut to <paramref name="maxChars"/>;
    /// <see cref="SelectionRead.None"/> for a password field, an unreadable password flag, a bare caret, or an
    /// element with no text pattern; <see cref="SelectionRead.Unreadable"/> when no focused element can be read.
    /// Call off the UI thread; every read is a cross-process round trip.
    /// </summary>
    public SelectionRead CaptureSelectedText(int maxChars)
    {
        var element = FocusedElement();
        if (element is null)
        {
            return FieldContextRules.ClassifySelection(elementReachable: false, isPassword: null, selected: null, maxChars);
        }

        var isPassword = Read(() => (bool?)element.Current.IsPassword);
        if (isPassword is not false)
        {
            return FieldContextRules.ClassifySelection(elementReachable: true, isPassword, selected: null, maxChars);
        }

        var selected = Read(() => ReadSelection(element, maxChars));
        return FieldContextRules.ClassifySelection(elementReachable: true, isPassword, selected, maxChars);
    }

    private static string? ReadSelection(AutomationElement element, int maxChars)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) || pattern is not TextPattern text)
        {
            return null;
        }

        var selection = text.GetSelection();
        return selection.Length == 0 ? null : selection[0].GetText(maxChars);
    }

    private static AutomationElement? FocusedElement() => Read(() => AutomationElement.FocusedElement);

    private static (string? Prior, string? Selected) ReadText(AutomationElement element)
    {
        var fromTextPattern = Read(() => ReadThroughTextPattern(element));
        if (fromTextPattern is { } read)
        {
            return read;
        }

        var value = Read(() => element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
            && pattern is ValuePattern valuePattern
            ? valuePattern.Current.Value
            : null);
        return (value, null);
    }

    private static (string? Prior, string? Selected)? ReadThroughTextPattern(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) || pattern is not TextPattern text)
        {
            return null;
        }

        var selection = text.GetSelection();
        if (selection.Length == 0)
        {
            return null;
        }

        var caret = selection[0];
        var before = caret.Clone();
        before.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
        before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -FocusCaptureRules.MaxPriorChars);
        var prior = before.GetText(-1);
        var selected = caret.GetText(FocusCaptureRules.MaxSelectedChars);
        return (prior, selected);
    }

    private static T? Read<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception)
        {
            BlurtLog.Info(LogCategory, $"UI Automation read failed: {exception.GetType().Name}");
            return default;
        }
    }
}
