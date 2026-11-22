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
        /// <summary>The document was saved (or opened from a file).</summary>
        public event Action Saved;
        public event Action<string> Status;

        public Editor(Settings s) { Settings = s; }

        public Layer Current { get { return Doc != null && LayerIndex >= 0 && LayerIndex < Doc.Layers.Count ? Doc.Layers[LayerIndex] : null; } }

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

        public void NotifySaved() { if (Saved != null) Saved(); }

        // ------------------------------------------------------------------ change notifications

        /// <summary>Call before changing the document. Calls with the same key in quick succession share one Undo step.</summary>
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

        /// <summary>The layer drawing goes on (made if the sprite has none).</summary>
        public Layer EnsureLayer()
        {
            if (Doc == null) return null;
            if (Current != null) return Current;
            var l = Doc.NewLayer(Doc.UniqueName("Paint"));
            Doc.Layers.Add(l);
            LayerIndex = Doc.Layers.Count - 1;
            if (LayersChanged != null) LayersChanged();
            if (SelectionChanged != null) SelectionChanged();
            return l;
        }

        public void AddLayer()
        {
            if (Doc == null) return;
            BeginChange();
            var l = Doc.NewLayer(Doc.UniqueName("Paint"));
            int at = LayerIndex + 1;
            Doc.Layers.Insert(at, l);
            LayerIndex = at;
            LayersEdited();
        }

        /// <summary>Adds a picture as a new layer above the selected one.</summary>
        public void AddLayerFromFrames(string name, IList<PixelImage> frames)
        {
            if (Doc == null) return;
            BeginChange();
            var l = Doc.LayerFromFrames(Doc.UniqueName(name), frames);
            int at = LayerIndex + 1;
            Doc.Layers.Insert(at, l);
            LayerIndex = at;
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

        public void MergeDown()
        {
            if (Current == null || LayerIndex == 0) return;
            var below = Doc.Layers[LayerIndex - 1];
            BeginChange();
            for (int i = 0; i < Doc.FrameCount; i++) below.Frames[i].Blit(Current.Frames[i], 0, 0);
            Doc.Layers.RemoveAt(LayerIndex);
            LayerIndex--;
            LayersEdited();
        }

        // ------------------------------------------------------------------ frames

        public void CopyFrame()
        {
            if (Doc == null) return;
            var l = Current;
            FrameClipboard = l != null ? l.Frames[Frame].Clone() : Doc.Composite(Frame);
            Say(l != null ? "Copied frame " + (Frame + 1) + " of \"" + l.Name + "\"." : "Copied frame " + (Frame + 1) + ".");
        }

        public void PasteFrame()
        {
            if (Doc == null || FrameClipboard == null) return;
            BeginChange();
            var l = EnsureLayer();
            l.Frames[Frame] = FrameClipboard.Crop(0, 0, Doc.Layout.FrameW, Doc.Layout.FrameH);
            Changed();
        }

        public void ClearFrame()
        {
            var l = Current;
            if (l == null) return;
            BeginChange();
            l.Frames[Frame].Clear();
            Changed();
        }

        public void CopyToAllFrames()
        {
            var l = Current;
            if (l == null) { Say("Select a layer to copy its frame to the others."); return; }
            BeginChange();
            for (int i = 0; i < Doc.FrameCount; i++) if (i != Frame) l.Frames[i] = l.Frames[Frame].Clone();
            Changed();
        }

        /// <summary>Overworld: the Right row becomes a mirror of the Left row (or the other way) on every layer.</summary>
        public void MirrorRows(bool leftToRight)
        {
            if (Doc == null || Doc.Kind != SpriteKind.Overworld) return;
            BeginChange();
            int from = leftToRight ? 1 : 2, to = leftToRight ? 2 : 1, cols = Doc.Layout.Cols;
            foreach (var l in Doc.Layers)
                for (int c = 0; c < cols; c++) l.Frames[to * cols + c] = l.Frames[from * cols + c].FlipH();
            Changed();
            Say(leftToRight ? "Right-facing frames are now a mirror of the left-facing ones." : "Left-facing frames are now a mirror of the right-facing ones.");
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
