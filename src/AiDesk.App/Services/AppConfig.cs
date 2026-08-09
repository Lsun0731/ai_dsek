using System.IO;
using System.Text.Json;

namespace AiDesk.App.Services;

/// <summary>
/// 应用统一配置持久化（%LOCALAPPDATA%\AiDesk\settings.json）。
/// 首次启动自动迁移旧配置（config.json 小组件）到 settings.json。
/// </summary>
public static class AppConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDesk");

    /// <summary>数据目录（%LOCALAPPDATA%\AiDesk），会话历史等附属文件存放于此。</summary>
    public static string DataDirectory => ConfigDir;

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    /// <summary>旧小组件配置路径。</summary>
    private static readonly string LegacyWidgetPath = Path.Combine(ConfigDir, "config.json");

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

        // 迁移旧配置（首次从 config.json 合并）
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

        Save(settings); // 迁移完成后写主配置
        return settings;
    }

    private sealed class LegacyWidgetSettings
    {
        public double Opacity { get; set; } = 0.9;
        public string WeatherCity { get; set; } = "北京";
        public Dictionary<string, WidgetState>? Widgets { get; set; }
    }
}
