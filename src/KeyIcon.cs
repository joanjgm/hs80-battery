using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Hs80Battery
{
    // Stream Deck key: the tray icon's design (headband, ear cups, the HS80's boom mic, the
    // percentage between the cups) redrawn as smooth vector shapes. Keys are 72-144 px, where
    // the 16 px pixel art would just look blocky.
    static class KeyIcon
    {
        // Laid out on a 144 x 144 canvas and scaled to the requested size.
        const float Canvas = 144f;

        public static Bitmap Render(Reading r, int size)
        {
            string text;
            Color color;
            BatteryIcon.Describe(r, false, out text, out color);
            if (text == "-") text = "–";
            return Draw(color, size, text, r.State == HeadsetState.Charging);
        }

        // The headset alone: default key image and plugin icons.
        public static Bitmap Glyph(Color color, int size)
        {
            return Draw(color, size, null, false);
        }

        static Bitmap Draw(Color color, int size, string text, bool charging)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(color))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(size / Canvas, size / Canvas);

                // Headband: half of a 96 px circle whose ends drop into the cups.
                using (var band = new Pen(color, 9f))
                {
                    band.StartCap = band.EndCap = LineCap.Round;
                    g.DrawArc(band, 24, 20, 96, 96, 180, 180);
                }

                // Ear cups: a slim yoke into a rounded pad on each side.
                using (var yoke = new Pen(color, 5f))
                {
                    g.DrawLine(yoke, 24, 66, 24, 72);
                    g.DrawLine(yoke, 120, 66, 120, 72);
                }
                using (var left = Pill(new RectangleF(11, 70, 26, 46)))
                using (var right = Pill(new RectangleF(107, 70, 26, 46)))
                {
                    g.FillPath(brush, left);
                    g.FillPath(brush, right);
                }

                // Boom mic: curves from the bottom of the left cup towards the centre.
                using (var boom = new Pen(color, 5f))
                {
                    boom.StartCap = boom.EndCap = LineCap.Round;
                    g.DrawBezier(boom, new PointF(24, 112), new PointF(26, 130),
                        new PointF(40, 134), new PointF(56, 133));
                }
                using (var tip = Pill(new RectangleF(54, 127, 20, 12)))
                    g.FillPath(brush, tip);

                if (charging) DrawBolt(g, brush);
                if (text != null) DrawText(g, brush, text, new RectangleF(46, 70, 52, 40));
            }
            return bmp;
        }

        // Largest bold text whose ink fits `box`, centred on it.
        static void DrawText(Graphics g, Brush brush, string text, RectangleF box)
        {
            using (var family = new FontFamily("Segoe UI"))
            {
                float em = box.Height * 1.6f;
                while (true)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddString(text, family, (int)FontStyle.Bold, em, PointF.Empty,
                            StringFormat.GenericTypographic);
                        var ink = path.GetBounds();
                        if ((ink.Width <= box.Width && ink.Height <= box.Height) || em <= 8)
                        {
                            using (var m = new Matrix())
                            {
                                m.Translate(box.X + (box.Width - ink.Width) / 2f - ink.X,
                                            box.Y + (box.Height - ink.Height) / 2f - ink.Y);
                                path.Transform(m);
                            }
                            g.FillPath(brush, path);
                            return;
                        }
                    }
                    em -= 1f;
                }
            }
        }

        // Small lightning bolt under the headband while charging.
        static void DrawBolt(Graphics g, Brush brush)
        {
            var bolt = new[]
            {
                new PointF(76, 30), new PointF(63, 47), new PointF(71, 47),
                new PointF(67, 60), new PointF(81, 42), new PointF(73, 42), new PointF(76, 30),
            };
            g.FillPolygon(brush, bolt);
        }

        static GraphicsPath Pill(RectangleF r)
        {
            float d = Math.Min(r.Width, r.Height);
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
