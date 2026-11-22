using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    public enum LightFrom { Auto, Left, TopLeft, Top, TopRight, Right }
    public enum OutlineMode { Outside, Edge, None }

    public class ShadeOptions
    {
        public LightFrom Light = LightFrom.Auto;
        public OutlineMode Outline = OutlineMode.Outside;
        public bool CastShadow = true;          // darker row just under a part that overlaps from above
        public bool SoftLitOutline = false;     // lighter outline on the side facing the light
        public bool OnlyExisting = true;        // never suggest colours that aren't in the sprite yet
        public bool Natural = false;            // may change the shape too: necks, gaps, bumps, stray pixels
    }

    /// <summary>The colours a suggestion uses (0 = none).</summary>
    public class ShadeRamp
    {
        public int Outline, Under, Shadow, Base, Highlight;
        public readonly HashSet<int> New = new HashSet<int>();
    }

    public class ShadeSuggestion
    {
        public Rectangle Area;                  // what the user picked (clipped to the frame)
        public Rectangle Region;                // Area plus the 1-pixel border an outside outline may use
        public PixelImage Before, After;        // the layer's whole frame, before and after
        public PixelImage Overlay;              // only the changed pixels (for drawing over the canvas)
        public ShadeRamp Ramp;
        public LightFrom Light;                 // the light used (never Auto)
        public bool LightGuessed;
        public int Changed, Pixels;
        public readonly List<string> Notes = new List<string>();
        public bool HasChanges { get { return Changed > 0; } }
    }

    /// <summary>
    /// Suggests shading for one part of a sprite (a leg, an arm, a sleeve), the way a pixel artist would fix it:
    /// light from one side (worked out from the rest of the sprite), a dark outline where the part meets the
    /// background, the shadow tone down the side away from the light, a thin highlight one pixel in from the lit
    /// edge, and a darker row under anything that overlaps it from above. It uses the part's own colour family
    /// from the sprite's palette, so it normally adds no colours.
    /// </summary>
    public static class ShadingHelper
    {
        // ------------------------------------------------------------------ colour families

        static int Chroma(int c)
        {
            int r = PixelImage.R(c), g = PixelImage.G(c), b = PixelImage.B(c);
            return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        }

        static double Hue(int c) { double h, s, l; ColorTools.ToHsl(c, out h, out s, out l); return h; }

        static double HueDiff(double a, double b) { double d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }

        /// <summary>Two colours are shades of the same thing: both greyish, or close in hue.</summary>
        public static bool Related(int a, int b)
        {
            if (a == 0 || b == 0) return false;
            if (a == b) return true;
            bool ga = Chroma(a) < 20, gb = Chroma(b) < 20;
            if (ga || gb) return ga && gb;
            return HueDiff(Hue(a), Hue(b)) <= 18;
        }

        static double L(int c) { return PixelImage.Luma(c); }

        /// <summary>Clicking instead of dragging: the box around the connected part of the clicked colour's family.</summary>
        public static Rectangle AreaAround(PixelImage img, Point p)
        {
            if (!img.Inside(p.X, p.Y)) return Rectangle.Empty;
            int start = img.Get(p.X, p.Y);
            if (start == 0) return Rectangle.Empty;
            double thr = L(start) * 0.55;
            var seen = new bool[img.W * img.H];
            var stack = new Stack<Point>();
            stack.Push(p); seen[p.Y * img.W + p.X] = true;
            int x0 = p.X, y0 = p.Y, x1 = p.X, y1 = p.Y;
            while (stack.Count > 0)
            {
                var q = stack.Pop();
                x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                foreach (var d in Four)
                {
                    int nx = q.X + d.X, ny = q.Y + d.Y;
                    if (!img.Inside(nx, ny) || seen[ny * img.W + nx]) continue;
                    int c = img.Get(nx, ny);
                    if (!Related(c, start) || L(c) < thr) continue;
                    seen[ny * img.W + nx] = true;
                    stack.Push(new Point(nx, ny));
                }
            }
            return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        static readonly Point[] Four = { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) };

        // ------------------------------------------------------------------ light

        static Point Vec(LightFrom l)
        {
            switch (l)
            {
                case LightFrom.Left: return new Point(-1, 0);
                case LightFrom.TopLeft: return new Point(-1, -1);
                case LightFrom.Top: return new Point(0, -1);
                case LightFrom.TopRight: return new Point(1, -1);
                default: return new Point(1, 0);
            }
        }

        public static string Describe(LightFrom l)
        {
            switch (l)
            {
                case LightFrom.Left: return "the left";
                case LightFrom.TopLeft: return "the top left";
                case LightFrom.Top: return "above";
                case LightFrom.TopRight: return "the top right";
                case LightFrom.Right: return "the right";
                default: return "automatic";
            }
        }

        /// <summary>
        /// Which side the sprite is lit from: across every row, compares the inside pixels just in from the
        /// left and right edges of each solid stretch. Brighter on the right = light from the right.
        /// </summary>
        public static LightFrom GuessLight(PixelImage img)
        {
            double sum = 0; int n = 0;
            for (int y = 0; y < img.H; y++)
            {
                int x = 0;
                while (x < img.W)
                {
                    while (x < img.W && img.Get(x, y) == 0) x++;
                    int a = x;
                    while (x < img.W && img.Get(x, y) != 0) x++;
                    int b = x - 1;
                    // step in past the outline (dark pixels) at both ends, then compare the first two pixels inside
                    int ia = a, ib = b;
                    while (ia <= b && L(img.Get(ia, y)) < 0.25) ia++;
                    while (ib >= a && L(img.Get(ib, y)) < 0.25) ib--;
                    if (ib - ia + 1 < 5) continue;
                    double left = (L(img.Get(ia, y)) + L(img.Get(ia + 1, y))) / 2;
                    double right = (L(img.Get(ib, y)) + L(img.Get(ib - 1, y))) / 2;
                    sum += right - left; n++;
                }
            }
            if (n == 0) return LightFrom.Right;
            double avg = sum / n;
            if (avg > 0.015) return LightFrom.Right;
            if (avg < -0.015) return LightFrom.Left;
            return LightFrom.Top;
        }

        // ------------------------------------------------------------------ made-up tones (only when allowed)

        static double TowardHue(double h, double target, double amount)
        {
            double d = ((target - h) % 360 + 540) % 360 - 180;   // shortest way round
            return h + Math.Sign(d) * Math.Min(Math.Abs(d), amount);
        }

        public static int MakeShadow(int c)
        {
            double h, s, l; ColorTools.ToHsl(c, out h, out s, out l);
            if (Chroma(c) >= 20) h = TowardHue(h, 250, 10);
            return ColorTools.FromHsl(h, Math.Min(1, s * 1.1), l * 0.78);
        }

        public static int MakeHighlight(int c)
        {
            double h, s, l; ColorTools.ToHsl(c, out h, out s, out l);
            if (Chroma(c) >= 20) h = TowardHue(h, 55, 8);
            return ColorTools.FromHsl(h, s * 0.95, l + (1 - l) * 0.45);
        }

        // ------------------------------------------------------------------ the suggestion

        public static ShadeSuggestion Suggest(PixelImage layer, PixelImage composite, Rectangle area, ICollection<int> spriteColors, ShadeOptions o)
        {
            var s = new ShadeSuggestion { Before = layer, After = layer.Clone(), Ramp = new ShadeRamp() };
            area.Intersect(new Rectangle(0, 0, layer.W, layer.H));
            s.Area = area;
            s.Region = area;
            s.Overlay = new PixelImage(layer.W, layer.H);
            if (area.Width <= 0 || area.Height <= 0) { s.Notes.Add("Drag a box over one part of the sprite (a leg, an arm, a sleeve), or click it."); return s; }

            // 1. the part: the biggest colour family in the box
            var counts = new Dictionary<int, int>();
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int c = layer.Get(x, y);
                    if (c != 0) { int n; counts.TryGetValue(c, out n); counts[c] = n + 1; }
                }
            if (counts.Count == 0) { s.Notes.Add("There's nothing on the selected layer in that box."); return s; }
            int top = 0, topFamily = -1;
            // near-black is nearly always outline (or a dark shirt): only pick it when there's nothing lighter in the box
            bool anyLight = counts.Keys.Any(c => L(c) >= 0.25);
            foreach (var c in counts.Keys)
            {
                if (anyLight && L(c) < 0.25) continue;
                int fam = counts.Where(kv => Related(kv.Key, c) && (!anyLight || L(kv.Key) >= 0.25)).Sum(kv => kv.Value);
                if (fam > topFamily || (fam == topFamily && counts[c] > counts[top])) { topFamily = fam; top = c; }
            }
            // the most used colour of that family is the part's main colour
            top = counts.Where(kv => Related(kv.Key, top)).OrderByDescending(kv => kv.Value).First().Key;
            double thr = L(top) * 0.55;   // much darker than the part = outline

            // 2. its tones in the whole sprite, light to dark
            var family = spriteColors.Concat(counts.Keys).Distinct().Where(c => Related(c, top)).ToList();
            var light = family.Where(c => L(c) >= thr).OrderBy(c => L(c)).ToList();
            var dark = family.Where(c => L(c) < thr).OrderBy(c => L(c)).ToList();
            var ramp = s.Ramp;
            int nl = light.Count;
            if (nl == 0) ramp.Base = top;
            else
            {
                // the main colour = the shade the part uses most; with 4+ shades a most-used lightest one is an
                // overused highlight, so the next one down is the main colour
                int mi = Enumerable.Range(0, nl).OrderByDescending(i => { int n; counts.TryGetValue(light[i], out n); return n; }).ThenByDescending(i => i).First();
                if (mi == nl - 1 && nl >= 4) mi = nl - 2;
                ramp.Base = light[mi];
                if (mi < nl - 1) ramp.Highlight = light[nl - 1 == mi + 1 || nl >= 4 ? nl - 1 : mi + 1];
                if (mi >= 1) ramp.Shadow = light[mi - 1];
                if (mi >= 2) ramp.Under = light[mi - 2];
            }
            if (ramp.Shadow == 0)
            {
                if (!o.OnlyExisting) { ramp.Shadow = MakeShadow(ramp.Base); ramp.New.Add(ramp.Shadow); }
                else s.Notes.Add("There's only one shade of this colour, so there's no darker tone to shade with. Untick \"Only colours already in the sprite\" to get one (" + PixelImage.Hex(MakeShadow(ramp.Base)) + ").");
            }
            if (ramp.Highlight == 0)
            {
                if (!o.OnlyExisting) { ramp.Highlight = MakeHighlight(ramp.Base); ramp.New.Add(ramp.Highlight); }
                else if (nl <= 1) s.Notes.Add("No lighter shade of this colour for a highlight. Untick \"Only colours already in the sprite\" to get one (" + PixelImage.Hex(MakeHighlight(ramp.Base)) + ").");
            }
            if (ramp.Under == 0)
            {
                // a darker tone again for the shadow under overlapping parts
                var darker = light.Where(c => L(c) < L(ramp.Shadow == 0 ? ramp.Base : ramp.Shadow)).ToList();
                ramp.Under = darker.Count > 0 ? darker[darker.Count - 1] : (dark.Count > 0 && L(dark[dark.Count - 1]) > L(dark[0]) ? dark[dark.Count - 1] : (ramp.Shadow != 0 ? ramp.Shadow : ramp.Base));
            }
            if (ramp.Shadow == 0) ramp.Shadow = ramp.Base;
            if (ramp.Highlight == 0) ramp.Highlight = ramp.Base;
            ramp.Outline = dark.Count > 0 ? dark[0]
                         : spriteColors.Where(c => c != 0 && L(c) < thr).OrderBy(c => L(c)).FirstOrDefault();
            if (ramp.Outline == 0)
            {
                if (!o.OnlyExisting) { ramp.Outline = ColorTools.FromHsl(Hue(ramp.Base), 0.25, Math.Max(0.08, L(ramp.Base) * 0.35)); ramp.New.Add(ramp.Outline); }
                else if (o.Outline != OutlineMode.None) s.Notes.Add("This colour is too dark for an outline from your colours, so none is added.");
            }

            // 3. the part's pixels in the box, and what's around them
            int W = layer.W, H = layer.H;
            var inPart = new bool[W * H];
            int partCount = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int c = layer.Get(x, y);
                    if (Related(c, top) && L(c) >= thr) { inPart[y * W + x] = true; partCount++; }
                }
            s.Pixels = partCount;
            Func<int, int, bool> part = (x, y) => x >= 0 && y >= 0 && x < W && y < H && inPart[y * W + x];
            var after = s.After;
            // empty = nothing there now (a pixel this tool removed counts as empty even though the old picture showed it)
            Func<int, int, bool> empty = (x, y) => x >= 0 && y >= 0 && x < W && y < H && after.Get(x, y) == 0 && (composite.Get(x, y) == 0 || layer.Get(x, y) != 0);

            // 3b. "more natural": fix the shape as well as the shading
            var nat = new NaturalInfo();
            if (o.Natural) Naturalise(after, composite, area, inPart, top, thr, ramp, nat);
            Func<int, bool> inNeck = y => nat.NeckTop >= 0 && y >= nat.NeckTop && y <= nat.NeckBottom;

            // edges against the background
            var bgEdge = new List<Point>();       // part pixels touching the background
            var outside = new HashSet<Point>();   // background pixels next to the part
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    if (!part(x, y)) continue;
                    bool touches = false;
                    foreach (var d in Four)
                        if (empty(x + d.X, y + d.Y)) { touches = true; outside.Add(new Point(x + d.X, y + d.Y)); }
                    if (touches) bgEdge.Add(new Point(x, y));
                }

            // 4. light
            s.Light = o.Light == LightFrom.Auto ? GuessLight(composite) : o.Light;
            s.LightGuessed = o.Light == LightFrom.Auto;
            var lv = Vec(s.Light);

            // "on the edge": the edge pixels become the outline, the rest is shaded
            var edgeSet = new HashSet<Point>();
            if (o.Outline == OutlineMode.Edge && ramp.Outline != 0)
                foreach (var p in bgEdge) { edgeSet.Add(p); inPart[p.Y * W + p.X] = false; }

            // details drawn in a darker shade inside the part (a mouth, a nose, a crease) stay as they are:
            // small clusters (up to 4 pixels) darker than the main colour that don't touch the part's edge
            var detail = new HashSet<Point>();
            {
                Func<int, int, bool> darkInside = (x, y) =>
                {
                    if (!part(x, y) || L(after.Get(x, y)) >= L(ramp.Base) - 0.05) return false;
                    foreach (var d in Four) if (!part(x + d.X, y + d.Y) && !edgeSet.Contains(new Point(x + d.X, y + d.Y))) return false;
                    return true;
                };
                var seen = new HashSet<Point>();
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        var p0 = new Point(x, y);
                        if (seen.Contains(p0) || !darkInside(x, y)) continue;
                        var cluster = new List<Point>(); var st = new Stack<Point>(); st.Push(p0); seen.Add(p0);
                        while (st.Count > 0)
                        {
                            var q = st.Pop(); cluster.Add(q);
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    var r = new Point(q.X + dx, q.Y + dy);
                                    if (!seen.Contains(r) && darkInside(r.X, r.Y)) { seen.Add(r); st.Push(r); }
                                }
                        }
                        if (cluster.Count <= 4) foreach (var q in cluster) detail.Add(q);
                    }
            }

            // 5. tones by distance to the lit and the shadowed side
            int underCount = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    if (!part(x, y) || (detail.Contains(new Point(x, y)) && !inNeck(y))) continue;
                    int dl = 1; while (part(x + lv.X * dl, y + lv.Y * dl)) dl++;
                    int ds = 1; while (part(x - lv.X * ds, y - lv.Y * ds)) ds++;
                    int width = dl + ds - 1;
                    int tone;
                    bool under = false;
                    if (o.CastShadow && y > 0)
                    {
                        // something solid sits right above (not just a 1-pixel outline over empty space)
                        int up = layer.Get(x, y - 1), up2 = y > 1 ? layer.Get(x, y - 2) : 0;
                        if (up != 0 && !part(x, y - 1) && !edgeSet.Contains(new Point(x, y - 1)) && up2 != 0) under = true;
                    }
                    if (inNeck(y))
                    {
                        // a neck (or anything tucked under a wider part of the same colour) is in that part's shadow:
                        // a darker row right under it, then the shadow tone, with a sliver of light on the lit side
                        tone = y == nat.NeckTop ? ramp.Under : (width >= 4 && dl == 2 ? ramp.Base : ramp.Shadow);
                    }
                    else if (under) { tone = ramp.Under; underCount++; }
                    else if (width <= 2) tone = (width == 2 && ds == 1) ? ramp.Shadow : ramp.Base;
                    else
                    {
                        int shadowBand = Math.Max(1, (int)Math.Round(width * 0.25));
                        bool hi = width >= 4 && (dl == 2 || (width >= 8 && dl == 3));
                        tone = ds <= shadowBand ? ramp.Shadow : hi ? ramp.Highlight : ramp.Base;
                    }
                    after.Set(x, y, tone);
                }

            // 6. outline
            if (ramp.Outline != 0 && o.Outline != OutlineMode.None)
            {
                var lit = o.SoftLitOutline ? (ramp.Under != ramp.Base ? ramp.Under : ramp.Shadow) : ramp.Outline;
                if (o.Outline == OutlineMode.Outside)
                {
                    foreach (var q in outside)
                    {
                        // which side of the part it's on
                        bool litSide = false;
                        foreach (var d in Four)
                            if (part(q.X - d.X, q.Y - d.Y) && d.X * lv.X + d.Y * lv.Y > 0) litSide = true;
                        after.Set(q.X, q.Y, litSide ? lit : ramp.Outline);
                    }
                    if (outside.Count > 0) s.Region = Rectangle.Intersect(Rectangle.Inflate(area, 1, 1), new Rectangle(0, 0, W, H));
                }
                else
                {
                    foreach (var p in edgeSet)
                    {
                        bool litSide = false;
                        foreach (var d in Four)
                            if (empty(p.X + d.X, p.Y + d.Y) && d.X * lv.X + d.Y * lv.Y > 0) litSide = true;
                        after.Set(p.X, p.Y, litSide ? lit : ramp.Outline);
                    }
                }
            }

            // 7. what changed, and why
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (after.Get(x, y) != layer.Get(x, y)) { s.Changed++; s.Overlay.Set(x, y, after.Get(x, y)); }
            Explain(s, layer, inPart, part, bgEdge, outside, lv, o, underCount, counts, top);
            if (detail.Count > 0)
            {
                int at2 = s.Notes.FindIndex(n => n.StartsWith("Changes ") || n.StartsWith("Nothing to change"));
                s.Notes.Insert(at2 < 0 ? s.Notes.Count : at2, "Keeps " + detail.Count + " small detail pixel" + (detail.Count == 1 ? "" : "s") + " drawn in a darker shade (like a mouth or nose).");
            }
            // the "Changes N pixels" line goes last
            int ci = s.Notes.FindIndex(n => n.StartsWith("Changes ") || n.StartsWith("Nothing to change"));
            if (ci >= 0 && ci != s.Notes.Count - 1) { var c = s.Notes[ci]; s.Notes.RemoveAt(ci); s.Notes.Add(c); }
            if (o.Natural)
            {
                int at = s.Notes.FindIndex(n => n.StartsWith("Changes ") || n.StartsWith("Nothing to change"));
                if (at < 0) at = s.Notes.Count;
                var nn = new List<string>();
                if (nat.NeckTop >= 0)
                    nn.Add("It's a narrower part tucked under a wider part of the same colour (like a neck under the head), so it's put in that part's shadow: a darker row right under it and the shadow tone below, with a little light on the lit side.");
                if (nat.Narrowed > 0)
                    nn.Add("The neck was nearly as wide as the head (" + nat.WidthBefore + " of " + nat.HeadWidth + " pixels), so it's narrowed to " + nat.WidthAfter + ". Necks are a bit over half as wide as the head.");
                if (nat.Specks > 0) nn.Add("Removes " + nat.Specks + " stray dark pixel" + (nat.Specks == 1 ? "" : "s") + " inside it.");
                if (nat.Gaps > 0) nn.Add("Fills " + nat.Gaps + " gap" + (nat.Gaps == 1 ? "" : "s") + " in its edge.");
                if (nat.Bumps > 0) nn.Add("Smooths " + nat.Bumps + " one-pixel bump" + (nat.Bumps == 1 ? "" : "s") + " on its edge.");
                if (nn.Count == 0) nn.Add("The shape looks fine, so only the shading changes.");
                s.Notes.InsertRange(at, nn);
            }
            return s;
        }

        class NaturalInfo
        {
            public int NeckTop = -1, NeckBottom = -1, HeadWidth, WidthBefore, WidthAfter, Narrowed, Specks, Gaps, Bumps;
        }

        /// <summary>
        /// Shape fixes for "more natural": fills gaps and stray dark specks inside the part, smooths one-pixel bumps,
        /// and finds a neck (a narrower stretch hanging under a wider mass of the same colour), narrowing it when it's
        /// nearly as wide as the head. Changes only pixels in the box; updates the part mask.
        /// </summary>
        static void Naturalise(PixelImage after, PixelImage composite, Rectangle area, bool[] inPart, int top, double thr, ShadeRamp ramp, NaturalInfo nat)
        {
            int W = after.W, H = after.H;
            Func<int, int, bool> part = (x, y) => x >= 0 && y >= 0 && x < W && y < H && inPart[y * W + x];
            Func<int, bool> fam = c => Related(c, top) && L(c) >= thr;

            // gaps and specks: a dark pixel with the part on all four sides, or an empty one with it on three
            for (int pass = 0; pass < 2; pass++)
            {
                var add = new List<Point>();
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        if (part(x, y)) continue;
                        int c = after.Get(x, y), n = 0;
                        foreach (var d in Four) if (part(x + d.X, y + d.Y)) n++;
                        bool darkSpeck = c != 0 && L(c) < thr && n == 4;
                        bool gap = c == 0 && composite.Get(x, y) == 0 && n >= 3;
                        if (darkSpeck || gap) { add.Add(new Point(x, y)); if (darkSpeck) nat.Specks++; else nat.Gaps++; }
                    }
                foreach (var p in add) { inPart[p.Y * W + p.X] = true; after.Set(p.X, p.Y, ramp.Base); }
                if (add.Count == 0) break;
            }

            // one-pixel bumps sticking out of a solid edge (a 1-pixel-wide line keeps its tip)
            var bumps = new List<Point>();
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    if (!part(x, y)) continue;
                    int n = 0; Point nb = Point.Empty;
                    foreach (var d in Four) if (part(x + d.X, y + d.Y)) { n++; nb = new Point(x + d.X, y + d.Y); }
                    if (n != 1) continue;
                    int nn = 0;
                    foreach (var d in Four) if (part(nb.X + d.X, nb.Y + d.Y)) nn++;
                    if (nn >= 3) bumps.Add(new Point(x, y));
                }
            foreach (var p in bumps)
            {
                // becomes what's around it
                var around = Four.Select(d => new Point(p.X + d.X, p.Y + d.Y)).Where(q => after.Inside(q.X, q.Y) && !part(q.X, q.Y))
                                 .Select(q => after.Get(q.X, q.Y)).GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                inPart[p.Y * W + p.X] = false;
                after.Set(p.X, p.Y, around);
                nat.Bumps++;
            }

            // the whole part (also outside the box, for what's above it)
            var comp = new bool[W * H];
            var stack = new Stack<Point>();
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (part(x, y) && !comp[y * W + x]) { comp[y * W + x] = true; stack.Push(new Point(x, y)); }
            while (stack.Count > 0)
            {
                var q = stack.Pop();
                foreach (var d in Four)
                {
                    int nx = q.X + d.X, ny = q.Y + d.Y;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H || comp[ny * W + nx]) continue;
                    bool inside = area.Contains(nx, ny) ? part(nx, ny) : fam(after.Get(nx, ny));
                    if (!inside) continue;
                    comp[ny * W + nx] = true; stack.Push(new Point(nx, ny));
                }
            }
            var minX = new int[H]; var maxX = new int[H];
            for (int y = 0; y < H; y++)
            {
                minX[y] = -1; maxX[y] = -1;
                for (int x = 0; x < W; x++) if (comp[y * W + x]) { if (minX[y] < 0) minX[y] = x; maxX[y] = x; }
            }
            Func<int, int> width = y => y < 0 || y >= H || minX[y] < 0 ? 0 : maxX[y] - minX[y] + 1;

            // the neck: the first row in the box that steps in by 2+ under a wider mass (3+ wide rows above)
            for (int y = Math.Max(1, area.Top); y < area.Bottom; y++)
            {
                int wy = width(y), wa = width(y - 1);
                if (wy == 0 || wa == 0 || wa - wy < 2) continue;
                if (minX[y] < minX[y - 1] || maxX[y] > maxX[y - 1]) continue;   // hangs under it
                int rows = 0, headW = 0;
                for (int k = 1; k <= 8; k++) { int w = width(y - k); if (w >= wa - 1) rows++; headW = Math.Max(headW, w); }
                if (rows < 3) continue;
                nat.NeckTop = y; nat.HeadWidth = headW;
                break;
            }
            if (nat.NeckTop < 0) return;
            // down to where it widens again (shoulders, chest) or ends
            int narrowest = width(nat.NeckTop);
            nat.NeckBottom = nat.NeckTop;
            for (int y = nat.NeckTop + 1; y < area.Bottom && width(y) > 0 && width(y) <= narrowest + 1; y++) { nat.NeckBottom = y; narrowest = Math.Min(narrowest, width(y)); }

            // nearly as wide as the head: narrow it to about 60%, keeping its middle
            int target = Math.Max(3, (int)Math.Round(nat.HeadWidth * 0.6));
            for (int y = nat.NeckTop; y <= nat.NeckBottom; y++)
            {
                int w = width(y);
                if (w <= target || w <= nat.HeadWidth * 0.7) continue;
                if (nat.WidthBefore == 0) nat.WidthBefore = w;
                int left = minX[y] + (w - target) / 2, right = left + target - 1;
                int outsideL = minX[y] > 0 ? after.Get(minX[y] - 1, y) : 0, outsideR = maxX[y] < W - 1 ? after.Get(maxX[y] + 1, y) : 0;
                for (int x = minX[y]; x <= maxX[y]; x++)
                {
                    if ((x >= left && x <= right) || !area.Contains(x, y) || !part(x, y)) continue;
                    inPart[y * W + x] = false;
                    after.Set(x, y, x < left ? outsideL : outsideR);
                    nat.Narrowed++;
                }
                nat.WidthAfter = target;
            }
        }

        static void Explain(ShadeSuggestion s, PixelImage before, bool[] inPart, Func<int, int, bool> part, List<Point> bgEdge,
                            HashSet<Point> outside, Point lv, ShadeOptions o, int underCount, Dictionary<int, int> counts, int top)
        {
            var notes = new List<string>();
            var r = s.Ramp;
            notes.Add("Light from " + Describe(s.Light) + (s.LightGuessed ? " (worked out from the rest of the sprite)." : "."));
            if (s.Pixels == 0) { s.Notes.InsertRange(0, notes); return; }

            // outline
            if (bgEdge.Count > 0 && o.Outline != OutlineMode.None && r.Outline != 0)
                notes.Add("No outline where it meets the background, so it looks pasted on. " +
                          (o.Outline == OutlineMode.Outside ? "Adds a " + PixelImage.Hex(r.Outline) + " outline around it." : "Turns its edge into a " + PixelImage.Hex(r.Outline) + " outline."));
            else if (bgEdge.Count == 0) notes.Add("It's already outlined (or doesn't touch the background).");

            // one-sided or both-sided shading, measured on the picture as it is now
            double litSum = 0, shadeSum = 0, midSum = 0; int litN = 0, shadeN = 0, midN = 0;
            var A = s.Area;
            for (int y = A.Top; y < A.Bottom; y++)
                for (int x = A.Left; x < A.Right; x++)
                {
                    int c = before.Get(x, y);
                    if (!Related(c, top) || L(c) < L(top) * 0.55) continue;
                    Func<int, int, bool> inside = (px, py) => before.Inside(px, py) && Related(before.Get(px, py), top) && L(before.Get(px, py)) >= L(top) * 0.55;
                    int dl = 1; while (inside(x + lv.X * dl, y + lv.Y * dl)) dl++;
                    int ds = 1; while (inside(x - lv.X * ds, y - lv.Y * ds)) ds++;
                    if (dl + ds - 1 < 3) continue;
                    if (dl == 1) { litSum += L(c); litN++; }
                    else if (ds == 1) { shadeSum += L(c); shadeN++; }
                    else { midSum += L(c); midN++; }
                }
            if (litN > 0 && shadeN > 0)
            {
                double li = litSum / litN, sh = shadeSum / shadeN, mi = midN > 0 ? midSum / midN : (li + sh) / 2;
                string darkSide = DarkSide(s.Light);
                if (mi > li + 0.03 && mi > sh + 0.03 && Math.Abs(li - sh) < 0.04)
                    notes.Add("It's shaded on both sides with the light colour down the middle (\"pillow shading\"), which looks flat. Keep the dark tone on the side away from the light (" + darkSide + ").");
                else if (li <= sh + 0.01)
                    notes.Add("The lit side is as dark as the shadow side. Put the shadow on " + darkSide + " only.");
            }

            // highlight size
            int hiNow = 0, partNow = 0;
            for (int y = A.Top; y < A.Bottom; y++)
                for (int x = A.Left; x < A.Right; x++)
                {
                    int c = before.Get(x, y);
                    if (!Related(c, top) || L(c) < L(top) * 0.55) continue;
                    partNow++;
                    if (c == r.Highlight && r.Highlight != r.Base) hiNow++;
                }
            if (partNow > 0 && hiNow * 100 / partNow > 35)
                notes.Add("The highlight colour covers " + (hiNow * 100 / partNow) + "% of it. Keep highlights to a thin line one pixel in from the lit edge.");

            int tones = counts.Keys.Count(c => Related(c, top) && L(c) >= L(top) * 0.55);
            if (tones >= 5) notes.Add(tones + " shades of the same colour. Three (plus the outline) read better at this size.");
            if (underCount > 0) notes.Add("Adds a darker row (" + PixelImage.Hex(r.Under) + ") right under the part above it, as if it casts a shadow.");

            if (r.New.Count > 0)
                notes.Add("Uses " + r.New.Count + " new colour" + (r.New.Count > 1 ? "s" : "") + ": " + string.Join(", ", r.New.Select(PixelImage.Hex).ToArray()) + ".");
            notes.Add(s.Changed == 0 ? "Nothing to change: it already follows these rules." : "Changes " + s.Changed + " pixel" + (s.Changed == 1 ? "" : "s") + ". Apply puts it on the selected layer (Undo takes it off).");
            s.Notes.InsertRange(0, notes);
        }

        static string DarkSide(LightFrom l)
        {
            switch (l)
            {
                case LightFrom.Left: return "the right side";
                case LightFrom.Right: return "the left side";
                case LightFrom.TopLeft: return "the right and bottom";
                case LightFrom.TopRight: return "the left and bottom";
                default: return "the bottom";
            }
        }
    }
}
