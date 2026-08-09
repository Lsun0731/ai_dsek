using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AiDesk.App.Services;

/// <summary>全局热键注册（RegisterHotKey + 隐藏消息窗口）。</summary>
public sealed class HotKeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly int _id;
    private HwndSource? _source;

    public event Action? Pressed;

    public HotKeyService(int id) => _id = id;

    /// <summary>注册热键。modifiers：MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4, MOD_WIN=0x8。</summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        var parameters = new HwndSourceParameters("AiDeskHotKey") { Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        var ok = RegisterHotKey(_source.Handle, _id, modifiers, virtualKey);
        if (!ok)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
        return ok;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
