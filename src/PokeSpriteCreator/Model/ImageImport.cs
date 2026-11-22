using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    /// <summary>
    /// Bringing a picture (PNG, GIF, BMP) into a sprite: animated GIFs give one picture per frame,
    /// strips and 4×4 sheets are cut into frames, pixel-doubled art is shrunk, and pictures with a
    /// different size than the canvas are fitted (empty space trimmed, feet kept on the bottom row).
    /// </summary>
    public static class ImageImport
    {
        public const string Filter = "Pictures (*.png;*.gif;*.bmp)|*.png;*.gif;*.bmp|All files|*.*";

        /// <summary>Every frame of the picture (animated GIFs have several). Throws if it can't be read.</summary>
        public static List<PixelImage> LoadFrames(string path)
        {
            var list = new List<PixelImage>();
            var bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            {
                int n = 1;
                try { if (img.FrameDimensionsList.Contains(FrameDimension.Time.Guid)) n = img.GetFrameCount(FrameDimension.Time); } catch { n = 1; }
                for (int i = 0; i < Math.Max(1, Math.Min(n, 64)); i++)
                {
                    if (n > 1) img.SelectActiveFrame(FrameDimension.Time, i);
                    using (var bmp = new Bitmap(img)) list.Add(PixelImage.FromBitmap(bmp));
                }
            }
            // animated GIFs often repeat frames; keep only the distinct ones in order
            var distinct = new List<PixelImage>();
            foreach (var f in list) if (!distinct.Any(d => d.SameAs(f))) distinct.Add(f);
            return distinct;
        }

        /// <summary>The pixel-doubling factor (2-4) of a picture that's bigger than it needs to be, or 1.</summary>
        public static int BlockyScale(PixelImage img, int maxW, int maxH)
        {
            if (img.W <= maxW && img.H <= maxH) return 1;
            for (int k = 4; k >= 2; k--) if (img.IsBlocky(k)) return k;
            return 1;
        }

        /// <summary>
        /// Works out the frame layout for a picture brought in as a new sprite of this kind.
        /// note says what was done (shown to the user), or is empty.
        /// </summary>
        public static FrameLayout Suggest(List<PixelImage> pics, SpriteKind kind, GenStyle st, out string note)
        {
            note = "";
            var def = st.DefaultLayout(kind);
            if (pics.Count == 0) return def;
            int k = BlockyScale(pics[0], def.SheetW, def.SheetH);
            if (k > 1) { pics = pics.Select(p => p.Downscale(k)).ToList(); note = "Shrunk from " + k + "× size. "; }
            var first = pics[0];
            switch (kind)
            {
                case SpriteKind.Back:
                    if (pics.Count > 1)
                    {
                        int n = Math.Min(16, pics.Count);
                        int w = Fits(pics, def.FrameW, def.FrameH) ? def.FrameW : pics.Max(p => p.W);
                        int h = Fits(pics, def.FrameW, def.FrameH) ? def.FrameH : pics.Max(p => p.H);
                        note += n + " animation frames.";
                        return new FrameLayout(w, h, n, 1);
                    }
                    if (first.W > first.H && first.W % first.H == 0)
                    {
                        note += first.W / first.H + " frames (a strip of " + first.H + "×" + first.H + " pictures).";
                        return new FrameLayout(first.H, first.H, Math.Min(16, first.W / first.H), 1);
                    }
                    break;
                case SpriteKind.Overworld:
                    if (first.W % 4 == 0 && first.H % 4 == 0 && first.W >= 16 && first.H >= 16)
                    {
                        note += "4 × 4 sheet of " + first.W / 4 + "×" + first.H / 4 + " frames.";
                        return new FrameLayout(first.W / 4, first.H / 4, 4, 4);
                    }
                    note += "Not a 4 × 4 sheet, so it was put in the first frame.";
                    return def;
            }
            // one picture
            if (pics.Count > 1 && kind == SpriteKind.Front) note += "Animated picture: the first frame was used. ";
            if (first.W == def.FrameW && first.H == def.FrameH) return def;
            var b = first.Bounds();
            if (!b.IsEmpty && b.Width <= def.FrameW && b.Height <= def.FrameH)
            {
                note += first.W + "×" + first.H + " picture fitted into " + def.FrameW + "×" + def.FrameH + " (empty space trimmed, feet on the bottom row).";
                return kind == SpriteKind.Back ? new FrameLayout(def.FrameW, def.FrameH, 1, 1) : def;
            }
            note += "The picture is bigger than " + st.Name + "'s " + def.FrameW + "×" + def.FrameH + ", so the canvas is " + first.W + "×" + first.H + ".";
            return new FrameLayout(first.W, first.H, 1, 1);
        }

        static bool Fits(List<PixelImage> pics, int w, int h)
        {
            return pics.All(p => { var b = p.Bounds(); return b.IsEmpty || (b.Width <= w && b.Height <= h); });
        }

        /// <summary>The picture's frames for a layout (one per frame; frames with nothing are empty).</summary>
        public static List<PixelImage> FramesFor(List<PixelImage> pics, FrameLayout lay)
        {
            var result = Enumerable.Range(0, lay.Count).Select(i => new PixelImage(lay.FrameW, lay.FrameH)).ToList();
            if (pics.Count == 0) return result;
            int k = BlockyScale(pics[0], lay.SheetW, lay.SheetH);
            if (k > 1) pics = pics.Select(p => p.Downscale(k)).ToList();
            if (pics.Count > 1)
            {
                // animation: one picture per frame
                for (int i = 0; i < lay.Count && i < pics.Count; i++) result[i] = FitFrame(pics[i], lay.FrameW, lay.FrameH, pics[0]);
                return result;
            }
            var p0 = pics[0];
            if (p0.W == lay.SheetW && p0.H == lay.SheetH && lay.Count > 1) return Cut(p0, lay);
            if (lay.Count > 1 && p0.W % lay.FrameW == 0 && p0.H % lay.FrameH == 0 && p0.W <= lay.SheetW && p0.H <= lay.SheetH && (p0.W > lay.FrameW || p0.H > lay.FrameH))
                return Cut(p0, lay);
            result[0] = FitFrame(p0, lay.FrameW, lay.FrameH, p0);
            return result;
        }

        /// <summary>
        /// Puts a picture in a w×h frame. Same size: unchanged. Otherwise the drawn part is kept whole when it fits:
        /// its left/right position is kept when the widths match (else centred) and it stands on the bottom row.
        /// reference = the frame whose bounds decide the placement (so animation frames don't jump around).
        /// </summary>
        public static PixelImage FitFrame(PixelImage img, int w, int h, PixelImage reference)
        {
            if (img.W == w && img.H == h) return img.Clone();
            var b = (reference ?? img).Bounds();
            if (b.IsEmpty) return new PixelImage(w, h);
            int dx = img.W == w ? 0 : (w - b.Width) / 2 - b.X;
            int dy = img.H == h ? 0 : (h - b.Height) - b.Y;
            if (b.Height > h) dy = -b.Y;            // too tall: keep the top
            if (b.Width > w && img.W != w) dx = -b.X;
            var r = new PixelImage(w, h);
            r.Blit(img, dx, dy);
            return r;
        }

        /// <summary>
        /// Cuts a sheet into a layout's frames. A sheet with the layout's grid gives each frame its own picture;
        /// one picture is used on every frame; one column = one picture per row; one row = used on every row.
        /// </summary>
        public static List<PixelImage> Cut(PixelImage s, FrameLayout lay)
        {
            var dest = new List<PixelImage>();
            if (s.W % lay.FrameW != 0 || s.H % lay.FrameH != 0)
            {
                for (int i = 0; i < lay.Count; i++) dest.Add(s);
                return dest;
            }
            int cols = s.W / lay.FrameW, rows = s.H / lay.FrameH, n = cols * rows;
            var cells = new PixelImage[n];
            for (int i = 0; i < n; i++) cells[i] = s.Crop((i % cols) * lay.FrameW, (i / cols) * lay.FrameH, lay.FrameW, lay.FrameH);
            for (int i = 0; i < lay.Count; i++)
            {
                int col = i % lay.Cols, row = i / lay.Cols, src;
                if (cols == lay.Cols && rows == lay.Rows) src = i;
                else if (n == 1) src = 0;
                else if (cols == 1 && rows == lay.Rows) src = row;
                else if (rows == 1 && cols == lay.Cols) src = col;
                else src = i % n;
                dest.Add(cells[src]);
            }
            return dest;
        }
    }
}
