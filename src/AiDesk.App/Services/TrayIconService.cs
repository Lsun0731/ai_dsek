using System.Windows.Forms;

namespace AiDesk.App.Services;

/// <summary>系统托盘图标 + 右键菜单。</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? ShowSecondDesktopRequested;
    public event Action? ShowMainWindowRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = ExtractAppIcon(),
            Text = "AiDesk 系统工具箱",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("进入第二桌面", null, (_, _) => ShowSecondDesktopRequested?.Invoke());
        menu.Items.Add("打开主窗口", null, (_, _) => ShowMainWindowRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowSecondDesktopRequested?.Invoke();
    }

    private static System.Drawing.Icon ExtractAppIcon()
    {
        try
        {
            var exe = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            return exe is not null ? System.Drawing.Icon.ExtractAssociatedIcon(exe) ?? System.Drawing.SystemIcons.Application
                                   : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
