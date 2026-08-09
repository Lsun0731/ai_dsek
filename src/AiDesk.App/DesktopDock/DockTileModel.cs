using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiDesk.App.DesktopDock;

/// <summary>磁贴数据（运行中应用窗口）。IsActive 变更自动通知，高亮只更新单个磁贴。</summary>
public sealed partial class DockTileModel : ObservableObject
{
    public required string Title { get; init; }
    public required int ProcessId { get; init; }
    public ImageSource? Icon { get; init; }

    [ObservableProperty]
    private bool _isActive;
}
