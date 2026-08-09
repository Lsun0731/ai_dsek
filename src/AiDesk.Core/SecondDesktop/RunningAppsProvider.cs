using System.Diagnostics;

namespace AiDesk.Core.SecondDesktop;

/// <summary>一个正在运行且拥有可见主窗口的应用。</summary>
public sealed record RunningAppInfo
{
    public required int ProcessId { get; init; }
    public required string Title { get; init; }
    public required string ExecutableName { get; init; }
    public string? ExecutablePath { get; init; }
}

/// <summary>
/// 枚举当前正在运行、有可见主窗口的应用（第二桌面磁贴数据源）。
/// </summary>
public sealed class RunningAppsProvider
{
    /// <summary>排除的进程名（不含 .exe）。</summary>
    private static readonly string[] ExcludedNames =
    {
        "AiDesk.App",
        "explorer",      // 外壳本身（Program Manager）
        "SearchApp",     // 搜索宿主
        "TextInputHost", // 输入法宿主
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "sihost",
        "dllhost",
        "ApplicationFrameHost",
        "RuntimeBroker",
    };

    public IReadOnlyList<RunningAppInfo> Refresh()
    {
        var list = new List<RunningAppInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id <= 0 || process.HasExited)
                    continue;
                if (ExcludedNames.Contains(process.ProcessName))
                    continue;

                // 只保留有可见主窗口的进程
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;
                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                list.Add(new RunningAppInfo
                {
                    ProcessId = process.Id,
                    Title = title,
                    ExecutableName = process.ProcessName,
                    ExecutablePath = SafeGetPath(process),
                });
            }
            catch
            {
                // 进程可能在枚举期间退出，跳过
            }
        }

        return list.OrderBy(x => x.Title, StringComparer.CurrentCulture).ToList();
    }

    private static string? SafeGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null; // 权限不足（如系统进程）时无法读取路径
        }
    }
}
