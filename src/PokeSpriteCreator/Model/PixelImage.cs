using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PokeSpriteCreator.Model
{
    /// <summary>
    /// A small ARGB image held as an int array. Sprites are pixel art, so a pixel is either
    /// fully transparent (0) or fully opaque; anything half-transparent is snapped when loaded.
    /// </summary>
    public class PixelImage
    {
        public readonly int W, H;
        public readonly int[] Px;

        public PixelImage(int w, int h) { W = Math.Max(1, w); H = Math.Max(1, h); Px = new int[W * H]; }
        PixelImage(int w, int h, int[] px) { W = w; H = h; Px = px; }

        public PixelImage Clone() { return new PixelImage(W, H, (int[])Px.Clone()); }

        public int Get(int x, int y) { return x < 0 || y < 0 || x >= W || y >= H ? 0 : Px[y * W + x]; }
        public void Set(int x, int y, int c) { if (x >= 0 && y >= 0 && x < W && y < H) Px[y * W + x] = c; }
        public bool Inside(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H; }
        public void Clear() { Array.Clear(Px, 0, Px.Length); }

        public bool IsEmpty
        {
            get { for (int i = 0; i < Px.Length; i++) if (Px[i] != 0) return false; return true; }
        }

        public bool SameAs(PixelImage o)
        {
            if (o == null || o.W != W || o.H != H) return false;
            for (int i = 0; i < Px.Length; i++) if (Px[i] != o.Px[i]) return false;
            return true;
        }

        public static bool Opaque(int c) { return ((c >> 24) & 0xFF) >= 128; }
        public static int Snap(int c) { return Opaque(c) ? (c | unchecked((int)0xFF000000)) : 0; }

        /// <summary>Draws src on top of this image (opaque pixels only).</summary>
        public void Blit(PixelImage src, int dx, int dy, bool flipH = false)
        {
            if (src == null) return;
            for (int y = 0; y < src.H; y++)
            {
                int ty = dy + y;
                if (ty < 0 || ty >= H) continue;
                for (int x = 0; x < src.W; x++)
                {
                    int c = src.Px[y * src.W + (flipH ? src.W - 1 - x : x)];
                    if (c == 0) continue;
                    int tx = dx + x;
                    if (tx < 0 || tx >= W) continue;
                    Px[ty * W + tx] = c;
                }
            }
        }

        /// <summary>Copies src over this image including transparent pixels.</summary>
        public void Paste(PixelImage src, int dx, int dy)
        {
            for (int y = 0; y < src.H; y++)
                for (int x = 0; x < src.W; x++)
                    Set(dx + x, dy + y, src.Px[y * src.W + x]);
        }

        public PixelImage Crop(int sx, int sy, int w, int h)
        {
            var r = new PixelImage(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    r.Px[y * w + x] = Get(sx + x, sy + y);
            return r;
        }

        public PixelImage FlipH()
        {
            var r = new PixelImage(W, H);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) r.Px[y * W + x] = Px[y * W + (W - 1 - x)];
            return r;
        }

        /// <summary>Moves the pixels; what slides off is lost.</summary>
        public PixelImage Shift(int dx, int dy)
        {
            var r = new PixelImage(W, H);
            r.Blit(this, dx, dy);
            return r;
        }

        public PixelImage Scale(int k)
        {
            if (k <= 1) return Clone();
            var r = new PixelImage(W * k, H * k);
            for (int y = 0; y < r.H; y++)
                for (int x = 0; x < r.W; x++)
                    r.Px[y * r.W + x] = Px[(y / k) * W + (x / k)];
            return r;
        }

        /// <summary>Shrinks an image drawn at k× size back to 1× (takes the top-left pixel of each block).</summary>
        public PixelImage Downscale(int k)
        {
            if (k <= 1) return Clone();
            var r = new PixelImage(W / k, H / k);
            for (int y = 0; y < r.H; y++)
                for (int x = 0; x < r.W; x++)
                    r.Px[y * r.W + x] = Px[(y * k) * W + x * k];
            return r;
        }

        /// <summary>True when every k×k block is one colour (the image is a pixel-doubled/tripled version).</summary>
        public bool IsBlocky(int k)
        {
            if (k <= 1 || W % k != 0 || H % k != 0) return false;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (Px[y * W + x] != Px[(y - y % k) * W + (x - x % k)]) return false;
            return true;
        }

        /// <summary>Bounding box of the opaque pixels, or an empty rectangle.</summary>
        public Rectangle Bounds()
        {
            int x0 = W, y0 = H, x1 = -1, y1 = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (Px[y * W + x] != 0) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            return x1 < 0 ? Rectangle.Empty : new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        public HashSet<int> Colors()
        {
            var s = new HashSet<int>();
            foreach (var c in Px) if (c != 0) s.Add(c);
            return s;
        }

        // ------------------------------------------------------------------ Bitmap conversion

        public static PixelImage FromBitmap(Bitmap bmp)
        {
            var r = new PixelImage(bmp.Width, bmp.Height);
            using (var b32 = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b32)) { g.Clear(Color.Transparent); g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height)); }
                var data = b32.LockBits(new Rectangle(0, 0, r.W, r.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < r.H; y++)
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), r.Px, y * r.W, r.W);
                }
                finally { b32.UnlockBits(data); }
            }
            for (int i = 0; i < r.Px.Length; i++) r.Px[i] = Snap(r.Px[i]);
            return r;
        }

        public Bitmap ToBitmap()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < H; y++)
                    Marshal.Copy(Px, y * W, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), W);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        /// <summary>Loads a picture without keeping the file locked. Returns null if it can't be read.</summary>
        public static PixelImage Load(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                using (var bmp = new Bitmap(img))
                    return FromBitmap(bmp);
            }
            catch { return null; }
        }

        public void Save(string path)
        {
            using (var b = ToBitmap()) b.Save(path, ImageFormat.Png);
        }

        public byte[] ToPngBytes()
        {
            using (var b = ToBitmap())
            using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }

        public static PixelImage FromPngBytes(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            using (var bmp = new Bitmap(img))
                return FromBitmap(bmp);
        }

        // ------------------------------------------------------------------ colour helpers

        public static int FromColor(Color c) { return c.A < 128 ? 0 : (unchecked((int)0xFF000000) | (c.R << 16) | (c.G << 8) | c.B); }
        public static Color ToColor(int c) { return c == 0 ? Color.Transparent : Color.FromArgb(255, (c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF); }
        public static int R(int c) { return (c >> 16) & 0xFF; }
        public static int G(int c) { return (c >> 8) & 0xFF; }
        public static int B(int c) { return c & 0xFF; }
        public static int Rgb(int r, int g, int b)
        {
            r = Math.Max(0, Math.Min(255, r)); g = Math.Max(0, Math.Min(255, g)); b = Math.Max(0, Math.Min(255, b));
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
        public static double Luma(int c) { return (0.299 * R(c) + 0.587 * G(c) + 0.114 * B(c)) / 255.0; }
        public static string Hex(int c) { return c == 0 ? "transparent" : "#" + (c & 0xFFFFFF).ToString("X6"); }
        public static int ParseHex(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "transparent") return 0;
            s = s.TrimStart('#');
            return unchecked((int)0xFF000000) | Convert.ToInt32(s, 16);
        }
    }
}
