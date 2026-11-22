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
            foreach (var c in counts.Keys)
            {
                int fam = counts.Where(kv => Related(kv.Key, c)).Sum(kv => kv.Value);
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
            if (nl >= 4)
            {
                ramp.Highlight = light[nl - 1];
                var mids = light.Skip(1).Take(nl - 2).ToList();
                ramp.Base = mids.OrderByDescending(c => { int n; counts.TryGetValue(c, out n); return n; }).ThenByDescending(c => L(c)).First();
                int bi = light.IndexOf(ramp.Base);
                ramp.Shadow = light[bi - 1];
                ramp.Under = bi >= 2 ? light[bi - 2] : (dark.Count > 0 ? dark[dark.Count - 1] : ramp.Shadow);
            }
            else if (nl == 3) { ramp.Shadow = light[0]; ramp.Base = light[1]; ramp.Highlight = light[2]; }
            else if (nl == 2) { ramp.Shadow = light[0]; ramp.Base = light[1]; }
            else ramp.Base = light.Count > 0 ? light[0] : top;
            if (ramp.Shadow == 0)
            {
                if (!o.OnlyExisting) { ramp.Shadow = MakeShadow(ramp.Base); ramp.New.Add(ramp.Shadow); }
                else s.Notes.Add("There's only one shade of this colour, so there's no darker tone to shade with. Untick \"Only colours already in the sprite\" to get one (" + PixelImage.Hex(MakeShadow(ramp.Base)) + ").");
            }
            if (ramp.Highlight == 0)
            {
                if (!o.OnlyExisting) { ramp.Highlight = MakeHighlight(ramp.Base); ramp.New.Add(ramp.Highlight); }
                else s.Notes.Add("No lighter shade of this colour for a highlight. Untick \"Only colours already in the sprite\" to get one (" + PixelImage.Hex(MakeHighlight(ramp.Base)) + ").");
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
            Func<int, int, bool> empty = (x, y) => x >= 0 && y >= 0 && x < W && y < H && layer.Get(x, y) == 0 && composite.Get(x, y) == 0;

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

            // 5. tones by distance to the lit and the shadowed side
            var after = s.After;
            int underCount = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    if (!part(x, y)) continue;
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
                    if (under) { tone = ramp.Under; underCount++; }
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
            return s;
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
