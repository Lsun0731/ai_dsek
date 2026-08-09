using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// AiDesk 应用图标生成器：渐变圆角 + 白色 "A" + 三色小圆点（多工具集合）
// 用法：dotnet run --project tools/GenerateIcon
// 输出：src/AiDesk.App/Assets/ai-desk.ico（多尺寸，Vista+ PNG 压缩格式）

const string OutPath = @"src\AiDesk.App\Assets\ai-desk.ico";
int[] sizes = [16, 24, 32, 48, 64, 128, 256];

using var ms = new MemoryStream();
using var bw = new BinaryWriter(ms);
bw.Write((ushort)0);          // reserved
bw.Write((ushort)1);          // type: icon
bw.Write((ushort)sizes.Length); // count

var entries = new List<byte[]>();
int dataOffset = 6 + 16 * sizes.Length;

foreach (var size in sizes)
{
    var png = RenderIcon(size);
    bw.Write((byte)size);                    // width
    bw.Write((byte)size);                    // height
    bw.Write((byte)0);                       // palette
    bw.Write((byte)0);                       // reserved
    bw.Write((ushort)1);                     // planes
    bw.Write((ushort)32);                    // bpp
    bw.Write((uint)png.Length);              // data size
    bw.Write((uint)dataOffset);              // data offset
    dataOffset += png.Length;
    entries.Add(png);
}

foreach (var e in entries)
    bw.Write(e);
bw.Flush();
File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), OutPath), ms.ToArray());
Console.WriteLine($"✅ 图标已生成: {OutPath} ({new FileInfo(OutPath).Length} bytes, {sizes.Length} 个尺寸)");

static byte[] RenderIcon(int size)
{
    using var bmp = new Bitmap(size, size);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    g.Clear(Color.Transparent);

    var radius = Math.Max(3, (int)(size * 0.22));
    using var clip = RoundRectPath(size, size, radius);
    g.SetClip(clip);

    // 垂直渐变底（上 #5B8DF5 → 下 #3B66D0）
    using var grad = new LinearGradientBrush(
        new Rectangle(0, 0, size, size),
        Color.FromArgb(255, 0x5B, 0x8D, 0xF5),
        Color.FromArgb(255, 0x3B, 0x66, 0xD0),
        90f);
    g.FillRectangle(grad, new Rectangle(0, 0, size, size));

    // 顶部高光
    var glowRect = new Rectangle(0, 0, size, (int)(size * 0.45));
    using var glow = new LinearGradientBrush(
        glowRect,
        Color.FromArgb(70, 255, 255, 255),
        Color.FromArgb(0, 255, 255, 255),
        90f);
    g.FillRectangle(glow, glowRect);
    g.ResetClip();

    // 白色 "A"
    var fontSize = Math.Max(6, (int)(size * 0.58));
    using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    var textRect = new RectangleF(0, -(int)(size * 0.06f), size, size);
    g.DrawString("A", font, Brushes.White, textRect, sf);

    // 三色小圆点（右下角，代表"多工具集合"；小尺寸省略）
    if (size >= 32)
    {
        var dotR = Math.Max(2, (int)(size * 0.045));
        Color[] dotColors = [Color.FromArgb(255, 0xE5, 0x48, 0x4D), Color.FromArgb(255, 0x30, 0xA4, 0x6C), Color.White];
        var dotY = (int)(size * 0.80);
        var x = (int)(size * 0.66);
        for (var i = 0; i < dotColors.Length; i++)
        {
            using var brush = new SolidBrush(dotColors[i]);
            g.FillEllipse(brush, x + i * dotR * 3, dotY, dotR * 2, dotR * 2);
        }
    }

    using var pngMs = new MemoryStream();
    bmp.Save(pngMs, ImageFormat.Png);
    return pngMs.ToArray();
}

static GraphicsPath RoundRectPath(int w, int h, int r)
{
    var p = new GraphicsPath();
    var d = r * 2;
    p.AddArc(0, 0, d, d, 180, 90);
    p.AddArc(w - d, 0, d, d, 270, 90);
    p.AddArc(w - d, h - d, d, d, 0, 90);
    p.AddArc(0, h - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
}
