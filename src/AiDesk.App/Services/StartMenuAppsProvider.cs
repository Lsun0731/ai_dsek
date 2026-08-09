using System.IO;

namespace AiDesk.App.Services;

/// <summary>已安装应用条目（开始菜单扫描结果）。</summary>
public sealed record StartMenuApp
{
    public required string Name { get; init; }
    public required string LnkPath { get; init; }
}

/// <summary>
/// 扫描开始菜单快捷方式，提供应用搜索数据源。
/// </summary>
public static class StartMenuAppsProvider
{
    private static readonly string[] Roots =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs)),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)),
    };

    /// <summary>收集所有 .lnk 快捷方式（递归）。</summary>
    public static IReadOnlyList<StartMenuApp> Scan()
    {
        var apps = new List<StartMenuApp>();
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root))
                continue;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    apps.Add(new StartMenuApp { Name = name, LnkPath = lnk });
                }
            }
            catch
            {
                // 个别目录不可读则跳过
            }
        }

        return apps
            .GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
