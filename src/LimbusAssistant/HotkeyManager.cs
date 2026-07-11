using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Tsundosika.LimbusAssistant;

public sealed class HotkeyManager : IDisposable
{
    const int HotkeyMessage = 0x0312;

    readonly HwndSource _source;
    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 1;
    bool _disposed;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("LimbusAssistantHotkeys")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public bool Register(HotkeyBinding binding, Action action)
    {
        var id = _nextId++;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);
        if (!RegisterHotKey(_source.Handle, id, (uint)binding.Modifiers, virtualKey))
        {
            return false;
        }
        _actions[id] = action;
        return true;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyMessage && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr handle, int id);
}
