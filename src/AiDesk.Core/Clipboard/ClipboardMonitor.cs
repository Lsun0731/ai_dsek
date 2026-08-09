using System.Runtime.InteropServices;

namespace AiDesk.Core.Clipboard;

/// <summary>
/// 剪贴板文本历史：轮询监听剪贴板变化，保存最近条目，支持点击复制回剪贴板。
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly object _lock = new();
    private readonly List<string> _history = new();
    private string _lastText = "";
    private const int MaxHistory = 50;

    /// <summary>剪贴板历史（最新在前）。</summary>
    public IReadOnlyList<string> History
    {
        get { lock (_lock) return _history.ToList(); }
    }

    public ClipboardMonitor(int pollIntervalMs = 800)
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, pollIntervalMs);
    }

    private void Poll()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return;
            string? text = null;
            try
            {
                var handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                    return;
                var ptr = GlobalLock(handle);
                try
                {
                    if (ptr == IntPtr.Zero)
                        return;
                    text = Marshal.PtrToStringUni(ptr);
                }
                finally
                {
                    if (ptr != IntPtr.Zero)
                        GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }

            if (string.IsNullOrWhiteSpace(text) || text.Length > 4000)
                return;
            lock (_lock)
            {
                if (text == _lastText)
                    return;
                _lastText = text;
                _history.Remove(text);
                _history.Insert(0, text);
                if (_history.Count > MaxHistory)
                    _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            }
        }
        catch
        {
            // 剪贴板被占用时跳过本轮
        }
    }

    /// <summary>把指定历史条目复制回剪贴板。</summary>
    public void CopyToClipboard(string text)
    {
        try
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var data = Marshal.StringToHGlobalUni(text);
                    SetClipboardData(CF_UNICODETEXT, data);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            lock (_lock)
            {
                _lastText = text;
            }
        }
        catch
        {
            // 忽略
        }
    }

    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    public void Dispose() => _timer.Dispose();
}
