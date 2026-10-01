using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace Hs80Battery
{
    // Renders every tray-icon state to a PNG (to review the design without waiting for the
    // battery to drain) and writes assets\hs80.ico for the exe. Run with tools\preview.ps1.
    static class Preview
    {
        static readonly Reading[] States =
        {
            R(HeadsetState.Discharging, 62), R(HeadsetState.Discharging, 100), R(HeadsetState.Discharging, 14),
            R(HeadsetState.Discharging, 4), R(HeadsetState.Charging, 42), R(HeadsetState.Off, 0),
        };

        static readonly Color DarkBar = Color.FromArgb(0x1C, 0x1C, 0x1C);
        static readonly Color LightBar = Color.FromArgb(0xEE, 0xEE, 0xEE);

        static void Main(string[] args)
        {
            string root = args[0];
            int[] sizes = { 16, 20, 24 };   // 100 %, 125 %, 150 % display scaling

            // Icons at 16 px blown up x6 on dark and light taskbars, then the same icons at real
            // size (16/20/24 px) as they would sit in the tray.
            const int zoom = 6, pad = 14, gap = 18;
            int cell = 16 * zoom + pad;
            int realW = 0;
            foreach (int s in sizes) realW += States.Length * (s + 10) + 24;
            int width = Math.Max(pad + States.Length * cell * 2 + gap, pad + realW) + pad;

            using (var sheet = new Bitmap(width, pad + cell + 6 + 32 + pad))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int theme = 0; theme < 2; theme++)
                {
                    bool light = theme == 1;
                    int x0 = pad + theme * (States.Length * cell + gap);
                    using (var b = new SolidBrush(light ? LightBar : DarkBar))
                        g.FillRectangle(b, x0, pad, States.Length * cell, cell);
                    for (int i = 0; i < States.Length; i++)
                        using (var icon = BatteryIcon.Render(States[i], 16, light))
                            g.DrawImage(icon, x0 + pad / 2 + i * cell, pad + pad / 2, 16 * zoom, 16 * zoom);
                }

                // Real size: no scaling, like the taskbar.
                int ry = pad + cell + 6, rx = pad;
                using (var b = new SolidBrush(DarkBar))
                    g.FillRectangle(b, pad, ry, realW, 32);
                foreach (int s in sizes)
                {
                    for (int i = 0; i < States.Length; i++)
                        using (var icon = BatteryIcon.Render(States[i], s, false))
                            g.DrawImageUnscaled(icon, rx + i * (s + 10) + 6, ry + (32 - s) / 2);
                    rx += States.Length * (s + 10) + 24;
                }
                sheet.Save(Path.Combine(root, "dist\\preview.png"), ImageFormat.Png);
            }

            Directory.CreateDirectory(Path.Combine(root, "assets"));
            WriteIco(Path.Combine(root, "assets\\hs80.ico"), new[] { 16, 24, 32, 48, 256 });

            WriteStreamDeckImages(Path.Combine(root, "streamdeck\\imgs"));
            WriteKeyPreview(Path.Combine(root, "dist\\keys.png"));
        }

        // Images the plugin manifest points at. Action and category icons are white glyphs, as
        // the Stream Deck guidelines ask; the key image is only shown until the first reading.
        static void WriteStreamDeckImages(string dir)
        {
            Directory.CreateDirectory(dir);
            Save(BatteryIcon.Glyph(Color.White, 20, 1), dir, "action.png");
            Save(BatteryIcon.Glyph(Color.White, 40, 2), dir, "action@2x.png");
            Save(BatteryIcon.Glyph(Color.White, 28, 1), dir, "category.png");
            Save(BatteryIcon.Glyph(Color.White, 56, 3), dir, "category@2x.png");
            Save(BatteryIcon.Glyph(Color.White, 72, 4), dir, "key.png");
            Save(BatteryIcon.Glyph(Color.White, 144, 8), dir, "key@2x.png");
            Save(AppIcon(256), dir, "plugin.png");
            Save(AppIcon(512), dir, "plugin@2x.png");
        }

        // Every state as it lands on a (black, rounded) Stream Deck key.
        static void WriteKeyPreview(string path)
        {
            const int key = 144, gap = 20;
            using (var sheet = new Bitmap(gap + States.Length * (key + gap), key + 2 * gap))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(0x2B, 0x2B, 0x2B));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < States.Length; i++)
                {
                    int x = gap + i * (key + gap);
                    using (var face = Rounded(new RectangleF(x, gap, key, key), 18))
                        g.FillPath(Brushes.Black, face);
                    string uri = BatteryIcon.KeyImage(States[i]);
                    var bytes = Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
                    using (var ms = new MemoryStream(bytes))
                    using (var img = Image.FromStream(ms))
                        g.DrawImageUnscaled(img, x, gap);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }

        static void Save(Bitmap bmp, string dir, string name)
        {
            using (bmp) bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
        }

        static Reading R(HeadsetState s, int p)
        {
            return new Reading { State = s, Percent = p, Tenths = p * 10 };
        }

        // Exe icon: a rounded green tile with a white battery.
        static Bitmap AppIcon(int s)
        {
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float r = s * 0.22f;
                using (var tile = Rounded(new RectangleF(0, 0, s, s), r))
                using (var b = new SolidBrush(Color.FromArgb(0x2E, 0xA0, 0x6B)))
                    g.FillPath(b, tile);

                float w = s * 0.62f, h = s * 0.34f;
                float x = (s - w) / 2f - s * 0.03f, y = (s - h) / 2f;
                float stroke = Math.Max(1f, s * 0.06f);
                using (var pen = new Pen(Color.White, stroke))
                using (var body = Rounded(new RectangleF(x, y, w, h), s * 0.06f))
                    g.DrawPath(pen, body);
                using (var white = new SolidBrush(Color.White))
                {
                    g.FillRectangle(white, x + w + stroke * 0.5f, y + h * 0.3f, s * 0.05f, h * 0.4f);
                    float inset = stroke * 1.6f;
                    g.FillRectangle(white, x + inset, y + inset, (w - 2 * inset) * 0.7f, h - 2 * inset);
                }
            }
            return bmp;
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ICO with PNG-compressed entries (supported since Vista).
        static void WriteIco(string path, int[] sizes)
        {
            var pngs = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var bmp = AppIcon(sizes[i]))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs[i] = ms.ToArray();
                }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }
}
