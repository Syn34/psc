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
        public int Outline, HairLight, HairMid, HairDark, Skin, Torso, Inner, Legs, Shoes;
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
                    // the figure has a spare row above (for the walking bob) and below (for the stepping foot)
                    var fig = new PixelImage(g.MaxW, g.CharH + 2);
                    Body(fig, g, col, dir == Right ? Left : dir, step, symmetric && (dir == Down || dir == Up), 1);
                    int bob = g.Big && (step == 1 || step == 3) ? 1 : 0;
                    int hx = (g.MaxW - head.W) / 2 + ((dir == Left || dir == Right) && g.MaxW > 16 ? -1 : 0);
                    fig.Blit(head, Math.Max(0, hx), 1 - bob);
                    if (dir == Right) fig = fig.FlipH();
                    var f = new PixelImage(st.OwW, st.OwH);
                    // standing feet on the bottom row (big style: one row up, so a stepping foot can reach the bottom row)
                    int standRow = g.Big ? st.OwH - 2 : st.OwH - 1;
                    f.Blit(fig, (st.OwW - fig.W) / 2, standRow - g.CharH);
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
            public bool Big;
            public int MaxW, CharH, HeadW, HeadH, TorsoW, TorsoH, LegW;
            public int HeadBottom { get { return HeadH - 1; } }
            public Geometry(GenStyle st, PixelImage srcHead)
            {
                // Gen 4/5 (32x32): about 29 tall with a head ~62% of that (measured from Gen 5 sprites); smaller styles keep smaller heads
                Big = st.OwW >= 32 && st.OwH >= 32;
                bool tiny = st.OwH <= 16;
                CharH = Big ? st.OwH - 3 : tiny ? st.OwH : st.OwH - 6;
                MaxW = Big ? Math.Min(st.OwW, 22) : st.OwW;
                HeadH = Math.Max(6, (int)Math.Round(CharH * (Big ? 0.62 : tiny ? 0.5 : 0.54)));     // with outline
                double aspect = srcHead.W / (double)Math.Max(1, srcHead.H);
                HeadW = Clamp((int)Math.Round((HeadH - 2) * Math.Min(1.15, Math.Max(0.9, aspect))) + 2, 6, Big ? MaxW - 2 : MaxW);
                if (HeadW % 2 != MaxW % 2) HeadW--;                                     // so it centres exactly
                int bodyH = CharH - HeadH + 1;                                             // shares a row with the head
                TorsoH = Math.Max(3, (int)Math.Round(bodyH * 0.5));
                TorsoW = Clamp(HeadW - (Big ? 8 : 2) - (HeadW % 2 == 1 ? 1 : 0), 5, MaxW - 6);
                if (TorsoW % 2 != MaxW % 2) TorsoW--;
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
            var byLight = fam.OrderByDescending(PixelImage.Luma).ToList();
            c.HairLight = byLight[0];
            c.HairDark = byLight.Count > 1 ? byLight[byLight.Count - 1] : ColorTools.Adjust(main, 0, 0, -30);
            if (c.HairLight == c.HairDark) c.HairLight = ColorTools.Adjust(main, 0, 0, 20);
            c.HairMid = byLight.Count > 2 ? byLight[1] : main == c.HairLight && byLight.Count == 2 ? c.HairLight : main;
            if (byLight.Count == 2) c.HairLight = ColorTools.Adjust(c.HairLight, 0, 0, 16);

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

        /// <summary>A box with an outline border and a fill; shade = darker inside columns on the right (and left when both).</summary>
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

        /// <summary>
        /// The body for one direction (Down, Left or Up) and walking step (0 stand, 1 step, 2 stand, 3 other step).
        /// oy = the figure's top row; the standing feet are on row oy + CharH - 1. In the big (32×32) style a step
        /// lifts the body a pixel while the stepping foot reaches one row lower and the other foot lifts, like Gen 4/5.
        /// </summary>
        static void Body(PixelImage fig, Geometry g, TrainerColors c, int dir, int step, bool symmetric, int oy)
        {
            int o = c.Outline, cx = g.MaxW / 2;
            bool stepping = step == 1 || step == 3, first = step == 1;
            int bob = g.Big && stepping ? 1 : 0;
            int torsoTop = oy + g.HeadBottom - bob, torsoBot = torsoTop + g.TorsoH;
            int bottom = oy + g.CharH - 1;                       // standing feet
            bool side = dir == Left;
            int tw = side ? Math.Max(5, (int)Math.Round(g.TorsoW * 0.72)) : g.TorsoW;
            int tx0 = cx - tw / 2, tx1 = tx0 + tw - 1;
            if (side && g.MaxW > 16) { tx0++; tx1++; }

            // legs
            if (!side)
            {
                int lx0 = tx0 + (tw >= 8 ? 1 : 0), rx1 = tx1 - (tw >= 8 ? 1 : 0);
                int lw = Math.Max(2, Math.Min(g.LegW, (rx1 - lx0 + 1) / 2));     // the two legs meet without overlapping
                int leftBot = bottom, rightBot = bottom;
                if (stepping)
                {
                    int down = g.Big ? bottom + 1 : bottom, up = g.Big ? bottom - 2 : bottom - 1;
                    if (first) { rightBot = down; leftBot = up; } else { leftBot = down; rightBot = up; }
                }
                Leg(fig, lx0, lx0 + lw - 1, torsoBot, leftBot, c, false, g.Big);
                Leg(fig, rx1 - lw + 1, rx1, torsoBot, rightBot, c, false, g.Big);
            }
            else
            {
                int lw = Math.Max(3, g.LegW);
                int lx = cx - lw / 2 + (g.MaxW > 16 ? 1 : 0);
                int spread = stepping ? Math.Max(1, lw / 2) : 0;
                int frontBot = stepping && g.Big ? bottom + 1 : bottom, backBot = stepping && g.Big ? bottom - 1 : bottom;
                // back leg (darker) then front leg (facing left, so forward is to the left)
                Leg(fig, lx + spread, lx + spread + lw - 1, torsoBot, backBot, c, true, g.Big);
                Leg(fig, lx - spread, lx - spread + lw - 1, torsoBot, frontBot, c, false, g.Big);
            }

            // torso, with the inner shirt showing down the middle when facing down
            Box(fig, tx0, torsoTop, tx1, torsoBot, c.Torso, o, true, symmetric);
            if (dir == Down && c.Inner != 0 && tw >= 6)
                Fill(fig, cx - 1, torsoTop + 1, cx, torsoBot - 1, c.Inner);

            // arms with hands; they swing a little when walking
            int armBot = Math.Min(bottom - 1, torsoBot + (g.Big ? 2 : 1));
            if (!side)
            {
                int la = stepping ? (first ? 1 : -1) : 0;
                Arm(fig, tx0 - 2, tx0, torsoTop + 1, armBot + la, c);
                Arm(fig, tx1, tx1 + 2, torsoTop + 1, armBot - la, c);
            }
            else
            {
                int swing = stepping ? (first ? 1 : -1) : 0;
                int ax = cx + swing - (g.MaxW > 16 ? 0 : 1);
                Arm(fig, ax, ax + 2, torsoTop + 1, armBot, c);
            }
        }

        static void Leg(PixelImage fig, int x0, int x1, int top, int bottom, TrainerColors c, bool back, bool big)
        {
            int legC = back ? Shade(c.Legs) : c.Legs, shoeC = back ? Shade(c.Shoes) : c.Shoes;
            Box(fig, x0, top, x1, bottom, legC, c.Outline, false);
            int shoeRows = big && bottom - top >= 5 ? 2 : 1;
            if (bottom - top >= 3 && x1 - x0 >= 2) Fill(fig, x0 + 1, bottom - shoeRows, x1 - 1, bottom - 1, shoeC);
        }

        static void Arm(PixelImage fig, int x0, int x1, int top, int bottom, TrainerColors c)
        {
            Box(fig, x0, top, x1, bottom, c.Torso, c.Outline, false);
            if (bottom - top >= 3) fig.Set(x0 + 1, bottom - 1, c.Skin);
        }

        /// <summary>
        /// The head for one direction: a big round head keeping the trainer's hair shape, with the face (down),
        /// side face (left/right) or all hair (up). Hair is outlined in a dark hair colour, the face in the outline colour.
        /// </summary>
        static PixelImage Head(PixelImage srcHead, Geometry g, TrainerColors c, int dir, bool symmetric)
        {
            int hw = g.HeadW - 2, hh = g.HeadH - 2;
            bool side = dir == Left || dir == Right;
            if (side) hw = Math.Max(4, hw - (g.Big ? 1 : 0));
            var shrunk = Shrink(srcHead, hw, hh);
            int faceTop = Math.Max(2, (int)Math.Round(hh * (g.Big ? 0.56 : 0.45)));

            // silhouette: a round head, plus the hair shape from the sprite (spikes, buns, pigtails) above the face
            var mask = new bool[hw, hh];
            double rx = hw / 2.0, ry = hh / 2.0;
            for (int y = 0; y < hh; y++)
            {
                int a = -1, z = -1;
                for (int x = 0; x < hw; x++) if (shrunk.Get(x, y) != 0) { if (a < 0) a = x; z = x; }
                for (int x = 0; x < hw; x++)
                {
                    double nx = (x + 0.5 - rx) / rx, ny = (y + 0.5 - ry) / ry;
                    bool round = nx * nx + ny * ny <= 1.08;
                    bool hairShape = a >= 0 && x >= a && x <= z && y < faceTop;
                    mask[x, y] = round || hairShape;
                }
            }
            if (symmetric)
                for (int y = 0; y < hh; y++)
                    for (int x = 0; x < hw; x++) if (mask[x, y]) mask[hw - 1 - x, y] = true;

            // hair in three tones following the light and shade of the sprite's hair; darker towards the bottom
            var head = new PixelImage(hw, hh);
            double l1 = PixelImage.Luma(c.HairLight), l2 = PixelImage.Luma(c.HairMid), l3 = PixelImage.Luma(c.HairDark);
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < hw; x++)
                {
                    if (!mask[x, y]) continue;
                    int s = shrunk.Get(x, y);
                    int col = c.HairMid;
                    if (s != 0 && y < faceTop)
                    {
                        double l = PixelImage.Luma(s);
                        col = Math.Abs(l - l1) < Math.Abs(l - l2) ? c.HairLight : (Math.Abs(l - l3) < Math.Abs(l - l2) ? c.HairDark : c.HairMid);
                        if (l > 0.85 && dir != Down) col = c.HairLight;   // skin/eyes don't show from behind
                    }
                    if (y >= hh - 2 || (dir == Up && y >= faceTop + 1)) col = c.HairDark;
                    head.Set(x, y, col);
                }
            if (symmetric)
                for (int y = 0; y < hh; y++)
                    for (int x = hw / 2; x < hw; x++) head.Set(x, y, head.Get(hw - 1 - x, y));

            int skinShade = ColorTools.Adjust(c.Skin, 0, -25, -10);
            int eyeH = hh >= 10 ? 2 : 1;
            int mid2 = hw - 1;  // for symmetric fringe: columns x and hw-1-x match
            if (dir == Down)
            {
                int fx0 = g.Big ? Math.Max(2, (int)Math.Round(hw * 0.17)) : 1, fx1 = hw - 1 - fx0;   // hair frames the face
                for (int y = faceTop; y < hh; y++)
                    for (int x = fx0; x <= fx1; x++)
                    {
                        if (!mask[x, y]) continue;
                        if (y == hh - 1 && (x == fx0 || x == fx1)) continue;           // rounded chin
                        // a fringe: hair points hanging over the top of the face
                        int d = Math.Min(x - fx0, fx1 - x);
                        if (y == faceTop && g.Big && d % 3 != 1) continue;
                        head.Set(x, y, y == hh - 1 ? skinShade : c.Skin);
                    }
                int e1 = fx0 + (g.Big ? 2 : 1), e2 = fx1 - (g.Big ? 2 : 1);
                if (e2 - e1 < 2) { e1 = hw / 2 - 1; e2 = hw - 1 - e1; }
                int ey = faceTop + (g.Big ? 2 : 1);
                for (int k = 0; k < eyeH; k++) { head.Set(e1, ey + k, c.Outline); head.Set(e2, ey + k, c.Outline); }
                if (g.Big) { head.Set(e1 - 1, ey + eyeH - 1, PixelImage.Rgb(248, 248, 248)); head.Set(e2 + 1, ey + eyeH - 1, PixelImage.Rgb(248, 248, 248)); }
            }
            else if (side)
            {
                // drawn facing left; facing right is the mirror of the whole figure
                int faceRight = Math.Max(2, (int)Math.Round(hw * 0.55));
                for (int y = faceTop; y < hh; y++)
                    for (int x = 0; x <= faceRight; x++)
                    {
                        if (!mask[x, y]) continue;
                        if (y == faceTop && g.Big && x % 3 != 1) continue;               // fringe
                        head.Set(x, y, y == hh - 1 ? skinShade : c.Skin);
                    }
                int ex = g.Big ? 3 : 1, ey = faceTop + (g.Big ? 2 : 1);
                for (int k = 0; k < eyeH; k++) head.Set(ex, ey + k, c.Outline);
                if (g.Big) head.Set(ex - 1, ey + eyeH - 1, PixelImage.Rgb(248, 248, 248));
            }
            if (c.Band != 0 && faceTop >= 3)
                for (int x = 0; x < hw; x++) if (mask[x, faceTop - 1]) head.Set(x, faceTop - 1, c.Band);
            return OutlineHead(head, c, faceTop);
        }

        /// <summary>One-pixel outline around the outside: dark hair colour around the hair, the outline colour around the face.</summary>
        static PixelImage OutlineHead(PixelImage img, TrainerColors c, int faceTop)
        {
            var r = new PixelImage(img.W + 2, img.H + 2);
            r.Blit(img, 1, 1);
            var copy = r.Clone();
            int hairLine = PixelImage.Luma(c.HairDark) < 0.2 ? c.Outline : ColorTools.Adjust(c.HairDark, 0, 0, -45);
            var hair = new HashSet<int> { c.HairLight, c.HairMid, c.HairDark };
            for (int y = 0; y < r.H; y++)
                for (int x = 0; x < r.W; x++)
                {
                    if (copy.Get(x, y) != 0) continue;
                    var nb = new[] { copy.Get(x - 1, y), copy.Get(x + 1, y), copy.Get(x, y - 1), copy.Get(x, y + 1) };
                    if (nb.All(n => n == 0)) continue;
                    bool byHair = nb.Where(n => n != 0).All(n => hair.Contains(n)) && y <= faceTop;
                    r.Set(x, y, byHair ? hairLine : c.Outline);
                }
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
