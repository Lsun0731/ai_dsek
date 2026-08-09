namespace AiDesk.Core.ContextMenu;

/// <summary>
/// 右键菜单挂载位置（对应注册表 HKCR 中的标准路径）。
/// </summary>
public enum ContextMenuLocation
{
    /// <summary>任意文件的右键菜单：HKCR\*\shell</summary>
    File,

    /// <summary>文件夹本身的右键菜单：HKCR\Directory\shell</summary>
    Directory,

    /// <summary>文件夹空白处的右键菜单：HKCR\Directory\Background\shell</summary>
    DirectoryBackground,

    /// <summary>桌面空白处的右键菜单：HKCR\DesktopBackground\shell</summary>
    DesktopBackground,

    /// <summary>文件夹类对象的右键菜单：HKCR\Folder\shell</summary>
    Folder,

    /// <summary>磁盘驱动器右键菜单：HKCR\Drive\shell</summary>
    Drive,

    /// <summary>所有文件系统对象：HKCR\AllFilesystemObjects\shell</summary>
    AllFilesystemObjects,

    /// <summary>文件 COM 扩展：HKCR\*\shellex\ContextMenuHandlers</summary>
    FileExtensions,

    /// <summary>文件夹 COM 扩展：HKCR\Directory\shellex\ContextMenuHandlers</summary>
    DirectoryExtensions,

    /// <summary>文件夹背景 COM 扩展：HKCR\Directory\Background\shellex\ContextMenuHandlers</summary>
    DirectoryBackgroundExtensions,

    /// <summary>桌面背景 COM 扩展：HKCR\DesktopBackground\shellex\ContextMenuHandlers</summary>
    DesktopBackgroundExtensions,

    /// <summary>文件夹类 COM 扩展：HKCR\Folder\shellex\ContextMenuHandlers</summary>
    FolderExtensions,

    /// <summary>驱动器 COM 扩展：HKCR\Drive\shellex\ContextMenuHandlers</summary>
    DriveExtensions,
}
