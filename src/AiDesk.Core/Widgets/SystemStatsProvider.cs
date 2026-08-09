using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AiDesk.Core.Widgets;

/// <summary>系统状态快照。</summary>
public sealed record SystemStats(
    double CpuPercent,
    double MemPercent,
    double MemUsedGb,
    double MemTotalGb,
    IReadOnlyList<DiskStat> Disks,
    double DownKbPerSec,
    double UpKbPerSec);

/// <summary>单个磁盘状态。</summary>
public sealed record DiskStat(string Name, double UsedGb, double TotalGb, double Percent);

/// <summary>
/// 系统状态采样：CPU / 内存 / 磁盘 / 网络速度。
/// CPU 与网络基于 PerformanceCounter（需要预热采样），内存用 GlobalMemoryStatusEx（精确）。
/// </summary>
public sealed class SystemStatsProvider : IDisposable
{
    private PerformanceCounter? _cpu;
    private PerformanceCounter? _netDown;
    private PerformanceCounter? _netUp;
    private string? _netInstance;

    public SystemStatsProvider()
    {
        try
        {
            _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpu.NextValue(); // 预热：首次为 0

            _netInstance = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                     n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                ?.Name;
            if (_netInstance is not null)
            {
                _netDown = new PerformanceCounter("Network Interface", "Bytes Received/sec", _netInstance);
                _netUp = new PerformanceCounter("Network Interface", "Bytes Sent/sec", _netInstance);
                _netDown.NextValue();
                _netUp.NextValue();
            }
        }
        catch
        {
            // 计数器不可用时相关项显示 --%
        }
    }

    /// <summary>采样一次系统状态。</summary>
    public SystemStats Sample()
    {
        var cpu = ReadCpu();
        var (memPercent, usedGb, totalGb) = ReadMemory();
        var disks = ReadDisks();
        var (down, up) = ReadNetworkKbPerSec();
        return new SystemStats(cpu, memPercent, usedGb, totalGb, disks, down, up);
    }

    private double ReadCpu()
    {
        try
        {
            return _cpu is null ? 0 : Math.Clamp(_cpu.NextValue(), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private static (double Percent, double UsedGb, double TotalGb) ReadMemory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(status))
        {
            var totalGb = status.ullTotalPhys / (1024.0 * 1024 * 1024);
            var usedGb = (status.ullTotalPhys - status.ullAvailPhys) / (1024.0 * 1024 * 1024);
            return (status.dwMemoryLoad, usedGb, totalGb);
        }
        return (0, 0, 0);
    }

    private static IReadOnlyList<DiskStat> ReadDisks()
    {
        var list = new List<DiskStat>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var total = drive.TotalSize / (1024.0 * 1024 * 1024);
                var used = (drive.TotalSize - drive.AvailableFreeSpace) / (1024.0 * 1024 * 1024);
                list.Add(new DiskStat(drive.Name.TrimEnd('\\'), used, total,
                    total <= 0 ? 0 : used / total * 100));
            }
        }
        catch
        {
            // 磁盘读取失败跳过
        }
        return list;
    }

    private (double Down, double Up) ReadNetworkKbPerSec()
    {
        try
        {
            var down = _netDown is null ? 0 : Math.Max(0, _netDown.NextValue()) / 1024.0;
            var up = _netUp is null ? 0 : Math.Max(0, _netUp.NextValue()) / 1024.0;
            return (down, up);
        }
        catch
        {
            return (0, 0);
        }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _netDown?.Dispose();
        _netUp?.Dispose();
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);
}
