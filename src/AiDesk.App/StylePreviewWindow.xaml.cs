using System.Windows;

namespace AiDesk.App;

/// <summary>
/// UI 风格预览窗口：三种风格（深色玻璃 / 浅色清爽 / 霓虹渐变）并列展示，供选择。
/// 通过命令行参数 --preview 启动（不加载主界面）。
/// </summary>
public partial class StylePreviewWindow : Window
{
    public StylePreviewWindow()
    {
        InitializeComponent();
    }
}
