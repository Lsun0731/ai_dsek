using AiDesk.Core.Cursors;

namespace AiDesk.Core.Tests.Cursors;

public class CursorThemeServiceTests
{
    [Fact]
    public void GetSchemes_真实系统_返回列表且无空名()
    {
        var service = new CursorThemeService();
        var schemes = service.GetSchemes();

        // 无自定义方案的机器返回空列表（Schemes 键不存在），但绝不抛异常
        Assert.All(schemes, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(schemes.Count, schemes.Distinct().Count());
    }

    [Fact]
    public void GetSchemes_方案名_非空且去重()
    {
        var service = new CursorThemeService();
        var schemes = service.GetSchemes();

        Assert.All(schemes, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(schemes.Count, schemes.Distinct().Count());
    }

    [Fact]
    public void GetCurrentScheme_真实系统_返回方案名或空_不抛异常()
    {
        var service = new CursorThemeService();
        var current = service.GetCurrentScheme();

        Assert.True(current is null || current.Length > 0);
    }

    [Fact]
    public void ApplyScheme_不存在的方案_返回false()
    {
        var service = new CursorThemeService();
        // 只验证查找逻辑：不存在的方案名必然返回 false（不会触碰注册表写入）
        Assert.False(service.ApplyScheme($"__不存在的方案__{Guid.NewGuid():N}"));
    }
}
