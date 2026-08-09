using AiDesk.Core.ContextMenu;

namespace AiDesk.Core.Tests.ContextMenu;

/// <summary>
/// 真实注册表只读枚举测试：Windows 上 HKCR 必然存在系统右键菜单项，
/// 验证服务可完整枚举且不抛异常。写操作（禁用/删除）不做自动化测试，手工验证。
/// </summary>
public class ContextMenuServiceTests
{
    [Fact]
    public void Enumerate_真实系统_返回非空列表()
    {
        var service = new ContextMenuService();
        var items = service.Enumerate();

        Assert.NotEmpty(items);
    }

    [Fact]
    public void Enumerate_真实系统_包含shell项与扩展项()
    {
        var service = new ContextMenuService();
        var items = service.Enumerate();

        Assert.Contains(items, i => !i.IsExtension);
        Assert.Contains(items, i => i.IsExtension);
    }

    [Fact]
    public void Enumerate_所有项_注册表路径完整()
    {
        var service = new ContextMenuService();
        var items = service.Enumerate();

        foreach (var item in items)
        {
            Assert.StartsWith(@"HKEY_CLASSES_ROOT\", item.RegistryPath);
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
        }
    }
}
