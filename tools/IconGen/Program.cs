using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Standalone generator that renders the CaptureIt camera icon at multiple
// resolutions and packs them into a PNG-compressed multi-size .ico file.
// Kept identical in spirit to TrayIconManager.CreateCameraIcon so the tray,
// taskbar and window icons all share one design.

static Bitmap Draw(int size)
{
    var bmp = new Bitmap(size, size);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.Clear(Color.Transparent);

    // All coordinates authored on a 32x32 grid, scaled to the target size.
    float s = size / 32f;
    RectangleF R(float x, float y, float w, float h) => new(x * s, y * s, w * s, h * s);

    var accent = Color.FromArgb(255, 45, 140, 240);
    var accentDark = Color.FromArgb(255, 30, 100, 190);
    var lensOuter = Color.FromArgb(255, 235, 240, 250);
    var lensInner = Color.FromArgb(255, 30, 100, 190);

    using (var bumpBrush = new SolidBrush(accent))
        g.FillRectangle(bumpBrush, R(11f, 5f, 8f, 4f));

    var bodyRect = R(3f, 8f, 26f, 19f);
    using (var bodyPath = RoundedRect(bodyRect, 4f * s))
    using (var bodyBrush = new SolidBrush(accent))
    using (var borderPen = new Pen(accentDark, Math.Max(1f, 1.5f * s)))
    {
        g.FillPath(bodyBrush, bodyPath);
        g.DrawPath(borderPen, bodyPath);
    }

    using (var lensOuterBrush = new SolidBrush(lensOuter))
        g.FillEllipse(lensOuterBrush, R(11f, 12f, 11f, 11f));
    using (var lensInnerBrush = new SolidBrush(lensInner))
        g.FillEllipse(lensInnerBrush, R(13.5f, 14.5f, 6f, 6f));
    using (var glintBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        g.FillEllipse(glintBrush, R(14.5f, 15.5f, 2f, 2f));

    using (var flashBrush = new SolidBrush(Color.FromArgb(255, 255, 210, 80)))
        g.FillEllipse(flashBrush, R(24f, 11f, 3f, 3f));

    return bmp;
}

static GraphicsPath RoundedRect(RectangleF rect, float radius)
{
    float d = radius * 2f;
    var path = new GraphicsPath();
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

string outPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "capture.ico");

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var payloads = new List<byte[]>();
foreach (var sz in sizes)
{
    using var bmp = Draw(sz);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    payloads.Add(ms.ToArray());
}

using var fs = new FileStream(outPath, FileMode.Create);
using var bw = new BinaryWriter(fs);
bw.Write((short)0);              // reserved
bw.Write((short)1);              // type = icon
bw.Write((short)sizes.Length);   // image count

int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int sz = sizes[i];
    bw.Write((byte)(sz >= 256 ? 0 : sz)); // width (0 => 256)
    bw.Write((byte)(sz >= 256 ? 0 : sz)); // height
    bw.Write((byte)0);                    // palette count
    bw.Write((byte)0);                    // reserved
    bw.Write((short)1);                   // color planes
    bw.Write((short)32);                  // bits per pixel
    bw.Write(payloads[i].Length);         // data size
    bw.Write(offset);                     // data offset
    offset += payloads[i].Length;
}
foreach (var p in payloads)
    bw.Write(p);

Console.WriteLine($"Wrote {outPath} ({sizes.Length} sizes, {new FileInfo(outPath).Length} bytes)");
