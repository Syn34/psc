using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>The big zoomed view of the current frame, where the drawing tools work.</summary>
    public class SpriteCanvas : Control
    {
        readonly Editor E;
        /// <summary>The tab this canvas shows; null = whichever tab is active.</summary>
        public Session Bound;
        public const int ViewMargin = 48;
        Point? _hover;
        bool _down, _panning, _space;
        MouseButtons _btn;
        Point _start, _last, _panFrom, _panStart;
        PixelImage _shapeBase;
        List<PixelImage> _moveBase;
        Layer _target;

        public event Action<Point?, int> HoverChanged;
        public event Action ZoomChanged;
        /// <summary>Zoom, scroll or size changed (the scroll bars follow).</summary>
        public event Action ViewChanged;
        /// <summary>This canvas was clicked (it shows the tab that's now being edited).</summary>
        public event Action Activated;

        public SpriteCanvas(Editor e)
        {
            E = e;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            E.DocChanged += Invalidate;
            E.FrameChanged += Invalidate;
            E.SelectionChanged += Invalidate;
            E.ToolChanged += UpdateCursor;
            E.PartSwitched += () =>
            {
                if (S != null && S.NeedsFit && Width > 40 && Height > 40) FitToWindow();
                else if (S != null) ClampScroll();
                Invalidate();
                if (ViewChanged != null) ViewChanged();
            };
        }

        /// <summary>The tab shown here.</summary>
        public Session S { get { return Bound ?? E.S; } }
        SpriteDocument D { get { var s = S; return s == null ? null : s.Doc; } }
        public bool IsActive { get { return S != null && S == E.S; } }

        public int Zoom { get { return S == null ? 8 : S.Zoom; } set { if (S != null) S.Zoom = value; } }
        public int ScrollX { get { return S == null ? 0 : S.ScrollX; } }
        public int ScrollY { get { return S == null ? 0 : S.ScrollY; } }

        /// <summary>The scrollable size: on each side where the zoomed frame is bigger than the view,
        /// the frame plus a margin all round; where it fits, just the view (nothing to scroll).</summary>
        public Size ContentSize
        {
            get
            {
                if (D == null) return Size.Empty;
                int w = D.Layout.FrameW * Zoom, h = D.Layout.FrameH * Zoom;
                return new Size(w <= Width ? Width : w + 2 * ViewMargin, h <= Height ? Height : h + 2 * ViewMargin);
            }
        }

        public void SetScroll(int x, int y)
        {
            if (S == null) return;
            var c = ContentSize;
            S.ScrollX = Math.Max(0, Math.Min(Math.Max(0, c.Width - Width), x));
            S.ScrollY = Math.Max(0, Math.Min(Math.Max(0, c.Height - Height), y));
            Invalidate();
            if (ViewChanged != null) ViewChanged();
        }

        void ClampScroll() { if (S != null) SetScroll(S.ScrollX, S.ScrollY); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (S != null && S.NeedsFit && Width > 40 && Height > 40) FitToWindow();
            else ClampScroll();
        }

        public void FitToWindow()
        {
            if (D == null || Width < 40 || Height < 40) return;
            const int pad = 12;   // a small border; the scroll margin is only needed once zoomed past the window
            double k = Math.Min((Width - 2 * pad) / (double)D.Layout.FrameW, (Height - 2 * pad) / (double)D.Layout.FrameH);
            Zoom = Math.Max(1, Math.Min(48, (int)Math.Floor(k)));
            S.NeedsFit = false;
            SetScroll(0, 0);
            if (ZoomChanged != null) ZoomChanged();
        }

        public void ZoomBy(int dir, Point? around = null)
        {
            if (D == null) return;
            int[] steps = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24, 32, 40, 48 };
            int idx = Array.IndexOf(steps, Zoom);
            if (idx < 0) idx = steps.Count(s => s < Zoom);
            SetZoom(steps[Math.Max(0, Math.Min(steps.Length - 1, idx + dir))], around);
        }

        /// <summary>Zooms keeping the pixel under 'around' (or the middle of the view) where it is.</summary>
        public void SetZoom(int nz, Point? around = null)
        {
            if (D == null || S == null) return;
            nz = Math.Max(1, Math.Min(48, nz));
            if (nz == Zoom) return;
            var at = around ?? new Point(Width / 2, Height / 2);
            var o = Origin;
            double px = (at.X - o.X) / (double)Zoom, py = (at.Y - o.Y) / (double)Zoom;
            Zoom = nz;
            S.NeedsFit = false;
            // the scroll that puts that pixel back under the mouse (when the frame is bigger than the view)
            SetScroll((int)Math.Round(ViewMargin + px * Zoom - at.X), (int)Math.Round(ViewMargin + py * Zoom - at.Y));
            if (ZoomChanged != null) ZoomChanged();
        }

        Point Origin
        {
            get
            {
                if (D == null) return Point.Empty;
                var c = ContentSize;
                int ox = c.Width <= Width ? (Width - D.Layout.FrameW * Zoom) / 2 : ViewMargin - ScrollX;
                int oy = c.Height <= Height ? (Height - D.Layout.FrameH * Zoom) / 2 : ViewMargin - ScrollY;
                return new Point(ox, oy);
            }
        }

        Rectangle ImageRect
        {
            get { var o = Origin; return new Rectangle(o.X, o.Y, D.Layout.FrameW * Zoom, D.Layout.FrameH * Zoom); }
        }

        Point ToPixel(Point m)
        {
            var o = Origin;
            return new Point((int)Math.Floor((m.X - o.X) / (double)Zoom), (int)Math.Floor((m.Y - o.Y) / (double)Zoom));
        }

        void UpdateCursor()
        {
            switch (E.Tool)
            {
                case Tool.Move: Cursor = Cursors.SizeAll; break;
                case Tool.Picker: Cursor = Cursors.Hand; break;
                default: Cursor = Cursors.Cross; break;
            }
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.CanvasBack);
            if (S != null && S.NeedsFit && Width > 40 && Height > 40) FitToWindow();
            if (D == null)
            {
                TextRenderer.DrawText(g, "File > New sprite (Ctrl+N) to start.", Theme.Heading, ClientRectangle, Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            var r = ImageRect;
            using (var sh = new SolidBrush(Color.FromArgb(Theme.Dark ? 90 : 40, 0, 0, 0))) g.FillRectangle(sh, r.X + 4, r.Y + 4, r.Width, r.Height);
            Pix.Checker(g, r, Math.Max(4, Math.Min(16, Zoom)));
            if (E.OnionSkin && D.FrameCount > 1)
            {
                int prev = S.Frame > 0 ? S.Frame - 1 : D.FrameCount - 1;
                Pix.Draw(g, D.Composite(prev), r, 0.3f);
            }
            var frames = S.Preview;
            if (S.Frame < frames.Count) Pix.Draw(g, frames[S.Frame], r);

            if (Zoom >= 6 && E.Settings.ShowGrid)
            {
                using (var p = new Pen(Color.FromArgb(Theme.Dark ? 40 : 34, Theme.Dark ? 255 : 0, Theme.Dark ? 255 : 0, Theme.Dark ? 255 : 0)))
                using (var p8 = new Pen(Color.FromArgb(Theme.Dark ? 80 : 70, Theme.Dark ? 255 : 0, Theme.Dark ? 255 : 0, Theme.Dark ? 255 : 0)))
                {
                    for (int x = 1; x < D.Layout.FrameW; x++) g.DrawLine(x % 8 == 0 ? p8 : p, r.X + x * Zoom, r.Y, r.X + x * Zoom, r.Bottom - 1);
                    for (int y = 1; y < D.Layout.FrameH; y++) g.DrawLine(y % 8 == 0 ? p8 : p, r.X, r.Y + y * Zoom, r.Right - 1, r.Y + y * Zoom);
                }
            }
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);

            if (E.Mirror)
            {
                int mx = r.X + D.Layout.FrameW * Zoom / 2;
                using (var p = new Pen(Color.FromArgb(160, Theme.Accent)) { DashStyle = DashStyle.Dot }) g.DrawLine(p, mx, r.Y - 6, mx, r.Bottom + 5);
            }
            if (_hover.HasValue && !_panning)
            {
                var h = _hover.Value;
                if (h.X >= 0 && h.Y >= 0 && h.X < D.Layout.FrameW && h.Y < D.Layout.FrameH)
                {
                    using (var p = new Pen(Color.FromArgb(200, Theme.Dark ? Color.White : Color.Black)))
                        g.DrawRectangle(p, r.X + h.X * Zoom, r.Y + h.Y * Zoom, Zoom - 1, Zoom - 1);
                    if (E.Mirror)
                    {
                        int mx = D.Layout.FrameW - 1 - h.X;
                        using (var p = new Pen(Color.FromArgb(110, Theme.Dark ? Color.White : Color.Black)))
                            g.DrawRectangle(p, r.X + mx * Zoom, r.Y + h.Y * Zoom, Zoom - 1, Zoom - 1);
                    }
                }
            }
            if (D.FrameCount > 1)
            {
                string label = FrameLabel(D, S.Frame);
                TextRenderer.DrawText(g, label, Theme.Small, new Point(r.X, r.Y - 18), Theme.SubText);
            }
        }

        public static string FrameLabel(SpriteDocument d, int i)
        {
            if (d.Kind == SpriteKind.Overworld)
                return OverworldSheets.Directions[Math.Min(3, i / d.Layout.Cols)] + " " + (i % d.Layout.Cols + 1) + "  (frame " + (i + 1) + " of " + d.FrameCount + ")";
            return "Frame " + (i + 1) + " of " + d.FrameCount;
        }

        // ------------------------------------------------------------------ mouse

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (D == null) return;
            if (!IsActive)
            {
                // the first click on the other side of a comparison just picks it for editing (no stray paint)
                E.Activate(S); if (Activated != null) Activated();
                return;
            }
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _space))
            {
                _panning = true; _panFrom = e.Location; _panStart = new Point(ScrollX, ScrollY); Cursor = Cursors.SizeAll; return;
            }
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            var p = ToPixel(e.Location);
            _btn = e.Button; _start = _last = p; _down = true;
            Capture = true;
            int color = e.Button == MouseButtons.Left ? E.Primary : E.Secondary;
            var tool = E.Tool;
            if ((ModifierKeys & Keys.Alt) != 0 && (tool == Tool.Pencil || tool == Tool.Fill || tool == Tool.Line || tool == Tool.Rectangle || tool == Tool.Ellipse)) tool = Tool.Picker;

            switch (tool)
            {
                case Tool.Picker: Pick(p, e.Button); _down = false; break;
                case Tool.Pencil:
                case Tool.Eraser:
                    if (tool == Tool.Eraser && E.Current == null) { E.Say("Select a layer to erase on."); _down = false; break; }
                    E.BeginChange();
                    _target = E.EnsureLayer();
                    Plot(_target.Frames[S.Frame], p.X, p.Y, tool == Tool.Eraser ? 0 : color);
                    E.Changed();
                    break;
                case Tool.Fill:
                    E.BeginChange();
                    _target = E.EnsureLayer();
                    Fill(p, color);
                    E.Changed();
                    _down = false;
                    break;
                case Tool.Line:
                case Tool.Rectangle:
                case Tool.Ellipse:
                    E.BeginChange();
                    _target = E.EnsureLayer();
                    _shapeBase = _target.Frames[S.Frame].Clone();
                    DrawShape(p, color);
                    break;
                case Tool.Move:
                    if (E.Current == null) { E.Say("Select a layer to move."); _down = false; break; }
                    E.BeginChange();
                    _moveBase = E.Current.Frames.Select(f => f.Clone()).ToList();
                    break;
                case Tool.Recolor:
                    Recolor(p, color);
                    _down = false;
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (D == null) return;
            if (_panning)
            {
                SetScroll(_panStart.X - (e.X - _panFrom.X), _panStart.Y - (e.Y - _panFrom.Y)); return;
            }
            var p = ToPixel(e.Location);
            if (!_hover.HasValue || _hover.Value != p)
            {
                _hover = p;
                if (HoverChanged != null)
                {
                    bool inside = p.X >= 0 && p.Y >= 0 && p.X < D.Layout.FrameW && p.Y < D.Layout.FrameH;
                    var frames = S.Preview;
                    HoverChanged(inside ? (Point?)p : null, inside && S.Frame < frames.Count ? frames[S.Frame].Get(p.X, p.Y) : 0);
                }
                Invalidate();
            }
            if (!_down) return;
            int color = _btn == MouseButtons.Left ? E.Primary : E.Secondary;
            switch (E.Tool)
            {
                case Tool.Pencil:
                case Tool.Eraser:
                    if (_target == null) break;
                    if (p != _last)
                    {
                        var f = _target.Frames[S.Frame];
                        int c = E.Tool == Tool.Eraser ? 0 : color;
                        foreach (var q in LinePoints(_last, p)) Plot(f, q.X, q.Y, c);
                        _last = p;
                        E.Changed();
                    }
                    break;
                case Tool.Line:
                case Tool.Rectangle:
                case Tool.Ellipse:
                    if (_target != null && p != _last) { _last = p; DrawShape(p, color); }
                    break;
                case Tool.Move:
                    if (p == _last) break;
                    _last = p;
                    int dx = p.X - _start.X, dy = p.Y - _start.Y;
                    if (E.Current != null && _moveBase != null)
                    {
                        bool thisOnly = (ModifierKeys & Keys.Alt) != 0;
                        for (int i = 0; i < _moveBase.Count; i++)
                            E.Current.Frames[i] = (thisOnly && i != S.Frame) ? _moveBase[i].Clone() : _moveBase[i].Shift(dx, dy);
                    }
                    E.Changed();
                    break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_panning) { _panning = false; UpdateCursor(); return; }
            if (_down)
            {
                _down = false;
                _shapeBase = null; _moveBase = null; _target = null;
                E.Changed();
            }
            Capture = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = null;
            if (HoverChanged != null) HoverChanged(null, 0);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (D == null) return;
            var c = ContentSize;
            bool canV = c.Height > Height, canH = c.Width > Width;
            if ((ModifierKeys & Keys.Control) != 0 || ((ModifierKeys & Keys.Shift) == 0 && !canV && !canH))
                ZoomBy(e.Delta > 0 ? 1 : -1, e.Location);                       // Ctrl+wheel (or nothing to scroll): zoom
            else if ((ModifierKeys & Keys.Shift) != 0 || !canV) SetScroll(ScrollX - e.Delta / 2, ScrollY);   // Shift+wheel: sideways
            else SetScroll(ScrollX, ScrollY - e.Delta / 2);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode) { case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true; }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { _space = true; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) _space = false;
            base.OnKeyUp(e);
        }

        /// <summary>Arrow keys: nudge the selected layer one pixel.</summary>
        public void Nudge(int dx, int dy)
        {
            if (D == null || E.Current == null) return;
            E.BeginChange("nudge");
            for (int i = 0; i < E.Current.Frames.Count; i++) E.Current.Frames[i] = E.Current.Frames[i].Shift(dx, dy);
            E.Changed();
        }

        // ------------------------------------------------------------------ tools

        void Plot(PixelImage f, int x, int y, int c)
        {
            f.Set(x, y, c);
            if (E.Mirror) f.Set(f.W - 1 - x, y, c);
        }

        void Pick(Point p, MouseButtons b)
        {
            // the real colour, not the colour-limited view (picking a merged colour would paint it in for real)
            if (S.Frame >= D.FrameCount) return;
            var img = D.Composite(S.Frame);
            if (!img.Inside(p.X, p.Y)) return;
            int c = img.Get(p.X, p.Y);
            if (b == MouseButtons.Left) E.SetPrimary(c); else E.SetSecondary(c);
            int shown;
            string merged = D.LimitPalette && D.LastMerged.TryGetValue(c, out shown) ? " (over the colour limit, so it's shown as " + PixelImage.Hex(shown) + ")" : "";
            E.Say("Picked " + PixelImage.Hex(c) + (c == 0 ? " (drawing with transparent erases)" : merged));
        }

        void Fill(Point p, int color)
        {
            var f = _target.Frames[S.Frame];
            if (!f.Inside(p.X, p.Y)) return;
            // the area is decided by what you see (all visible layers) or by the paint layer alone
            var reference = E.SampleMerged ? D.Composite(S.Frame) : f;
            int seed = reference.Get(p.X, p.Y);
            var seen = new bool[f.W * f.H];
            var stack = new Stack<Point>();
            stack.Push(p);
            int n = 0;
            while (stack.Count > 0)
            {
                var q = stack.Pop();
                if (!f.Inside(q.X, q.Y)) continue;
                int i = q.Y * f.W + q.X;
                if (seen[i] || reference.Px[i] != seed) continue;
                seen[i] = true;
                Plot(f, q.X, q.Y, color);
                n++;
                stack.Push(new Point(q.X + 1, q.Y)); stack.Push(new Point(q.X - 1, q.Y));
                stack.Push(new Point(q.X, q.Y + 1)); stack.Push(new Point(q.X, q.Y - 1));
            }
            if (n == f.W * f.H && seed == 0 && E.SampleMerged) E.Say("Filled the whole frame - the outline has a gap, or there's nothing under the fill yet.");
        }

        void DrawShape(Point to, int color)
        {
            var f = _shapeBase.Clone();
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            IEnumerable<Point> pts;
            if (E.Tool == Tool.Line)
            {
                if (shift) to = Snap45(_start, to);
                pts = LinePoints(_start, to);
            }
            else if (E.Tool == Tool.Rectangle) pts = RectPoints(_start, to, shift);
            else pts = EllipsePoints(_start, to, shift);
            foreach (var q in pts) Plot(f, q.X, q.Y, color);
            _target.Frames[S.Frame] = f;
            E.Changed();
        }

        void Recolor(Point p, int color)
        {
            var src = D.Composite(S.Frame);
            if (!src.Inside(p.X, p.Y)) return;
            int from = src.Get(p.X, p.Y);
            if (from == 0) { E.Say("Click a coloured pixel to change that colour."); return; }
            if (color == 0) { E.Say("Pick a colour first (the current colour is transparent)."); return; }
            bool everywhere = (ModifierKeys & Keys.Shift) != 0;
            if (everywhere)
            {
                E.BeginChange();
                D.ReplaceColorEverywhere(from, color);
                E.Say("Changed " + PixelImage.Hex(from) + " to " + PixelImage.Hex(color) + " on every layer.");
            }
            else if (E.Current != null && D.Kind == SpriteKind.Overworld)
            {
                // walking sprites: just this frame, then "copy my change" sends it where you want
                var f = E.Current.Frames[S.Frame];
                if (!f.Px.Contains(from)) { E.Say("That colour isn't on the selected layer. Hold Shift to change it everywhere."); return; }
                E.BeginChange();
                for (int i = 0; i < f.Px.Length; i++) if (f.Px[i] == from) f.Px[i] = color;
                E.Say(PixelImage.Hex(from) + " is now " + PixelImage.Hex(color) + " on this frame. Copy the change to the other frames on the right (Shift+click changes it everywhere).");
            }
            else if (E.Current != null)
            {
                if (!E.Current.Frames.Any(f => f.Px.Contains(from))) { E.Say("That colour isn't on the selected layer. Hold Shift to change it on every layer."); return; }
                E.BeginChange();
                E.Say(PixelImage.Hex(from) + " is now " + PixelImage.Hex(color) + " on \"" + E.Current.Name + "\".  (Shift+click changes it on every layer)");
                foreach (var f in E.Current.Frames)
                    for (int i = 0; i < f.Px.Length; i++) if (f.Px[i] == from) f.Px[i] = color;
            }
            else return;
            E.Changed();
        }

        // ------------------------------------------------------------------ shapes

        public static IEnumerable<Point> LinePoints(Point a, Point b)
        {
            int x0 = a.X, y0 = a.Y, x1 = b.X, y1 = b.Y;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                yield return new Point(x0, y0);
                if (x0 == x1 && y0 == y1) yield break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static Point Snap45(Point a, Point b)
        {
            int dx = b.X - a.X, dy = b.Y - a.Y;
            if (Math.Abs(dx) > Math.Abs(dy) * 2) return new Point(b.X, a.Y);
            if (Math.Abs(dy) > Math.Abs(dx) * 2) return new Point(a.X, b.Y);
            int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new Point(a.X + Math.Sign(dx) * d, a.Y + Math.Sign(dy) * d);
        }

        public static IEnumerable<Point> RectPoints(Point a, Point b, bool filled)
        {
            int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (filled || x == x0 || x == x1 || y == y0 || y == y1) yield return new Point(x, y);
        }

        public static IEnumerable<Point> EllipsePoints(Point a, Point b, bool filled)
        {
            int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var inside = new bool[w, h];
            double rx = w / 2.0, ry = h / 2.0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double nx = (x + 0.5 - rx) / rx, ny = (y + 0.5 - ry) / ry;
                    inside[x, y] = nx * nx + ny * ny <= 1.0 + 0.5 / Math.Max(rx, ry);
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!inside[x, y]) continue;
                    bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1 || !inside[x - 1, y] || !inside[x + 1, y] || !inside[x, y - 1] || !inside[x, y + 1];
                    if (filled || edge) yield return new Point(x0 + x, y0 + y);
                }
        }
    }
}
