using System.Runtime.InteropServices;

namespace Tsundosika.LimbusAssistant.Vision;

public static class GameWindowLocator
{
    public const string DefaultWindowTitle = "LimbusCompany";

    public static GameWindow? Find(string windowTitle = DefaultWindowTitle)
    {
        var handle = FindWindowW(null, windowTitle);
        return handle == IntPtr.Zero ? null : FromHandle(handle);
    }

    public static GameWindow? FromHandle(IntPtr handle)
    {
        if (!IsWindow(handle) || IsIconic(handle))
        {
            return null;
        }
        if (!GetClientRect(handle, out var rect))
        {
            return null;
        }
        var topLeft = new NativePoint { X = 0, Y = 0 };
        if (!ClientToScreen(handle, ref topLeft))
        {
            return null;
        }
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }
        return new GameWindow(handle, new WindowBounds(topLeft.X, topLeft.Y, width, height));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowW(string? className, string windowName);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr handle);
}
