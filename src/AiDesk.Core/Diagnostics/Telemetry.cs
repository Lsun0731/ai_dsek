using System.IO;
using System.Text;

namespace AiDesk.Core.Diagnostics;

/// <summary>
/// 轻量埋点组件：把用户操作、功能结果与异常写入按天分割的日志文件，便于事后定位问题。
/// 日志目录：%LOCALAPPDATA%\AiDesk\logs\ai-desk-YYYY-MM-DD.log（测试可注入临时目录）。
/// 线程安全；格式为「时间 [级别] [类别] 消息」，便于 grep。
/// </summary>
public static class Telemetry
{
    private static readonly object Sync = new();

    private static string? _logDirOverride;

    /// <summary>测试用：覆盖日志目录。</summary>
    public static void SetLogDirectory(string path) => _logDirOverride = path;

    public static string LogDirectory =>
        _logDirOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDesk", "logs");

    /// <summary>记录一次操作事件（如页面导航）。</summary>
    public static void Event(string category, string name, string? detail = null) =>
        Write("EVENT", $"[{category}] {name}{(detail is null ? "" : $" {detail}")}");

    /// <summary>记录一次功能调用结果（成功/失败 + 耗时 + 关键参数）。</summary>
    public static void Function(string name, bool success, long elapsedMs, string? detail = null) =>
        Write(success ? "FUNC" : "FUNC-FAIL",
            $"[{name}] {(success ? "成功" : "失败")} {elapsedMs}ms{(detail is null ? "" : $" {detail}")}");

    /// <summary>记录异常（含堆栈）。</summary>
    public static void Error(string source, Exception ex) =>
        Write("ERROR", $"[{source}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace ?? "（无堆栈信息）"}");

    /// <summary>记录普通信息（应用启动/退出等）。</summary>
    public static void Info(string category, string message) =>
        Write("INFO", $"[{category}] {message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                var dir = LogDirectory;
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"ai-desk-{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(file, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 埋点失败绝不影响主流程
        }
    }
}
