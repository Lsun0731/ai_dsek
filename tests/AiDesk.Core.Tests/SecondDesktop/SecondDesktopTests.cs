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

public class TaskbarHiderTests
{
    [Fact]
    public void GetTaskbarHandle_能找到任务栏()
    {
        var hider = new TaskbarHider();
        // 只读检查：任务栏窗口存在（当前会话有 explorer 外壳）
        Assert.NotEqual(IntPtr.Zero, hider.GetTaskbarHandle());
    }
}

public class DesktopIconHiderTests
{
    [Fact]
    public void GetIconListHandle_能找到桌面图标窗口()
    {
        var hider = new DesktopIconHider();
        // 只读检查：桌面图标列表窗口（Progman→SHELLDLL_DefView）存在
        Assert.NotEqual(IntPtr.Zero, hider.GetIconListHandle());
    }
}
