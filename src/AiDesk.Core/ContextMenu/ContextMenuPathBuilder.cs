namespace AiDesk.Core.ContextMenu;

/// <summary>
/// 将 <see cref="ContextMenuLocation"/> 映射为注册表路径（纯函数，便于单元测试）。
/// </summary>
public static class ContextMenuPathBuilder
{
    public static string GetPath(ContextMenuLocation location) => location switch
    {
        ContextMenuLocation.File => @"*\shell",
        ContextMenuLocation.Directory => @"Directory\shell",
        ContextMenuLocation.DirectoryBackground => @"Directory\Background\shell",
        ContextMenuLocation.DesktopBackground => @"DesktopBackground\shell",
        ContextMenuLocation.Folder => @"Folder\shell",
        ContextMenuLocation.Drive => @"Drive\shell",
        ContextMenuLocation.AllFilesystemObjects => @"AllFilesystemObjects\shell",
        ContextMenuLocation.FileExtensions => @"*\shellex\ContextMenuHandlers",
        ContextMenuLocation.DirectoryExtensions => @"Directory\shellex\ContextMenuHandlers",
        ContextMenuLocation.DirectoryBackgroundExtensions => @"Directory\Background\shellex\ContextMenuHandlers",
        ContextMenuLocation.DesktopBackgroundExtensions => @"DesktopBackground\shellex\ContextMenuHandlers",
        ContextMenuLocation.FolderExtensions => @"Folder\shellex\ContextMenuHandlers",
        ContextMenuLocation.DriveExtensions => @"Drive\shellex\ContextMenuHandlers",
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, "未知的右键菜单位置"),
    };

    /// <summary>
    /// 该项在注册表中的完整键路径（HKCR 根下）。
    /// </summary>
    public static string GetItemPath(ContextMenuLocation location, string itemName) =>
        $@"HKEY_CLASSES_ROOT\{GetPath(location)}\{itemName}";
}
