using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Tsundosika.LimbusAssistant.Vision;

public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> ListCaptureCandidates()
    {
        var ownProcessId = Environment.ProcessId;
        var results = new List<WindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }
            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == ownProcessId)
            {
                return true;
            }
            var bounds = GameWindowLocator.FromHandle(handle)?.ClientBounds;
            if (bounds is not { Width: >= 320, Height: >= 240 })
            {
                return true;
            }
            results.Add(new WindowInfo(handle, title, GetProcessName(processId)));
            return true;
        }, IntPtr.Zero);
        return results;
    }

    static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLengthW(handle);
        if (length <= 0)
        {
            return "";
        }
        var buffer = new StringBuilder(length + 1);
        GetWindowTextW(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    delegate bool EnumWindowsCallback(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr handle, StringBuilder buffer, int maxLength);

    [DllImport("user32.dll")]
    static extern int GetWindowTextLengthW(IntPtr handle);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
}
