using System.Runtime.InteropServices;

namespace Blurt.Engine.Windows.Audio.Interop;

/// <summary><c>IAudioRenderClient</c>. Methods are in vtable order.</summary>
[ComImport]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig]
    int GetBuffer(uint frames, out IntPtr data);

    [PreserveSig]
    int ReleaseBuffer(uint written, uint flags);
}
