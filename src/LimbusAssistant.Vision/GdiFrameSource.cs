using System.Runtime.InteropServices;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class GdiFrameSource(IntPtr windowHandle) : IFrameSource
{
    const uint RenderFullContent = 2;

    public CaptureFrame? TryCapture()
    {
        var window = GameWindowLocator.FromHandle(windowHandle);
        if (window is null)
        {
            return null;
        }
        var width = window.ClientBounds.Width;
        var height = window.ClientBounds.Height;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }
        var memoryDc = CreateCompatibleDC(screenDc);
        var info = BitmapInfo.ForTopDownBgra(width, height);
        var bitmap = CreateDIBSection(screenDc, ref info, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }
            var previous = SelectObject(memoryDc, bitmap);
            var painted = PrintWindow(windowHandle, memoryDc, RenderFullContent);
            SelectObject(memoryDc, previous);
            if (!painted)
            {
                return null;
            }
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new CaptureFrame(pixels, width, height);
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }
            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public int ColorsUsed;
        public int ColorsImportant;

        public static BitmapInfo ForTopDownBgra(int width, int height) => new()
        {
            Size = Marshal.SizeOf<BitmapInfo>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr handle);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr handle, IntPtr deviceContext);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo info,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr gdiObject);
}
