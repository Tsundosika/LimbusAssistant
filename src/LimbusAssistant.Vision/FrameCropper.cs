using System.Runtime.InteropServices;

namespace Tsundosika.LimbusAssistant.Vision;

public static class FrameCropper
{
    public static CaptureFrame CropToClient(CaptureFrame frame, IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var windowRect))
        {
            return frame;
        }
        var client = GameWindowLocator.FromHandle(windowHandle);
        if (client is null)
        {
            return frame;
        }
        var offsetX = client.ClientBounds.X - windowRect.Left;
        var offsetY = client.ClientBounds.Y - windowRect.Top;
        var width = client.ClientBounds.Width;
        var height = client.ClientBounds.Height;
        if (offsetX <= 0 && offsetY <= 0 && width >= frame.Width && height >= frame.Height)
        {
            return frame;
        }
        offsetX = Math.Clamp(offsetX, 0, Math.Max(0, frame.Width - 1));
        offsetY = Math.Clamp(offsetY, 0, Math.Max(0, frame.Height - 1));
        width = Math.Clamp(width, 1, frame.Width - offsetX);
        height = Math.Clamp(height, 1, frame.Height - offsetY);
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                frame.PixelsBgra,
                (offsetY + row) * frame.Stride + offsetX * 4,
                pixels,
                row * width * 4,
                width * 4);
        }
        return new CaptureFrame(pixels, width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
}
