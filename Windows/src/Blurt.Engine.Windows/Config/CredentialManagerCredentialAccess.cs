using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Windows.Config.Interop;

namespace Blurt.Engine.Windows.Config;

/// <summary>Reads, writes, and deletes generic Windows Credential Manager credentials via P/Invoke.</summary>
public sealed class CredentialManagerCredentialAccess : ICredentialAccess
{
    private const string LogCategory = "CredentialManagerCredentialAccess";
    private const string CredentialUserName = "blurt";

    /// <inheritdoc/>
    public CredentialReadResult Read(string target)
    {
        if (!AdvApi32Credentials.CredReadW(target, AdvApi32Credentials.CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            return error == AdvApi32Credentials.ErrorNotFound
                ? CredentialReadResult.Absent
                : new CredentialReadResult.Unavailable(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<AdvApi32Credentials.Credential>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return CredentialReadResult.Absent;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            var key = Encoding.Unicode.GetString(blob).TrimmedNonEmpty();
            return key is null ? CredentialReadResult.Absent : new CredentialReadResult.Found(key);
        }
        finally
        {
            AdvApi32Credentials.CredFree(credentialPtr);
        }
    }

    /// <inheritdoc/>
    public bool Write(string target, string value)
    {
        var blob = Encoding.Unicode.GetBytes(value);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var credential = new AdvApi32Credentials.Credential
            {
                Type = AdvApi32Credentials.CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = AdvApi32Credentials.CredPersistLocalMachine,
                UserName = CredentialUserName,
            };

            if (AdvApi32Credentials.CredWriteW(ref credential, 0))
            {
                return true;
            }

            BlurtLog.Warn(LogCategory, new Win32Exception(Marshal.GetLastWin32Error()).Message);
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    /// <inheritdoc/>
    public bool Delete(string target)
    {
        if (AdvApi32Credentials.CredDeleteW(target, AdvApi32Credentials.CredTypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == AdvApi32Credentials.ErrorNotFound)
        {
            return true;
        }

        BlurtLog.Warn(LogCategory, new Win32Exception(error).Message);
        return false;
    }
}
