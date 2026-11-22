using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    /// <summary>
    /// Copies an edit made on one frame of a walking sprite to other frames.
    /// The edit is worked out by comparing the frame before and after:
    ///   * a colour that was changed (almost) everywhere it appears is a recolour, and is recoloured on the other frames too;
    ///   * any other changed pixels are pasted onto the other frames, lined up with each one by finding where the area
    ///     around them matches best (so it follows the bob of a walking step), and mirrored for the opposite side view.
    /// </summary>
    public class ChangeCopier
    {
        public readonly PixelImage Before, After;
        public readonly Dictionary<int, int> Recolors = new Dictionary<int, int>();
        /// <summary>Changed pixels that aren't part of a recolour (their new colour; 0 = erased).</summary>
        public readonly List<KeyValuePair<Point, int>> Pixels = new List<KeyValuePair<Point, int>>();

        public ChangeCopier(PixelImage before, PixelImage after)
        {
            Before = before; After = after;
            var changedFrom = new Dictionary<int, List<int>>();   // old colour -> new colours of its changed pixels
            var oldCount = new Dictionary<int, int>();
            for (int i = 0; i < before.Px.Length; i++)
            {
                int a = before.Px[i], b = after.Px[i];
                if (a != 0) { int n; oldCount.TryGetValue(a, out n); oldCount[a] = n + 1; }
                if (a == b || a == 0 || b == 0) continue;
                List<int> l;
                if (!changedFrom.TryGetValue(a, out l)) changedFrom[a] = l = new List<int>();
                l.Add(b);
            }
            foreach (var kv in changedFrom)
            {
                var to = kv.Value.Distinct().ToList();
                // a recolour: every changed pixel of that colour got the same new colour, and 80%+ of the colour changed
                if (to.Count == 1 && kv.Value.Count * 10 >= oldCount[kv.Key] * 8) Recolors[kv.Key] = to[0];
            }
            for (int y = 0; y < before.H; y++)
                for (int x = 0; x < before.W; x++)
                {
                    int a = before.Get(x, y), b = after.Get(x, y);
                    if (a == b) continue;
                    int m;
                    if (a != 0 && Recolors.TryGetValue(a, out m) && m == b) continue;
                    Pixels.Add(new KeyValuePair<Point, int>(new Point(x, y), b));
                }
        }

        public bool IsEmpty { get { return Recolors.Count == 0 && Pixels.Count == 0; } }

        public string Describe()
        {
            var parts = new List<string>();
            if (Recolors.Count > 0) parts.Add(Recolors.Count + " colour" + (Recolors.Count == 1 ? "" : "s") + " changed");
            if (Pixels.Count > 0) parts.Add(Pixels.Count + " pixel" + (Pixels.Count == 1 ? "" : "s") + " drawn or erased");
            return parts.Count == 0 ? "no change" : string.Join(", ", parts);
        }

        /// <summary>The target frame with the change copied onto it. mirror = the target faces the other way (left ↔ right).</summary>
        public PixelImage ApplyTo(PixelImage target, bool mirror, out Point offset)
        {
            var r = target.Clone();
            offset = Point.Empty;
            // recolours
            if (Recolors.Count > 0)
                for (int i = 0; i < r.Px.Length; i++) { int m; if (r.Px[i] != 0 && Recolors.TryGetValue(r.Px[i], out m)) r.Px[i] = m; }
            if (Pixels.Count == 0) return r;

            // drawn pixels: line them up where the area around them matches the target best
            var src = mirror ? Before.FlipH() : Before;
            var pts = Pixels.Select(kv => new KeyValuePair<Point, int>(mirror ? new Point(Before.W - 1 - kv.Key.X, kv.Key.Y) : kv.Key, kv.Value)).ToList();
            int x0 = pts.Min(p => p.Key.X) - 4, x1 = pts.Max(p => p.Key.X) + 4, y0 = pts.Min(p => p.Key.Y) - 4, y1 = pts.Max(p => p.Key.Y) + 4;
            int best = int.MinValue, bdx = 0, bdy = 0;
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int score = 0;
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int a = src.Get(x, y), b = target.Get(x + dx, y + dy);
                            if (a == 0 && b == 0) continue;
                            if (a == b) score += 2; else if (a != 0 && b != 0) score -= 1; else score -= 1;
                        }
                    score = score * 4 - (Math.Abs(dx) + Math.Abs(dy));   // prefer small moves when it's a tie
                    if (score > best) { best = score; bdx = dx; bdy = dy; }
                }
            offset = new Point(bdx, bdy);
            foreach (var p in pts) r.Set(p.Key.X + bdx, p.Key.Y + bdy, p.Value);
            return r;
        }
    }
}
