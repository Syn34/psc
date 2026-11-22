using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// The side panel for the "From a picture" tool: load a reference picture (kept with the sprite), drag a box around
    /// the thing in it, say what it is, drag a box on the sprite where it goes; the pixel version shows on the sprite;
    /// Confirm adds it as a layer, Redo tries another take, Cancel throws it away.
    /// </summary>
    public class RefPanel : Panel
    {
        readonly Editor E;
        readonly ComboBox _refs;
        readonly Button _load, _remove, _confirm, _redo, _cancel;
        readonly RefPictureView _pic;
        readonly TextBox _what;
        readonly ClusterView _clusters;
        readonly ShadePreviewBox _preview;
        readonly CheckBox _existing, _outline, _tidy;
        readonly Label _notes, _step;
        readonly ToolTip _tip = new ToolTip();
        bool _syncing;

        public RefPanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(8, 6, 8, 6);
            AutoScroll = true;

            var title = new Label { Text = "From a picture", Font = Theme.Heading, Left = 8, Top = 6, Width = 300, Height = 22, Tag = "accent" };
            _step = new Label { Left = 8, Top = 30, Width = 306, Height = 32, Font = Theme.Small, Tag = "sub" };

            _refs = new ComboBox { Left = 8, Top = 66, Width = 166, DropDownStyle = ComboBoxStyle.DropDownList };
            _load = new Button { Text = "Load…", Left = 180, Top = 64, Width = 64, Height = 26 };
            _remove = new Button { Text = "Remove", Left = 248, Top = 64, Width = 66, Height = 26 };
            _pic = new RefPictureView { Left = 8, Top = 96, Width = 306, Height = 230 };

            var whatLbl = new Label { Text = "What is it?", Left = 8, Top = 338, Width = 80, Height = 18 };
            _what = new TextBox { Left = 92, Top = 334, Width = 222 };
            var colLbl = new Label { Text = "Colours in your box (click one to leave it out):", Left = 8, Top = 362, Width = 306, Height = 16, Font = Theme.Small, Tag = "sub" };
            _clusters = new ClusterView { Left = 8, Top = 380, Width = 306, Height = 30 };

            var prevLbl = new Label { Text = "On the sprite", Left = 8, Top = 418, Width = 200, Height = 16, Font = Theme.Bold };
            _preview = new ShadePreviewBox { Left = 8, Top = 436, Width = 306, Height = 110 };
            _existing = new CheckBox { Text = "Only colours already in the sprite", Left = 8, Top = 552, Width = 306, Height = 22, Checked = true };
            _outline = new CheckBox { Text = "Outline where it meets the background", Left = 8, Top = 574, Width = 306, Height = 22, Checked = true };
            _tidy = new CheckBox { Text = "Tidy up photo noise", Left = 8, Top = 596, Width = 306, Height = 22, Checked = true };

            _confirm = new Button { Text = "Confirm", Left = 8, Top = 624, Width = 100, Height = 30, Enabled = false };
            _redo = new Button { Text = "Redo", Left = 112, Top = 624, Width = 100, Height = 30, Enabled = false };
            _cancel = new Button { Text = "Cancel", Left = 216, Top = 624, Width = 98, Height = 30, Enabled = false };
            _notes = new Label { Left = 8, Top = 662, Width = 306, Height = 120, Font = Theme.UiFont };

            _tip.SetToolTip(_load, "Load a picture to work from (a photo of a hairstyle, an outfit…). It's kept with this sprite, separate from its layers.");
            _tip.SetToolTip(_what, "A few words, like \"curly hair\" or \"cap\". It names the new layer; with hair words, skin and very light colours in the box are left out.");
            _tip.SetToolTip(_existing, "On: recolours it with your sprite's own shades (no new colours). Off: uses four shades from the photo.");
            _tip.SetToolTip(_confirm, "Adds it to the sprite as a new layer on top (Ctrl+Z undoes it).");
            _tip.SetToolTip(_redo, "Tries another take: a slightly different size, position, cut-out and shading.");

            Controls.AddRange(new Control[] { title, _step, _refs, _load, _remove, _pic, whatLbl, _what, colLbl, _clusters, prevLbl, _preview, _existing, _outline, _tidy, _confirm, _redo, _cancel, _notes });

            _load.Click += (s, a) => Load();
            _remove.Click += (s, a) => RemoveRef();
            _refs.SelectedIndexChanged += (s, a) =>
            {
                if (_syncing || E.Root == null) return;
                int i = _refs.SelectedIndex;
                E.SetRefPicture(i >= 0 && i < E.Root.References.Count ? E.Root.References[i] : null);
            };
            _pic.Selected += box => E.SetRefSource(box);
            _what.TextChanged += (s, a) => { if (!_syncing) E.SetRefDescription(_what.Text); };
            _clusters.Toggled += i => E.ToggleRefCluster(i);
            EventHandler opts = (s, a) =>
            {
                if (_syncing) return;
                E.RefOptions.OnlyExisting = _existing.Checked; E.RefOptions.Outline = _outline.Checked; E.RefOptions.Tidy = _tidy.Checked;
                E.RecomputeRef();
            };
            _existing.CheckedChanged += opts; _outline.CheckedChanged += opts; _tidy.CheckedChanged += opts;
            _confirm.Click += (s, a) => E.ConfirmRef();
            _redo.Click += (s, a) => E.RedoRef();
            _cancel.Click += (s, a) => E.CancelRef();

            E.RefChanged += Sync;
            E.TabsChanged += RefreshRefs;
            E.PartSwitched += () => { RefreshRefs(); if (E.RefTarget.HasValue) E.SetRefTarget(null); };
            E.FrameChanged += () => { if (E.RefTarget.HasValue) E.RecomputeRef(); };
            RefreshRefs();
            Sync();
        }

        void Load()
        {
            if (E.Root == null) return;
            using (var d = new OpenFileDialog { Title = "Load a reference picture", Filter = "Pictures (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                RefPicture p;
                try { p = RefPicture.FromFile(d.FileName); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Couldn't open that picture.\n\n" + ex.Message, "From a picture", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                E.Root.References.Add(p);
                E.Root.Dirty = true;
                E.NotifyTabs();
                RefreshRefs();
                _syncing = true; _refs.SelectedIndex = E.Root.References.Count - 1; _syncing = false;
                E.SetRefPicture(p);
                E.Say("Loaded \"" + p.Name + "\". Drag a box around the part you want in it.");
            }
        }

        void RemoveRef()
        {
            if (E.Root == null || E.RefPic == null) return;
            E.Root.References.Remove(E.RefPic);
            E.Root.Dirty = true;
            E.NotifyTabs();
            RefreshRefs();
            E.SetRefPicture(E.Root.References.FirstOrDefault());
        }

        /// <summary>The reference list for the sprite in the current tab.</summary>
        void RefreshRefs()
        {
            _syncing = true;
            try
            {
                var refs = E.Root == null ? new List<RefPicture>() : E.Root.References;
                _refs.Items.Clear();
                foreach (var r in refs) _refs.Items.Add(r.Name);
                if (refs.Count == 0) { _refs.Items.Add("(none yet)"); _refs.SelectedIndex = 0; _refs.Enabled = false; }
                else
                {
                    _refs.Enabled = true;
                    int i = refs.IndexOf(E.RefPic);
                    if (i < 0) { i = 0; E.RefPic = refs[0]; E.RefSource = null; E.RefClusters = null; }
                    _refs.SelectedIndex = i;
                }
                if (refs.Count == 0 && E.RefPic != null) { E.RefPic = null; E.RefSource = null; E.RefClusters = null; }
            }
            finally { _syncing = false; }
            Sync();
        }

        void Sync()
        {
            _syncing = true;
            try
            {
                _pic.Set(E.RefPic == null ? null : E.RefPic.Image, E.RefSource, E.RefClusters);
                _clusters.Clusters = E.RefClusters;
                if (_what.Text != E.RefDescription) _what.Text = E.RefDescription;
                var r = E.RefResult;
                _preview.Set(r == null || r.Empty ? null : r.Small, r == null || r.Empty ? Rectangle.Empty : new Rectangle(0, 0, r.Small.W, r.Small.H));
                _confirm.Enabled = _redo.Enabled = r != null && !r.Empty;
                _cancel.Enabled = E.RefTarget.HasValue;
                _remove.Enabled = E.RefPic != null;
                // what to do next
                _step.Text = E.RefPic == null ? "1. Load a reference picture (a photo is fine)."
                           : !E.RefSource.HasValue ? "2. Drag a box around the part you want in the picture (e.g. the hair)."
                           : !E.RefTarget.HasValue ? "3. Say what it is, then drag a box on the sprite where it should go."
                           : r != null && !r.Empty ? "4. Check it on the sprite: Confirm, Redo or Cancel." : "Adjust the boxes or the colours.";
                _notes.Text = r == null ? "" : string.Join("\n", r.Notes.Select(n => "• " + n).ToArray());
                var sz = TextRenderer.MeasureText(_notes.Text.Length == 0 ? " " : _notes.Text, _notes.Font, new Size(_notes.Width, 10000), TextFormatFlags.WordBreak);
                _notes.Height = sz.Height + 8;
            }
            finally { _syncing = false; }
        }

        public void ApplyTheme() { _pic.Invalidate(); _clusters.Invalidate(); _preview.Invalidate(); }
    }

    /// <summary>The reference picture, fitted; drag on it to box the part you want. Left-out colours in the box are faded.</summary>
    public class RefPictureView : Control
    {
        PixelImage _img; Rectangle? _sel; List<RefCluster> _clusters;
        Bitmap _bmp, _fade;
        Point? _dragFrom; Rectangle? _drag;
        public event Action<Rectangle> Selected;

        public RefPictureView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Cross;
        }

        public void Set(PixelImage img, Rectangle? sel, List<RefCluster> clusters)
        {
            if (img != _img) { if (_bmp != null) _bmp.Dispose(); _bmp = img == null ? null : img.ToBitmap(); }
            _img = img; _sel = sel; _clusters = clusters;
            if (_fade != null) { _fade.Dispose(); _fade = null; }
            if (img != null && sel.HasValue && clusters != null && clusters.Any(c => !c.Keep)) _fade = Fade(img, sel.Value, clusters);
            Invalidate();
        }

        /// <summary>A see-through white layer over the box where the colour is left out.</summary>
        static Bitmap Fade(PixelImage img, Rectangle box, List<RefCluster> clusters)
        {
            box.Intersect(new Rectangle(0, 0, img.W, img.H));
            if (box.Width <= 0 || box.Height <= 0) return null;
            var centres = clusters.Select(c => c.Color).ToArray();
            var b = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
            var data = b.LockBits(new Rectangle(0, 0, box.Width, box.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var px = new int[box.Width * box.Height];
            for (int y = 0; y < box.Height; y++)
                for (int x = 0; x < box.Width; x++)
                {
                    int c = img.Get(box.X + x, box.Y + y);
                    int best = 0, bd = int.MaxValue;
                    for (int j = 0; j < centres.Length; j++)
                    {
                        int dr = PixelImage.R(c) - PixelImage.R(centres[j]), dg = PixelImage.G(c) - PixelImage.G(centres[j]), db = PixelImage.B(c) - PixelImage.B(centres[j]);
                        int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                        if (d < bd) { bd = d; best = j; }
                    }
                    px[y * box.Width + x] = clusters[best].Keep ? 0 : unchecked((int)0xB4FFFFFF);
                }
            for (int y = 0; y < box.Height; y++) Marshal.Copy(px, y * box.Width, data.Scan0 + y * data.Stride, box.Width);
            b.UnlockBits(data);
            return b;
        }

        Rectangle PicRect
        {
            get
            {
                if (_img == null) return Rectangle.Empty;
                return Pix.Fit(_img.W, _img.H, new Rectangle(2, 2, Width - 4, Height - 4), false);
            }
        }

        Point ToImage(Point m)
        {
            var r = PicRect;
            int x = (int)((m.X - r.X) * _img.W / (double)r.Width), y = (int)((m.Y - r.Y) * _img.H / (double)r.Height);
            return new Point(Math.Max(0, Math.Min(_img.W - 1, x)), Math.Max(0, Math.Min(_img.H - 1, y)));
        }

        Rectangle ToScreen(Rectangle b)
        {
            var r = PicRect;
            double k = r.Width / (double)_img.W;
            return new Rectangle(r.X + (int)(b.X * k), r.Y + (int)(b.Y * k), Math.Max(1, (int)(b.Width * k)), Math.Max(1, (int)(b.Height * k)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.CanvasBack);
            if (_img == null)
            {
                TextRenderer.DrawText(g, "Load a reference picture", Theme.Small, ClientRectangle, Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                var r = PicRect;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_bmp, r);
                Rectangle? box = _drag ?? _sel;
                if (box.HasValue)
                {
                    var sr = ToScreen(box.Value);
                    if (_fade != null && !_drag.HasValue) g.DrawImage(_fade, sr);
                    using (var p1 = new Pen(Color.White, 1)) g.DrawRectangle(p1, sr);
                    using (var p2 = new Pen(Theme.AccentBase, 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p2, sr);
                }
            }
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_img == null || e.Button != MouseButtons.Left) return;
            _dragFrom = ToImage(e.Location); _drag = new Rectangle(_dragFrom.Value, new Size(1, 1)); Capture = true; Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragFrom.HasValue) return;
            var p = ToImage(e.Location); var a = _dragFrom.Value;
            _drag = Rectangle.FromLTRB(Math.Min(a.X, p.X), Math.Min(a.Y, p.Y), Math.Max(a.X, p.X) + 1, Math.Max(a.Y, p.Y) + 1);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragFrom.HasValue) return;
            var box = _drag.Value; _dragFrom = null; _drag = null; Capture = false;
            if (box.Width >= 4 && box.Height >= 4 && Selected != null) Selected(box);
            Invalidate();
        }
    }

    /// <summary>The colour groups in the boxed part of the picture; click one to leave it out (or put it back).</summary>
    public class ClusterView : Control
    {
        List<RefCluster> _c;
        public List<RefCluster> Clusters { get { return _c; } set { _c = value; Invalidate(); } }
        public event Action<int> Toggled;

        public ClusterView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        Rectangle Cell(int i) { return new Rectangle(i * 40, 2, 34, Height - 4); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            if (_c == null || _c.Count == 0)
            {
                TextRenderer.DrawText(g, "(box a part of the picture first)", Theme.Small, ClientRectangle, Theme.SubText, TextFormatFlags.VerticalCenter);
                return;
            }
            for (int i = 0; i < _c.Count; i++)
            {
                var r = Cell(i);
                Pix.Swatch(g, r, _c[i].Color, _c[i].Keep);
                if (!_c[i].Keep)
                {
                    using (var w = new SolidBrush(Color.FromArgb(150, Theme.Panel))) g.FillRectangle(w, r);
                    using (var p = new Pen(Theme.Warn, 2)) { g.DrawLine(p, r.Left + 4, r.Top + 4, r.Right - 5, r.Bottom - 5); g.DrawLine(p, r.Right - 5, r.Top + 4, r.Left + 4, r.Bottom - 5); }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_c == null) return;
            for (int i = 0; i < _c.Count; i++) if (Cell(i).Contains(e.Location) && Toggled != null) Toggled(i);
        }
    }
}
