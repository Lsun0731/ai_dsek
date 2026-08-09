using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.Widgets;

namespace AiDesk.App.Services;

/// <summary>
/// Agent 系统类工具实现（partial）：进程、网络、磁盘、文件搜索、截图、音量、清理。
/// </summary>
public static partial class AgentTools
{
    private static readonly Lazy<SystemStatsProvider> Stats = new(() => new SystemStatsProvider());

    private static string Dispatch(string name, string argumentsJson) => name switch
    {
        "get_system_info" => GetSystemInfo(),
        "launch_app" => ExecuteLaunchApp(argumentsJson),
        "open_path" => ExecuteOpenPath(argumentsJson),
        "open_website" => ExecuteOpenWebsite(argumentsJson),
        "open_system_settings" => ExecuteOpenSettings(argumentsJson),
        "set_wallpaper" => ExecuteSetWallpaper(argumentsJson),
        "clipboard_write" => ExecuteClipboardWrite(argumentsJson),
        "list_processes" => ExecuteListProcesses(argumentsJson),
        "kill_process" => ExecuteKillProcess(argumentsJson),
        "disk_usage" => ExecuteDiskUsage(),
        "get_network_info" => ExecuteNetworkInfo(),
        "ping_host" => ExecutePing(argumentsJson),
        "search_files" => ExecuteSearchFiles(argumentsJson),
        "set_volume" => ExecuteSetVolume(argumentsJson),
        "toggle_mute" => ExecuteToggleMute(),
        "screenshot" => ExecuteScreenshot(),
        "clear_temp" => ExecuteClearTemp(),
        _ => $"未知工具: {name}",
    };

    private static string GetSystemInfo()
    {
        var stats = Stats.Value.Sample();
        var os = Environment.OSVersion.VersionString;
        var cpu = $"{stats.CpuPercent:F0}%";
        var mem = $"{stats.MemPercent:F0}% 已用（{stats.MemUsedGb:F1}/{stats.MemTotalGb:F1} GB）";
        var disks = string.Join("；", stats.Disks.Select(d => $"{d.Name} {d.Percent:F0}% 已用"));
        return $"操作系统: {os}；CPU 使用率: {cpu}；内存: {mem}；磁盘: {disks}";
    }

    // ---- 进程 ----

    private static string ExecuteListProcesses(string argumentsJson)
    {
        var filter = GetStringArg(argumentsJson, "filter")?.Trim();
        var processes = Process.GetProcesses()
            .Where(p =>
            {
                try { return !string.IsNullOrEmpty(p.ProcessName); }
                catch { return false; }
            })
            .Where(p => filter is null || p.ProcessName.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .GroupBy(p => p.ProcessName)
            .Select(g => new { Name = g.Key, MemMb = g.Sum(p => TryGetMemory(p)) })
            .OrderByDescending(x => x.MemMb)
            .Take(20)
            .ToList();

        if (processes.Count == 0)
            return filter is null ? "没有正在运行的进程" : $"没有匹配「{filter}」的进程";
        var lines = processes.Select(x => $"{x.Name}（{x.MemMb:F0} MB）");
        return filter is null
            ? $"内存占用最高的进程：{string.Join("、", lines)}"
            : $"匹配「{filter}」的进程：{string.Join("、", lines)}";
    }

    private static long TryGetMemory(Process p)
    {
        try { return p.WorkingSet64 / 1024 / 1024; }
        catch { return 0; }
    }

    private static string ExecuteKillProcess(string argumentsJson)
    {
        var name = GetStringArg(argumentsJson, "name")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return "缺少进程名称参数";

        var targets = Process.GetProcesses()
            .Where(p =>
            {
                try { return p.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            })
            .ToList();
        if (targets.Count == 0)
            return $"没有运行中的进程「{name}」";

        var killed = 0;
        foreach (var p in targets)
        {
            try
            {
                p.Kill();
                killed++;
            }
            catch
            {
                // 无权限等跳过
            }
        }
        return killed > 0 ? $"已结束 {killed} 个「{name}」进程" : $"结束「{name}」失败（可能无权限）";
    }

    // ---- 磁盘 / 网络 ----

    private static string ExecuteDiskUsage()
    {
        var parts = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d =>
            {
                var totalGb = d.TotalSize / 1024.0 / 1024 / 1024;
                var freeGb = d.TotalFreeSpace / 1024.0 / 1024 / 1024;
                return $"{d.Name.TrimEnd('\\')} 剩余 {freeGb:F0}/{totalGb:F0} GB（{freeGb / totalGb * 100:F0}% 可用）";
            })
            .ToList();
        return parts.Count == 0 ? "未检测到磁盘" : string.Join("；", parts);
    }

    private static string ExecuteNetworkInfo()
    {
        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n =>
            {
                var ip = n.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? "-";
                return $"{n.Name}（{ip}）";
            })
            .ToList();
        var internet = NetworkInterface.GetIsNetworkAvailable() ? "已联网" : "未联网";
        return nics.Count == 0
            ? $"网络状态：{internet}，无活动网卡"
            : $"网络状态：{internet}；网卡：{string.Join("、", nics)}";
    }

    private static string ExecutePing(string argumentsJson)
    {
        var host = GetStringArg(argumentsJson, "host")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(host))
            return "缺少 host 参数";

        using var ping = new Ping();
        var times = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            try
            {
                var reply = ping.Send(host, 3000);
                if (reply.Status == IPStatus.Success)
                    times.Add(reply.RoundtripTime);
            }
            catch
            {
                // 继续
            }
        }
        if (times.Count == 0)
            return $"ping {host} 失败（无法连通或超时）";
        var avg = times.Average();
        return $"ping {host} 平均延迟 {avg:F0} ms（{times.Count}/3 成功）";
    }

    // ---- 文件搜索 ----

    private static string ExecuteSearchFiles(string argumentsJson)
    {
        var keyword = GetStringArg(argumentsJson, "keyword")?.Trim() ?? "";
        var folder = GetStringArg(argumentsJson, "folder")?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
            return "缺少 keyword 参数";

        if (string.IsNullOrWhiteSpace(folder))
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            folder = Path.Combine(user, "Desktop");
        }
        if (!Directory.Exists(folder))
            return $"目录不存在：{folder}";

        var hits = new List<string>();
        try
        {
            // 当前目录 + 一层子目录，避免全盘扫描过慢
            hits.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetFileName(f).Contains(keyword, StringComparison.CurrentCultureIgnoreCase)));
            foreach (var sub in Directory.EnumerateDirectories(folder).Take(30))
            {
                try
                {
                    hits.AddRange(Directory.EnumerateFiles(sub, "*", SearchOption.TopDirectoryOnly)
                        .Where(f => Path.GetFileName(f).Contains(keyword, StringComparison.CurrentCultureIgnoreCase)));
                }
                catch
                {
                    // 无权限目录跳过
                }
            }
        }
        catch (Exception ex)
        {
            return $"搜索失败: {ex.Message}";
        }

        if (hits.Count == 0)
            return $"在 {folder} 下未找到包含「{keyword}」的文件";
        var shown = string.Join("；", hits.Take(10).Select(Path.GetFileName));
        return hits.Count > 10
            ? $"找到 {hits.Count} 个文件（前 10 个：{shown}）"
            : $"找到 {hits.Count} 个文件：{shown}";
    }

    // ---- 音量 ----

    private static string ExecuteSetVolume(string argumentsJson)
    {
        var level = GetNumberArg(argumentsJson, "level");
        if (level < 0 || level > 100)
            return "音量参数需在 0-100 之间";
        return CoreAudioVolume.SetVolume(level)
            ? $"音量已设为 {level:F0}%"
            : "设置音量失败（无音频设备或接口不可用）";
    }

    private static string ExecuteToggleMute()
    {
        var muted = CoreAudioVolume.ToggleMute();
        return muted ? "已静音" : "已取消静音（或操作失败）";
    }

    // ---- 截图 ----

    private static string ExecuteScreenshot()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? throw new InvalidOperationException("无法获取屏幕信息");
        var saveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AiDesk");
        Directory.CreateDirectory(saveDir);
        var file = Path.Combine(saveDir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
        }
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        return $"截图已保存：{file}";
    }

    // ---- 清理临时文件（危险，需确认） ----

    private static string ExecuteClearTemp()
    {
        var temp = Path.GetTempPath();
        long freedBytes = 0;
        var removed = 0;
        var failed = 0;

        foreach (var file in EnumerateFilesSafe(temp))
        {
            try
            {
                var info = new FileInfo(file);
                var size = info.Length;
                File.Delete(file);
                freedBytes += size;
                removed++;
            }
            catch
            {
                failed++; // 被占用文件跳过
            }
        }
        foreach (var dir in EnumerateDirsSafe(temp))
        {
            try
            {
                Directory.Delete(dir, recursive: false);
            }
            catch
            {
                // 非空目录跳过
            }
        }

        return removed > 0
            ? $"已清理 {removed} 个临时文件，释放约 {freedBytes / 1024.0 / 1024:F0} MB（{failed} 个文件被占用跳过）"
            : "临时目录没有可清理的文件";
    }

    private static IEnumerable<string> EnumerateFilesSafe(string dir)
    {
        try { return Directory.EnumerateFiles(dir); }
        catch { return []; }
    }

    private static IEnumerable<string> EnumerateDirsSafe(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return []; }
    }
}
