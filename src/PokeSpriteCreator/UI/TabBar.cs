using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// The row of open sprites above the canvas. Click a tab to edit it, × or middle-click to close it, + for a new sprite.
    /// Drag a tab onto the canvas (or right-click it) to compare it side by side with the tab being edited.
    /// </summary>
    public class TabBar : Control
    {
        readonly Editor E;
        readonly List<Rectangle> _tabs = new List<Rectangle>(), _closes = new List<Rectangle>();
        Rectangle _plus;
        int _hoverClose = -1, _hoverTab = -1, _press = -1;
        Point _pressAt;
        bool _dragging;
        readonly ToolTip _tip = new ToolTip();

        public event Action NewRequested;
        public event Action<Session> CloseRequested;
        /// <summary>A tab was dragged and let go outside the bar (screen point).</summary>
        public event Action<Session, Point> Dropped;

        public TabBar(Editor e)
        {
            E = e;
            Height = 32;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            E.TabsChanged += Invalidate;
            E.DocChanged += Invalidate;
            E.PartSwitched += Invalidate;
        }

        string Label(Session s) { return s.TabName + (s.Doc != s.Root ? " · walking" : ""); }

        void Layout2(Graphics g)
        {
            _tabs.Clear(); _closes.Clear();
            int x = 4, n = E.Sessions.Count;
            var widths = E.Sessions.Select(s => Math.Max(96, Math.Min(230, TextRenderer.MeasureText(g, Label(s), Theme.UiFont).Width + 50))).ToList();
            int total = widths.Sum() + 40;
            if (total > Width && n > 0)
            {
                // squeeze the tabs so they all fit
                double k = (Width - 44.0) / widths.Sum();
                widths = widths.Select(w => Math.Max(60, (int)(w * k))).ToList();
            }
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle(x, 3, widths[i], Height - 3);
                _tabs.Add(r);
                _closes.Add(new Rectangle(r.Right - 22, r.Y + (r.Height - 16) / 2, 16, 16));
                x += widths[i] + 2;
            }
            _plus = new Rectangle(x + 2, 6, 26, Height - 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Panel);
            Layout2(g);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < E.Sessions.Count; i++)
            {
                var s = E.Sessions[i];
                var r = _tabs[i];
                bool active = s == E.S, compared = s == E.CompareLeft || s == E.CompareRight;
                using (var b = new SolidBrush(active ? Theme.Back : i == _hoverTab ? Theme.Hover : Theme.Panel)) g.FillRectangle(b, r);
                using (var p = new Pen(Theme.Border)) { g.DrawLine(p, r.X, r.Y, r.X, r.Bottom); g.DrawLine(p, r.Right, r.Y, r.Right, r.Bottom); g.DrawLine(p, r.X, r.Y, r.Right, r.Y); }
                if (active) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Y, r.Width + 1, 3);
                int tx = r.X + 8;
                if (compared)
                {
                    // two little panes: shown side by side
                    using (var p = new Pen(Theme.Accent))
                    {
                        g.DrawRectangle(p, tx, r.Y + 9, 6, 10);
                        g.DrawRectangle(p, tx + 7, r.Y + 9, 6, 10);
                    }
                    tx += 18;
                }
                TextRenderer.DrawText(g, Label(s), active ? Theme.Bold : Theme.UiFont, new Rectangle(tx, r.Y, _closes[i].X - tx - 2, r.Height),
                    active ? Theme.Text : Theme.SubText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                var c = _closes[i];
                if (i == _hoverClose) using (var b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, c);
                using (var p = new Pen(i == _hoverClose ? Theme.Warn : Theme.SubText, 1.5f))
                {
                    g.DrawLine(p, c.X + 4, c.Y + 4, c.Right - 5, c.Bottom - 5);
                    g.DrawLine(p, c.Right - 5, c.Y + 4, c.X + 4, c.Bottom - 5);
                }
            }
            using (var p = new Pen(Theme.SubText, 1.5f))
            {
                int cx = _plus.X + _plus.Width / 2, cy = _plus.Y + _plus.Height / 2;
                g.DrawLine(p, cx - 6, cy, cx + 6, cy); g.DrawLine(p, cx, cy - 6, cx, cy + 6);
            }
            if (_dragging) TextRenderer.DrawText(g, "Drop on the canvas to compare side by side", Theme.Small, new Rectangle(0, 0, Width - 6, Height), Theme.Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        int TabAt(Point p) { for (int i = 0; i < _tabs.Count; i++) if (_tabs[i].Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_press >= 0 && e.Button == MouseButtons.Left && !_dragging && (Math.Abs(e.X - _pressAt.X) > 8 || Math.Abs(e.Y - _pressAt.Y) > 8))
            {
                _dragging = true; Cursor = Cursors.SizeAll; Invalidate();
            }
            int t = TabAt(e.Location), c = t >= 0 && _closes[t].Contains(e.Location) ? t : -1;
            if (t != _hoverTab || c != _hoverClose)
            {
                _hoverTab = t; _hoverClose = c; Invalidate();
                _tip.SetToolTip(this, t >= 0 ? (E.Sessions[t].Root.FilePath ?? "Not saved yet") + "\r\nDrag onto the canvas or right-click to compare side by side." : _plus.Contains(e.Location) ? "New sprite (Ctrl+N)" : "");
            }
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverTab = _hoverClose = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int t = TabAt(e.Location);
            if (e.Button == MouseButtons.Left && t >= 0 && !_closes[t].Contains(e.Location)) { _press = t; _pressAt = e.Location; Capture = true; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Capture = false;
            int t = TabAt(e.Location);
            if (_dragging)
            {
                _dragging = false; Cursor = Cursors.Default;
                var s = _press >= 0 && _press < E.Sessions.Count ? E.Sessions[_press] : null;
                _press = -1;
                Invalidate();
                if (s != null && !ClientRectangle.Contains(e.Location) && Dropped != null) Dropped(s, PointToScreen(e.Location));
                return;
            }
            _press = -1;
            if (e.Button == MouseButtons.Left)
            {
                if (_plus.Contains(e.Location)) { if (NewRequested != null) NewRequested(); return; }
                if (t < 0) return;
                if (_closes[t].Contains(e.Location)) { if (CloseRequested != null) CloseRequested(E.Sessions[t]); return; }
                E.PickTab(E.Sessions[t]);
            }
            else if (e.Button == MouseButtons.Middle && t >= 0) { if (CloseRequested != null) CloseRequested(E.Sessions[t]); }
            else if (e.Button == MouseButtons.Right && t >= 0) ShowMenu(E.Sessions[t], e.Location);
        }

        void ShowMenu(Session s, Point at)
        {
            var m = new ContextMenuStrip();
            if (s != E.S && E.S != null)
                m.Items.Add("Compare side by side with \"" + E.S.TabName.TrimEnd(' ', '*') + "\"", null, (a, b) => E.StartCompare(E.S, s));
            if (E.Comparing && (s == E.CompareLeft || s == E.CompareRight))
                m.Items.Add("Stop comparing", null, (a, b) => E.StopCompare());
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Close", null, (a, b) => { if (CloseRequested != null) CloseRequested(s); });
            m.Items.Add("Close other tabs", null, (a, b) =>
            {
                foreach (var o in E.Sessions.ToList()) if (o != s && CloseRequested != null) CloseRequested(o);
            });
            Theme.Apply(m);
            m.Show(this, at);
        }
    }
}
