using AiDesk.Core.Diagnostics;

namespace AiDesk.Core.Tests.Diagnostics;

public class TelemetryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logFile;

    public TelemetryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDesk-TelemetryTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Telemetry.SetLogDirectory(_tempDir);
        _logFile = Path.Combine(_tempDir, $"ai-desk-{DateTime.Now:yyyy-MM-dd}.log");
    }

    public void Dispose()
    {
        Telemetry.SetLogDirectory(string.Empty);
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    [Fact]
    public void Info_写入日志文件_格式正确()
    {
        Telemetry.Info("App", "应用启动测试");

        Assert.True(File.Exists(_logFile));
        var line = File.ReadAllLines(_logFile).Single();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO\] \[App\] 应用启动测试$", line);
    }

    [Fact]
    public void Event_写入事件_含类别与名称()
    {
        Telemetry.Event("Navigate", "外观美化");

        var line = File.ReadAllLines(_logFile).Single();
        Assert.Contains("[EVENT] [Navigate] 外观美化", line);
    }

    [Fact]
    public void Function_成功与失败_级别区分()
    {
        Telemetry.Function("Demo.Ok", true, 12, "count=3");
        Telemetry.Function("Demo.Fail", false, 5, "错误原因");

        var lines = File.ReadAllLines(_logFile);
        Assert.Contains(lines, l => l.Contains("[FUNC] [Demo.Ok] 成功 12ms count=3"));
        Assert.Contains(lines, l => l.Contains("[FUNC-FAIL] [Demo.Fail] 失败 5ms 错误原因"));
    }

    [Fact]
    public void Error_写入异常类型与堆栈()
    {
        // 真实场景的异常都是已抛出的（StackTrace 非空）；此处模拟抛出路径
        Exception thrown;
        try
        {
            throw new InvalidOperationException("测试异常");
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Telemetry.Error("Dispatcher", thrown);

        var content = File.ReadAllText(_logFile);
        Assert.Contains("[ERROR] [Dispatcher] InvalidOperationException: 测试异常", content);
        Assert.Contains("at ", content);
    }

    [Fact]
    public void Error_未抛出异常_不崩溃()
    {
        Telemetry.Error("Test", new InvalidOperationException("从未抛出"));

        var content = File.ReadAllText(_logFile);
        Assert.Contains("（无堆栈信息）", content);
    }

    [Fact]
    public async Task 多线程并发写入_不丢行()
    {
        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => Task.Run(() =>
            {
                for (var j = 0; j < 10; j++)
                    Telemetry.Event("Concurrent", $"task{i}-msg{j}");
            })));

        var lines = File.ReadAllLines(_logFile);
        Assert.Equal(200, lines.Length);
        Assert.Equal(200, lines.Distinct().Count());
    }
}
