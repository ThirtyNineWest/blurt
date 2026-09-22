namespace Blurt.Engine;

/// <summary>
/// Which app the engine runs inside: the Credential Manager target the API key lands in, the
/// logging subsystem, the settings key prefix, the log directory name, the product name
/// user-facing copy says, and the GitHub release the update check reads.
/// </summary>
/// <param name="ProductName">The product name as it appears in user-facing copy.</param>
/// <param name="Subsystem">Reverse-DNS identity shared by the debug and release builds.</param>
/// <param name="CredentialTarget">The Credential Manager target name for the API key.</param>
/// <param name="SettingsPrefix">
/// Prefixed onto every settings key. Changing it for a shipped host abandons every existing
/// user's settings.
/// </param>
/// <param name="LogDirectoryName">The directory the developer-mode logs are written to.</param>
/// <param name="ReleaseUrl">The "latest release" endpoint the update check reads.</param>
public sealed record HostIdentity(
    string ProductName,
    string Subsystem,
    string CredentialTarget,
    string SettingsPrefix,
    string LogDirectoryName,
    Uri ReleaseUrl)
{
    /// <summary>Blurt's own identity, and the value an unconfigured host inherits.</summary>
    public static readonly HostIdentity Blurt = new(
        "Blurt",
        "dev.alex.blurt",
        "blurt",
        "Blurt",
        "Blurt",
        new Uri("https://api.github.com/repos/AssemblyAI/blurt/releases/latest"));

    /// <summary>
    /// The identity a debug build runs under. Only the credential target differs from
    /// <see cref="Blurt"/>, so a dev build never reads, overwrites, or deletes the shipping key.
    /// </summary>
    public static readonly HostIdentity BlurtDev = Blurt with { CredentialTarget = "blurt-dev" };

    private static HostIdentity s_current = Blurt;

    /// <summary>The identity every engine component reads. <see cref="Blurt"/> until <see cref="Configure"/> is called.</summary>
    public static HostIdentity Current => Volatile.Read(ref s_current);

    /// <summary>
    /// Adopts <paramref name="identity"/> for the rest of the process. Call once at the composition
    /// root before constructing any engine type: a reader that resolved the identity earlier keeps
    /// the value it saw.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="identity"/> is null.</exception>
    public static void Configure(HostIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Volatile.Write(ref s_current, identity);
    }

    /// <summary>Returns the settings key for <paramref name="suffix"/>: the prefix followed by the suffix.</summary>
    public string SettingsKey(string suffix) => SettingsPrefix + suffix;
}
