using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hs80Battery
{
    // Draws the tray icon: headphones with the HS80's boom mic around the battery percentage,
    // all in one colour that follows the battery state.
    //
    // Pixel art on a 16x16 grid (the tray icon size at 100 % scaling): antialiased 7 px digits
    // smear into grey, hand-placed pixels stay sharp. Larger sizes use whole-pixel multiples.
    static class BatteryIcon
    {
        public const int LowPercent = 15;

        static readonly Color Charging = Color.FromArgb(0x4C, 0xC3, 0x8A);
        static readonly Color Low = Color.FromArgb(0xFF, 0x9F, 0x0A);
        static readonly Color Critical = Color.FromArgb(0xFF, 0x45, 0x3A);
        static readonly Color Muted = Color.FromArgb(0x8E, 0x8E, 0x93);

        static readonly string[] Art =
        {
            ".....######.....",
            "...##......##...",
            "..#..........#..",
            ".#............#.",
            ".#............#.",
            "##............##",
            "##............##",
            "##............##",
            "##............##",
            "##............##",
            "##............##",
            "##............##",
            ".#............#.",
            ".#..............",
            "..##............",
            "....###.........",
        };

        // Digits sit between the ear pads: columns [2, 14), rows 5-11.
        const int TextLeft = 2, TextRight = 14, TextTop = 5;

        // 4x7 digits with 1 px strokes.
        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            { '0', new[] { ".##.", "#..#", "#..#", "#..#", "#..#", "#..#", ".##." } },
            { '1', new[] { ".#", "##", ".#", ".#", ".#", ".#", ".#" } },
            { '2', new[] { ".##.", "#..#", "...#", "..#.", ".#..", "#...", "####" } },
            { '3', new[] { "###.", "...#", "...#", ".##.", "...#", "...#", "###." } },
            { '4', new[] { "..#.", ".##.", "#.#.", "#.#.", "####", "..#.", "..#." } },
            { '5', new[] { "####", "#...", "###.", "...#", "...#", "#..#", ".##." } },
            { '6', new[] { ".##.", "#...", "#...", "###.", "#..#", "#..#", ".##." } },
            { '7', new[] { "####", "...#", "..#.", "..#.", ".#..", ".#..", ".#.." } },
            { '8', new[] { ".##.", "#..#", "#..#", ".##.", "#..#", "#..#", ".##." } },
            { '9', new[] { ".##.", "#..#", "#..#", ".###", "...#", "...#", ".##." } },
            { '-', new[] { "....", "....", "....", "####", "....", "....", "...." } },
        };

        // "100" only fits between the pads, with a pixel to spare on each side, using 3 px zeros.
        static readonly string[] NarrowZero = { ".#.", "#.#", "#.#", "#.#", "#.#", "#.#", ".#." };

        // Tray icon: the art scaled by whole pixels to fill `size`.
        public static Bitmap Render(Reading r, int size, bool lightTaskbar)
        {
            string text;
            Color color;
            Describe(r, lightTaskbar, out text, out color);
            return Draw(Compose(text), color, size, Math.Max(1, size / 16));
        }

        // Stream Deck key at the @2x size. Keys are black, so always the dark variant.
        public static string KeyImage(Reading r)
        {
            using (var bmp = KeyIcon.Render(r, 144))
                return PngDataUri(bmp);
        }

        // Just the headset, no digits, as pixel art: the smallest plugin icons.
        public static Bitmap Glyph(Color color, int size, int scale)
        {
            return Draw(Compose(""), color, size, scale);
        }

        public static string PngDataUri(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        internal static void Describe(Reading r, bool lightTaskbar, out string text, out Color color)
        {
            switch (r.State)
            {
                case HeadsetState.NoReceiver:
                case HeadsetState.Off:
                    text = "-";
                    color = Muted;
                    break;
                default:
                    text = r.Percent.ToString();
                    if (r.State != HeadsetState.Discharging) color = Charging;
                    else if (r.Percent <= 5) color = Critical;
                    else if (r.Percent <= LowPercent) color = Low;
                    else color = lightTaskbar ? Color.Black : Color.White;
                    break;
            }
        }

        static bool[,] Compose(string text)
        {
            var px = new bool[16, 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    px[x, y] = Art[y][x] == '#';

            var glyphs = new List<string[]>();
            foreach (char c in text)
                glyphs.Add(c == '0' && text.Length > 2 ? NarrowZero : Font[c]);
            int width = -1;
            foreach (var g in glyphs) width += g[0].Length + 1;
            int cx = TextLeft + (TextRight - TextLeft - width) / 2;
            foreach (var g in glyphs)
            {
                for (int y = 0; y < g.Length; y++)
                    for (int x = 0; x < g[y].Length; x++)
                        if (g[y][x] == '#') px[cx + x, TextTop + y] = true;
                cx += g[0].Length + 1;
            }
            return px;
        }

        // `k` screen pixels per art pixel, centred in a `size` square.
        static Bitmap Draw(bool[,] px, Color color, int size, int k)
        {
            int off = (size - 16 * k) / 2;
            var bmp = new Bitmap(size, size);
            using (var gfx = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(color))
            {
                gfx.Clear(Color.Transparent);
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        if (px[x, y]) gfx.FillRectangle(brush, off + x * k, off + y * k, k, k);
            }
            return bmp;
        }

        public static Icon ToIcon(Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            // Clone so the Icon owns its handle and the GDI one can be released right away.
            using (var tmp = Icon.FromHandle(h))
            {
                var icon = (Icon)tmp.Clone();
                DestroyIcon(h);
                return icon;
            }
        }

        public static bool TaskbarIsLight()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch (Exception) { return false; }
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    }
}
