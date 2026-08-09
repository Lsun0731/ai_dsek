using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AiDesk.App.Services;

/// <summary>
/// 全局热键注册（RegisterHotKey + 隐藏消息窗口），支持多个热键各自绑定处理函数。
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private int _nextId = 1;
    private readonly Dictionary<int, Action> _handlers = new();

    /// <summary>
    /// 注册热键并绑定处理函数。modifiers：MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4, MOD_WIN=0x8。
    /// 返回热键 id（&gt;0），失败返回 0（可能被其他程序占用）。
    /// </summary>
    public int Register(uint modifiers, uint virtualKey, Action handler)
    {
        if (_source is null)
        {
            _source = new HwndSource(new HwndSourceParameters("AiDeskHotKey")
            {
                Width = 0, Height = 0, WindowStyle = 0,
            });
            _source.AddHook(WndProc);
        }

        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
            return 0;
        _handlers[id] = handler;
        return id;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            foreach (var id in _handlers.Keys)
                UnregisterHotKey(_source.Handle, id);
            _handlers.Clear();
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
