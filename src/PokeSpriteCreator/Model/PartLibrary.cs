using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeSpriteCreator.Model
{
    /// <summary>
    /// One piece of a sprite (a hairstyle, a top, a pose outline...), loaded from the parts folder.
    /// A part can have a companion "NAME_behind.png" that is drawn underneath everything else
    /// (long hair behind the back, the far side of a backpack, a bike's back wheel...).
    /// </summary>
    public class Part
    {
        public string Category, Name, Path, BehindPath;
        /// <summary>Comes with the app (the "Built-in parts" folder next to the exe), not from the user's parts folder.</summary>
        public bool BuiltIn;
        PixelImage _sheet, _behind;
        bool _loaded;
        readonly Dictionary<string, PartFrames> _frames = new Dictionary<string, PartFrames>();

        public PixelImage Sheet { get { Load(); return _sheet; } }
        public PixelImage BehindSheet { get { Load(); return _behind; } }

        void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _sheet = PixelImage.Load(Path);
            if (BehindPath != null) _behind = PixelImage.Load(BehindPath);
        }

        public void Reload() { if (Path != null) _loaded = false; _frames.Clear(); }

        protected void SetPictures(PixelImage sheet, PixelImage behind) { _sheet = sheet; _behind = behind; _loaded = true; _frames.Clear(); }

        /// <summary>The part cut into frames for a sprite layout (cached).</summary>
        public PartFrames FramesFor(FrameLayout lay)
        {
            string key = lay.FrameW + "x" + lay.FrameH + "/" + lay.Cols + "x" + lay.Rows;
            PartFrames f;
            if (!_frames.TryGetValue(key, out f)) { f = new PartFrames(Sheet, BehindSheet, lay); _frames[key] = f; }
            return f;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// A part's pictures for every frame of a layout. A part sheet that matches the layout's grid is cut
    /// into frames; a single picture of frame size is used for every frame; one column with a row per
    /// direction is used for every frame of that direction. Anything else is a "loose" picture that the
    /// user places where it should go (the same picture on every frame).
    /// Sheets drawn pixel-doubled (2×, 3×, 4×) are shrunk back to the sprite's size automatically.
    /// </summary>
    public class PartFrames
    {
        public readonly PixelImage[] Front, Behind;
        public readonly bool Loose;
        public readonly int Scale = 1;

        public PartFrames(PixelImage sheet, PixelImage behind, FrameLayout lay)
        {
            Front = new PixelImage[lay.Count];
            Behind = behind == null ? null : new PixelImage[lay.Count];
            if (sheet == null) { Loose = true; return; }
            int k = DetectScale(sheet, lay);
            Scale = k;
            if (k > 1) { sheet = sheet.Downscale(k); if (behind != null && behind.W == sheet.W * k && behind.H == sheet.H * k) behind = behind.Downscale(k); }
            Loose = !Fits(sheet, lay);
            Cut(sheet, lay, Front);
            if (behind != null) Cut(behind, lay, Behind);
        }

        static bool Fits(PixelImage s, FrameLayout lay) { return s.W % lay.FrameW == 0 && s.H % lay.FrameH == 0; }

        static int DetectScale(PixelImage s, FrameLayout lay)
        {
            if (Fits(s, lay) && Known(s.W / lay.FrameW, s.H / lay.FrameH, lay)) return 1;
            for (int k = 2; k <= 4; k++)
                if (s.W % (lay.FrameW * k) == 0 && s.H % (lay.FrameH * k) == 0 && Known(s.W / k / lay.FrameW, s.H / k / lay.FrameH, lay) && s.IsBlocky(k)) return k;
            if (Fits(s, lay) && s.W <= lay.SheetW && s.H <= lay.SheetH) return 1;
            // loose pictures drawn doubled
            for (int k = 2; k <= 4; k++)
                if ((s.W > lay.FrameW || s.H > lay.FrameH) && s.IsBlocky(k)) return k;
            return 1;
        }

        static bool Known(int cols, int rows, FrameLayout lay)
        {
            return (cols == lay.Cols && rows == lay.Rows) || (cols == 1 && rows == 1) || (cols == 1 && rows == lay.Rows) || (rows == 1 && cols == lay.Cols);
        }

        static void Cut(PixelImage s, FrameLayout lay, PixelImage[] dest)
        {
            if (!Fits(s, lay)) { for (int i = 0; i < dest.Length; i++) dest[i] = s; return; }
            int cols = s.W / lay.FrameW, rows = s.H / lay.FrameH, n = cols * rows;
            var cells = new PixelImage[n];
            for (int i = 0; i < n; i++) cells[i] = s.Crop((i % cols) * lay.FrameW, (i / cols) * lay.FrameH, lay.FrameW, lay.FrameH);
            for (int i = 0; i < dest.Length; i++)
            {
                int col = i % lay.Cols, row = i / lay.Cols, src;
                if (cols == lay.Cols && rows == lay.Rows) src = i;
                else if (n == 1) src = 0;
                else if (cols == 1 && rows == lay.Rows) src = row;          // one picture per direction
                else if (rows == 1 && cols == lay.Cols) src = col;          // one row used for every direction
                else src = i % n;
                dest[i] = cells[src];
            }
        }
    }

    /// <summary>
    /// The parts folder: PARTS/Gen N/Front|Back/CATEGORY/*.png and PARTS/Gen N/Overworld/SHEET/CATEGORY/*.png.
    /// Parts come from two folders with the same layout: the user's own (Root) and the ones that ship with
    /// the app (BuiltInRoot). Both are listed together; a user part with the same name replaces the built-in one.
    /// </summary>
    public class PartLibrary
    {
        public string Root;
        public string BuiltInRoot;
        /// <summary>The bottom-to-top layer order used when parts are added (overridden by order.txt in a kind folder).</summary>
        public static readonly string[] DefaultOrder = { "Poses", "Shoes", "Bottoms", "Tops", "Jackets", "Gloves", "Backpacks", "Hair", "Hats", "Visors", "Sunglasses", "Accessories", "Bikes" };
        public const string PoseCategory = "Poses";
        static readonly string[] Exts = { ".png", ".gif", ".bmp" };
        readonly Dictionary<string, List<Part>> _cache = new Dictionary<string, List<Part>>(StringComparer.OrdinalIgnoreCase);

        public PartLibrary(string root, string builtInRoot = null) { Root = root; BuiltInRoot = builtInRoot; }

        public void Refresh() { _cache.Clear(); }

        /// <summary>The user's folder for a gen / kind / sheet (where new parts go).</summary>
        public string KindFolder(int gen, SpriteKind kind, string sheet) { return KindFolderIn(Root, gen, kind, sheet); }

        static string KindFolderIn(string root, int gen, SpriteKind kind, string sheet)
        {
            var p = System.IO.Path.Combine(root, "Gen " + gen, KindNames.Folder(kind));
            if (kind == SpriteKind.Overworld) p = System.IO.Path.Combine(p, string.IsNullOrEmpty(sheet) ? "Walk" : sheet);
            return p;
        }

        /// <summary>The existing roots, built-in first.</summary>
        IEnumerable<string> Roots
        {
            get
            {
                if (!string.IsNullOrEmpty(BuiltInRoot) && Directory.Exists(BuiltInRoot) && !SamePath(BuiltInRoot, Root)) yield return BuiltInRoot;
                if (!string.IsNullOrEmpty(Root)) yield return Root;
            }
        }

        static bool SamePath(string a, string b)
        {
            try { return string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>Category folders for a gen / kind / sheet (from both folders), in layer order.</summary>
        public List<string> Categories(int gen, SpriteKind kind, string sheet)
        {
            var names = new List<string>();
            foreach (var root in Roots)
            {
                var dir = KindFolderIn(root, gen, kind, sheet);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in Directory.GetDirectories(dir).Select(x => System.IO.Path.GetFileName(x)))
                    if (!names.Contains(d, StringComparer.OrdinalIgnoreCase)) names.Add(d);
            }
            var order = OrderFor(gen, kind, sheet);
            return names.OrderBy(n => Rank(order, n)).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public List<string> OrderFor(int gen, SpriteKind kind, string sheet)
        {
            // the user's order.txt wins over a built-in one
            foreach (var root in Roots.Reverse())
            {
                var f = System.IO.Path.Combine(KindFolderIn(root, gen, kind, sheet), "order.txt");
                if (!File.Exists(f)) continue;
                try { return File.ReadAllLines(f).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList(); }
                catch { }
            }
            return DefaultOrder.ToList();
        }

        /// <summary>Where a category sits in the stack (unknown categories go just under Accessories).</summary>
        public static int Rank(List<string> order, string cat)
        {
            int i = order.FindIndex(o => string.Equals(o, cat, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return i * 10;
            int acc = order.FindIndex(o => string.Equals(o, "Accessories", StringComparison.OrdinalIgnoreCase));
            return acc >= 0 ? acc * 10 - 5 : order.Count * 10;
        }

        public List<Part> Parts(int gen, SpriteKind kind, string sheet, string category)
        {
            var key = gen + "/" + kind + "/" + (kind == SpriteKind.Overworld ? sheet : "") + "/" + category;
            List<Part> list;
            if (_cache.TryGetValue(key, out list)) return list;
            var byName = new Dictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in Roots)
            {
                bool builtIn = root == BuiltInRoot;
                var dir = System.IO.Path.Combine(KindFolderIn(root, gen, kind, sheet), category);
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir).Where(f => Exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToList();
                foreach (var f in files)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.EndsWith("_behind", StringComparison.OrdinalIgnoreCase)) continue;
                    var behind = files.FirstOrDefault(o => string.Equals(System.IO.Path.GetFileNameWithoutExtension(o), name + "_behind", StringComparison.OrdinalIgnoreCase));
                    byName[name] = new Part { Category = category, Name = name, Path = f, BehindPath = behind, BuiltIn = builtIn };
                }
            }
            list = byName.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _cache[key] = list;
            return list;
        }

        public Part Find(int gen, SpriteKind kind, string sheet, string category, string name)
        {
            return Parts(gen, kind, sheet, category).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Overworld sheet folders that exist (always including the defaults).</summary>
        public List<string> Sheets(int gen)
        {
            var list = OverworldSheets.Defaults.ToList();
            foreach (var root in Roots)
            {
                var dir = System.IO.Path.Combine(root, "Gen " + gen, "Overworld");
                if (Directory.Exists(dir))
                    foreach (var d in Directory.GetDirectories(dir).Select(System.IO.Path.GetFileName))
                        if (!list.Contains(d, StringComparer.OrdinalIgnoreCase)) list.Add(d);
            }
            return list;
        }

        /// <summary>Parts in the user's folder.</summary>
        public int CountAll() { return CountIn(Root); }

        /// <summary>Parts that came with the app.</summary>
        public int CountBuiltIn() { return BuiltInRoot == null || SamePath(BuiltInRoot, Root ?? "") ? 0 : CountIn(BuiltInRoot); }

        static int CountIn(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;
            return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories).Count(f => Exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()) && !System.IO.Path.GetFileNameWithoutExtension(f).EndsWith("_behind", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Creates the empty folder tree (so it's clear where each part goes) and the guide.</summary>
        public void EnsureFolders()
        {
            try
            {
                Directory.CreateDirectory(Root);
                var front = new[] { "Poses", "Hair", "Hats", "Visors", "Sunglasses", "Tops", "Jackets", "Bottoms", "Shoes", "Gloves", "Backpacks", "Accessories" };
                foreach (var s in GenStyle.All)
                {
                    foreach (var k in new[] { SpriteKind.Front, SpriteKind.Back })
                        foreach (var c in front) Directory.CreateDirectory(System.IO.Path.Combine(KindFolder(s.Gen, k, null), c));
                    foreach (var sh in OverworldSheets.Defaults)
                    {
                        foreach (var c in front) Directory.CreateDirectory(System.IO.Path.Combine(KindFolder(s.Gen, SpriteKind.Overworld, sh), c));
                        if (sh == "Bike") Directory.CreateDirectory(System.IO.Path.Combine(KindFolder(s.Gen, SpriteKind.Overworld, sh), "Bikes"));
                    }
                }
                var guide = System.IO.Path.Combine(Root, "README.txt");
                if (!File.Exists(guide)) File.WriteAllText(guide, Guide);
            }
            catch { }
        }

        public const string Guide =
@"POKE SPRITE CREATOR - PARTS FOLDER
==================================

Put your part pictures (PNG) in these folders. The folder a picture is in decides
where it shows up in the app:

  Gen 1 .. Gen 5         the style (each generation has its own parts)
    Front                trainer front sprites (one picture)
    Back                 trainer back sprites (a strip of frames, left to right)
    Overworld
      Walk, Run, Bike,   overworld sheets (4 x 4 frames: rows are Down, Left,
      Surf, Fish         Right, Up; columns are the walking steps)
        Poses, Hair,     categories. Make any new folder to add a category;
        Hats, Tops ...   its name is shown as a tab in the app.

The file name is the part's name (Spiky.png shows as ""Spiky"").

SIZES (native game size, or exactly 2x/3x/4x pixel-doubled):
  Gen 1  front 56x56   back 32x32            overworld frames 16x16
  Gen 2  front 56x56   back 48x48            overworld frames 16x16
  Gen 3  front 64x64   back 64x64 x4 frames  overworld frames 16x32
  Gen 4  front 80x80   back 80x80 x5 frames  overworld frames 32x32
  Gen 5  front 80x80   back 80x80 x5 frames  overworld frames 32x32
A pose sets the canvas size, so if your poses use another size, draw the parts
for them at that size too.

HOW PART PICTURES ARE USED
  * Same size as the whole sprite/sheet: each frame comes from its own spot.
  * One frame's size: used on every frame.
  * One column with 4 frames (overworld): one picture per direction.
  * Any other size: a ""loose"" piece that you drag into place in the app.
  * Draw parts on a transparent background, already lined up with the poses
    (draw them over a pose, then hide the pose layer when saving).
  * NAME_behind.png next to NAME.png is drawn underneath the body
    (long hair behind the back, the far strap of a backpack, a bike's back wheel).

POSES are the blank outlines users start from and fill in.

BUILT-IN PARTS: the app also comes with a ""Built-in parts"" folder next to
PokeSpriteCreator.exe (same layout as this folder). Those parts are always
listed too. A part in this folder with the same category and name replaces
the built-in one, so you can't lose them by editing your own parts.

LAYER ORDER (bottom to top) when parts are added:
  Poses, Shoes, Bottoms, Tops, Jackets, Gloves, Backpacks, Hair, Hats, Visors,
  Sunglasses, Accessories, Bikes  (other categories go just under Accessories)
To change it for one folder (e.g. Gen 3\Overworld\Bike), put an order.txt there
listing the category names, bottom first, one per line.
";
    }
}
