using AiDesk.Core.AI;

namespace AiDesk.Core.Tests.AI;

public class TextSanitizerTests
{
    [Fact]
    public void Sanitize_删除emoji与符号()
    {
        var result = TextSanitizer.Sanitize("好的，马上帮你打开 😊 计算器 ★ 完成！");
        Assert.DoesNotContain("😊", result);
        Assert.DoesNotContain("★", result);
        Assert.Contains("好的，马上帮你打开", result);
        Assert.Contains("计算器", result);
        Assert.Contains("完成！", result);
    }

    [Fact]
    public void Sanitize_删除代理对emoji()
    {
        var result = TextSanitizer.Sanitize("已完成 🎉🎊 任务");
        Assert.DoesNotContain("🎉", result);
        Assert.DoesNotContain("🎊", result);
        Assert.Contains("已完成", result);
        Assert.Contains("任务", result);
    }

    [Fact]
    public void Sanitize_删除ZWJ序列emoji()
    {
        var result = TextSanitizer.Sanitize("👨‍💻 正在处理");
        Assert.DoesNotContain("👨", result);
        Assert.DoesNotContain("💻", result);
        Assert.Contains("正在处理", result);
    }

    [Fact]
    public void Sanitize_删除ASCII颜文字()
    {
        var result = TextSanitizer.Sanitize("好的 :-) 没问题 ^_^");
        Assert.DoesNotContain(":-)", result);
        Assert.DoesNotContain("^_^", result);
        Assert.Contains("好的", result);
    }

    [Fact]
    public void Sanitize_保留中文标点与字母数字()
    {
        var result = TextSanitizer.Sanitize("CPU 使用率 12.5%，内存 8 GB，完全正常！");
        Assert.Equal("CPU 使用率 12.5%，内存 8 GB，完全正常！", result);
    }

    [Fact]
    public void Sanitize_空输入安全()
    {
        Assert.Equal("", TextSanitizer.Sanitize(null));
        Assert.Equal("", TextSanitizer.Sanitize("  "));
    }
}
