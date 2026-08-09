using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AiDesk.App.Services;

/// <summary>从可执行文件/快捷方式提取图标为 WPF ImageSource（SHGetFileInfo，无需 System.Drawing）。</summary>
public static class IconHelper
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>提取文件关联的大图标；失败返回 null。</summary>
    public static ImageSource? GetFileIcon(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return null;
        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo(path, 0, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;
            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从 exe 路径提取图标（快捷方式场景：先解析 .lnk 目标）。</summary>
    public static ImageSource? GetExecutableIcon(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath))
            return null;
        return GetFileIcon(exePath);
    }
}
