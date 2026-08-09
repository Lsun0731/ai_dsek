using System.Windows;
using System.Windows.Media;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.Widgets;

namespace AiDesk.App.Widgets;

/// <summary>系统状态小组件：CPU / 内存 / 磁盘 / 网络。</summary>
public partial class StatsWidgetWindow : WidgetWindowBase
{
    private readonly SystemStatsProvider _provider = new();

    public StatsWidgetWindow() : base(Services.WidgetKind.Stats)
    {
        InitializeComponent();
        StartTicker(2);
    }

    protected override void OnTick()
    {
        var stats = _provider.Sample();

        CpuBar.Value = stats.CpuPercent;
        CpuText.Text = $"{stats.CpuPercent:F0}%";

        MemBar.Value = stats.MemPercent;
        MemText.Text = $"{stats.MemPercent:F0}%";
        MemDetail.Text = $"{stats.MemUsedGb:F1} / {stats.MemTotalGb:F1} GB";

        // 磁盘：显示第一个固定盘（大多数用户关心系统盘）
        var disk = stats.Disks.FirstOrDefault();
        if (disk is null)
        {
            DiskPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            DiskPanel.Visibility = Visibility.Visible;
            DiskName.Text = disk.Name;
            DiskBar.Value = disk.Percent;
            DiskText.Text = $"{disk.Percent:F0}%";
            DiskBar.Foreground = disk.Percent > 90 ? RedBrush : BlueBrush;
        }

        // 网络：上下行速率
        NetText.Text = $"↓ {FormatRate(stats.DownKbPerSec)}  ↑ {FormatRate(stats.UpKbPerSec)}";

        // 高占用变色提醒
        CpuBar.Foreground = stats.CpuPercent > 90 ? RedBrush : BlueBrush;
        MemBar.Foreground = stats.MemPercent > 90 ? RedBrush : BlueBrush;
    }

    private static readonly Brush BlueBrush = (Brush)new BrushConverter().ConvertFrom("#5B8DF5")!;
    private static readonly Brush RedBrush = (Brush)new BrushConverter().ConvertFrom("#E5484D")!;

    private static string FormatRate(double kbPerSec) =>
        kbPerSec >= 1024 ? $"{kbPerSec / 1024:F1} MB/s" : $"{kbPerSec:F0} KB/s";

    protected override void OnClosed(EventArgs e)
    {
        _provider.Dispose();
        base.OnClosed(e);
    }
}
