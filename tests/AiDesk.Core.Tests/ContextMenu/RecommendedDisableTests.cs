using AiDesk.Core.ContextMenu;

namespace AiDesk.Core.Tests.ContextMenu;

public class RecommendedDisableListTests
{
    [Fact]
    public void All_清单非空且无重复项()
    {
        var all = RecommendedDisableList.All;

        Assert.NotEmpty(all);
        var duplicates = all
            .GroupBy(i => (i.Location, i.KeyName))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void All_每个条目都有中文说明()
    {
        foreach (var item in RecommendedDisableList.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.False(string.IsNullOrWhiteSpace(item.KeyName));
            Assert.DoesNotContain("-", item.KeyName);
        }
    }

    [Fact]
    public void All_键名均可映射到注册表路径()
    {
        foreach (var item in RecommendedDisableList.All)
        {
            var path = ContextMenuPathBuilder.GetPath(item.Location);
            Assert.False(string.IsNullOrWhiteSpace(path));
        }
    }

    [Fact]
    public void All_禁止包含删除风险项()
    {
        // 这些是系统必需/常用项，绝不能出现在精简清单里（位置, 键名）
        var dangerous = new[]
        {
            (ContextMenuLocation.File, "Open"),
            (ContextMenuLocation.Directory, "Open"),
            (ContextMenuLocation.File, "OpenWith"),
            (ContextMenuLocation.Directory, "OpenWith"),
            (ContextMenuLocation.Directory, "New"),
            (ContextMenuLocation.Directory, "SendTo"),
            (ContextMenuLocation.Directory, "Properties"),
            (ContextMenuLocation.File, "Print"),
        };

        foreach (var (location, key) in dangerous)
        {
            Assert.DoesNotContain(RecommendedDisableList.All,
                i => i.Location == location && i.KeyName == key);
        }
    }
}

public class ContextMenuServiceRecommendedTests
{
    [Fact]
    public void FindRecommended_真实系统_返回的项均来自清单且未禁用()
    {
        var service = new ContextMenuService();
        var matched = service.FindRecommended(RecommendedDisableList.All);

        var matchedKeys = matched.Select(m => m.Definition.KeyName).ToHashSet();
        Assert.All(matched, m =>
        {
            Assert.Contains(m.Definition.KeyName, matchedKeys);
            Assert.False(m.Item.IsDisabled);
            Assert.Equal(m.Definition.Location, m.Item.Location);
            Assert.Equal(m.Definition.KeyName, m.Item.RawKeyName);
        });
    }
}
