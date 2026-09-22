namespace Blurt.Engine.Windows.Config;

/// <summary>The credential-store operations <see cref="CredentialManagerApiKeyStore"/> needs, seamed out so tests can fake it.</summary>
public interface ICredentialAccess
{
    /// <summary>Reads the credential at <paramref name="target"/>.</summary>
    CredentialReadResult Read(string target);

    /// <summary>Writes <paramref name="value"/> to <paramref name="target"/>. Returns whether the write succeeded.</summary>
    bool Write(string target, string value);

    /// <summary>Deletes the credential at <paramref name="target"/>. Returns true when it is gone, including when it never existed.</summary>
    bool Delete(string target);
}
