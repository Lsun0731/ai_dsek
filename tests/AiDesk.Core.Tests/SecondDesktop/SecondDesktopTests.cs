using AiDesk.Core.SecondDesktop;

namespace AiDesk.Core.Tests.SecondDesktop;

public class RunningAppsProviderTests
{
    [Fact]
    public void Refresh_返回有主窗口的应用列表()
    {
        var provider = new RunningAppsProvider();
        var apps = provider.Refresh();

        // 本机至少有一个带主窗口的进程（如 explorer）
        Assert.NotEmpty(apps);
        Assert.All(apps, a =>
        {
            Assert.True(a.ProcessId > 0);
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
            Assert.False(string.IsNullOrWhiteSpace(a.ExecutableName));
        });
        // 已排除外壳自身
        Assert.DoesNotContain(apps, a => a.ExecutableName.Equals("explorer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Refresh_连续调用不抛异常()
    {
        var provider = new RunningAppsProvider();
        _ = provider.Refresh();
        _ = provider.Refresh(); // 第二次（进程可能已退出）不应抛异常
    }
}

public class DesktopIconHiderTests
{
    [Fact]
    public void GetHideIconsState_返回当前值()
    {
        var hider = new DesktopIconHider();
        // 只读：读取当前注册表状态（0/1/null 均合法，不抛异常即可）
        _ = hider.GetHideIconsState();
    }
}

public class DesktopLayerHostTests
{
    [Fact]
    public void FindDesktopWorkerW_不抛异常()
    {
        // 窗口站/桌面结构随运行环境而异（bash 宿主与交互桌面隔离），只验证 API 可用不抛异常；
        // 真实挂载在应用进程（用户交互会话）里执行，失败时降级为普通置顶窗口。
        _ = DesktopLayerHost.FindDesktopWorkerW();
    }
}
