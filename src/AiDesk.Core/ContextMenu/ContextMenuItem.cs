namespace AiDesk.Core.ContextMenu;

/// <summary>
/// 一个右键菜单项（注册表中的一个子键）。
/// </summary>
public sealed class ContextMenuItem
{
    /// <summary>显示名称（注册表子键名，可能带 "-" 禁用前缀）。</summary>
    public required string Name { get; init; }

    /// <summary>是否为 COM 扩展项（ContextMenuHandlers 下的 CLSID 项）。</summary>
    public required bool IsExtension { get; init; }

    /// <summary>挂载位置。</summary>
    public required ContextMenuLocation Location { get; init; }

    /// <summary>该项注册表键的完整路径（用于定位/删除）。</summary>
    public required string RegistryPath { get; init; }

    /// <summary>注册表子键名（不含禁用前缀 "-"）。</summary>
    public required string RawKeyName { get; init; }

    /// <summary>是否已被禁用（名称以 "-" 开头）。</summary>
    public bool IsDisabled => RawKeyName.StartsWith("-", StringComparison.Ordinal);

    /// <summary>
    /// 菜单命令：shell 项读取默认值或 command 子键；扩展项读取默认值（CLSID）。
    /// </summary>
    public string? Command { get; init; }

    /// <summary>该项在注册表中的位置描述（用于界面展示）。</summary>
    public string LocationDisplay => GetLocationDisplay(Location);

    public static string GetLocationDisplay(ContextMenuLocation location) => location switch
    {
        ContextMenuLocation.File => "文件",
        ContextMenuLocation.Directory => "文件夹",
        ContextMenuLocation.DirectoryBackground => "文件夹背景",
        ContextMenuLocation.DesktopBackground => "桌面背景",
        ContextMenuLocation.Folder => "文件夹类",
        ContextMenuLocation.Drive => "磁盘驱动器",
        ContextMenuLocation.AllFilesystemObjects => "文件系统对象",
        ContextMenuLocation.FileExtensions => "文件扩展 (COM)",
        ContextMenuLocation.DirectoryExtensions => "文件夹扩展 (COM)",
        ContextMenuLocation.DirectoryBackgroundExtensions => "文件夹背景扩展 (COM)",
        ContextMenuLocation.DesktopBackgroundExtensions => "桌面背景扩展 (COM)",
        ContextMenuLocation.FolderExtensions => "文件夹类扩展 (COM)",
        ContextMenuLocation.DriveExtensions => "驱动器扩展 (COM)",
        _ => location.ToString(),
    };
}
