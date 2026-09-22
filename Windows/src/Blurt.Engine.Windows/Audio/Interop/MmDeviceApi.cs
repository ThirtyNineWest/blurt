using System.Runtime.InteropServices;

namespace Blurt.Engine.Windows.Audio.Interop;

/// <summary>Endpoint data-flow direction (<c>EDataFlow</c>).</summary>
internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

/// <summary>Endpoint role (<c>ERole</c>).</summary>
internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

/// <summary>Class identifiers and activation constants of the MMDevice API.</summary>
internal static class MmDeviceApi
{
    public static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public const uint ClsctxAll = 23;

    /// <summary>Creates an <see cref="IMMDeviceEnumerator"/>; the caller releases it.</summary>
    /// <exception cref="COMException">The enumerator class could not be instantiated.</exception>
    public static IMMDeviceEnumerator CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator, throwOnError: true)!;
        return (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
    }
}

/// <summary><c>IMMDeviceEnumerator</c>. Methods are in vtable order.</summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

/// <summary><c>IMMDevice</c>. Methods are in vtable order.</summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint storageAccess, out IntPtr properties);

    [PreserveSig]
    int GetId(out IntPtr id);

    [PreserveSig]
    int GetState(out uint state);
}
