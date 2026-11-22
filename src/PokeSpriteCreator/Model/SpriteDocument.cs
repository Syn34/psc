using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PokeSpriteCreator.Json;

namespace PokeSpriteCreator.Model
{
    /// <summary>A drawing layer: one picture per frame.</summary>
    public class Layer
    {
        public string Name = "Layer";
        public bool Visible = true;
        public List<PixelImage> Frames = new List<PixelImage>();

        public Layer Clone()
        {
            var l = (Layer)MemberwiseClone();
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
        /// <summary>The sprite's colours in the order they were first used (see ColorTools.ReduceTogether).</summary>
        public readonly List<int> ColorOrder = new List<int>();
        /// <summary>Reference pictures kept with the sprite (not part of the picture itself).</summary>
        public readonly List<RefPicture> References = new List<RefPicture>();
        /// <summary>After the colour limit was applied: each colour over the limit -> the colour it's shown as.</summary>
        public Dictionary<int, int> LastMerged = new Dictionary<int, int>();
        public string FilePath;
        public bool Dirty;
        public List<string> Warnings = new List<string>();

        /// <summary>
        /// A trainer front sprite's in-game walking sprites (an Overworld sprite saved in the same file), or null.
        /// Parent points back from the walking sprites to the front sprite.
        /// </summary>
        public SpriteDocument Overworld, Parent;

        /// <summary>The file this sprite belongs to (the front sprite for its walking sprites).</summary>
        public SpriteDocument Root { get { return Parent ?? this; } }

        /// <summary>Anything in the file changed since it was saved.</summary>
        public bool AnyDirty { get { return Dirty || (Overworld != null && Overworld.Dirty); } }

        /// <summary>Gives this front sprite a set of walking sprites (frames from a picture, or blank).</summary>
        public SpriteDocument MakeOverworld(FrameLayout lay, IList<PixelImage> frames)
        {
            var o = new SpriteDocument(Style, SpriteKind.Overworld, "Walk", lay ?? Style.DefaultLayout(SpriteKind.Overworld)) { Name = Name, LimitPalette = LimitPalette, Parent = this };
            o.Layers.Add(frames == null ? o.NewLayer("Walking sprites") : o.LayerFromFrames("Walking sprites", frames));
            Overworld = o;
            o.Dirty = true;
            return o;
        }

        public SpriteDocument(GenStyle style, SpriteKind kind, string sheet, FrameLayout layout)
        {
            Style = style; Kind = kind; Sheet = string.IsNullOrEmpty(sheet) ? "Walk" : sheet; Layout = layout;
        }

        public int FrameCount { get { return Layout.Count; } }

        public string Title
        {
            get { var r = Root; return (r.FilePath == null ? "Untitled" : Path.GetFileNameWithoutExtension(r.FilePath)) + (r.AnyDirty ? " *" : ""); }
        }

        public string Description
        {
            get
            {
                return Style.Name + " " + KindNames.Label(Kind).ToLowerInvariant() + (Kind == SpriteKind.Overworld ? " (" + Sheet + ")" : "") +
                    " · " + Layout.FrameW + "×" + Layout.FrameH + (FrameCount > 1 ? " × " + FrameCount + " frames" : "");
            }
        }

        public Layer NewLayer(string name)
        {
            var l = new Layer { Name = name };
            for (int i = 0; i < FrameCount; i++) l.Frames.Add(new PixelImage(Layout.FrameW, Layout.FrameH));
            return l;
        }

        public string UniqueName(string baseName)
        {
            if (!Layers.Any(l => l.Name == baseName)) return baseName;
            for (int i = 2; ; i++) if (!Layers.Any(l => l.Name == baseName + " " + i)) return baseName + " " + i;
        }

        /// <summary>A new layer holding these frames (cut to the frame size).</summary>
        public Layer LayerFromFrames(string name, IList<PixelImage> frames)
        {
            var l = NewLayer(name);
            for (int i = 0; i < FrameCount && i < frames.Count; i++) l.Frames[i] = frames[i].Crop(0, 0, Layout.FrameW, Layout.FrameH);
            return l;
        }

        // ------------------------------------------------------------------ drawing the sprite

        /// <summary>All visible layers flattened for one frame.</summary>
        public PixelImage Composite(int frame, Layer skip = null)
        {
            var img = new PixelImage(Layout.FrameW, Layout.FrameH);
            foreach (var l in Layers)
                if (l.Visible && l != skip && frame < l.Frames.Count) img.Blit(l.Frames[frame], 0, 0);
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
            Dictionary<int, int> merged;
            var r = ColorTools.ReduceTogether(frames, Style.MaxColors, ColorOrder, out merged);
            LastMerged = merged;
            return r;
        }

        public PixelImage BuildSheet(bool limit, int scale)
        {
            return MakeSheet(FinalFrames(limit), Layout, scale);
        }

        public static PixelImage MakeSheet(IList<PixelImage> frames, FrameLayout lay, int scale)
        {
            var sheet = new PixelImage(lay.SheetW, lay.SheetH);
            for (int i = 0; i < frames.Count && i < lay.Count; i++) sheet.Paste(frames[i], lay.FrameX(i), lay.FrameY(i));
            return scale > 1 ? sheet.Scale(scale) : sheet;
        }

        public HashSet<int> UsedColors()
        {
            var set = new HashSet<int>();
            for (int i = 0; i < FrameCount; i++) set.UnionWith(Composite(i).Colors());
            return set;
        }

        public int ColorCount() { return UsedColors().Count; }

        /// <summary>Swaps one colour for another on every layer and frame.</summary>
        public void ReplaceColorEverywhere(int from, int to)
        {
            if (from == 0 || from == to) return;
            foreach (var l in Layers)
                foreach (var f in l.Frames)
                    for (int i = 0; i < f.Px.Length; i++) if (f.Px[i] == from) f.Px[i] = to;
        }

        // ------------------------------------------------------------------ frames (back sprites)

        public void SetFrameCount(int n)
        {
            n = Math.Max(1, Math.Min(16, n));
            if (Kind == SpriteKind.Overworld || n == FrameCount) return;
            Layout = new FrameLayout(Layout.FrameW, Layout.FrameH, n, 1);
            foreach (var p in Layers)
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
            var o = ToJson();
            if (Overworld != null) o["overworld"] = Overworld.ToJson();
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Json.Write(o), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            FilePath = path;
            Dirty = false;
            if (Overworld != null) Overworld.Dirty = false;
        }

        JsonObject ToJson()
        {
            var o = new JsonObject();
            o["app"] = "PokeSpriteCreator";
            o["version"] = 2;
            o["gen"] = Style.Gen;
            o["kind"] = Kind.ToString();
            if (Kind == SpriteKind.Overworld) o["sheet"] = Sheet;
            o["name"] = Name;
            o["frameWidth"] = Layout.FrameW; o["frameHeight"] = Layout.FrameH;
            o["columns"] = Layout.Cols; o["rows"] = Layout.Rows;
            o["limitPalette"] = LimitPalette;
            if (References.Count > 0)
                o["references"] = References.Select(r => { var j = new JsonObject(); j["name"] = r.Name; j["picture"] = Convert.ToBase64String(r.Image.ToPngBytes()); return (object)j; }).ToList();
            if (ColorOrder.Count > 0) o["colorOrder"] = ColorOrder.Select(c => (object)PixelImage.Hex(c)).ToList();
            var arr = new List<object>();
            foreach (var l in Layers)
            {
                var j = new JsonObject();
                j["name"] = l.Name;
                j["visible"] = l.Visible;
                j["type"] = "paint";
                j["pixels"] = Convert.ToBase64String(MakeSheet(l.Frames, Layout, 1).ToPngBytes());
                arr.Add(j);
            }
            o["layers"] = arr;
            return o;
        }

        public static SpriteDocument Load(string path)
        {
            var o = Json.Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
            if (o == null || o.GetString("app") != "PokeSpriteCreator") throw new InvalidDataException("This isn't a Poke Sprite Creator file.");
            var d = FromJson(o);
            var ow = o.GetObject("overworld");
            if (ow != null && d.Kind == SpriteKind.Front)
            {
                d.Overworld = FromJson(ow);
                d.Overworld.Kind = SpriteKind.Overworld;
                d.Overworld.Parent = d;
                d.Warnings.AddRange(d.Overworld.Warnings);
            }
            d.FilePath = path;
            return d;
        }

        static SpriteDocument FromJson(JsonObject o)
        {
            var style = GenStyle.Get(o.GetInt("gen", 3));
            SpriteKind kind;
            if (!Enum.TryParse(o.GetString("kind", "Front"), out kind)) kind = SpriteKind.Front;
            var lay = new FrameLayout(o.GetInt("frameWidth", 64), o.GetInt("frameHeight", 64), o.GetInt("columns", 1), o.GetInt("rows", 1));
            var d = new SpriteDocument(style, kind, o.GetString("sheet", "Walk"), lay);
            d.Name = o.GetString("name", "NEWTRAINER");
            d.LimitPalette = o.GetBool("limitPalette", true);
            foreach (var r in (o.GetArray("references") ?? new List<object>()).OfType<JsonObject>())
            {
                try { d.References.Add(new RefPicture { Name = r.GetString("name", "Reference"), Image = PixelImage.FromPngBytes(Convert.FromBase64String(r.GetString("picture"))) }); }
                catch { d.Warnings.Add("A reference picture couldn't be read and was left out."); }
            }
            foreach (var h in (o.GetArray("colorOrder") ?? new List<object>()).OfType<string>())
            {
                try { d.ColorOrder.Add(PixelImage.ParseHex(h)); } catch { }
            }
            var layers = o.GetArray("layers") ?? new List<object>();
            foreach (var item in layers.OfType<JsonObject>())
            {
                var l = new Layer { Name = item.GetString("name", "Paint"), Visible = item.GetBool("visible", true) };
                if (item.GetString("type") == "part")
                {
                    // files from 0.1.x: a part layer becomes a paint layer made from the copy of the part saved in the file
                    l.Frames = LegacyPartFrames(item, lay);
                    d.Warnings.Add("\"" + l.Name + "\" was a part layer and is now a paint layer.");
                }
                else
                {
                    PixelImage sheet = null;
                    var px = item.GetString("pixels");
                    if (px != null) { try { sheet = PixelImage.FromPngBytes(Convert.FromBase64String(px)); } catch { } }
                    for (int i = 0; i < lay.Count; i++)
                        l.Frames.Add(sheet == null ? new PixelImage(lay.FrameW, lay.FrameH) : sheet.Crop(lay.FrameX(i), lay.FrameY(i), lay.FrameW, lay.FrameH));
                }
                d.Layers.Add(l);
            }
            return d;
        }

        static List<PixelImage> LegacyPartFrames(JsonObject item, FrameLayout lay)
        {
            var frames = Enumerable.Range(0, lay.Count).Select(i => new PixelImage(lay.FrameW, lay.FrameH)).ToList();
            int ox = item.GetInt("x"), oy = item.GetInt("y");
            bool flip = item.GetBool("flip");
            foreach (var key in new[] { "embeddedBehind", "embedded" })
            {
                var b64 = item.GetString(key);
                if (b64 == null) continue;
                PixelImage sheet;
                try { sheet = PixelImage.FromPngBytes(Convert.FromBase64String(b64)); } catch { continue; }
                var cut = ImageImport.Cut(sheet, lay);
                for (int i = 0; i < lay.Count; i++) frames[i].Blit(cut[i], ox, oy, flip);
            }
            var cm = item.GetObject("colors");
            if (cm != null)
            {
                var map = new Dictionary<int, int>();
                foreach (var kv in cm) map[PixelImage.ParseHex(kv.Key)] = PixelImage.ParseHex(Convert.ToString(kv.Value));
                foreach (var f in frames)
                    for (int i = 0; i < f.Px.Length; i++) { int m; if (f.Px[i] != 0 && map.TryGetValue(f.Px[i], out m)) f.Px[i] = m; }
            }
            int hue = item.GetInt("hue"), sat = item.GetInt("saturation"), light = item.GetInt("brightness");
            if (hue != 0 || sat != 0 || light != 0)
            {
                bool keep = item.GetBool("keepOutline", true);
                foreach (var f in frames)
                    for (int i = 0; i < f.Px.Length; i++)
                        if (f.Px[i] != 0 && !(keep && ColorTools.IsOutline(f.Px[i]))) f.Px[i] = ColorTools.Adjust(f.Px[i], hue, sat, light);
            }
            return frames;
        }
    }
}
