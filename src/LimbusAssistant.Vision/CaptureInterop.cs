using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Tsundosika.LimbusAssistant.Vision;

static class CaptureInterop
{
    static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static readonly Guid D3D11Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr windowHandle)
    {
        var interop = GetActivationFactory<IGraphicsCaptureItemInterop>("Windows.Graphics.Capture.GraphicsCaptureItem");
        var iid = GraphicsCaptureItemIid;
        var abi = interop.CreateForWindow(windowHandle, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    public static IDirect3DDevice CreateDirect3DDevice(IntPtr dxgiDevicePointer)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePointer, out var abi));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    public static IntPtr GetDxgiInterfacePointer(object winRtSurface, Guid interfaceIid)
    {
        var nativePointer = ((IWinRTObject)winRtSurface).NativeObject.ThisPtr;
        var accessIid = typeof(IDirect3DDxgiInterfaceAccess).GUID;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(nativePointer, ref accessIid, out var accessPointer));
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPointer);
            return access.GetInterface(ref interfaceIid);
        }
        finally
        {
            Marshal.Release(accessPointer);
        }
    }

    static T GetActivationFactory<T>(string runtimeClassName)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClassName, runtimeClassName.Length, out var hstring));
        try
        {
            var iid = typeof(T).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, ref iid, out var factory));
            try
            {
                return (T)Marshal.GetObjectForIUnknown(factory);
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
