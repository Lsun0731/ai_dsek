using AiDesk.Core.ContextMenu;

namespace AiDesk.Core.Tests.ContextMenu;

public class ContextMenuPathBuilderTests
{
    [Theory]
    [InlineData(ContextMenuLocation.File, @"*\shell")]
    [InlineData(ContextMenuLocation.Directory, @"Directory\shell")]
    [InlineData(ContextMenuLocation.DirectoryBackground, @"Directory\Background\shell")]
    [InlineData(ContextMenuLocation.DesktopBackground, @"DesktopBackground\shell")]
    [InlineData(ContextMenuLocation.Folder, @"Folder\shell")]
    [InlineData(ContextMenuLocation.Drive, @"Drive\shell")]
    [InlineData(ContextMenuLocation.AllFilesystemObjects, @"AllFilesystemObjects\shell")]
    [InlineData(ContextMenuLocation.FileExtensions, @"*\shellex\ContextMenuHandlers")]
    [InlineData(ContextMenuLocation.DirectoryExtensions, @"Directory\shellex\ContextMenuHandlers")]
    [InlineData(ContextMenuLocation.DirectoryBackgroundExtensions, @"Directory\Background\shellex\ContextMenuHandlers")]
    [InlineData(ContextMenuLocation.DesktopBackgroundExtensions, @"DesktopBackground\shellex\ContextMenuHandlers")]
    [InlineData(ContextMenuLocation.FolderExtensions, @"Folder\shellex\ContextMenuHandlers")]
    [InlineData(ContextMenuLocation.DriveExtensions, @"Drive\shellex\ContextMenuHandlers")]
    public void GetPath_所有位置_返回正确注册表路径(ContextMenuLocation location, string expected)
    {
        Assert.Equal(expected, ContextMenuPathBuilder.GetPath(location));
    }

    [Fact]
    public void GetItemPath_拼接完整键路径()
    {
        Assert.Equal(
            @"HKEY_CLASSES_ROOT\*\shell\MyTool",
            ContextMenuPathBuilder.GetItemPath(ContextMenuLocation.File, "MyTool"));
    }

    [Fact]
    public void GetPath_未知枚举值_抛出异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextMenuPathBuilder.GetPath((ContextMenuLocation)999));
    }
}

public class ContextMenuItemTests
{
    [Fact]
    public void IsDisabled_名称带前缀_返回True()
    {
        var item = new ContextMenuItem
        {
            Name = "-OpenWith",
            RawKeyName = "-OpenWith",
            IsExtension = false,
            Location = ContextMenuLocation.File,
            RegistryPath = @"HKEY_CLASSES_ROOT\*\shell\-OpenWith",
        };
        Assert.True(item.IsDisabled);
    }

    [Fact]
    public void IsDisabled_普通名称_返回False()
    {
        var item = new ContextMenuItem
        {
            Name = "OpenWith",
            RawKeyName = "OpenWith",
            IsExtension = false,
            Location = ContextMenuLocation.File,
            RegistryPath = @"HKEY_CLASSES_ROOT\*\shell\OpenWith",
        };
        Assert.False(item.IsDisabled);
    }

    [Fact]
    public void LocationDisplay_中文展示()
    {
        Assert.Equal("文件", ContextMenuItem.GetLocationDisplay(ContextMenuLocation.File));
        Assert.Equal("桌面背景", ContextMenuItem.GetLocationDisplay(ContextMenuLocation.DesktopBackground));
    }
}
