using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Windows.Config.Interop;

namespace Blurt.Engine.Windows.Config;

/// <summary>
/// Stores the AssemblyAI API key as a generic Windows Credential Manager credential under
/// <paramref name="target"/>. The stored value is read once and memoized; <see cref="Save"/> keeps
/// the memo in agreement with storage rather than requiring a second read.
/// </summary>
public sealed class CredentialManagerApiKeyStore(string target) : IApiKeyGateway
{
    private const string LogCategory = "CredentialManagerApiKeyStore";
    private const string CredentialUserName = "blurt";

    private readonly Lock _gate = new();
    private bool _loaded;
    private string? _cached;

    /// <inheritdoc/>
    public string? Current
    {
        get
        {
            lock (_gate)
            {
                if (!_loaded)
                {
                    _cached = ReadFromStore();
                    _loaded = true;
                }

                return _cached;
            }
        }
    }

    /// <inheritdoc/>
    public bool Save(string key)
    {
        var trimmed = key.TrimmedNonEmpty();

        lock (_gate)
        {
            var succeeded = trimmed is null ? DeleteFromStore() : WriteToStore(trimmed);
            if (succeeded)
            {
                _cached = trimmed;
                _loaded = true;
            }

            return succeeded;
        }
    }

    private string? ReadFromStore()
    {
        if (!AdvApi32Credentials.CredReadW(target, AdvApi32Credentials.CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != AdvApi32Credentials.ErrorNotFound)
            {
                BlurtLog.Warn(LogCategory, new Win32Exception(error).Message);
            }

            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<AdvApi32Credentials.Credential>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.Unicode.GetString(blob).TrimmedNonEmpty();
        }
        finally
        {
            AdvApi32Credentials.CredFree(credentialPtr);
        }
    }

    private bool WriteToStore(string trimmed)
    {
        var blob = Encoding.Unicode.GetBytes(trimmed);
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

    private bool DeleteFromStore()
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
