using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AiDesk.Core.AI;

/// <summary>
/// 纯文本净化器：删除 emoji、装饰符号、颜文字等非文字内容，保证 AI 回复为纯文字。
/// </summary>
public static class TextSanitizer
{
    // ASCII 颜文字（:-) :( :D ;) :P ^_^ T_T 等）
    private static readonly Regex AsciiEmoticons = new(
        @"[:;=8xX]\s*[-oO'`]?[)DdPp/\\|>\]\[(（\{]{1,2}|" +
        @"\^[_oO.]\^|[Tt]_[Tt]|\(-_-\)|\(-o-\)|o\(-_-\)o",
        RegexOptions.Compiled);

    // 变体选择符 / 组合序列（FE0F 表情变体、200D ZWJ、20E3 键帽；emoji 主体由逐字符 Surrogate/So 过滤兜底）
    private static readonly Regex CombiningMarks = new(
        @"[\uFE0E\uFE0F\u200D\u20E3]",
        RegexOptions.Compiled);

    /// <summary>
    /// 净化文本：删除 emoji（代理对）、装饰符号（So 类别）、颜文字与变体选择符。
    /// 保留中文/字母/数字/标点（含 ！？、。等）。
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var result = text;
        // 1) 常见 emoji 区块（含 ZWJ 序列与变体）
        result = CombiningMarks.Replace(result, "");
        // 2) ASCII 颜文字
        result = AsciiEmoticons.Replace(result, "");
        // 3) 逐字符：删代理对（emoji 大多落在代理区）与 So（其他符号：★☀♥ 等）
        var sb = new StringBuilder(result.Length);
        foreach (var ch in result)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.Surrogate or UnicodeCategory.OtherSymbol)
                continue;
            sb.Append(ch);
        }

        // 清理残留的重复空白与首尾空白
        var cleaned = sb.ToString().Trim();
        return Regex.Replace(cleaned, @"\s{2,}", " ");
    }
}
