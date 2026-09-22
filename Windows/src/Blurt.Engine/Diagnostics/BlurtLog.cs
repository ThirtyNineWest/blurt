using System.Diagnostics;

namespace Blurt.Engine.Diagnostics;

/// <summary>Process-wide diagnostic logging. Each line is written to <see cref="Sink"/> as <c>"{category}: {message}"</c>.</summary>
public static class BlurtLog
{
    private static Action<string>? s_sink = static line => Trace.WriteLine(line);

    /// <summary>Receives every formatted line; <see cref="Trace.WriteLine(string)"/> by default, null to drop lines.</summary>
    public static Action<string>? Sink
    {
        get => Volatile.Read(ref s_sink);
        set => Volatile.Write(ref s_sink, value);
    }

    /// <summary>Logs an informational line.</summary>
    public static void Info(string category, string message) => Write(category, message);

    /// <summary>Logs a warning line.</summary>
    public static void Warn(string category, string message) => Write(category, message);

    /// <summary>Logs an error line.</summary>
    public static void Error(string category, string message) => Write(category, message);

    private static void Write(string category, string message) => Sink?.Invoke($"{category}: {message}");
}
