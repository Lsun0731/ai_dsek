using System.Windows;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>日期小组件：星期 + 日期 + 年份。</summary>
public partial class DateWidgetWindow : WidgetWindowBase
{
    public DateWidgetWindow() : base(Services.WidgetKind.Date)
    {
        InitializeComponent();
        StartTicker(30); // 每 30 秒检查跨天
    }

    protected override void OnTick()
    {
        var now = DateTime.Now;
        WeekText.Text = GetWeekday(now);
        DateText.Text = $"{now.Month}月{now.Day}日";
        YearText.Text = $"{now.Year}年";
    }

    private static string GetWeekday(DateTime now) => now.DayOfWeek switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };
}
