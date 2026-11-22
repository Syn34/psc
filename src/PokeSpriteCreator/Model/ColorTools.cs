using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    /// <summary>Colour maths: hue/saturation/brightness shifts and palette reduction.</summary>
    public static class ColorTools
    {
        public static void ToHsl(int c, out double h, out double s, out double l)
        {
            double r = PixelImage.R(c) / 255.0, g = PixelImage.G(c) / 255.0, b = PixelImage.B(c) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (max == min) { h = 0; s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }

        public static int FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360; s = Math.Max(0, Math.Min(1, s)); l = Math.Max(0, Math.Min(1, l));
            if (s == 0) { int v = (int)Math.Round(l * 255); return PixelImage.Rgb(v, v, v); }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            return PixelImage.Rgb((int)Math.Round(Hue(p, q, h / 360 + 1.0 / 3) * 255), (int)Math.Round(Hue(p, q, h / 360) * 255), (int)Math.Round(Hue(p, q, h / 360 - 1.0 / 3) * 255));
        }

        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        /// <summary>
        /// Shifts a colour. hue in degrees (-180..180), sat and light in percent (-100..100).
        /// Greys pick up colour when saturation goes up and a hue is given, so a white shirt can be dyed.
        /// </summary>
        public static int Adjust(int c, int hue, int sat, int light)
        {
            if (c == 0) return 0;
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            h += hue;
            if (sat > 0) s = s + (1 - s) * sat / 100.0 * (s < 0.05 ? 0.6 : 1); else s = s * (1 + sat / 100.0);
            if (light > 0) l = l + (1 - l) * light / 100.0; else l = l * (1 + light / 100.0);
            return FromHsl(h, s, l);
        }

        /// <summary>Dark colours treated as outline (kept when a part is re-coloured).</summary>
        public static bool IsOutline(int c) { return c != 0 && PixelImage.Luma(c) < 0.16; }

        /// <summary>Distinct colours of an image, darkest first.</summary>
        public static List<int> Palette(PixelImage img)
        {
            return img.Colors().OrderBy(c => PixelImage.Luma(c)).ThenBy(c => c).ToList();
        }

        static int Dist(int a, int b)
        {
            int dr = PixelImage.R(a) - PixelImage.R(b), dg = PixelImage.G(a) - PixelImage.G(b), db = PixelImage.B(a) - PixelImage.B(b);
            return dr * dr * 3 + dg * dg * 4 + db * db * 2;
        }

        public static int Nearest(int c, IList<int> pal)
        {
            int best = pal[0], bd = int.MaxValue;
            foreach (var p in pal) { int d = Dist(c, p); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        /// <summary>Gen 1 look: every colour becomes one of four greys by brightness.</summary>
        public static PixelImage ToGreyscale4(PixelImage img, int[] shades)
        {
            var r = img.Clone();
            var pal = shades.OrderByDescending(c => PixelImage.Luma(c)).ToArray(); // light → dark
            for (int i = 0; i < r.Px.Length; i++)
            {
                int c = r.Px[i];
                if (c == 0) continue;
                double l = PixelImage.Luma(c);
                int idx = l > 0.80 ? 0 : l > 0.50 ? 1 : l > 0.22 ? 2 : 3;
                r.Px[i] = pal[Math.Min(idx, pal.Length - 1)];
            }
            return r;
        }

        /// <summary>
        /// Reduces an image to at most max colours (median cut over the colours actually used,
        /// weighted by how many pixels use them). Images already within the limit come back unchanged.
        /// </summary>
        public static PixelImage Reduce(PixelImage img, int max)
        {
            var counts = new Dictionary<int, int>();
            foreach (var c in img.Px) if (c != 0) { int n; counts.TryGetValue(c, out n); counts[c] = n + 1; }
            if (counts.Count <= max) return img.Clone();
            var pal = MedianCut(counts, max);
            var map = new Dictionary<int, int>();
            foreach (var c in counts.Keys) map[c] = Nearest(c, pal);
            var r = img.Clone();
            for (int i = 0; i < r.Px.Length; i++) if (r.Px[i] != 0) r.Px[i] = map[r.Px[i]];
            return r;
        }

        /// <summary>Applies one palette reduction to several images together (so every frame shares the same colours).</summary>
        public static List<PixelImage> ReduceTogether(IList<PixelImage> imgs, int max)
        {
            Dictionary<int, int> merged;
            return ReduceTogether(imgs, max, new List<int>(), out merged);
        }

        /// <summary>Keeps the sprite to 'max' colours without touching colours that were already there.
        /// 'order' is the sprite's colours in the order they arrived (kept between calls by the document):
        /// the first 'max' are shown as they are, and any newer colour is shown as the closest of those.
        /// 'merged' = each colour that had to be changed -> the colour it's shown as.</summary>
        public static List<PixelImage> ReduceTogether(IList<PixelImage> imgs, int max, List<int> order, out Dictionary<int, int> merged)
        {
            var counts = new Dictionary<int, int>();
            foreach (var img in imgs) foreach (var c in img.Px) if (c != 0) { int n; counts.TryGetValue(c, out n); counts[c] = n + 1; }
            UpdateOrder(order, counts, max);
            merged = new Dictionary<int, int>();
            if (counts.Count <= max) return imgs.Select(i => i.Clone()).ToList();
            var kept = order.Take(max).ToList();
            var keep = new HashSet<int>(kept);
            var map = new Dictionary<int, int>();
            foreach (var c in counts.Keys)
            {
                map[c] = keep.Contains(c) ? c : Nearest(c, kept);
                if (map[c] != c) merged[c] = map[c];
            }
            return imgs.Select(img => { var r = img.Clone(); for (int i = 0; i < r.Px.Length; i++) if (r.Px[i] != 0) r.Px[i] = map[r.Px[i]]; return r; }).ToList();
        }

        /// <summary>Drops colours no longer used and adds new ones at the end (most used first).
        /// A fresh picture with too many colours starts with a median-cut choice so it reduces well.</summary>
        static void UpdateOrder(List<int> order, Dictionary<int, int> counts, int max)
        {
            order.RemoveAll(c => !counts.ContainsKey(c));
            var have = new HashSet<int>(order);
            if (order.Count == 0 && counts.Count > max * 2)   // a picture with lots of colours (a few over: keep the most used)
                foreach (var c in MedianCut(counts, max).Distinct().OrderByDescending(c => counts[c]))
                    if (have.Add(c)) order.Add(c);
            foreach (var kv in counts.Where(kv => !have.Contains(kv.Key)).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
                order.Add(kv.Key);
        }

        static List<int> MedianCut(Dictionary<int, int> counts, int max)
        {
            var boxes = new List<List<KeyValuePair<int, int>>> { counts.ToList() };
            while (boxes.Count < max)
            {
                // split the box with the widest colour range (that still has 2+ colours)
                List<KeyValuePair<int, int>> pick = null; int pickRange = -1, pickCh = 0;
                foreach (var b in boxes)
                {
                    if (b.Count < 2) continue;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        int lo = 255, hi = 0;
                        foreach (var kv in b) { int v = Channel(kv.Key, ch); if (v < lo) lo = v; if (v > hi) hi = v; }
                        if (hi - lo > pickRange) { pickRange = hi - lo; pick = b; pickCh = ch; }
                    }
                }
                if (pick == null) break;
                int chn = pickCh;
                pick.Sort((a, b) => Channel(a.Key, chn).CompareTo(Channel(b.Key, chn)));
                long total = pick.Sum(kv => (long)kv.Value), acc = 0; int cut = 1;
                for (int i = 0; i < pick.Count - 1; i++) { acc += pick[i].Value; if (acc * 2 >= total) { cut = i + 1; break; } cut = i + 1; }
                boxes.Remove(pick);
                boxes.Add(pick.GetRange(0, cut));
                boxes.Add(pick.GetRange(cut, pick.Count - cut));
            }
            var pal = new List<int>();
            foreach (var b in boxes)
            {
                long r = 0, g = 0, bl = 0, n = 0;
                foreach (var kv in b) { r += PixelImage.R(kv.Key) * (long)kv.Value; g += PixelImage.G(kv.Key) * (long)kv.Value; bl += PixelImage.B(kv.Key) * (long)kv.Value; n += kv.Value; }
                if (n == 0) continue;
                int avg = PixelImage.Rgb((int)(r / n), (int)(g / n), (int)(bl / n));
                // snap to the most used real colour in the box (keeps the artist's own colours)
                pal.Add(b.OrderBy(kv => Dist(kv.Key, avg)).ThenByDescending(kv => kv.Value).First().Key);
            }
            return pal;
        }

        static int Channel(int c, int ch) { return ch == 0 ? PixelImage.R(c) : ch == 1 ? PixelImage.G(c) : PixelImage.B(c); }
    }
}
