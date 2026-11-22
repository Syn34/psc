using System.Collections.Generic;
using System.Drawing;

namespace PokeSpriteCreator.UI
{
    /// <summary>Little pixel-art tool icons, drawn from text patterns (so there are no image files to ship).</summary>
    public static class Icons
    {
        static readonly Dictionary<string, string[]> Patterns = new Dictionary<string, string[]>
        {
            { "Pencil", new[] {
                "................",
                "...........kk...",
                "..........kppk..",
                ".........kyppk..",
                "........kyyyk...",
                ".......kyyyk....",
                "......kyyyk.....",
                ".....kyyyk......",
                "....kyyyk.......",
                "...kyyyk........",
                "...kwyk.........",
                "..kwwk..........",
                "..kkk...........",
                "..k.............",
                "................",
                "................" } },
            { "Eraser", new[] {
                "................",
                "................",
                "........kkkk....",
                ".......kppppk...",
                "......kppppppk..",
                ".....kppppppk...",
                "....kwkppppk....",
                "...kwwwkppk.....",
                "..kwwwwwkk......",
                "..kwwwwwk.......",
                "...kwwwk........",
                "....kkk.........",
                "........kkkkkk..",
                "................",
                "................",
                "................" } },
            { "Fill", new[] {
                "................",
                ".....kk.........",
                "....k..k........",
                "....k.kkk.......",
                "....kkwwwk......",
                "...kwwwwwwk.....",
                "..kwwwwwwwwk....",
                ".kwwwwwwwwwwk...",
                "..kwwwwwwwwkbb..",
                "...kwwwwwwkbbbb.",
                "....kwwwwk.bbbb.",
                ".....kwwk..bbbb.",
                "......kk....bb..",
                "................",
                "................",
                "................" } },
            { "Picker", new[] {
                "................",
                "...........kkk..",
                "..........kgggk.",
                "..........kgggk.",
                ".........kkggk..",
                "........kwkkk...",
                ".......kwwk.....",
                "......kwwk......",
                ".....kwwk.......",
                "....kwwk........",
                "...kbbk.........",
                "..kbbk..........",
                "..kbk...........",
                ".k..............",
                "................",
                "................" } },
            { "Line", new[] {
                "................",
                "................",
                ".............kk.",
                "............kk..",
                "...........kk...",
                "..........kk....",
                ".........kk.....",
                "........kk......",
                ".......kk.......",
                "......kk........",
                ".....kk.........",
                "....kk..........",
                "...kk...........",
                "..kk............",
                "................",
                "................" } },
            { "Rectangle", new[] {
                "................",
                "................",
                "..kkkkkkkkkkkk..",
                "..k..........k..",
                "..k..........k..",
                "..k..........k..",
                "..k..........k..",
                "..k..........k..",
                "..k..........k..",
                "..k..........k..",
                "..k..........k..",
                "..k..........k..",
                "..kkkkkkkkkkkk..",
                "................",
                "................",
                "................" } },
            { "Ellipse", new[] {
                "................",
                "................",
                ".....kkkkkk.....",
                "...kk......kk...",
                "..k..........k..",
                ".k............k.",
                ".k............k.",
                ".k............k.",
                ".k............k.",
                ".k............k.",
                "..k..........k..",
                "...kk......kk...",
                ".....kkkkkk.....",
                "................",
                "................",
                "................" } },
            { "Move", new[] {
                "................",
                ".......k........",
                "......kkk.......",
                ".....k.k.k......",
                ".......k........",
                "...k...k...k....",
                "..k....k....k...",
                ".kkkkkkkkkkkkk..",
                "..k....k....k...",
                "...k...k...k....",
                ".......k........",
                ".....k.k.k......",
                "......kkk.......",
                ".......k........",
                "................",
                "................" } },
            { "Reference", new[] {
                "................",
                ".kkkkkkkkkkkkkk.",
                ".kwwwwwwwwwwwwk.",
                ".kwwwwwwwwwyywk.",
                ".kwwwwwwwwwyywk.",
                ".kwwwwwwwwwwwwk.",
                ".kwwwwgwwwwwwwk.",
                ".kwwwgggwwwwwwk.",
                ".kwwgggggwwbwwk.",
                ".kwgggggggbbbwk.",
                ".kgggggggbbbbbk.",
                ".kkkkkkkkkkkkkk.",
                "................",
                "......pppp......",
                ".....p....p.....",
                "................" } },
            { "Shade", new[] {
                "................",
                ".....kkkkkk.....",
                "...kkwwwwggkk...",
                "..kwwwwwwwgggk..",
                "..kwyywwwwgggk..",
                ".kwyyywwwwggggk.",
                ".kwwyywwwwggggk.",
                ".kwwwwwwwgggggk.",
                ".kwwwwwwggggggk.",
                ".kwwwwwgggggggk.",
                "..kwwwgggggggk..",
                "..kggggggggggk..",
                "...kkggggggkk...",
                ".....kkkkkk.....",
                "................",
                "................" } },
            { "Recolor", new[] {
                "................",
                "................",
                "..kkkkk.........",
                "..krrrk.........",
                "..krrrk...k.....",
                "..krrrk....k....",
                "..kkkkk.kkkkk...",
                "...........k....",
                "..........k.....",
                ".........kkkkk..",
                ".........kbbbk..",
                ".........kbbbk..",
                ".........kbbbk..",
                ".........kkkkk..",
                "................",
                "................" } },
            { "Mirror", new[] {
                "................",
                ".......a........",
                ".......a........",
                "...k...a...k....",
                "..kk...a...kk...",
                ".kkk...a...kkk..",
                "kkkk...a...kkkk.",
                ".kkk...a...kkk..",
                "..kk...a...kk...",
                "...k...a...k....",
                ".......a........",
                ".......a........",
                ".......a........",
                "................",
                "................",
                "................" } },
        };

        static readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(string name)
        {
            string key = name + (Theme.Dark ? "/d" : "/l");
            Bitmap b;
            if (_cache.TryGetValue(key, out b)) return b;
            string[] p;
            if (!Patterns.TryGetValue(name, out p)) return null;
            b = new Bitmap(16, 16);
            for (int y = 0; y < 16 && y < p.Length; y++)
                for (int x = 0; x < 16 && x < p[y].Length; x++)
                {
                    Color c;
                    switch (p[y][x])
                    {
                        case 'k': c = Theme.Dark ? Color.FromArgb(225, 227, 232) : Color.FromArgb(40, 40, 46); break;
                        case 'w': c = Theme.Dark ? Color.FromArgb(90, 92, 100) : Color.White; break;
                        case 'g': c = Color.FromArgb(150, 150, 160); break;
                        case 'y': c = Color.FromArgb(240, 192, 64); break;
                        case 'p': c = Color.FromArgb(232, 120, 140); break;
                        case 'b': c = Color.FromArgb(70, 130, 230); break;
                        case 'r': c = Color.FromArgb(220, 60, 50); break;
                        case 'a': c = Theme.Accent; break;
                        default: continue;
                    }
                    b.SetPixel(x, y, c);
                }
            _cache[key] = b;
            return b;
        }
    }
}
