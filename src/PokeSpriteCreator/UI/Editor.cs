using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    public enum Tool { Pencil, Eraser, Fill, Picker, Line, Rectangle, Ellipse, Move, Recolor, Shade, Reference }

    /// <summary>
    /// The editing session shared by all panels: the document, the selected layer and frame,
    /// the drawing colours and tool, and Undo / Redo.
    /// </summary>
    public class Editor
    {
        /// <summary>The open tabs, and the one being edited.</summary>
        public readonly List<Session> Sessions = new List<Session>();
        public Session S;
        /// <summary>Two tabs shown side by side (null when not comparing).</summary>
        public Session CompareLeft, CompareRight;

        /// <summary>The part being edited in the active tab: the sprite itself, or a front sprite's walking sprites (Doc.Root is the file).</summary>
        public SpriteDocument Doc { get { return S == null ? null : S.Doc; } set { S.Doc = value; } }
        public Settings Settings;
        public int LayerIndex { get { return S == null ? -1 : S.LayerIndex; } set { S.LayerIndex = value; } }
        public int Frame { get { return S == null ? 0 : S.Frame; } set { S.Frame = value; } }
        public int Primary = unchecked((int)0xFF000000), Secondary = unchecked((int)0xFFFFFFFF);
        public Tool Tool = Tool.Pencil;
        public bool Mirror, SampleMerged = true, OnionSkin;
        public PixelImage FrameClipboard;

        // the active tab's state
        List<DocState> _undo { get { return S.Undo; } set { S.Undo = value; } }
        List<DocState> _redo { get { return S.Redo; } set { S.Redo = value; } }
        Dictionary<SpriteDocument, List<DocState>[]> _history { get { return S.History; } }
        // the frame as it was when it was picked, for "copy my change"
        PixelImage _baseline { get { return S == null ? null : S.Baseline; } set { if (S != null) S.Baseline = value; } }
        Layer _baseLayer { get { return S == null ? null : S.BaseLayer; } set { if (S != null) S.BaseLayer = value; } }
        int _baseFrame { get { return S == null ? -1 : S.BaseFrame; } set { if (S != null) S.BaseFrame = value; } }
        string _lastKey { get { return S == null ? null : S.LastKey; } set { if (S != null) S.LastKey = value; } }
        DateTime _lastTime { get { return S == null ? DateTime.MinValue : S.LastTime; } set { if (S != null) S.LastTime = value; } }
        List<PixelImage> _preview { get { return S == null ? null : S.PreviewCache; } set { if (S != null) S.PreviewCache = value; } }

        /// <summary>Pixels / colours changed (redraw).</summary>
        public event Action DocChanged;
        /// <summary>Layers added, removed, reordered or renamed, or a new document.</summary>
        public event Action LayersChanged;
        public event Action SelectionChanged;
        public event Action FrameChanged;
        public event Action ColorsChanged;
        public event Action ToolChanged;
        /// <summary>The document was saved.</summary>
        public event Action Saved;
        /// <summary>Switched between the sprite and its walking sprites (or to another tab).</summary>
        public event Action PartSwitched;
        /// <summary>Tabs opened, closed, switched or renamed.</summary>
        public event Action TabsChanged;
        /// <summary>Started or stopped comparing two tabs.</summary>
        public event Action CompareChanged;
        public event Action<string> Status;

        public Editor(Settings s) { Settings = s; }

        public Layer Current { get { return Doc != null && LayerIndex >= 0 && LayerIndex < Doc.Layers.Count ? Doc.Layers[LayerIndex] : null; } }

        public void Say(string msg) { if (Status != null) Status(msg); }

        public SpriteDocument Root { get { return Doc == null ? null : Doc.Root; } }

        // ------------------------------------------------------------------ tabs

        /// <summary>Opens a sprite in a new tab and makes it the active one.</summary>
        public Session OpenTab(SpriteDocument d)
        {
            var s = new Session(d);
            int at = S == null ? Sessions.Count : Sessions.IndexOf(S) + 1;
            Sessions.Insert(Math.Max(0, Math.Min(Sessions.Count, at)), s);
            Activate(s);
            return s;
        }

        /// <summary>Makes a tab the one being edited (all panels switch to it).</summary>
        public void Activate(Session s)
        {
            if (s == null || !Sessions.Contains(s)) return;
            bool same = s == S;
            S = s;
            if (Frame >= Doc.FrameCount) Frame = 0;
            if (LayerIndex >= Doc.Layers.Count) LayerIndex = Doc.Layers.Count - 1;
            if (!same || _baseline == null) CaptureBaseline();
            if (LayersChanged != null) LayersChanged();
            if (FrameChanged != null) FrameChanged();
            if (SelectionChanged != null) SelectionChanged();
            if (DocChanged != null) DocChanged();
            if (PartSwitched != null) PartSwitched();
            if (TabsChanged != null) TabsChanged();
        }

        /// <summary>Closes a tab (asking to save is up to the caller). Another tab becomes active.</summary>
        public void CloseTab(Session s)
        {
            int i = Sessions.IndexOf(s);
            if (i < 0) return;
            if (s == CompareLeft || s == CompareRight) StopCompare();
            Sessions.RemoveAt(i);
            if (s == S)
            {
                S = null;
                if (Sessions.Count > 0) { Activate(Sessions[Math.Min(i, Sessions.Count - 1)]); return; }
            }
            if (TabsChanged != null) TabsChanged();
        }

        public void NotifyTabs() { if (TabsChanged != null) TabsChanged(); }

        public bool Comparing { get { return CompareLeft != null && CompareRight != null; } }

        /// <summary>Shows two tabs side by side. Both can be edited: clicking one makes it the active tab.</summary>
        public void StartCompare(Session left, Session right)
        {
            if (left == null || right == null || left == right) return;
            CompareLeft = left; CompareRight = right;
            if (S != left && S != right) Activate(left);
            if (CompareChanged != null) CompareChanged();
            if (TabsChanged != null) TabsChanged();
        }

        public void StopCompare()
        {
            if (!Comparing) return;
            CompareLeft = CompareRight = null;
            if (CompareChanged != null) CompareChanged();
            if (TabsChanged != null) TabsChanged();
        }

        /// <summary>
        /// A tab was picked in the tab bar. While comparing, a tab that isn't shown replaces the side being edited.
        /// </summary>
        public void PickTab(Session s)
        {
            if (Comparing && s != CompareLeft && s != CompareRight)
            {
                if (S == CompareRight) CompareRight = s; else CompareLeft = s;
                if (CompareChanged != null) CompareChanged();
            }
            Activate(s);
        }

        public void SetDocument(SpriteDocument d)
        {
            if (S == null) { OpenTab(d); return; }
            Doc = d;
            _history.Clear();
            _undo = new List<DocState>(); _redo = new List<DocState>(); _lastKey = null;
            _history[d] = new[] { _undo, _redo };
            Frame = 0;
            LayerIndex = d.Layers.Count - 1;
            _preview = null;
            CaptureBaseline();
            if (LayersChanged != null) LayersChanged();
            if (FrameChanged != null) FrameChanged();
            if (SelectionChanged != null) SelectionChanged();
            if (DocChanged != null) DocChanged();
            if (PartSwitched != null) PartSwitched();
            if (TabsChanged != null) TabsChanged();
        }

        /// <summary>Edit another part of the same file (the sprite or its walking sprites); each keeps its own Undo.</summary>
        public void EditPart(SpriteDocument d, int frame = 0)
        {
            if (d == null) return;
            if (d == Doc) { SelectFrame(frame); return; }
            Doc = d;
            List<DocState>[] h;
            if (!_history.TryGetValue(d, out h)) _history[d] = h = new[] { new List<DocState>(), new List<DocState>() };
            _undo = h[0]; _redo = h[1]; _lastKey = null;
            S.NeedsFit = true;                                   // the sprite and its walking sprites have different sizes
            Frame = Math.Max(0, Math.Min(d.FrameCount - 1, frame));
            LayerIndex = d.Layers.Count - 1;
            _preview = null;
            CaptureBaseline();
            if (LayersChanged != null) LayersChanged();
            if (FrameChanged != null) FrameChanged();
            if (SelectionChanged != null) SelectionChanged();
            if (DocChanged != null) DocChanged();
            if (PartSwitched != null) PartSwitched();
            if (TabsChanged != null) TabsChanged();
        }

        public void NotifySaved() { if (Saved != null) Saved(); if (TabsChanged != null) TabsChanged(); }

        /// <summary>Takes the walking sprites away from this front sprite completely (Ctrl+Z puts them back).</summary>
        public void RemoveWalkingSprites()
        {
            var r = Root;
            if (r == null || r.Kind != SpriteKind.Front || r.Overworld == null) return;
            var set = r.Overworld;
            if (Doc != r) EditPart(r);
            BeginChange();
            var step = _undo[_undo.Count - 1];
            step.WalkStep = true; step.Walk = set;
            r.Overworld = null;
            Changed();
            PartsChanged();
            Say("Walking sprites removed: this sprite has none now. Ctrl+Z brings them back.");
        }

        /// <summary>Empties the walking sprites to a blank sheet of the same size (Ctrl+Z brings them back).</summary>
        public void ClearWalkingSprites()
        {
            var r = Root;
            if (r == null) return;
            var set = r.Kind == SpriteKind.Overworld ? r : r.Overworld;
            if (set == null) return;
            if (Doc != set) EditPart(set, 0);
            BeginChange();
            set.Layers.Clear();
            set.Layers.Add(set.NewLayer("Walking sprites"));
            LayerIndex = 0;
            LayersEdited();
            CaptureBaseline();
            Say("Walking sprites cleared to a blank sheet. Ctrl+Z brings them back.");
        }

        /// <summary>The walking sprites were added or replaced (tells the panels).</summary>
        public void PartsChanged() { if (PartSwitched != null) PartSwitched(); }

        // ------------------------------------------------------------------ copy a change to other frames

        /// <summary>Remembers the selected frame as it is now (what "copy my change" compares against).</summary>
        public void CaptureBaseline()
        {
            var l = Current;
            if (Doc == null || l == null || Frame >= l.Frames.Count) { _baseline = null; return; }
            _baseline = l.Frames[Frame].Clone(); _baseLayer = l; _baseFrame = Frame;
        }

        /// <summary>What changed on the selected frame since it was picked (null if it can't be worked out).</summary>
        public ChangeCopier PendingChange
        {
            get
            {
                var l = Current;
                if (_baseline == null || l == null || l != _baseLayer || Frame != _baseFrame || Frame >= l.Frames.Count) return null;
                return new ChangeCopier(_baseline, l.Frames[Frame]);
            }
        }

        /// <summary>
        /// Copies the change made on the selected frame to the other walking frames facing the same way
        /// (allDirections = false) or to every other frame. Returns a message for the user.
        /// </summary>
        public string CopyChange(bool allDirections)
        {
            if (Doc == null || Doc.Kind != SpriteKind.Overworld) return "Copying a change works on walking sprites.";
            var ch = PendingChange;
            if (ch == null || ch.IsEmpty) return "Nothing to copy: change the selected frame first (the change is what you did since you picked the frame).";
            var l = Current;
            int cols = Doc.Layout.Cols, row = Frame / cols;
            BeginChange();
            int n = 0;
            for (int i = 0; i < Doc.FrameCount; i++)
            {
                if (i == Frame) continue;
                int r = i / cols;
                if (!allDirections && r != row) continue;
                // left and right face opposite ways, so the change is mirrored between them
                bool mirror = (row == 1 && r == 2) || (row == 2 && r == 1);
                // something removed only goes to the poses facing the same way: other directions show a different side
                Point off;
                l.Frames[i] = ch.ApplyTo(l.Frames[i], mirror, r == row, out off);
                n++;
            }
            Changed();
            CaptureBaseline();
            string erasedNote = allDirections && ch.Erased > 0 ? " Erased pixels went only to the frames facing the same way." : "";
            return "Copied " + ch.Describe() + " to " + n + " frame" + (n == 1 ? "" : "s") + (allDirections ? (ch.Drawn > 0 ? " (lined up with each frame, mirrored between left and right)" : "") : " facing the same way") + "." + erasedNote + " Undo (Ctrl+Z) if it doesn't fit.";
        }

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
            var s = from[from.Count - 1];
            var now = Doc.Capture(LayerIndex);
            if (s.WalkStep) { now.WalkStep = true; now.Walk = Doc.Overworld; }
            to.Add(now);
            from.RemoveAt(from.Count - 1);
            Doc.Restore(s);
            if (s.WalkStep)
            {
                Doc.Overworld = s.Walk;
                if (s.Walk != null) s.Walk.Parent = Doc;
                Doc.Dirty = true;
                PartsChanged();
            }
            LayerIndex = Math.Min(s.Selected, Doc.Layers.Count - 1);
            if (Frame >= Doc.FrameCount) Frame = Doc.FrameCount - 1;
            _lastKey = null;
            CaptureBaseline();
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
            CaptureBaseline();
            if (SelectionChanged != null) SelectionChanged();
        }

        public void SelectFrame(int f)
        {
            if (Doc == null) return;
            f = Math.Max(0, Math.Min(Doc.FrameCount - 1, f));
            if (f == Frame) return;
            Frame = f;
            CaptureBaseline();
            if (FrameChanged != null) FrameChanged();
        }

        public void SetTool(Tool t) { Tool = t; if (ToolChanged != null) ToolChanged(); }

        // ------------------------------------------------------------------ shading helper

        /// <summary>The box picked with the shading helper (on the current frame), and its suggestion.</summary>
        public Rectangle? ShadeArea;
        public ShadeSuggestion Shade;
        public readonly ShadeOptions ShadeOptions = new ShadeOptions();
        public bool ShadePreview = true;
        public event Action ShadeChanged;

        public void SetShadeArea(Rectangle? area)
        {
            if (area != ShadeArea) ShadeOptions.PartColor = 0;   // a new box: the biggest part again
            ShadeArea = area;
            RecomputeShade();
        }

        public void RecomputeShade()
        {
            Shade = null;
            if (ShadeArea.HasValue && Doc != null && Frame < Doc.FrameCount)
            {
                var layer = Current;
                if (layer == null) { ShadeArea = null; Say("Select a layer to shade."); }
                else Shade = ShadingHelper.Suggest(layer.Frames[Frame], Doc.Composite(Frame), ShadeArea.Value, Doc.UsedColors(), ShadeOptions);
            }
            if (ShadeChanged != null) ShadeChanged();
        }

        // ------------------------------------------------------------------ from a reference picture

        public RefPicture RefPic;
        public Rectangle? RefSource;        // the box on the reference picture
        public Rectangle? RefTarget;        // the box on the sprite
        public string RefDescription = "";
        public List<RefCluster> RefClusters;
        public readonly RefOptions RefOptions = new RefOptions();
        public RefResult RefResult;
        public event Action RefChanged;

        void FireRef() { if (RefChanged != null) RefChanged(); }

        public void SetRefPicture(RefPicture p)
        {
            RefPic = p; RefSource = null; RefClusters = null; RefOptions.Variation = 0;
            RecomputeRef();
        }

        public void SetRefSource(Rectangle? box)
        {
            RefSource = box;
            RefClusters = RefPic != null && box.HasValue ? RefConvert.Clusters(RefPic.Image, box.Value, RefDescription) : null;
            RefOptions.Variation = 0;
            RecomputeRef();
        }

        public void SetRefTarget(Rectangle? box) { RefTarget = box; RefOptions.Variation = 0; RecomputeRef(); }

        public void SetRefDescription(string d)
        {
            bool hairBefore = RefConvert.IsHair(RefDescription);
            RefDescription = d ?? "";
            // "hair" changes which colours in the box are kept, so look again
            if (RefPic != null && RefSource.HasValue && hairBefore != RefConvert.IsHair(RefDescription))
                RefClusters = RefConvert.Clusters(RefPic.Image, RefSource.Value, RefDescription);
            RecomputeRef();
        }

        public void ToggleRefCluster(int i)
        {
            if (RefClusters == null || i < 0 || i >= RefClusters.Count) return;
            RefClusters[i].Keep = !RefClusters[i].Keep;
            RecomputeRef();
        }

        public void RecomputeRef()
        {
            RefResult = null;
            if (RefPic != null && RefSource.HasValue && RefTarget.HasValue && RefClusters != null && Doc != null && Frame < Doc.FrameCount)
                RefResult = RefConvert.Convert(RefPic.Image, RefSource.Value, RefClusters, Doc.Composite(Frame), RefTarget.Value, Doc.UsedColors(), RefDescription, RefOptions);
            FireRef();
        }

        public void RedoRef()
        {
            if (RefResult == null) return;
            RefOptions.Variation++;
            RecomputeRef();
        }

        public void CancelRef()
        {
            RefTarget = null; RefResult = null; RefOptions.Variation = 0;
            FireRef();
            Say("Cancelled. Nothing was added.");
        }

        /// <summary>Adds the result as a new layer on top (one undo step).</summary>
        public void ConfirmRef()
        {
            if (RefResult == null || RefResult.Empty || Doc == null) return;
            var frames = Enumerable.Range(0, Doc.FrameCount).Select(i => i == Frame ? RefResult.Overlay.Clone() : new PixelImage(Doc.Layout.FrameW, Doc.Layout.FrameH)).ToList();
            string name = string.IsNullOrWhiteSpace(RefDescription) ? "From picture" : char.ToUpper(RefDescription.Trim()[0]) + RefDescription.Trim().Substring(1);
            LayerIndex = Doc.Layers.Count - 1;
            AddLayerFromFrames(name, frames);
            RefTarget = null; RefResult = null; RefOptions.Variation = 0;
            FireRef();
            Say("Added \"" + Current.Name + "\" as a new layer on top. Ctrl+Z to undo.");
        }

        public void ApplyShade()
        {
            if (Shade == null || !Shade.HasChanges || Current == null) return;
            BeginChange();
            Current.Frames[Frame] = Shade.After.Clone();
            int n = Shade.Changed;
            Changed();
            RecomputeShade();
            Say("Shading applied (" + n + " pixels). Ctrl+Z to undo.");
        }

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
            if (Doc.Kind == SpriteKind.Overworld)
            {
                // walking sprites: copy what changed on this frame to the other poses facing the same way
                // (copying the whole frame to every direction would put a front view on the side and back frames)
                Say(CopyChange(false));
                return;
            }
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
