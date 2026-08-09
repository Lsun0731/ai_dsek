using System.IO;
using System.Text.Json;

namespace AiDesk.App.Services;

/// <summary>
/// 应用统一配置持久化（%LOCALAPPDATA%\AiDesk\settings.json）。
/// 首次启动自动迁移旧配置：config.json（小组件）+ dock.json（Dock）合并到 settings.json。
/// </summary>
public static class AppConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDesk");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    /// <summary>旧小组件配置路径。</summary>
    private static readonly string LegacyWidgetPath = Path.Combine(ConfigDir, "config.json");

    /// <summary>旧 Dock 配置路径。</summary>
    private static readonly string LegacyDockPath = Path.Combine(ConfigDir, "dock.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // 主配置损坏时尝试从旧配置迁移
        }

        // 迁移旧配置（首次从 config.json/dock.json 合并）
        return MigrateFromLegacy();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json);
            try
            {
                File.Replace(tmp, ConfigPath, null); // 原子替换，崩溃不损坏主配置
            }
            catch (FileNotFoundException)
            {
                File.Move(tmp, ConfigPath); // 首次保存（主配置不存在）
            }
        }
        catch
        {
            // 保存失败不影响运行（临时文件残留由下次保存覆盖）
        }
    }

    private static AppSettings MigrateFromLegacy()
    {
        var settings = new AppSettings();

        // 旧小组件配置：Opacity / WeatherCity / Widgets
        try
        {
            if (File.Exists(LegacyWidgetPath))
            {
                var json = File.ReadAllText(LegacyWidgetPath);
                var legacy = JsonSerializer.Deserialize<LegacyWidgetSettings>(json);
                if (legacy is not null)
                {
                    settings.Opacity = legacy.Opacity;
                    settings.WeatherCity = legacy.WeatherCity;
                    settings.Widgets = legacy.Widgets ?? new();
                }
            }
        }
        catch
        {
            // 忽略损坏的旧配置
        }

        // 旧 Dock 配置：Enabled / HideTaskbar / HideIcons
        try
        {
            if (File.Exists(LegacyDockPath))
            {
                var json = File.ReadAllText(LegacyDockPath);
                var legacy = JsonSerializer.Deserialize<LegacyDockSettings>(json);
                if (legacy is not null)
                {
                    settings.Dock.Enabled = legacy.Enabled;
                    settings.Dock.HideTaskbar = legacy.HideTaskbar;
                    settings.Dock.HideIcons = legacy.HideIcons;
                }
            }
        }
        catch
        {
            // 忽略损坏的旧配置
        }

        Save(settings); // 迁移完成后写主配置
        return settings;
    }

    private sealed class LegacyWidgetSettings
    {
        public double Opacity { get; set; } = 0.9;
        public string WeatherCity { get; set; } = "北京";
        public Dictionary<string, WidgetState>? Widgets { get; set; }
    }

    private sealed class LegacyDockSettings
    {
        public bool Enabled { get; set; } = true;
        public bool HideTaskbar { get; set; } = true;
        public bool HideIcons { get; set; }
    }
}
