using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PokeSpriteCreator.Json;

namespace PokeSpriteCreator.Model
{
    public abstract class Layer
    {
        public string Name = "Layer";
        public bool Visible = true;
        public abstract Layer Clone();
        public abstract bool IsPart { get; }
    }

    /// <summary>A layer made from a part in the parts folder, with its own colours and position.</summary>
    public class PartLayer : Layer
    {
        public string Category, PartName;
        public Part Part;                     // null when the picture can't be found
        public int OffsetX, OffsetY;
        public bool FlipH;
        public Dictionary<int, int> ColorMap = new Dictionary<int, int>();
        public int Hue, Sat, Light;
        public bool KeepOutline = true;

        Dictionary<int, int> _map; string _mapKey;

        public override bool IsPart { get { return true; } }

        public override Layer Clone()
        {
            var l = (PartLayer)MemberwiseClone();
            l.ColorMap = new Dictionary<int, int>(ColorMap);
            l._map = null; l._mapKey = null;
            return l;
        }

        public bool HasColorChanges { get { return ColorMap.Count > 0 || Hue != 0 || Sat != 0 || Light != 0; } }

        public void ResetColors() { ColorMap.Clear(); Hue = Sat = Light = 0; _mapKey = null; }

        /// <summary>The colour a part colour is shown as (after the user's swaps and hue/sat/brightness).</summary>
        public int MapColor(int c)
        {
            if (c == 0) return 0;
            int m;
            if (ColorMap.TryGetValue(c, out m)) return m;
            if (Hue == 0 && Sat == 0 && Light == 0) return c;
            if (KeepOutline && ColorTools.IsOutline(c)) return c;
            return ColorTools.Adjust(c, Hue, Sat, Light);
        }

        /// <summary>The colours the part picture uses (front and behind), darkest first.</summary>
        public List<int> SourcePalette()
        {
            if (Part == null || Part.Sheet == null) return new List<int>();
            var set = Part.Sheet.Colors();
            if (Part.BehindSheet != null) set.UnionWith(Part.BehindSheet.Colors());
            return set.OrderBy(c => PixelImage.Luma(c)).ThenBy(c => c).ToList();
        }

        Dictionary<int, int> Map()
        {
            string key = Hue + "," + Sat + "," + Light + "," + KeepOutline + "," + string.Join(";", ColorMap.Select(kv => kv.Key + ">" + kv.Value));
            if (_map != null && key == _mapKey) return _map;
            _map = new Dictionary<int, int>();
            foreach (var c in SourcePalette()) _map[c] = MapColor(c);
            _mapKey = key;
            return _map;
        }

        public bool IsLoose(FrameLayout lay) { return Part != null && Part.Sheet != null && Part.FramesFor(lay).Loose; }

        /// <summary>The re-coloured picture for one frame (null if none).</summary>
        public PixelImage FrameImage(FrameLayout lay, int i, bool behind)
        {
            if (Part == null || Part.Sheet == null) return null;
            var f = Part.FramesFor(lay);
            var arr = behind ? f.Behind : f.Front;
            if (arr == null || i >= arr.Length || arr[i] == null) return null;
            var src = arr[i];
            if (!HasColorChanges) return src;
            var map = Map();
            var r = src.Clone();
            for (int k = 0; k < r.Px.Length; k++)
            {
                int c = r.Px[k];
                if (c == 0) continue;
                int m; r.Px[k] = map.TryGetValue(c, out m) ? m : MapColor(c);
            }
            return r;
        }
    }

    /// <summary>A free-hand layer: one picture per frame.</summary>
    public class PaintLayer : Layer
    {
        public List<PixelImage> Frames = new List<PixelImage>();
        public override bool IsPart { get { return false; } }
        public override Layer Clone()
        {
            var l = (PaintLayer)MemberwiseClone();
            l.Frames = Frames.Select(f => f.Clone()).ToList();
            return l;
        }
    }

    /// <summary>A snapshot used by Undo / Redo.</summary>
    public class DocState
    {
        public List<Layer> Layers;
        public FrameLayout Layout;
        public int Selected;
    }

    /// <summary>The sprite being edited: style, kind, frame layout and a stack of layers (bottom first).</summary>
    public class SpriteDocument
    {
        public const string Extension = ".psc";
        public GenStyle Style;
        public SpriteKind Kind;
        public string Sheet = "Walk";
        public string Name = "NEWTRAINER";
        public FrameLayout Layout;
        public List<Layer> Layers = new List<Layer>();
        public bool LimitPalette = true;
        public string FilePath;
        public bool Dirty;
        public List<string> Warnings = new List<string>();

        public SpriteDocument(GenStyle style, SpriteKind kind, string sheet, FrameLayout layout)
        {
            Style = style; Kind = kind; Sheet = string.IsNullOrEmpty(sheet) ? "Walk" : sheet; Layout = layout;
        }

        public int FrameCount { get { return Layout.Count; } }

        public string Title
        {
            get { return (FilePath == null ? "Untitled" : Path.GetFileNameWithoutExtension(FilePath)) + (Dirty ? " *" : ""); }
        }

        public string Description
        {
            get
            {
                return Style.Name + " " + KindNames.Label(Kind).ToLowerInvariant() + (Kind == SpriteKind.Overworld ? " (" + Sheet + ")" : "") +
                    " · " + Layout.FrameW + "×" + Layout.FrameH + (FrameCount > 1 ? " × " + FrameCount + " frames" : "");
            }
        }

        public PaintLayer NewPaintLayer(string name)
        {
            var l = new PaintLayer { Name = name };
            for (int i = 0; i < FrameCount; i++) l.Frames.Add(new PixelImage(Layout.FrameW, Layout.FrameH));
            return l;
        }

        public string UniqueName(string baseName)
        {
            if (!Layers.Any(l => l.Name == baseName)) return baseName;
            for (int i = 2; ; i++) if (!Layers.Any(l => l.Name == baseName + " " + i)) return baseName + " " + i;
        }

        public PartLayer MakePartLayer(Part p)
        {
            var l = new PartLayer { Name = p.Category, Category = p.Category, PartName = p.Name, Part = p };
            if (p.Sheet != null && p.FramesFor(Layout).Loose)
            {
                var img = p.FramesFor(Layout).Front[0];
                l.OffsetX = (Layout.FrameW - img.W) / 2;
                l.OffsetY = (Layout.FrameH - img.H) / 2;
            }
            return l;
        }

        /// <summary>Index a new layer of this category should go at (by the category layer order).</summary>
        public int InsertIndexFor(string category, List<string> order)
        {
            int rank = PartLibrary.Rank(order, category);
            for (int i = 0; i < Layers.Count; i++)
            {
                var pl = Layers[i] as PartLayer;
                if (pl != null && PartLibrary.Rank(order, pl.Category) > rank) return i;
            }
            return Layers.Count;
        }

        // ------------------------------------------------------------------ drawing the sprite

        /// <summary>All visible layers flattened for one frame (behind pictures first). skip = a layer to leave out.</summary>
        public PixelImage Composite(int frame, Layer skip = null)
        {
            var img = new PixelImage(Layout.FrameW, Layout.FrameH);
            foreach (var l in Layers)
            {
                var pl = l as PartLayer;
                if (pl == null || !pl.Visible || l == skip) continue;
                var b = pl.FrameImage(Layout, frame, true);
                if (b != null) img.Blit(b, pl.OffsetX, pl.OffsetY, pl.FlipH);
            }
            foreach (var l in Layers)
            {
                if (!l.Visible || l == skip) continue;
                DrawLayer(img, l, frame);
            }
            return img;
        }

        public void DrawLayer(PixelImage img, Layer l, int frame)
        {
            var pl = l as PartLayer;
            if (pl != null)
            {
                var f = pl.FrameImage(Layout, frame, false);
                if (f != null) img.Blit(f, pl.OffsetX, pl.OffsetY, pl.FlipH);
            }
            else
            {
                var p = (PaintLayer)l;
                if (frame < p.Frames.Count) img.Blit(p.Frames[frame], 0, 0);
            }
        }

        /// <summary>Just one layer for one frame (for thumbnails).</summary>
        public PixelImage LayerImage(Layer l, int frame)
        {
            var img = new PixelImage(Layout.FrameW, Layout.FrameH);
            var pl = l as PartLayer;
            if (pl != null) { var b = pl.FrameImage(Layout, frame, true); if (b != null) img.Blit(b, pl.OffsetX, pl.OffsetY, pl.FlipH); }
            DrawLayer(img, l, frame);
            return img;
        }

        /// <summary>Every frame, with the style's colour limit applied when LimitPalette is on (all frames share the palette).</summary>
        public List<PixelImage> FinalFrames(bool limit)
        {
            var frames = Enumerable.Range(0, FrameCount).Select(i => Composite(i)).ToList();
            return limit ? ApplyStyle(frames) : frames;
        }

        public List<PixelImage> ApplyStyle(List<PixelImage> frames)
        {
            if (Style.Greyscale) return frames.Select(f => ColorTools.ToGreyscale4(f, Style.Palette)).ToList();
            return ColorTools.ReduceTogether(frames, Style.MaxColors);
        }

        public PixelImage BuildSheet(bool limit, int scale)
        {
            var frames = FinalFrames(limit);
            var sheet = new PixelImage(Layout.SheetW, Layout.SheetH);
            for (int i = 0; i < frames.Count; i++) sheet.Paste(frames[i], Layout.FrameX(i), Layout.FrameY(i));
            return scale > 1 ? sheet.Scale(scale) : sheet;
        }

        public int ColorCount()
        {
            var set = new HashSet<int>();
            for (int i = 0; i < FrameCount; i++) set.UnionWith(Composite(i).Colors());
            return set.Count;
        }

        public HashSet<int> UsedColors()
        {
            var set = new HashSet<int>();
            for (int i = 0; i < FrameCount; i++) set.UnionWith(Composite(i).Colors());
            return set;
        }

        /// <summary>Swaps one colour for another on every layer (part layers get a colour override).</summary>
        public void ReplaceColorEverywhere(int from, int to)
        {
            if (from == 0 || from == to) return;
            foreach (var l in Layers)
            {
                var pl = l as PartLayer;
                if (pl != null)
                {
                    foreach (var src in pl.SourcePalette())
                        if (pl.MapColor(src) == from) pl.ColorMap[src] = to;
                }
                else
                    foreach (var f in ((PaintLayer)l).Frames)
                        for (int i = 0; i < f.Px.Length; i++) if (f.Px[i] == from) f.Px[i] = to;
            }
        }

        // ------------------------------------------------------------------ frames (back sprites)

        public void SetFrameCount(int n)
        {
            n = Math.Max(1, Math.Min(16, n));
            if (Kind == SpriteKind.Overworld || n == FrameCount) return;
            Layout = new FrameLayout(Layout.FrameW, Layout.FrameH, n, 1);
            foreach (var p in Layers.OfType<PaintLayer>())
            {
                while (p.Frames.Count < n) p.Frames.Add(p.Frames.Count > 0 ? p.Frames[p.Frames.Count - 1].Clone() : new PixelImage(Layout.FrameW, Layout.FrameH));
                while (p.Frames.Count > n) p.Frames.RemoveAt(p.Frames.Count - 1);
            }
        }

        // ------------------------------------------------------------------ undo snapshots

        public DocState Capture(int selected)
        {
            return new DocState { Layers = Layers.Select(l => l.Clone()).ToList(), Layout = Layout, Selected = selected };
        }

        public void Restore(DocState s)
        {
            Layers = s.Layers.Select(l => l.Clone()).ToList();
            Layout = s.Layout;
        }

        // ------------------------------------------------------------------ save / load

        public void Save(string path)
        {
            var o = new JsonObject();
            o["app"] = "PokeSpriteCreator";
            o["version"] = 1;
            o["gen"] = Style.Gen;
            o["kind"] = Kind.ToString();
            if (Kind == SpriteKind.Overworld) o["sheet"] = Sheet;
            o["name"] = Name;
            o["frameWidth"] = Layout.FrameW; o["frameHeight"] = Layout.FrameH;
            o["columns"] = Layout.Cols; o["rows"] = Layout.Rows;
            o["limitPalette"] = LimitPalette;
            var arr = new List<object>();
            foreach (var l in Layers)
            {
                var j = new JsonObject();
                j["name"] = l.Name;
                j["visible"] = l.Visible;
                var pl = l as PartLayer;
                if (pl != null)
                {
                    j["type"] = "part";
                    j["category"] = pl.Category;
                    j["part"] = pl.PartName;
                    j["x"] = pl.OffsetX; j["y"] = pl.OffsetY;
                    j["flip"] = pl.FlipH;
                    j["hue"] = pl.Hue; j["saturation"] = pl.Sat; j["brightness"] = pl.Light;
                    j["keepOutline"] = pl.KeepOutline;
                    var cm = new JsonObject();
                    foreach (var kv in pl.ColorMap) cm[PixelImage.Hex(kv.Key)] = PixelImage.Hex(kv.Value);
                    j["colors"] = cm;
                    // a copy of the part's pictures, so the sprite still opens on a PC without these parts
                    if (pl.Part != null && pl.Part.Sheet != null)
                    {
                        j["embedded"] = Convert.ToBase64String(pl.Part.Sheet.ToPngBytes());
                        if (pl.Part.BehindSheet != null) j["embeddedBehind"] = Convert.ToBase64String(pl.Part.BehindSheet.ToPngBytes());
                    }
                }
                else
                {
                    j["type"] = "paint";
                    var p = (PaintLayer)l;
                    var sheet = new PixelImage(Layout.SheetW, Layout.SheetH);
                    for (int i = 0; i < p.Frames.Count && i < FrameCount; i++) sheet.Paste(p.Frames[i], Layout.FrameX(i), Layout.FrameY(i));
                    j["pixels"] = Convert.ToBase64String(sheet.ToPngBytes());
                }
                arr.Add(j);
            }
            o["layers"] = arr;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Json.Write(o), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            FilePath = path;
            Dirty = false;
        }

        public static SpriteDocument Load(string path, PartLibrary lib)
        {
            var o = Json.Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
            if (o == null || o.GetString("app") != "PokeSpriteCreator") throw new InvalidDataException("This isn't a Poke Sprite Creator file.");
            var style = GenStyle.Get(o.GetInt("gen", 3));
            SpriteKind kind;
            if (!Enum.TryParse(o.GetString("kind", "Front"), out kind)) kind = SpriteKind.Front;
            var lay = new FrameLayout(o.GetInt("frameWidth", 64), o.GetInt("frameHeight", 64), o.GetInt("columns", 1), o.GetInt("rows", 1));
            var d = new SpriteDocument(style, kind, o.GetString("sheet", "Walk"), lay);
            d.Name = o.GetString("name", "NEWTRAINER");
            d.LimitPalette = o.GetBool("limitPalette", true);
            var layers = o.GetArray("layers") ?? new List<object>();
            foreach (var item in layers.OfType<JsonObject>())
            {
                if (item.GetString("type") == "part")
                {
                    var l = new PartLayer
                    {
                        Name = item.GetString("name", "Part"), Visible = item.GetBool("visible", true),
                        Category = item.GetString("category", ""), PartName = item.GetString("part", ""),
                        OffsetX = item.GetInt("x"), OffsetY = item.GetInt("y"), FlipH = item.GetBool("flip"),
                        Hue = item.GetInt("hue"), Sat = item.GetInt("saturation"), Light = item.GetInt("brightness"),
                        KeepOutline = item.GetBool("keepOutline", true),
                    };
                    var cm = item.GetObject("colors");
                    if (cm != null) foreach (var kv in cm) l.ColorMap[PixelImage.ParseHex(kv.Key)] = PixelImage.ParseHex(Convert.ToString(kv.Value, CultureInfo.InvariantCulture));
                    l.Part = lib == null ? null : lib.Find(style.Gen, kind, d.Sheet, l.Category, l.PartName);
                    if (l.Part == null)
                    {
                        var emb = item.GetString("embedded");
                        if (emb != null)
                        {
                            l.Part = new EmbeddedPart(l.Category, l.PartName, Convert.FromBase64String(emb), item.GetString("embeddedBehind"));
                            d.Warnings.Add("Part \"" + l.Category + " / " + l.PartName + "\" isn't in your parts folder; using the copy saved in the sprite.");
                        }
                        else d.Warnings.Add("Part \"" + l.Category + " / " + l.PartName + "\" wasn't found.");
                    }
                    d.Layers.Add(l);
                }
                else
                {
                    var p = new PaintLayer { Name = item.GetString("name", "Paint"), Visible = item.GetBool("visible", true) };
                    PixelImage sheet = null;
                    var px = item.GetString("pixels");
                    if (px != null) { try { sheet = PixelImage.FromPngBytes(Convert.FromBase64String(px)); } catch { } }
                    for (int i = 0; i < lay.Count; i++)
                        p.Frames.Add(sheet == null ? new PixelImage(lay.FrameW, lay.FrameH) : sheet.Crop(lay.FrameX(i), lay.FrameY(i), lay.FrameW, lay.FrameH));
                    d.Layers.Add(p);
                }
            }
            d.FilePath = path;
            return d;
        }
    }

    /// <summary>A part restored from the copy saved inside a sprite file.</summary>
    public class EmbeddedPart : Part
    {
        public EmbeddedPart(string cat, string name, byte[] png, string behindB64)
        {
            Category = cat; Name = name;
            SetPictures(PixelImage.FromPngBytes(png), behindB64 == null ? null : PixelImage.FromPngBytes(Convert.FromBase64String(behindB64)));
        }
    }
}
