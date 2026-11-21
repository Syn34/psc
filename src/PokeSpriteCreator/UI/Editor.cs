using System;
using System.Collections.Generic;
using System.Linq;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    public enum Tool { Pencil, Eraser, Fill, Picker, Line, Rectangle, Ellipse, Move, Recolor }

    /// <summary>
    /// The editing session shared by all panels: the document, the selected layer and frame,
    /// the drawing colours and tool, and Undo / Redo.
    /// </summary>
    public class Editor
    {
        public SpriteDocument Doc;
        public PartLibrary Lib;
        public Settings Settings;
        public int LayerIndex = -1;
        public int Frame;
        public int Primary = unchecked((int)0xFF000000), Secondary = unchecked((int)0xFFFFFFFF);
        public Tool Tool = Tool.Pencil;
        public bool Mirror, SampleMerged = true, OnionSkin;
        public PixelImage FrameClipboard;

        readonly List<DocState> _undo = new List<DocState>(), _redo = new List<DocState>();
        string _lastKey; DateTime _lastTime;
        List<PixelImage> _preview;

        /// <summary>Pixels / colours changed (redraw).</summary>
        public event Action DocChanged;
        /// <summary>Layers added, removed, reordered or renamed, or a new document.</summary>
        public event Action LayersChanged;
        public event Action SelectionChanged;
        public event Action FrameChanged;
        public event Action ColorsChanged;
        public event Action ToolChanged;
        public event Action<string> Status;

        public Editor(Settings s) { Settings = s; Lib = new PartLibrary(s.PartsFolder); }

        public Layer Current { get { return Doc != null && LayerIndex >= 0 && LayerIndex < Doc.Layers.Count ? Doc.Layers[LayerIndex] : null; } }
        public PartLayer CurrentPart { get { return Current as PartLayer; } }
        public PaintLayer CurrentPaint { get { return Current as PaintLayer; } }

        public void Say(string msg) { if (Status != null) Status(msg); }

        public void SetDocument(SpriteDocument d)
        {
            Doc = d;
            _undo.Clear(); _redo.Clear(); _lastKey = null;
            Frame = 0;
            LayerIndex = d.Layers.Count - 1;
            _preview = null;
            if (LayersChanged != null) LayersChanged();
            if (FrameChanged != null) FrameChanged();
            if (SelectionChanged != null) SelectionChanged();
            if (DocChanged != null) DocChanged();
        }

        // ------------------------------------------------------------------ change notifications

        /// <summary>Call before changing the document. Calls with the same key in quick succession (slider drags) share one Undo step.</summary>
        public void BeginChange(string key = null)
        {
            if (Doc == null) return;
            var now = DateTime.Now;
            if (key != null && key == _lastKey && (now - _lastTime).TotalSeconds < 1.5) { _lastTime = now; return; }
            _lastKey = key; _lastTime = now;
            _undo.Add(Doc.Capture(LayerIndex));
            if (_undo.Count > 200) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public void Changed()
        {
            if (Doc == null) return;
            Doc.Dirty = true;
            _preview = null;
            if (DocChanged != null) DocChanged();
        }

        public void LayersEdited()
        {
            if (Doc == null) return;
            Doc.Dirty = true;
            _preview = null;
            if (LayerIndex >= Doc.Layers.Count) LayerIndex = Doc.Layers.Count - 1;
            if (LayersChanged != null) LayersChanged();
            if (SelectionChanged != null) SelectionChanged();
            if (DocChanged != null) DocChanged();
        }

        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        public void Undo() { Step(_undo, _redo, "Undo"); }
        public void Redo() { Step(_redo, _undo, "Redo"); }

        void Step(List<DocState> from, List<DocState> to, string what)
        {
            if (Doc == null || from.Count == 0) { Say("Nothing to " + what.ToLowerInvariant() + "."); return; }
            to.Add(Doc.Capture(LayerIndex));
            var s = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Doc.Restore(s);
            LayerIndex = Math.Min(s.Selected, Doc.Layers.Count - 1);
            if (Frame >= Doc.FrameCount) Frame = Doc.FrameCount - 1;
            _lastKey = null;
            LayersEdited();
            if (FrameChanged != null) FrameChanged();
        }

        /// <summary>Every frame as it will be exported (colour limit applied when that's switched on). Cached.</summary>
        public List<PixelImage> Preview
        {
            get
            {
                if (Doc == null) return new List<PixelImage>();
                if (_preview == null) _preview = Doc.FinalFrames(Doc.LimitPalette);
                return _preview;
            }
        }

        public void InvalidatePreview() { _preview = null; }

        // ------------------------------------------------------------------ selection

        public void SelectLayer(int i)
        {
            if (Doc == null) return;
            i = Math.Max(-1, Math.Min(Doc.Layers.Count - 1, i));
            if (i == LayerIndex) return;
            LayerIndex = i;
            if (SelectionChanged != null) SelectionChanged();
        }

        public void SelectFrame(int f)
        {
            if (Doc == null) return;
            f = Math.Max(0, Math.Min(Doc.FrameCount - 1, f));
            if (f == Frame) return;
            Frame = f;
            if (FrameChanged != null) FrameChanged();
        }

        public void SetTool(Tool t) { Tool = t; if (ToolChanged != null) ToolChanged(); }

        public void SetPrimary(int c) { Primary = c; if (ColorsChanged != null) ColorsChanged(); }
        public void SetSecondary(int c) { Secondary = c; if (ColorsChanged != null) ColorsChanged(); }
        public void SwapColors() { int t = Primary; Primary = Secondary; Secondary = t; if (ColorsChanged != null) ColorsChanged(); }

        // ------------------------------------------------------------------ layers

        /// <summary>
        /// The layer free-hand drawing goes on: the selected paint layer, or (when a part is selected)
        /// the paint layer just above it, made if needed. Parts themselves are never drawn on.
        /// </summary>
        public PaintLayer EnsurePaintLayer(bool emptySpot = false)
        {
            if (Doc == null) return null;
            var p = CurrentPaint;
            if (p != null) return p;
            int above = LayerIndex + 1;
            int lowest = Doc.Layers.FindIndex(l => l is PaintLayer);
            if (emptySpot && lowest >= 0)
            {
                // colouring in empty space (e.g. inside the pose outline): use the bottom paint layer, under the clothes
                LayerIndex = lowest;
                Say("Drawing on \"" + Doc.Layers[lowest].Name + "\" (the paint layer under the parts). Select another layer to draw somewhere else.");
            }
            else if (above < Doc.Layers.Count && Doc.Layers[above] is PaintLayer)
            {
                LayerIndex = above;
            }
            else
            {
                var l = Doc.NewPaintLayer(Doc.UniqueName("Paint"));
                Doc.Layers.Insert(above, l);
                LayerIndex = above;
                if (LayersChanged != null) LayersChanged();
                Say("Parts aren't drawn on directly, so this went on the paint layer \"" + l.Name + "\". (Layer > Convert to paint layer lets you edit a part's pixels.)");
            }
            if (SelectionChanged != null) SelectionChanged();
            return CurrentPaint;
        }

        public void AddPaintLayer()
        {
            if (Doc == null) return;
            BeginChange();
            var l = Doc.NewPaintLayer(Doc.UniqueName("Paint"));
            int at = LayerIndex + 1;
            Doc.Layers.Insert(at, l);
            LayerIndex = at;
            LayersEdited();
        }

        /// <summary>Puts a part on the sprite. Replaces the selected layer's part when it's the same category,
        /// otherwise the first layer of that category; extra = always add a new layer.</summary>
        public void SetPart(Part p, bool extra)
        {
            if (Doc == null || p == null) return;
            if (p.Sheet == null) { Say("Couldn't read the picture " + p.Path); return; }
            BeginChange();
            PartLayer existing = null;
            if (!extra)
            {
                var cur = CurrentPart;
                existing = cur != null && cur.Category == p.Category ? cur : Doc.Layers.OfType<PartLayer>().FirstOrDefault(l => l.Category == p.Category);
            }
            if (existing != null)
            {
                bool wasLoose = existing.IsLoose(Doc.Layout);
                existing.Part = p; existing.PartName = p.Name;
                // a new picture means new colours: keep hue/sat/brightness, drop exact swaps that no longer apply
                var src = new HashSet<int>(existing.SourcePalette());
                foreach (var k in existing.ColorMap.Keys.ToList()) if (!src.Contains(k)) existing.ColorMap.Remove(k);
                if (!existing.IsLoose(Doc.Layout)) { existing.OffsetX = 0; existing.OffsetY = 0; }
                else if (!wasLoose) { var fresh = Doc.MakePartLayer(p); existing.OffsetX = fresh.OffsetX; existing.OffsetY = fresh.OffsetY; }
                LayerIndex = Doc.Layers.IndexOf(existing);
            }
            else
            {
                var l = Doc.MakePartLayer(p);
                if (extra) l.Name = Doc.UniqueName(p.Category);
                int at = Doc.InsertIndexFor(p.Category, Lib.OrderFor(Doc.Style.Gen, Doc.Kind, Doc.Sheet));
                Doc.Layers.Insert(at, l);
                LayerIndex = at;
            }
            LayersEdited();
            Say(p.Category + ": " + p.Name + (p.FramesFor(Doc.Layout).Loose ? "  (a loose piece - drag it into place with the Move tool or the arrow keys)" : ""));
        }

        public void RemoveCategory(string cat)
        {
            if (Doc == null) return;
            var l = CurrentPart != null && CurrentPart.Category == cat ? CurrentPart : Doc.Layers.OfType<PartLayer>().LastOrDefault(x => x.Category == cat);
            if (l == null) return;
            BeginChange();
            int i = Doc.Layers.IndexOf(l);
            Doc.Layers.RemoveAt(i);
            LayerIndex = Math.Min(i, Doc.Layers.Count - 1);
            LayersEdited();
        }

        public void DeleteLayer()
        {
            if (Current == null) return;
            BeginChange();
            Doc.Layers.RemoveAt(LayerIndex);
            LayerIndex = Math.Min(LayerIndex, Doc.Layers.Count - 1);
            LayersEdited();
        }

        public void DuplicateLayer()
        {
            if (Current == null) return;
            BeginChange();
            var c = Current.Clone();
            c.Name = Doc.UniqueName(Current.Name);
            Doc.Layers.Insert(LayerIndex + 1, c);
            LayerIndex++;
            LayersEdited();
        }

        public void MoveLayer(int dir)
        {
            if (Current == null) return;
            int j = LayerIndex + dir;
            if (j < 0 || j >= Doc.Layers.Count) return;
            BeginChange();
            var l = Doc.Layers[LayerIndex];
            Doc.Layers.RemoveAt(LayerIndex);
            Doc.Layers.Insert(j, l);
            LayerIndex = j;
            LayersEdited();
        }

        /// <summary>Turns a part layer into a paint layer (its pictures, with its colours, become editable pixels).</summary>
        public void ConvertToPaint()
        {
            var pl = CurrentPart;
            if (pl == null) return;
            BeginChange();
            var p = Doc.NewPaintLayer(pl.Name);
            p.Visible = pl.Visible;
            for (int i = 0; i < Doc.FrameCount; i++) p.Frames[i] = Doc.LayerImage(pl, i);
            Doc.Layers[LayerIndex] = p;
            LayersEdited();
            Say("\"" + p.Name + "\" is now a paint layer: its pixels can be drawn over and erased.");
        }

        public void MergeDown()
        {
            if (Current == null || LayerIndex == 0) return;
            var below = Doc.Layers[LayerIndex - 1] as PaintLayer;
            if (below == null) { Say("Merge down needs a paint layer underneath (convert the part below to a paint layer first)."); return; }
            BeginChange();
            for (int i = 0; i < Doc.FrameCount; i++) below.Frames[i].Blit(Doc.LayerImage(Current, i), 0, 0);
            Doc.Layers.RemoveAt(LayerIndex);
            LayerIndex--;
            LayersEdited();
        }

        // ------------------------------------------------------------------ frames

        public void CopyFrame()
        {
            if (Doc == null) return;
            var p = CurrentPaint;
            FrameClipboard = p != null ? p.Frames[Frame].Clone() : Doc.Composite(Frame);
            Say(p != null ? "Copied frame " + (Frame + 1) + " of \"" + p.Name + "\"." : "Copied the whole frame " + (Frame + 1) + " (paste puts it on a paint layer).");
        }

        public void PasteFrame()
        {
            if (Doc == null || FrameClipboard == null) return;
            BeginChange();
            var p = EnsurePaintLayer();
            p.Frames[Frame] = FrameClipboard.Crop(0, 0, Doc.Layout.FrameW, Doc.Layout.FrameH);
            Changed();
        }

        public void ClearFrame()
        {
            var p = CurrentPaint;
            if (p == null) return;
            BeginChange();
            p.Frames[Frame].Clear();
            Changed();
        }

        public void CopyToAllFrames()
        {
            var p = CurrentPaint;
            if (p == null) { Say("Select a paint layer to copy its frame to the others."); return; }
            BeginChange();
            for (int i = 0; i < Doc.FrameCount; i++) if (i != Frame) p.Frames[i] = p.Frames[Frame].Clone();
            Changed();
        }

        /// <summary>Overworld: the Right row becomes a mirror of the Left row (or the other way) on every paint layer.</summary>
        public void MirrorRows(bool leftToRight)
        {
            if (Doc == null || Doc.Kind != SpriteKind.Overworld) return;
            BeginChange();
            int from = leftToRight ? 1 : 2, to = leftToRight ? 2 : 1, cols = Doc.Layout.Cols;
            foreach (var p in Doc.Layers.OfType<PaintLayer>())
                for (int c = 0; c < cols; c++) p.Frames[to * cols + c] = p.Frames[from * cols + c].FlipH();
            Changed();
            Say(leftToRight ? "Right-facing frames are now a mirror of the left-facing ones (paint layers)." : "Left-facing frames are now a mirror of the right-facing ones (paint layers).");
        }

        public void SetFrameCount(int n)
        {
            if (Doc == null || Doc.Kind == SpriteKind.Overworld) return;
            BeginChange();
            Doc.SetFrameCount(n);
            if (Frame >= Doc.FrameCount) Frame = Doc.FrameCount - 1;
            LayersEdited();
            if (FrameChanged != null) FrameChanged();
        }
    }
}
