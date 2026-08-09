namespace AiDesk.Core.ContextMenu;

/// <summary>一条推荐禁用的系统冗余菜单项定义。</summary>
/// <param name="Location">挂载位置。</param>
/// <param name="KeyName">注册表子键名（不带 "-" 前缀）。</param>
/// <param name="Description">中文说明（界面展示）。</param>
public sealed record RecommendedDisableItem(
    ContextMenuLocation Location,
    string KeyName,
    string Description);

/// <summary>
/// 推荐精简清单：Windows 系统自带的、绝大多数用户用不到的冗余右键菜单项。
/// 均为「禁用」（可逆），绝不删除；条目在不同系统版本可能不存在，执行时会自动跳过。
/// </summary>
public static class RecommendedDisableList
{
    public static IReadOnlyList<RecommendedDisableItem> All { get; } =
    [
        // —— 文件右键（*）——
        new(ContextMenuLocation.File, "3D Print", "3D 打印"),
        new(ContextMenuLocation.File, "3D Edit", "使用画图 3D 编辑"),
        new(ContextMenuLocation.File, "Enqueue", "使用 Windows Media Player 播放"),

        // —— 文件夹右键 ——
        new(ContextMenuLocation.Directory, "LibraryLocation", "包含到库中"),
        new(ContextMenuLocation.Folder, "LibraryLocation", "包含到库中"),

        // —— 共享（COM 扩展，多位置）——
        new(ContextMenuLocation.FileExtensions, "Sharing", "共享"),
        new(ContextMenuLocation.DirectoryExtensions, "Sharing", "共享"),
        new(ContextMenuLocation.FolderExtensions, "Sharing", "共享"),
        new(ContextMenuLocation.DirectoryBackgroundExtensions, "Sharing", "共享"),

        // —— 工作文件夹（COM 扩展，多位置）——
        new(ContextMenuLocation.FileExtensions, "WorkFolders", "工作文件夹"),
        new(ContextMenuLocation.DirectoryExtensions, "WorkFolders", "工作文件夹"),
        new(ContextMenuLocation.FolderExtensions, "WorkFolders", "工作文件夹"),
        new(ContextMenuLocation.DirectoryBackgroundExtensions, "WorkFolders", "工作文件夹"),

        // —— 磁盘驱动器（共享/工作文件夹也出现在驱动器右键）——
        new(ContextMenuLocation.DriveExtensions, "Sharing", "共享"),
        new(ContextMenuLocation.DriveExtensions, "WorkFolders", "工作文件夹"),
    ];
}
