using System.Drawing;
using AiDesk.Core.Desktop;

namespace AiDesk.Core.Tests.Desktop;

public class WallpaperStyleMapperTests
{
    [Theory]
    [InlineData(WallpaperStyle.Fill, "10", "0")]
    [InlineData(WallpaperStyle.Fit, "6", "0")]
    [InlineData(WallpaperStyle.Stretch, "2", "0")]
    [InlineData(WallpaperStyle.Tile, "0", "1")]
    [InlineData(WallpaperStyle.Center, "0", "0")]
    [InlineData(WallpaperStyle.Span, "22", "0")]
    public void ToRegistryValues_各样式_返回正确注册表值(WallpaperStyle style, string expectedStyle, string expectedTile)
    {
        var (styleValue, tileValue) = WallpaperStyleMapper.ToRegistryValues(style);
        Assert.Equal(expectedStyle, styleValue);
        Assert.Equal(expectedTile, tileValue);
    }

    [Fact]
    public void ToRegistryValues_未知值_抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WallpaperStyleMapper.ToRegistryValues((WallpaperStyle)999));
    }

    [Fact]
    public void DisplayName_全部有中文名()
    {
        foreach (var style in Enum.GetValues<WallpaperStyle>())
            Assert.False(string.IsNullOrWhiteSpace(WallpaperStyleMapper.DisplayName(style)));
    }
}

public class AccentColorConverterTests
{
    [Fact]
    public void ToDword_已知颜色_返回ABGR格式()
    {
        // 纯红 #FF0000 → ABGR: A=0, B=0, G=0, R=255 → 0x000000FF
        Assert.Equal(0x000000FFu, AccentColorConverter.ToDword(Color.FromArgb(255, 0, 0)));
        // 纯蓝 #0000FF → ABGR: B=255 → 0x00FF0000
        Assert.Equal(0x00FF0000u, AccentColorConverter.ToDword(Color.FromArgb(0, 0, 255)));
        // 绿 #00FF00 → 0x0000FF00
        Assert.Equal(0x0000FF00u, AccentColorConverter.ToDword(Color.FromArgb(0, 255, 0)));
    }

    [Fact]
    public void FromDword_与ToDword_往返一致()
    {
        var colors = new[]
        {
            Color.FromArgb(255, 73, 124, 243),   // 品牌蓝 #497CF3
            Color.FromArgb(255, 0, 0, 0),
            Color.FromArgb(255, 255, 255, 255),
            Color.FromArgb(255, 229, 72, 77),
        };
        foreach (var color in colors)
        {
            var roundTrip = AccentColorConverter.FromDword(AccentColorConverter.ToDword(color));
            Assert.Equal(color.ToArgb(), roundTrip.ToArgb());
        }
    }
}

public class WallpaperServiceTests
{
    [Fact]
    public void SetWallpaper_空路径_抛参数异常()
    {
        var service = new WallpaperService();
        Assert.Throws<ArgumentException>(() => service.SetWallpaper("", WallpaperStyle.Fill));
        service.Dispose();
    }

    [Fact]
    public void SetWallpaper_文件不存在_抛文件未找到()
    {
        var service = new WallpaperService();
        Assert.Throws<FileNotFoundException>(() =>
            service.SetWallpaper(@"C:\nonexistent\wallpaper.png", WallpaperStyle.Fill));
        service.Dispose();
    }

    [Fact]
    public void StartSlideshow_空列表_抛参数异常()
    {
        var service = new WallpaperService();
        Assert.Throws<ArgumentException>(() =>
            service.StartSlideshow([], TimeSpan.FromMinutes(1), WallpaperStyle.Fill, false, false));
        service.Dispose();
    }

    [Fact]
    public void StartSlideshow_间隔为零_抛参数异常()
    {
        var service = new WallpaperService();
        Assert.Throws<ArgumentException>(() =>
            service.StartSlideshow(["a.jpg"], TimeSpan.Zero, WallpaperStyle.Fill, false, false));
        service.Dispose();
    }

    [Fact]
    public void GetCurrentWallpaper_真实系统_返回路径或空_不抛异常()
    {
        var service = new WallpaperService();
        var path = service.GetCurrentWallpaper();
        Assert.True(path is null || path.Length > 0);
        service.Dispose();
    }
}

public class AppearanceServiceTests
{
    [Fact]
    public void IsDarkTheme_真实系统_返回状态或空_不抛异常()
    {
        var service = new AppearanceService();
        var dark = service.IsDarkTheme();
        Assert.True(dark is null || dark == true || dark == false);
    }

    [Fact]
    public void IsTransparencyEnabled_真实系统_返回状态或空_不抛异常()
    {
        var service = new AppearanceService();
        var enabled = service.IsTransparencyEnabled();
        Assert.True(enabled is null || enabled == true || enabled == false);
    }

    [Fact]
    public void GetAccentColor_真实系统_返回颜色或空_不抛异常()
    {
        var service = new AppearanceService();
        var color = service.GetAccentColor();
        Assert.True(color is null || color.Value.A == 255);
    }
}
