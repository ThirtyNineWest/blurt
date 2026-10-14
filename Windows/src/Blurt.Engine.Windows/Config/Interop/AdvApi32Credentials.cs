using System.Runtime.InteropServices;

namespace Blurt.Engine.Windows.Config.Interop;

/// <summary>The generic-credential slice of the Windows Credential Manager P/Invoke surface.</summary>
internal static class AdvApi32Credentials
{
    /// <summary>A credential holding an arbitrary byte blob, keyed by target name alone.</summary>
    internal const uint CredTypeGeneric = 1;

    /// <summary>The credential survives logoff and reboot, scoped to the current user on this machine.</summary>
    internal const uint CredPersistLocalMachine = 2;

    /// <summary>Win32 error for "no credential matches this target".</summary>
    internal const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredReadW(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredDeleteW(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    internal static extern void CredFree(IntPtr buffer);
}
