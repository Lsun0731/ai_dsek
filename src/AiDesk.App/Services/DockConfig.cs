using System.IO;
using System.Text.Json;

namespace AiDesk.App.Services;

/// <summary>桌面 Dock 集成设置。</summary>
public sealed class DockSettings
{
    /// <summary>启用桌面 Dock（磁贴条）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>隐藏系统任务栏（默认隐藏，用 Dock 替代）。</summary>
    public bool HideTaskbar { get; set; } = true;

    /// <summary>隐藏桌面图标。</summary>
    public bool HideIcons { get; set; }
}

/// <summary>桌面 Dock 配置持久化（%LOCALAPPDATA%\AiDesk\dock.json）。</summary>
public static class DockConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiDesk", "dock.json");

    public static DockSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<DockSettings>(json);
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // 配置损坏时使用默认值
        }
        return new DockSettings();
    }

    public static void Save(DockSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}
