using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// The frames of a back sprite or overworld sheet, with an animated preview at game size.
    /// Overworld sheets show their four directions (Down, Left, Right, Up) in groups.
    /// </summary>
    public class FrameStrip : Panel
    {
        readonly Editor E;
        readonly FrameView _view;
        readonly PreviewBox _preview;
        readonly CheckBox _play, _onion;
        readonly Button _copyAll, _mirrorLR, _mirrorRL, _add, _remove;
        readonly Timer _timer;
        int _animStep;
        readonly ToolTip _tip = new ToolTip();

        public FrameStrip(Editor e)
        {
            E = e;
            Tag = "panel";
            Height = 132;
            Padding = new Padding(8, 4, 8, 4);

            _preview = new PreviewBox { Dock = DockStyle.Right, Width = 150 };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
            _play = new CheckBox { Text = "Play", Appearance = Appearance.Button, AutoSize = true, Height = 26, Checked = true, TextAlign = ContentAlignment.MiddleCenter };
            _onion = new CheckBox { Text = "Onion skin", Appearance = Appearance.Button, AutoSize = true, Height = 26, TextAlign = ContentAlignment.MiddleCenter };
            _copyAll = Btn("Copy to all frames", "Copies the selected paint layer's current frame to every frame");
            _mirrorLR = Btn("Left → Right", "Make the right-facing frames a mirror of the left-facing ones (paint layers)");
            _mirrorRL = Btn("Right → Left", "Make the left-facing frames a mirror of the right-facing ones (paint layers)");
            _add = Btn("+ Frame", "Add a frame (copies the last one)");
            _remove = Btn("− Frame", "Remove the last frame");
            tools.Controls.AddRange(new Control[] { _play, _onion, _copyAll, _mirrorLR, _mirrorRL, _add, _remove });
            _view = new FrameView(E) { Dock = DockStyle.Fill };
            Controls.Add(_view);
            Controls.Add(tools);
            Controls.Add(_preview);

            _copyAll.Click += (s, a) => E.CopyToAllFrames();
            _mirrorLR.Click += (s, a) => E.MirrorRows(true);
            _mirrorRL.Click += (s, a) => E.MirrorRows(false);
            _add.Click += (s, a) => E.SetFrameCount(E.Doc.FrameCount + 1);
            _remove.Click += (s, a) => { if (E.Doc.FrameCount > 1) E.SetFrameCount(E.Doc.FrameCount - 1); };
            _onion.CheckedChanged += (s, a) => { E.OnionSkin = _onion.Checked; E.Changed(); };
            _play.CheckedChanged += (s, a) => { if (_play.Checked) _timer.Start(); else { _timer.Stop(); _preview.Invalidate(); } };
            _tip.SetToolTip(_onion, "Shows the previous frame faintly under the one you're drawing");

            _timer = new Timer { Interval = 150 };
            _timer.Tick += (s, a) => { _animStep++; UpdatePreview(); };
            _timer.Start();

            E.LayersChanged += Setup;
            E.DocChanged += () => { _view.Invalidate(); UpdatePreview(); };
            E.FrameChanged += () => { _view.Invalidate(); UpdatePreview(); };
        }

        Button Btn(string text, string tip)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 26, Margin = new Padding(4, 0, 0, 0) };
            _tip.SetToolTip(b, tip);
            return b;
        }

        void Setup()
        {
            if (E.Doc == null) return;
            bool ow = E.Doc.Kind == SpriteKind.Overworld;
            _mirrorLR.Visible = _mirrorRL.Visible = ow;
            _add.Visible = _remove.Visible = E.Doc.Kind == SpriteKind.Back;
            _view.Invalidate();
            UpdatePreview();
        }

        /// <summary>Overworld: walks the selected direction (frames 1-2-3-4). Back sprite: plays every frame.</summary>
        void UpdatePreview()
        {
            if (E.Doc == null) return;
            var frames = E.Preview;
            int idx;
            if (!_play.Checked) idx = E.Frame;
            else if (E.Doc.Kind == SpriteKind.Overworld)
            {
                int row = E.Frame / E.Doc.Layout.Cols;
                idx = row * E.Doc.Layout.Cols + _animStep % E.Doc.Layout.Cols;
                _timer.Interval = 150;
            }
            else { idx = _animStep % E.Doc.FrameCount; _timer.Interval = 180; }
            _preview.Image = idx < frames.Count ? frames[idx] : null;
            _preview.Caption = E.Doc.Kind == SpriteKind.Overworld ? "Walking " + OverworldSheets.Directions[Math.Min(3, E.Frame / E.Doc.Layout.Cols)].ToLowerInvariant() : "Animation";
            _preview.Invalidate();
        }

        public void Stop() { _timer.Stop(); }
    }

    /// <summary>The clickable frame thumbnails.</summary>
    public class FrameView : Control
    {
        readonly Editor E;
        public FrameView(Editor e) { E = e; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }

        int Cell
        {
            get
            {
                if (E.Doc == null) return 48;
                int n = E.Doc.FrameCount, groups = E.Doc.Kind == SpriteKind.Overworld ? E.Doc.Layout.Rows : 1;
                int byWidth = (Width - groups * 10) / Math.Max(1, n) - 4;
                return Math.Max(24, Math.Min(Height - 22, byWidth));
            }
        }

        Rectangle CellRect(int i)
        {
            int c = Cell;
            int group = E.Doc.Kind == SpriteKind.Overworld ? i / E.Doc.Layout.Cols : 0;
            int x = i * (c + 4) + group * 10;
            return new Rectangle(x, 18, c, c);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            if (E.Doc == null) return;
            var frames = E.Preview;
            bool ow = E.Doc.Kind == SpriteKind.Overworld;
            for (int i = 0; i < E.Doc.FrameCount; i++)
            {
                var r = CellRect(i);
                if (ow && i % E.Doc.Layout.Cols == 0)
                    TextRenderer.DrawText(g, OverworldSheets.Directions[Math.Min(3, i / E.Doc.Layout.Cols)], Theme.Small, new Point(r.X, 1), Theme.SubText);
                else if (!ow)
                    TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Small, new Point(r.X, 1), Theme.SubText);
                Pix.Checker(g, r, 4);
                if (i < frames.Count) Pix.Draw(g, frames[i], Pix.Fit(frames[i].W, frames[i].H, r, false));
                bool sel = i == E.Frame;
                using (var p = new Pen(sel ? Theme.Accent : Theme.Border, sel ? 2 : 1))
                    g.DrawRectangle(p, r.X - (sel ? 1 : 0), r.Y - (sel ? 1 : 0), r.Width + (sel ? 1 : -1), r.Height + (sel ? 1 : -1));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (E.Doc == null) return;
            for (int i = 0; i < E.Doc.FrameCount; i++)
                if (CellRect(i).Contains(e.Location)) { E.SelectFrame(i); return; }
        }
    }

    /// <summary>The sprite at real size (2×) on a plain background, as it would look in game.</summary>
    public class PreviewBox : Control
    {
        public PixelImage Image;
        public string Caption = "";
        public PreviewBox() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            var box = new Rectangle(4, 18, Width - 8, Height - 22);
            using (var b = new SolidBrush(Color.FromArgb(248, 248, 240))) g.FillRectangle(b, box);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
            TextRenderer.DrawText(g, Caption, Theme.Small, new Point(4, 1), Theme.SubText);
            if (Image == null) return;
            int k = Math.Min(Math.Min((box.Width - 4) / Image.W, (box.Height - 4) / Image.H), 2);
            var inner = Rectangle.Inflate(box, -2, -2);
            var r = k >= 1 ? new Rectangle(box.X + (box.Width - Image.W * k) / 2, box.Y + (box.Height - Image.H * k) / 2, Image.W * k, Image.H * k)
                           : Pix.Fit(Image.W, Image.H, inner, false);
            Pix.Draw(g, Image, r);
        }
    }
}
