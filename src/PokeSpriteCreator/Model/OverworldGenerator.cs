using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    /// <summary>The generated overworld sheet (4 × 4: rows Down, Left, Right, Up; columns stand, step, stand, step).</summary>
    public class OverworldResult
    {
        public FrameLayout Layout;
        public List<PixelImage> Frames = new List<PixelImage>();
        public string Note = "";
        public TrainerColors Colors;
        public PixelImage Frame(int direction, int step) { return Frames[direction * Layout.Cols + step]; }
    }

    /// <summary>The colours picked out of a trainer sprite for its overworld sprite.</summary>
    public class TrainerColors
    {
        public int Outline, HairLight, HairDark, Skin, Torso, Inner, Legs, Shoes;
        /// <summary>A headband / visor / hat-band colour, or 0.</summary>
        public int Band;
    }

    /// <summary>
    /// Makes an overworld walking sprite (4 × 4 sheet in the generation's overworld frame size) from a trainer front sprite.
    /// The head keeps the trainer's hair shape (shrunk from the sprite); the body is a clean overworld-style body
    /// coloured with the trainer's own colours, so it works whatever pose the battle sprite has. The up view can take
    /// its hair shape from a back sprite. It's a starting point to touch up, not a finished sprite.
    /// </summary>
    public static class OverworldGenerator
    {
        public const int Down = 0, Left = 1, Right = 2, Up = 3;

        public static OverworldResult Make(PixelImage front, GenStyle st, bool symmetric, PixelImage back)
        {
            var res = new OverworldResult { Layout = new FrameLayout(st.OwW, st.OwH, 4, 4) };
            var b = front == null ? Rectangle.Empty : front.Bounds();
            if (b.IsEmpty || b.Width < 4 || b.Height < 8)
            {
                for (int i = 0; i < 16; i++) res.Frames.Add(new PixelImage(st.OwW, st.OwH));
                res.Note = "Draw the trainer first: the walking sprite is made from the front sprite.";
                return res;
            }
            var src = front.Crop(b.X, b.Y, b.Width, b.Height);
            var parts = Split(src);
            var col = PickColors(src, parts);
            res.Colors = col;

            PixelImage backHead = null;
            if (back != null && !back.Bounds().IsEmpty)
            {
                var bb = back.Bounds();
                backHead = Split(back.Crop(bb.X, bb.Y, bb.Width, bb.Height)).Head;
            }

            var g = new Geometry(st, parts.Head);
            for (int dir = 0; dir < 4; dir++)
            {
                var headSrc = dir == Up && backHead != null ? backHead : parts.Head;
                var head = Head(headSrc, g, col, dir, symmetric && (dir == Down || dir == Up));
                for (int step = 0; step < 4; step++)
                {
                    var fig = new PixelImage(g.MaxW, g.CharH);
                    Body(fig, g, col, dir == Right ? Left : dir, step, symmetric && (dir == Down || dir == Up));
                    int hx = (g.MaxW - head.W) / 2 + ((dir == Left || dir == Right) && g.MaxW > 16 ? -1 : 0);
                    fig.Blit(head, Math.Max(0, hx), 0);
                    if (dir == Right) fig = fig.FlipH();
                    var f = new PixelImage(st.OwW, st.OwH);
                    f.Blit(fig, (st.OwW - fig.W) / 2, st.OwH - fig.H);
                    res.Frames.Add(f);
                }
            }
            res.Frames = st.Greyscale ? res.Frames.Select(f => ColorTools.ToGreyscale4(f, st.Palette)).ToList() : ColorTools.ReduceTogether(res.Frames, st.MaxColors);
            res.Note = backHead != null ? "Made from the front sprite, with the back sprite's hair for the up view."
                                        : "Made from the front sprite. Touch it up with \"Edit as overworld sprite\" if you like.";
            return res;
        }

        // ------------------------------------------------------------------ sizes

        class Geometry
        {
            public int MaxW, CharH, HeadW, HeadH, TorsoW, TorsoH, LegW;
            public int HeadBottom { get { return HeadH - 1; } }
            public Geometry(GenStyle st, PixelImage srcHead)
            {
                bool tiny = st.OwH <= 16;
                CharH = tiny ? st.OwH : Math.Min(st.OwH, (int)Math.Round(st.OwH * 0.84));
                MaxW = st.OwW <= 16 ? st.OwW : Math.Min(st.OwW, 20);
                HeadH = Math.Max(6, (int)Math.Round(CharH * 0.5));                        // with outline
                double aspect = srcHead.W / (double)Math.Max(1, srcHead.H);
                HeadW = Clamp((int)Math.Round((HeadH - 2) * Math.Min(1.25, Math.Max(0.85, aspect))) + 2, 6, MaxW);
                if (HeadW % 2 != MaxW % 2) HeadW--;                                     // so it centres exactly
                int bodyH = CharH - HeadH + 1;                                             // shares a row with the head
                TorsoH = Math.Max(3, (int)Math.Round(bodyH * 0.5));
                TorsoW = Clamp(HeadW - 2 - (HeadW % 2 == 1 ? 1 : 0), 5, MaxW - 4);
                LegW = Math.Max(3, (TorsoW + 1) / 2);
            }
        }

        static int Clamp(int v, int lo, int hi) { return hi < lo ? lo : Math.Max(lo, Math.Min(hi, v)); }

        // ------------------------------------------------------------------ reading the trainer sprite

        class Parts { public PixelImage Head, Body; }

        /// <summary>Splits a trainer at the neck: the narrowest row between 18% and 42% of the height.</summary>
        static Parts Split(PixelImage src)
        {
            int h = src.H, r0 = Math.Max(1, (int)Math.Round(h * 0.18)), r1 = Math.Max(r0 + 1, (int)Math.Round(h * 0.42));
            int neck = r0, best = int.MaxValue;
            for (int y = r0; y <= r1 && y < h - 1; y++)
            {
                int n = 0;
                for (int x = 0; x < src.W; x++) if (src.Get(x, y) != 0) n++;
                if (n > 0 && n <= best) { best = n; neck = y; }
            }
            return new Parts { Head = Trim(src.Crop(0, 0, src.W, neck)), Body = Trim(src.Crop(0, neck, src.W, h - neck)) };
        }

        static PixelImage Trim(PixelImage img)
        {
            var b = img.Bounds();
            return b.IsEmpty ? new PixelImage(1, 1) : img.Crop(b.X, b.Y, b.Width, b.Height);
        }

        static Dictionary<int, int> Count(PixelImage img, double y0, double y1, double x0 = 0, double x1 = 1)
        {
            var d = new Dictionary<int, int>();
            for (int y = (int)(img.H * y0); y < Math.Min(img.H, (int)Math.Ceiling(img.H * y1)); y++)
                for (int x = (int)(img.W * x0); x < Math.Min(img.W, (int)Math.Ceiling(img.W * x1)); x++)
                {
                    int c = img.Get(x, y);
                    if (c == 0) continue;
                    int n; d.TryGetValue(c, out n); d[c] = n + 1;
                }
            return d;
        }

        static int Top(Dictionary<int, int> counts, Func<int, bool> ok, int fallback)
        {
            var c = counts.Where(kv => ok(kv.Key)).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).ToList();
            return c.Count > 0 ? c[0] : fallback;
        }

        /// <summary>Colourfulness (HSV saturation: near-whites and greys are low).</summary>
        static double Sat(int c)
        {
            int mx = Math.Max(PixelImage.R(c), Math.Max(PixelImage.G(c), PixelImage.B(c))), mn = Math.Min(PixelImage.R(c), Math.Min(PixelImage.G(c), PixelImage.B(c)));
            return mx == 0 ? 0 : (mx - mn) / (double)mx;
        }
        static double Hue(int c) { double h, s, l; ColorTools.ToHsl(c, out h, out s, out l); return h; }
        static double HueDiff(int a, int b) { double d = Math.Abs(Hue(a) - Hue(b)) % 360; return d > 180 ? 360 - d : d; }
        static bool SkinLike(int c)
        {
            // skin tones: red a bit above green, green a bit above blue (rules out reds, oranges and greys)
            int r = PixelImage.R(c), g = PixelImage.G(c), b = PixelImage.B(c);
            return r - g >= 15 && r - g <= 85 && g - b >= 8 && g - b <= 70 && PixelImage.Luma(c) > 0.5;
        }
        static int Dist(int a, int b)
        {
            int dr = PixelImage.R(a) - PixelImage.R(b), dg = PixelImage.G(a) - PixelImage.G(b), db = PixelImage.B(a) - PixelImage.B(b);
            return dr * dr + dg * dg + db * db;
        }

        static TrainerColors PickColors(PixelImage src, Parts p)
        {
            var c = new TrainerColors();
            var all = Count(src, 0, 1);
            c.Outline = Top(all, x => PixelImage.Luma(x) < 0.22, all.Keys.OrderBy(PixelImage.Luma).First());
            Func<int, bool> notDark = x => !ColorTools.IsOutline(x) && x != c.Outline;

            // skin: the most used skin-like colour in the lower part of the head (the face)
            var face = Count(p.Head, 0.4, 1, 0.15, 0.85);
            c.Skin = Top(face, x => notDark(x) && SkinLike(x), Top(all, x => notDark(x) && SkinLike(x), PixelImage.Rgb(248, 208, 168)));
            // overworld faces use the light skin tone: take a lighter skin colour when it's nearly as common
            int skinCount; face.TryGetValue(c.Skin, out skinCount);
            var lighter = face.Where(kv => SkinLike(kv.Key) && PixelImage.Luma(kv.Key) > PixelImage.Luma(c.Skin) && kv.Value * 10 >= skinCount * 6).OrderByDescending(kv => kv.Value).ToList();
            if (lighter.Count > 0) c.Skin = lighter[0].Key;
            Func<int, bool> isSkin = x => Dist(x, c.Skin) < 900 || (SkinLike(x) && HueDiff(x, c.Skin) < 14 && Dist(x, c.Skin) < 9000);

            // hair: the most used colours at the top of the head
            var top = Count(p.Head, 0, 0.4);
            var hair = top.Where(kv => notDark(kv.Key) && !isSkin(kv.Key)).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(3).ToList();
            if (hair.Count == 0) hair.Add(Top(top, x => x != 0, c.Outline));
            int main = hair[0];
            var fam = hair.Where(h => (HueDiff(h, main) < 30 && (Sat(h) >= 0.15) == (Sat(main) >= 0.15)) || (Sat(h) < 0.15 && Sat(main) < 0.15)).ToList();
            c.HairLight = fam.OrderByDescending(PixelImage.Luma).First();
            c.HairDark = fam.Count > 1 ? fam.OrderBy(PixelImage.Luma).First() : ColorTools.Adjust(main, 0, 0, -30);
            if (c.HairLight == c.HairDark) c.HairLight = ColorTools.Adjust(main, 0, 0, 20);

            // a headband / visor / hat band: a strong colour in the head that isn't hair or skin
            var headAll = Count(p.Head, 0, 0.75);
            int headTotal = Math.Max(1, headAll.Values.Sum());
            var bandC = headAll.Where(kv => notDark(kv.Key) && !isSkin(kv.Key) && !fam.Contains(kv.Key) && Sat(kv.Key) > 0.35 &&
                                            fam.All(h => Dist(kv.Key, h) > 6000) && kv.Value * 100 >= headTotal * 5)
                               .OrderByDescending(kv => kv.Value).ToList();
            c.Band = bandC.Count > 0 ? bandC[0].Key : 0;

            // clothes: top of the body = shirt / jacket, lower = trousers, bottom = shoes
            var torso = Count(p.Body, 0.05, 0.42);
            c.Torso = Top(torso, x => notDark(x) && !isSkin(x), Top(torso, x => x != 0, c.Outline));
            var inner = torso.Where(kv => notDark(kv.Key) && !isSkin(kv.Key) && kv.Key != c.Torso && Dist(kv.Key, c.Torso) > 7000)
                             .OrderByDescending(kv => kv.Value).ToList();
            int torsoTotal = Math.Max(1, torso.Values.Sum());
            c.Inner = inner.Count > 0 && inner[0].Value * 100 >= torsoTotal * 8 ? inner[0].Key : 0;
            var legs = Count(p.Body, 0.55, 0.85, 0.15, 0.85);
            c.Legs = Top(legs, x => notDark(x) && !isSkin(x), c.Torso);
            var shoes = Count(p.Body, 0.88, 1);
            c.Shoes = Top(shoes, x => notDark(x) && !isSkin(x) && x != c.Legs, Top(shoes, x => x != 0 && !isSkin(x), c.Outline));
            return c;
        }

        // ------------------------------------------------------------------ drawing

        static int Shade(int c) { return ColorTools.Adjust(c, 0, 0, -28); }

        static void Fill(PixelImage img, int x0, int y0, int x1, int y1, int c)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) img.Set(x, y, c);
        }

        /// <summary>A box with an outline border, a fill and a darker right-hand column inside.</summary>
        static void Box(PixelImage img, int x0, int y0, int x1, int y1, int fill, int outline, bool shadeRight = true, bool shadeBoth = false)
        {
            if (x1 < x0 || y1 < y0) return;
            Fill(img, x0, y0, x1, y1, outline);
            if (x1 - x0 >= 2 && y1 - y0 >= 2)
            {
                Fill(img, x0 + 1, y0 + 1, x1 - 1, y1 - 1, fill);
                if ((shadeRight || shadeBoth) && x1 - x0 >= 4) Fill(img, x1 - 1, y0 + 1, x1 - 1, y1 - 1, Shade(fill));
                if (shadeBoth && x1 - x0 >= 6) Fill(img, x0 + 1, y0 + 1, x0 + 1, y1 - 1, Shade(fill));
            }
        }

        /// <summary>The body for one direction (Down, Left or Up) and walking step (0 stand, 1 step, 2 stand, 3 other step).</summary>
        static void Body(PixelImage fig, Geometry g, TrainerColors c, int dir, int step, bool symmetric)
        {
            int o = c.Outline, cx = g.MaxW / 2;
            int torsoTop = g.HeadBottom, torsoBot = torsoTop + g.TorsoH;
            int bottom = g.CharH - 1;
            bool side = dir == Left;
            int tw = side ? Math.Max(5, (int)Math.Round(g.TorsoW * 0.75)) : g.TorsoW;
            int tx0 = cx - tw / 2, tx1 = tx0 + tw - 1;
            if (side && g.MaxW > 16) { tx0++; tx1++; }

            // legs
            if (!side)
            {
                int lx0 = tx0 + (tw >= 8 ? 1 : 0), rx1 = tx1 - (tw >= 8 ? 1 : 0);
                int lw = Math.Max(2, Math.Min(g.LegW, (rx1 - lx0 + 1) / 2));     // the two legs meet without overlapping
                int leftLift = step == 3 ? 1 : 0, rightLift = step == 1 ? 1 : 0;
                Leg(fig, lx0, lx0 + lw - 1, torsoBot, bottom - leftLift, c);
                Leg(fig, rx1 - lw + 1, rx1, torsoBot, bottom - rightLift, c);
            }
            else
            {
                int lw = Math.Max(3, g.LegW);
                int lx = cx - lw / 2 + 1;
                int spread = step == 1 || step == 3 ? Math.Max(1, lw / 2) : 0;
                // back leg (darker) then front leg (facing left, so forward is to the left)
                Leg(fig, lx + spread, lx + spread + lw - 1, torsoBot, bottom, c, true);
                Leg(fig, lx - spread, lx - spread + lw - 1, torsoBot, bottom, c);
            }

            // torso, with the inner shirt showing down the middle when facing down
            Box(fig, tx0, torsoTop, tx1, torsoBot, c.Torso, o, true, symmetric);
            if (dir == Down && c.Inner != 0 && tw >= 6)
                Fill(fig, cx - 1, torsoTop + 1, cx, torsoBot - 1, c.Inner);

            // arms with hands
            int armBot = Math.Min(bottom - 1, torsoBot + 1);
            if (!side)
            {
                Arm(fig, tx0 - 2, tx0, torsoTop + 1, armBot, c);
                Arm(fig, tx1, tx1 + 2, torsoTop + 1, armBot, c);
            }
            else
            {
                int swing = step == 1 ? 1 : step == 3 ? -1 : 0;
                int ax = cx + swing;
                Arm(fig, ax, ax + 2, torsoTop + 1, armBot, c);
            }
        }

        static void Leg(PixelImage fig, int x0, int x1, int top, int bottom, TrainerColors c, bool back = false)
        {
            Box(fig, x0, top, x1, bottom, back ? Shade(c.Legs) : c.Legs, c.Outline, false);
            if (bottom - top >= 3 && x1 - x0 >= 2) Fill(fig, x0 + 1, bottom - 1, x1 - 1, bottom - 1, back ? Shade(c.Shoes) : c.Shoes);
        }

        static void Arm(PixelImage fig, int x0, int x1, int top, int bottom, TrainerColors c)
        {
            Box(fig, x0, top, x1, bottom, c.Torso, c.Outline, false);
            if (bottom - top >= 3) fig.Set(x0 + 1, bottom - 1, c.Skin);
        }

        /// <summary>The head for one direction: the trainer's hair shape, with the face (down), side face (left/right) or all hair (up).</summary>
        static PixelImage Head(PixelImage srcHead, Geometry g, TrainerColors c, int dir, bool symmetric)
        {
            int hw = g.HeadW - 2, hh = g.HeadH - 2;
            if (dir == Left || dir == Right) hw = Math.Max(4, (int)Math.Round(hw * 0.9));
            var shrunk = Shrink(srcHead, hw, hh);
            // solid silhouette: fill each row between its first and last pixel; the lower part is at least face-wide
            var mask = new bool[hw, hh];
            for (int y = 0; y < hh; y++)
            {
                int a = -1, z = -1;
                for (int x = 0; x < hw; x++) if (shrunk.Get(x, y) != 0) { if (a < 0) a = x; z = x; }
                if (y >= hh / 3) { a = a < 0 ? 1 : Math.Min(a, 1); z = Math.Max(z, hw - 2); }
                for (int x = Math.Max(0, a); x <= z && a >= 0; x++) mask[x, y] = true;
            }
            if (symmetric)
                for (int y = 0; y < hh; y++)
                    for (int x = 0; x < hw; x++) if (mask[x, y]) mask[hw - 1 - x, y] = true;

            var head = new PixelImage(hw, hh);
            double mid = (PixelImage.Luma(c.HairLight) + PixelImage.Luma(c.HairDark)) / 2;
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < hw; x++)
                {
                    if (!mask[x, y]) continue;
                    int s = shrunk.Get(x, y);
                    bool light = s != 0 && PixelImage.Luma(s) >= mid && y < hh * (dir == Down ? 0.6 : 0.4);   // no face details on the back of the head
                    head.Set(x, y, light ? c.HairLight : c.HairDark);
                }
            if (symmetric)
                for (int y = 0; y < hh; y++)
                    for (int x = hw / 2; x < hw; x++) head.Set(x, y, head.Get(hw - 1 - x, y));

            int faceTop = Math.Max(2, (int)Math.Round(hh * 0.42));
            int eyeH = hh >= 10 ? 2 : 1;
            if (c.Band != 0 && faceTop >= 3)
                for (int x = 0; x < hw; x++) if (mask[x, faceTop - 1]) head.Set(x, faceTop - 1, c.Band);
            if (dir == Down)
            {
                for (int y = faceTop; y < hh; y++)
                    for (int x = 1; x < hw - 1; x++) if (mask[x, y]) head.Set(x, y, c.Skin);
                int cxm = hw / 2;
                int e1 = hw >= 8 ? cxm - 2 : cxm - 1, e2 = hw >= 8 ? cxm + 1 : cxm;
                for (int k = 0; k < eyeH; k++) { head.Set(e1, faceTop + 1 + k, c.Outline); head.Set(e2, faceTop + 1 + k, c.Outline); }
            }
            else if (dir == Left || dir == Right)
            {
                // drawn facing left; facing right is the mirror of the whole figure
                int faceRight = Math.Max(2, (int)Math.Round(hw * 0.5));
                for (int y = faceTop; y < hh; y++)
                    for (int x = 0; x <= faceRight; x++) if (mask[x, y]) head.Set(x, y, c.Skin);
                for (int k = 0; k < eyeH; k++) head.Set(1, faceTop + 1 + k, c.Outline);
            }
            return OutlineAround(head, c.Outline);
        }

        /// <summary>Adds a one-pixel outline around the outside (the picture grows by 1 on every side).</summary>
        static PixelImage OutlineAround(PixelImage img, int outline)
        {
            var r = new PixelImage(img.W + 2, img.H + 2);
            r.Blit(img, 1, 1);
            var copy = r.Clone();
            for (int y = 0; y < r.H; y++)
                for (int x = 0; x < r.W; x++)
                    if (copy.Get(x, y) == 0 && (copy.Get(x - 1, y) != 0 || copy.Get(x + 1, y) != 0 || copy.Get(x, y - 1) != 0 || copy.Get(x, y + 1) != 0))
                        r.Set(x, y, outline);
            return r;
        }

        /// <summary>Pixel-art shrink: each new pixel takes the most common colour of the area it covers, and is empty when that area is mostly empty.</summary>
        public static PixelImage Shrink(PixelImage img, int tw, int th)
        {
            tw = Math.Max(1, tw); th = Math.Max(1, th);
            var r = new PixelImage(tw, th);
            var cells = new Dictionary<int, int>[tw * th];
            var total = new int[tw * th];
            var empty = new int[tw * th];
            for (int y = 0; y < img.H; y++)
                for (int x = 0; x < img.W; x++)
                {
                    int i = Math.Min(th - 1, y * th / img.H) * tw + Math.Min(tw - 1, x * tw / img.W);
                    total[i]++;
                    int c = img.Px[y * img.W + x];
                    if (c == 0) { empty[i]++; continue; }
                    if (cells[i] == null) cells[i] = new Dictionary<int, int>();
                    int n; cells[i].TryGetValue(c, out n); cells[i][c] = n + 1;
                }
            for (int i = 0; i < r.Px.Length; i++)
                if (cells[i] != null && empty[i] * 2 <= total[i])
                    r.Px[i] = cells[i].OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            return r;
        }
    }
}
