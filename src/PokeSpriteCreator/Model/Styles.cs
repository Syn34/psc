using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    /// <summary>What a sprite is for. Each kind has its own frame layout and its own parts folder.</summary>
    public enum SpriteKind { Front, Back, Overworld }

    /// <summary>How the frames of a sprite are arranged on the exported sheet.</summary>
    public class FrameLayout
    {
        public int FrameW, FrameH, Cols, Rows;
        public FrameLayout(int fw, int fh, int cols, int rows) { FrameW = fw; FrameH = fh; Cols = Math.Max(1, cols); Rows = Math.Max(1, rows); }
        public int Count { get { return Cols * Rows; } }
        public int SheetW { get { return FrameW * Cols; } }
        public int SheetH { get { return FrameH * Rows; } }
        public int FrameX(int i) { return (i % Cols) * FrameW; }
        public int FrameY(int i) { return (i / Cols) * FrameH; }
    }

    /// <summary>
    /// The look of one generation: sprite sizes and how many colours a sprite may use.
    /// Sizes are the games' own (native) sizes; Export can scale them up for a fan game.
    /// </summary>
    public class GenStyle
    {
        public int Gen;
        public string Games;
        public int FrontSize, BackSize, BackFrames, OwW, OwH;
        /// <summary>Colours a sprite may use, not counting transparency.</summary>
        public int MaxColors;
        /// <summary>Gen 1: four shades of grey only.</summary>
        public bool Greyscale;
        public int[] Palette;
        public string Notes;

        public string Name { get { return "Gen " + Gen; } }
        public override string ToString() { return Name + "  (" + Games + ")"; }

        public string Describe()
        {
            return Name + ": " + Games + "\r\n" +
                "Trainer front " + FrontSize + "×" + FrontSize + ", back " + BackSize + "×" + BackSize + (BackFrames > 1 ? " (" + BackFrames + " frames)" : "") +
                ", overworld " + OwW + "×" + OwH + " per frame.\r\n" +
                (Greyscale ? "4 shades of grey." : MaxColors + " colours per sprite (plus transparency).") +
                (string.IsNullOrEmpty(Notes) ? "" : "\r\n" + Notes);
        }

        public FrameLayout DefaultLayout(SpriteKind k)
        {
            switch (k)
            {
                case SpriteKind.Back: return new FrameLayout(BackSize, BackSize, BackFrames, 1);
                case SpriteKind.Overworld: return new FrameLayout(OwW, OwH, 4, 4);
                default: return new FrameLayout(FrontSize, FrontSize, 1, 1);
            }
        }

        static int C(int r, int g, int b) { return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b; }

        public static readonly GenStyle[] All =
        {
            new GenStyle { Gen = 1, Games = "Red / Blue / Yellow", FrontSize = 56, BackSize = 32, BackFrames = 1, OwW = 16, OwH = 16, MaxColors = 4, Greyscale = true,
                Palette = new[] { C(255,255,255), C(170,170,170), C(85,85,85), C(0,0,0) },
                Notes = "Blocky shapes, thick outlines, no colour. Back sprites are small and were shown doubled." },
            new GenStyle { Gen = 2, Games = "Gold / Silver / Crystal", FrontSize = 56, BackSize = 48, BackFrames = 1, OwW = 16, OwH = 16, MaxColors = 4,
                Palette = new[] { C(255,255,255), C(248,184,136), C(208,64,48), C(0,0,0), C(248,208,96), C(96,152,224), C(104,176,88), C(168,112,64), C(184,184,184) },
                Notes = "White, black and two colours per sprite (pick your two)." },
            new GenStyle { Gen = 3, Games = "Ruby / Sapphire / Emerald / FireRed / LeafGreen", FrontSize = 64, BackSize = 64, BackFrames = 4, OwW = 16, OwH = 32, MaxColors = 15,
                Palette = new[] { C(0,0,0), C(255,255,255), C(248,216,184), C(232,168,120), C(176,112,80), C(232,72,64), C(168,32,40), C(88,136,232), C(48,72,160),
                                  C(248,208,64), C(80,176,80), C(160,104,64), C(96,64,48), C(200,200,208), C(112,112,128) },
                Notes = "Soft shading, coloured outlines on light areas." },
            new GenStyle { Gen = 4, Games = "Diamond / Pearl / Platinum / HeartGold / SoulSilver", FrontSize = 80, BackSize = 80, BackFrames = 5, OwW = 32, OwH = 32, MaxColors = 15,
                Palette = new[] { C(16,16,16), C(255,255,255), C(255,224,200), C(240,184,144), C(192,128,96), C(224,64,56), C(152,32,40), C(80,128,224), C(40,64,152),
                                  C(248,200,56), C(72,168,88), C(176,112,72), C(104,72,56), C(208,208,216), C(120,120,136) },
                Notes = "Taller proportions, darker outlines, more detail." },
            new GenStyle { Gen = 5, Games = "Black / White / Black 2 / White 2", FrontSize = 80, BackSize = 80, BackFrames = 5, OwW = 32, OwH = 32, MaxColors = 15,
                Palette = new[] { C(24,24,32), C(255,255,255), C(255,232,208), C(240,192,152), C(200,136,104), C(232,80,64), C(160,40,48), C(88,144,232), C(48,72,168),
                                  C(248,208,72), C(80,184,104), C(184,120,80), C(112,80,64), C(216,216,224), C(128,128,144) },
                Notes = "Same size as Gen 4 but more detail: dark coloured outlines and strong two-tone shading." },
        };

        public static GenStyle Get(int gen) { return All.FirstOrDefault(s => s.Gen == gen) ?? All[2]; }
    }

    /// <summary>Overworld sheet types (each is its own 4×4 sheet with its own parts).</summary>
    public static class OverworldSheets
    {
        public static readonly string[] Defaults = { "Walk", "Run", "Bike", "Surf", "Fish" };
        public static readonly string[] Directions = { "Down", "Left", "Right", "Up" };
    }

    public static class KindNames
    {
        public static string Folder(SpriteKind k) { return k.ToString(); }
        public static string Label(SpriteKind k)
        {
            switch (k) { case SpriteKind.Back: return "Trainer back sprite"; case SpriteKind.Overworld: return "Overworld sheet"; default: return "Trainer front sprite"; }
        }
    }
}
