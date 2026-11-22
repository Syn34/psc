using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    /// <summary>A reference picture kept with a sprite (a photo of a hairstyle, a jacket…), shrunk to a workable size.</summary>
    public class RefPicture
    {
        public string Name;
        public PixelImage Image;
        public const int MaxSide = 480;

        public static RefPicture FromFile(string path)
        {
            var img = PixelImage.Load(path);
            int side = Math.Max(img.W, img.H);
            if (side > MaxSide)
            {
                double k = MaxSide / (double)side;
                img = Smooth(img, Math.Max(1, (int)Math.Round(img.W * k)), Math.Max(1, (int)Math.Round(img.H * k)));
            }
            return new RefPicture { Name = System.IO.Path.GetFileNameWithoutExtension(path), Image = img };
        }

        /// <summary>Area-average downscale (keeps photo detail instead of picking stray pixels).</summary>
        public static PixelImage Smooth(PixelImage src, int w, int h)
        {
            var dst = new PixelImage(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int x0 = x * src.W / w, x1 = Math.Max(x0 + 1, (x + 1) * src.W / w);
                    int y0 = y * src.H / h, y1 = Math.Max(y0 + 1, (y + 1) * src.H / h);
                    long r = 0, g = 0, b = 0, n = 0, t = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++)
                        {
                            int c = src.Get(xx, yy); t++;
                            if (c == 0) continue;
                            r += PixelImage.R(c); g += PixelImage.G(c); b += PixelImage.B(c); n++;
                        }
                    if (n * 2 >= t && n > 0) dst.Set(x, y, PixelImage.Rgb((int)(r / n), (int)(g / n), (int)(b / n)));
                }
            return dst;
        }
    }

    /// <summary>One group of similar colours in the boxed part of the photo; the user can keep or drop each.</summary>
    public class RefCluster
    {
        public int Color, Count;
        public double BorderShare;   // how much of the box's edge is this colour (background touches the edge a lot)
        public bool Keep = true;
    }

    public class RefOptions
    {
        public bool OnlyExisting = true;   // recolour with the sprite's own colours (no new colours)
        public bool Outline = true;        // outline where it meets the sprite's empty background
        public bool Tidy = true;           // turn photo noise into clumps, drop stray pixels, fill holes
        public int Variation;              // Redo: 0, 1, 2… each a slightly different take
    }

    public class RefResult
    {
        public PixelImage Overlay;         // the new pixels on a transparent frame-sized image
        public PixelImage Small;           // the pixel version on its own (for the panel preview)
        public Rectangle Placed;           // where it landed on the sprite (including its outline)
        public readonly List<int> Tones = new List<int>();
        public readonly List<string> Notes = new List<string>();
        public int Pixels;
        public bool Empty { get { return Pixels == 0; } }
    }

    /// <summary>
    /// Turns part of a reference photo into sprite pixels: groups the colours in the box, keeps the groups that are
    /// the thing (not the background, and not skin when it's hair), shrinks it into the target box on the sprite,
    /// recolours it with a ramp of the sprite's own colours by lightness (so the photo's light and shade carry over),
    /// tidies photo noise into clumps and outlines it where it meets the background.
    /// </summary>
    public static class RefConvert
    {
        static double L(int c) { return PixelImage.Luma(c); }

        static int Dist(int a, int b)
        {
            int dr = PixelImage.R(a) - PixelImage.R(b), dg = PixelImage.G(a) - PixelImage.G(b), db = PixelImage.B(a) - PixelImage.B(b);
            return dr * dr * 3 + dg * dg * 4 + db * db * 2;
        }

        static bool SkinLike(int c)
        {
            double h, s, l; ColorTools.ToHsl(c, out h, out s, out l);
            int mx = Math.Max(PixelImage.R(c), Math.Max(PixelImage.G(c), PixelImage.B(c))), mn = Math.Min(PixelImage.R(c), Math.Min(PixelImage.G(c), PixelImage.B(c)));
            return h >= 5 && h <= 45 && L(c) > 0.5 && mx - mn >= 25 && PixelImage.R(c) > PixelImage.B(c) + 25;
        }

        static readonly string[] HairWords = { "hair", "fringe", "bangs", "curl", "afro", "braid", "ponytail", "beard", "fade", "mullet", "quiff" };

        public static bool IsHair(string description)
        {
            var d = (description ?? "").ToLowerInvariant();
            return HairWords.Any(w => d.Contains(w));
        }

        /// <summary>Groups the colours in the box (k-means) and guesses which groups are the thing.</summary>
        public static List<RefCluster> Clusters(PixelImage photo, Rectangle box, string description, int k = 5)
        {
            box.Intersect(new Rectangle(0, 0, photo.W, photo.H));
            var result = new List<RefCluster>();
            if (box.Width < 2 || box.Height < 2) return result;
            var px = new List<int>();
            int step = Math.Max(1, (int)Math.Sqrt(box.Width * box.Height / 6000.0));
            for (int y = box.Top; y < box.Bottom; y += step)
                for (int x = box.Left; x < box.Right; x += step) { int c = photo.Get(x, y); if (c != 0) px.Add(c); }
            if (px.Count == 0) return result;
            k = Math.Min(k, px.Distinct().Count());
            // start from colours spread across lightness
            var sorted = px.OrderBy(c => L(c)).ToList();
            var centres = Enumerable.Range(0, k).Select(i => sorted[(int)((i + 0.5) * sorted.Count / k)]).ToArray();
            var assign = new int[px.Count];
            for (int it = 0; it < 12; it++)
            {
                for (int i = 0; i < px.Count; i++)
                {
                    int best = 0, bd = int.MaxValue;
                    for (int j = 0; j < k; j++) { int d = Dist(px[i], centres[j]); if (d < bd) { bd = d; best = j; } }
                    assign[i] = best;
                }
                for (int j = 0; j < k; j++)
                {
                    long r = 0, g = 0, b = 0, n = 0;
                    for (int i = 0; i < px.Count; i++) if (assign[i] == j) { r += PixelImage.R(px[i]); g += PixelImage.G(px[i]); b += PixelImage.B(px[i]); n++; }
                    if (n > 0) centres[j] = PixelImage.Rgb((int)(r / n), (int)(g / n), (int)(b / n));
                }
            }
            // how much of the box's edge each group covers
            var border = new int[k]; int borderN = 0;
            for (int x = box.Left; x < box.Right; x++) foreach (int y in new[] { box.Top, box.Bottom - 1 }) { border[Nearest(photo.Get(x, y), centres)]++; borderN++; }
            for (int y = box.Top; y < box.Bottom; y++) foreach (int x in new[] { box.Left, box.Right - 1 }) { border[Nearest(photo.Get(x, y), centres)]++; borderN++; }
            for (int j = 0; j < k; j++)
            {
                int n = assign.Count(a => a == j);
                if (n == 0) continue;
                result.Add(new RefCluster { Color = centres[j], Count = n * step * step, BorderShare = border[j] / (double)Math.Max(1, borderN) });
            }
            // guess: the background is the group that covers the most of the edge (more than its share of the box)
            int total = result.Sum(c => c.Count);
            var bg = result.OrderByDescending(c => c.BorderShare).First();
            if (bg.BorderShare > 0.25 && bg.BorderShare > 1.3 * bg.Count / (double)total) bg.Keep = false;
            if (IsHair(description))
                foreach (var c in result) if (SkinLike(c.Color) || L(c.Color) > 0.78) c.Keep = false;
            if (!result.Any(c => c.Keep)) result.OrderByDescending(c => c.Count).First().Keep = true;
            return result.OrderBy(c => L(c.Color)).ToList();
        }

        static int Nearest(int c, int[] centres)
        {
            int best = 0, bd = int.MaxValue;
            for (int j = 0; j < centres.Length; j++) { int d = Dist(c, centres[j]); if (d < bd) { bd = d; best = j; } }
            return best;
        }

        // ------------------------------------------------------------------ the conversion

        public static RefResult Convert(PixelImage photo, Rectangle src, List<RefCluster> clusters, PixelImage sprite, Rectangle dst,
                                        ICollection<int> spriteColors, string description, RefOptions o)
        {
            var res = new RefResult { Overlay = new PixelImage(sprite.W, sprite.H), Small = new PixelImage(1, 1) };
            src.Intersect(new Rectangle(0, 0, photo.W, photo.H));
            dst.Intersect(new Rectangle(0, 0, sprite.W, sprite.H));
            if (src.Width < 2 || src.Height < 2) { res.Notes.Add("Drag a box around the thing in the reference picture."); return res; }
            if (dst.Width < 2 || dst.Height < 2) { res.Notes.Add("Drag a box on the sprite where it should go."); return res; }
            if (clusters == null || !clusters.Any(c => c.Keep)) { res.Notes.Add("Tick at least one of the colours in the box."); return res; }
            int v = o.Variation;
            bool hair = IsHair(description);

            // 1. which photo pixels are the thing
            var centres = clusters.Select(c => c.Color).ToArray();
            var keep = clusters.Select(c => c.Keep).ToArray();
            int sw = src.Width, sh = src.Height;
            var mask = new bool[sw * sh];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    int c = photo.Get(src.X + x, src.Y + y);
                    mask[y * sw + x] = c != 0 && keep[Nearest(c, centres)];
                }
            KeepBigParts(mask, sw, sh, 0.12);
            // trim to what's kept, so the thing itself (not the wall or skin around it) fills the box on the sprite
            {
                int x0 = sw, y0 = sh, x1 = -1, y1 = -1;
                for (int y = 0; y < sh; y++) for (int x = 0; x < sw; x++) if (mask[y * sw + x]) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                if (x1 < 0) { res.Notes.Add("None of the box is left once the left-out colours are taken away. Tick more colours under the picture."); return res; }
                int nw = x1 - x0 + 1, nh = y1 - y0 + 1;
                var trimmed = new bool[nw * nh];
                for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++) trimmed[y * nw + x] = mask[(y + y0) * sw + x + x0];
                mask = trimmed; src = new Rectangle(src.X + x0, src.Y + y0, nw, nh); sw = nw; sh = nh;
            }

            // 2. size and place: fit inside the box keeping its shape, centred, sitting on the box's bottom edge
            double[] scales = { 1.0, 0.9, 1.0, 0.95, 1.0, 0.85 };
            int[] shiftX = { 0, 0, -1, 1, 1, 0 };
            double scale = Math.Min(dst.Width / (double)sw, dst.Height / (double)sh) * scales[v % scales.Length];
            int tw = Math.Max(1, (int)Math.Round(sw * scale)), th = Math.Max(1, (int)Math.Round(sh * scale));
            int ox = dst.X + (dst.Width - tw) / 2 + shiftX[v % shiftX.Length], oy = dst.Bottom - th;

            // 3. shrink: each sprite pixel takes the photo pixels under it (on if enough of them are the thing)
            double[] cover = { 0.5, 0.42, 0.58, 0.5, 0.46, 0.54 };
            var on = new bool[tw * th];
            var col = new int[tw * th];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    int x0 = x * sw / tw, x1 = Math.Max(x0 + 1, (x + 1) * sw / tw), y0 = y * sh / th, y1 = Math.Max(y0 + 1, (y + 1) * sh / th);
                    long r = 0, g = 0, b = 0, n = 0, t = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++)
                        {
                            t++;
                            if (!mask[yy * sw + xx]) continue;
                            int c = photo.Get(src.X + xx, src.Y + yy);
                            r += PixelImage.R(c); g += PixelImage.G(c); b += PixelImage.B(c); n++;
                        }
                    if (n > 0 && n >= cover[v % cover.Length] * t) { on[y * tw + x] = true; col[y * tw + x] = PixelImage.Rgb((int)(r / n), (int)(g / n), (int)(b / n)); }
                }
            if (o.Tidy) TidyShape(on, tw, th);
            int count = on.Count(b => b);
            if (count == 0) { res.Notes.Add("Nothing was left after shrinking it. Make the box on the sprite bigger, or tick more colours."); return res; }

            // 4. colours: a ramp of the sprite's own shades, given out by lightness so the photo's light and shade carry over
            var avg = Average(Enumerable.Range(0, tw * th).Where(i => on[i]).Select(i => col[i]));
            var ramp = o.OnlyExisting ? SpriteRamp(avg, spriteColors) : PhotoRamp(Enumerable.Range(0, tw * th).Where(i => on[i]).Select(i => col[i]).ToList());
            if (ramp.Count == 0) ramp.Add(avg);
            var order = Enumerable.Range(0, tw * th).Where(i => on[i]).OrderBy(i => L(col[i])).ToList();
            var cuts = Cuts(ramp.Count, v);
            var tone = new int[tw * th];
            for (int k = 0; k < order.Count; k++)
            {
                double q = (k + 0.5) / order.Count;
                int t = 0; while (t < cuts.Length && q > cuts[t]) t++;
                tone[order[k]] = ramp[Math.Min(t, ramp.Count - 1)];
            }
            if (o.Tidy) TidyTones(on, tone, tw, th);

            // 5. onto the frame, with an outline where it meets the sprite's empty background
            int outline = spriteColors.Where(c => c != 0).OrderBy(c => L(c)).FirstOrDefault();
            if (outline == 0) outline = PixelImage.Rgb(0, 0, 0);
            var ov = res.Overlay;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    if (!on[y * tw + x]) continue;
                    int fx = ox + x, fy = oy + y;
                    if (ov.Inside(fx, fy)) { ov.Set(fx, fy, tone[y * tw + x]); res.Pixels++; }
                }
            int outCount = 0;
            if (o.Outline)
            {
                var add = new List<Point>();
                for (int y = oy - 1; y <= oy + th; y++)
                    for (int x = ox - 1; x <= ox + tw; x++)
                    {
                        if (!ov.Inside(x, y) || ov.Get(x, y) != 0 || sprite.Get(x, y) != 0) continue;
                        bool next = false;
                        foreach (var d in new[] { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) })
                            if (ov.Inside(x + d.X, y + d.Y) && ov.Get(x + d.X, y + d.Y) != 0) next = true;
                        if (next) add.Add(new Point(x, y));
                    }
                foreach (var p in add) ov.Set(p.X, p.Y, outline);
                outCount = add.Count;
            }
            var bounds = ov.Bounds();
            res.Placed = bounds;
            res.Small = bounds.Width > 0 ? ov.Crop(bounds.X, bounds.Y, bounds.Width, bounds.Height) : new PixelImage(1, 1);
            res.Tones.AddRange(ramp);

            // 6. what happened
            string what = string.IsNullOrWhiteSpace(description) ? "it" : "the " + description.Trim();
            res.Notes.Add("Made " + what + " " + tw + " × " + th + " pixels to fit your box" + (scales[v % scales.Length] < 1 ? " (a little smaller this time)" : "") + ", sitting on the box's bottom edge.");
            res.Notes.Add("Colours: " + string.Join(", ", ramp.Select(PixelImage.Hex).ToArray()) + (o.OnlyExisting ? " (from your sprite, given out by how light each bit of the photo is)." : " (from the photo; these may add colours)."));
            if (hair) res.Notes.Add("Skin and very light colours in the box are left out because it's hair (tick them under the picture to put them back).");
            if (outCount > 0) res.Notes.Add("Outlined in " + PixelImage.Hex(outline) + " where it meets the background.");
            if (o.Tidy) res.Notes.Add("Photo noise is tidied into clumps, stray pixels removed and holes filled.");
            res.Notes.Add("Try " + (v + 1) + ". Confirm adds it as a new layer; Redo tries another take; Cancel throws it away.");
            return res;
        }

        static int Average(IEnumerable<int> cols)
        {
            long r = 0, g = 0, b = 0, n = 0;
            foreach (var c in cols) { r += PixelImage.R(c); g += PixelImage.G(c); b += PixelImage.B(c); n++; }
            return n == 0 ? 0 : PixelImage.Rgb((int)(r / n), (int)(g / n), (int)(b / n));
        }

        /// <summary>Cut points (0..1) between tones, darkest first: a little shadow, mostly the middle shades, a little highlight.</summary>
        static double[] Cuts(int n, int v)
        {
            double shift = new[] { 0.0, 0.06, -0.06, 0.03, -0.03, 0.09 }[v % 6];
            double[] c;
            switch (n)
            {
                case 1: c = new double[0]; break;
                case 2: c = new[] { 0.35 }; break;
                case 3: c = new[] { 0.22, 0.78 }; break;
                case 4: c = new[] { 0.14, 0.48, 0.84 }; break;
                default: c = Enumerable.Range(1, n - 1).Select(i => i / (double)n).ToArray(); break;
            }
            return c.Select(x => Math.Max(0.02, Math.Min(0.98, x + shift))).ToArray();
        }

        /// <summary>The sprite's shades closest to the thing's colour: its colour family, plus a darker one for depth.</summary>
        public static List<int> SpriteRamp(int avg, ICollection<int> spriteColors)
        {
            var cols = spriteColors.Where(c => c != 0).Distinct().ToList();
            if (cols.Count == 0) return new List<int>();
            // the sprite colour nearest the thing (hue counts more than lightness: lighting in photos varies)
            double ah, asat, al; ColorTools.ToHsl(avg, out ah, out asat, out al);
            int seed = cols.OrderBy(c =>
            {
                double h, s, l; ColorTools.ToHsl(c, out h, out s, out l);
                double dh = Math.Abs(h - ah) % 360; if (dh > 180) dh = 360 - dh;
                bool greyA = Chroma(avg) < 25, greyC = Chroma(c) < 20;
                double hueCost = greyA || greyC ? (greyA == greyC ? 0 : 60) : dh;
                return hueCost * 2 + Math.Abs(L(c) - L(avg)) * 120 + Math.Abs(Chroma(c) - Chroma(avg)) * 0.3;
            }).First();
            // its shades: same family and not too far from it in lightness (skin is near brown hair in hue, but much lighter)
            var fam = cols.Where(c => ShadingHelper.Related(c, seed) && Math.Abs(L(c) - L(seed)) <= 0.3).OrderBy(c => L(c)).ToList();
            // a darker shade for depth: a dark grey or a darker shade of the same hue (not some other colour like navy)
            var darker = cols.Where(c => L(c) < L(fam[0]) - 0.08 && L(c) > 0.04 && (Chroma(c) < 20 || ShadingHelper.Related(c, seed)))
                             .OrderByDescending(c => L(c)).FirstOrDefault();
            if (darker != 0) fam.Insert(0, darker);
            // drop near-duplicates, keep at most 4
            var ramp = new List<int>();
            foreach (var c in fam) if (ramp.Count == 0 || L(c) - L(ramp[ramp.Count - 1]) >= 0.06) ramp.Add(c);
            while (ramp.Count > 4) ramp.RemoveAt(ramp.Count / 2);
            return ramp;
        }

        static int Chroma(int c)
        {
            int r = PixelImage.R(c), g = PixelImage.G(c), b = PixelImage.B(c);
            return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        }

        static List<int> PhotoRamp(List<int> cols)
        {
            // four tones from the photo itself: the average of each lightness quarter
            var s = cols.OrderBy(c => L(c)).ToList();
            var ramp = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                var part = s.Skip(i * s.Count / 4).Take(Math.Max(1, s.Count / 4)).ToList();
                if (part.Count > 0) ramp.Add(Average(part));
            }
            return ramp.Distinct().ToList();
        }

        /// <summary>Keeps only the connected pieces of the mask that are at least 'min' of the biggest.</summary>
        static void KeepBigParts(bool[] m, int w, int h, double min)
        {
            var id = new int[w * h]; var sizes = new List<int> { 0 };
            for (int i = 0; i < m.Length; i++)
            {
                if (!m[i] || id[i] != 0) continue;
                int n = sizes.Count, size = 0;
                var st = new Stack<int>(); st.Push(i); id[i] = n;
                while (st.Count > 0)
                {
                    int p = st.Pop(); size++;
                    int x = p % w, y = p / w;
                    foreach (int q in new[] { x > 0 ? p - 1 : -1, x < w - 1 ? p + 1 : -1, y > 0 ? p - w : -1, y < h - 1 ? p + w : -1 })
                        if (q >= 0 && m[q] && id[q] == 0) { id[q] = n; st.Push(q); }
                }
                sizes.Add(size);
            }
            int big = sizes.Max();
            for (int i = 0; i < m.Length; i++) if (m[i] && sizes[id[i]] < big * min) m[i] = false;
        }

        /// <summary>Drops lone pixels and fills one-pixel holes.</summary>
        static void TidyShape(bool[] on, int w, int h)
        {
            Func<int, int, bool> at = (x, y) => x >= 0 && y >= 0 && x < w && y < h && on[y * w + x];
            for (int pass = 0; pass < 2; pass++)
            {
                var flip = new List<int>();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int n = (at(x - 1, y) ? 1 : 0) + (at(x + 1, y) ? 1 : 0) + (at(x, y - 1) ? 1 : 0) + (at(x, y + 1) ? 1 : 0);
                        if (at(x, y) && n == 0) flip.Add(y * w + x);
                        else if (!at(x, y) && n >= 3 && x > 0 && y > 0 && x < w - 1 && y < h - 1) flip.Add(y * w + x);
                    }
                foreach (var i in flip) on[i] = !on[i];
            }
        }

        /// <summary>A pixel whose tone none of its neighbours share takes the most common neighbouring tone (clumps, not speckles).</summary>
        static void TidyTones(bool[] on, int[] tone, int w, int h)
        {
            var copy = (int[])tone.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!on[i]) continue;
                    var nb = new List<int>();
                    if (x > 0 && on[i - 1]) nb.Add(copy[i - 1]);
                    if (x < w - 1 && on[i + 1]) nb.Add(copy[i + 1]);
                    if (y > 0 && on[i - w]) nb.Add(copy[i - w]);
                    if (y < h - 1 && on[i + w]) nb.Add(copy[i + w]);
                    if (nb.Count >= 3 && !nb.Contains(copy[i]))
                        tone[i] = nb.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
                }
        }
    }
}
