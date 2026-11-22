using System;
using System.Drawing;
using System.Windows.Forms;

namespace PokeSpriteCreator.UI
{
    /// <summary>A sprite canvas with scroll bars along the bottom and right, and (when comparing) a header with the tab's name.</summary>
    public class CanvasView : Panel
    {
        readonly Editor E;
        public readonly SpriteCanvas Canvas;
        readonly HScrollBar _h;
        readonly VScrollBar _v;
        readonly Panel _bottom, _corner, _header;
        readonly Label _title;
        readonly Button _stop;
        bool _sync;

        /// <summary>The header's "Stop comparing" button was clicked.</summary>
        public event Action StopRequested;

        public CanvasView(Editor e, bool header)
        {
            E = e;
            Tag = "keep";
            Canvas = new SpriteCanvas(e) { Dock = DockStyle.Fill };
            _v = new VScrollBar { Dock = DockStyle.Right };
            _bottom = new Panel { Dock = DockStyle.Bottom, Height = SystemInformation.HorizontalScrollBarHeight, Tag = "keep" };
            _h = new HScrollBar { Dock = DockStyle.Fill };
            _corner = new Panel { Dock = DockStyle.Right, Width = SystemInformation.VerticalScrollBarWidth, Tag = "keep" };
            _bottom.Controls.Add(_h);
            _bottom.Controls.Add(_corner);

            _header = new Panel { Dock = DockStyle.Top, Height = 28, Visible = header, Tag = "keep", Padding = new Padding(8, 0, 4, 0) };
            _title = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Bold, AutoEllipsis = true };
            _stop = new Button { Dock = DockStyle.Right, Width = 128, Text = "Stop comparing", FlatStyle = FlatStyle.Flat };
            _header.Controls.Add(_title);
            _header.Controls.Add(_stop);

            Controls.Add(Canvas);
            Controls.Add(_v);
            Controls.Add(_bottom);
            Controls.Add(_header);

            _h.Scroll += (s, a) => { if (!_sync) Canvas.SetScroll(_h.Value, Canvas.ScrollY); };
            _v.Scroll += (s, a) => { if (!_sync) Canvas.SetScroll(Canvas.ScrollX, _v.Value); };
            _stop.Click += (s, a) => { if (StopRequested != null) StopRequested(); };
            _title.Click += (s, a) => { if (Canvas.S != null && !Canvas.IsActive) E.Activate(Canvas.S); };
            Canvas.ViewChanged += SyncBars;
            Canvas.Resize += (s, a) => SyncBars();
            E.TabsChanged += UpdateHeader;
            E.DocChanged += UpdateHeader;
            E.PartSwitched += () => { UpdateHeader(); SyncBars(); };
            ApplyTheme();
        }

        /// <summary>Puts the scroll bars in step with the canvas (greyed out when the whole frame fits).</summary>
        public void SyncBars()
        {
            _sync = true;
            try
            {
                var c = Canvas.ContentSize;
                Set(_h, c.Width, Canvas.Width, Canvas.ScrollX);
                Set(_v, c.Height, Canvas.Height, Canvas.ScrollY);
            }
            finally { _sync = false; }
        }

        void Set(ScrollBar b, int content, int view, int value)
        {
            if (content <= view || view <= 0)
            {
                b.Enabled = false;
                b.Minimum = 0; b.Maximum = 0; b.LargeChange = 1; b.Value = 0;
                return;
            }
            b.Enabled = true;
            b.Minimum = 0;
            b.Maximum = content - 1;
            b.LargeChange = Math.Max(1, view);
            b.SmallChange = Math.Max(4, Canvas.Zoom * 2);
            b.Value = Math.Max(0, Math.Min(content - view, value));
        }

        public void UpdateHeader()
        {
            var s = Canvas.S;
            if (s == null) { _title.Text = ""; return; }
            _title.Text = (Canvas.IsActive ? "Editing: " : "") + s.TabName + (s.Root.FilePath != null ? " · " + s.Root.Name : "") + (s.Doc != s.Root ? " (walking sprites)" : "");
            _header.BackColor = Canvas.IsActive ? Theme.Selection : Theme.Panel;
            _title.ForeColor = Canvas.IsActive ? Theme.Text : Theme.SubText;
        }

        public void ApplyTheme()
        {
            _header.BackColor = Theme.Panel;
            _corner.BackColor = Theme.Panel;
            _bottom.BackColor = Theme.Panel;
            _stop.BackColor = Theme.ButtonBack; _stop.ForeColor = Theme.Text; _stop.FlatAppearance.BorderColor = Theme.Border;
            UpdateHeader();
            Canvas.Invalidate();
        }
    }
}
