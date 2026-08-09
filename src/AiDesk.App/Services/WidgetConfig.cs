using System.IO;
using System.Text.Json;

namespace AiDesk.App.Services;

/// <summary>小组件设置。</summary>
public sealed class WidgetSettings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Opacity { get; set; } = 0.85;
    public bool IsWidgetOpen { get; set; }
}

/// <summary>小组件配置持久化（%LOCALAPPDATA%\AiDesk\config.json）。</summary>
public static class WidgetConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiDesk", "config.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<WidgetSettings>(json);
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // 配置损坏时使用默认值
        }
        return new WidgetSettings();
    }

    public static void Save(WidgetSettings settings)
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
