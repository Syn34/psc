using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>The layer stack (top layer first) with show/hide, order and the selected layer's settings.</summary>
    public class LayersPanel : Panel
    {
        readonly Editor E;
        readonly ListBox _list;
        readonly Panel _props;
        bool _filling;
        const int Row = 40;

        // part layer settings
        readonly Label _propTitle;
        readonly FlowLayoutPanel _swatches;
        readonly TrackBar _hue, _sat, _light;
        readonly List<KeyValuePair<Control, int>> _below = new List<KeyValuePair<Control, int>>();
        readonly CheckBox _keepOutline, _flip;
        readonly NumericUpDown _x, _y;
        readonly Button _resetColors;
        readonly Panel _partBox;
        readonly Label _paintInfo;
        readonly ToolTip _tip = new ToolTip();

        public LayersPanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(6, 6, 8, 6);

            var title = new Label { Text = "Layers", Font = Theme.Heading, Dock = DockStyle.Top, Height = 24, Tag = "accent" };
            _list = new ListBox { Dock = DockStyle.Top, Height = 5 * Row + 4, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Row, BorderStyle = BorderStyle.FixedSingle };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
            buttons.Controls.Add(Btn("+ Paint", "New paint layer for free-hand drawing (Ctrl+Shift+N)", () => E.AddPaintLayer()));
            buttons.Controls.Add(Btn("Copy", "Duplicate layer", () => E.DuplicateLayer()));
            buttons.Controls.Add(Btn("▲", "Move up (Ctrl+])", () => E.MoveLayer(1), 30));
            buttons.Controls.Add(Btn("▼", "Move down (Ctrl+[)", () => E.MoveLayer(-1), 30));
            buttons.Controls.Add(Btn("Delete", "Delete layer", () => E.DeleteLayer()));

            _props = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 8, 0, 0) };
            _propTitle = new Label { Dock = DockStyle.Top, Height = 22, Font = Theme.Bold };

            // ---- part settings
            _partBox = new Panel { Dock = DockStyle.Top, Height = 330 };
            var colorsLbl = new Label { Text = "Colours (click to change, right-click to undo):", Left = 0, Top = 0, Width = 300, Height = 18, Tag = "sub", Font = Theme.Small };
            _swatches = new FlowLayoutPanel { Left = 0, Top = 18, Width = 290, Height = 52, AutoScroll = true, WrapContents = true };
            int y = 76;
            Label unused;
            _hue = Slider(-180, 180, ref y, "Hue", out unused);
            _sat = Slider(-100, 100, ref y, "Saturation", out unused);
            _light = Slider(-100, 100, ref y, "Brightness", out unused);
            _keepOutline = new CheckBox { Text = "Keep dark outlines", Left = 0, Top = y, Width = 150, Checked = true };
            _resetColors = new Button { Text = "Reset colours", Left = 160, Top = y - 2, Width = 110, Height = 25 };
            y += 30;
            _flip = new CheckBox { Text = "Flip", Left = 0, Top = y + 2, Width = 60 };
            var xl = new Label { Text = "X", Left = 70, Top = y + 4, Width = 14 };
            _x = new NumericUpDown { Left = 86, Top = y, Width = 60, Minimum = -512, Maximum = 512 };
            var yl = new Label { Text = "Y", Left = 154, Top = y + 4, Width = 14 };
            _y = new NumericUpDown { Left = 170, Top = y, Width = 60, Minimum = -512, Maximum = 512 };
            y += 30;
            var conv = new Button { Text = "Convert to paint layer", Left = 0, Top = y, Width = 170, Height = 25 };
            _tip.SetToolTip(conv, "Turns the part into pixels you can draw over and erase (it stops being linked to the parts folder).");
            _partBox.Controls.AddRange(new Control[] { colorsLbl, _swatches, _keepOutline, _resetColors, _flip, xl, _x, yl, _y, conv });
            _partBox.Height = y + 34;
            // everything under the swatches moves up or down with the number of swatch rows
            foreach (Control c in _partBox.Controls) if (c.Top > _swatches.Top) _below.Add(new KeyValuePair<Control, int>(c, c.Top));

            _paintInfo = new Label { Dock = DockStyle.Top, Height = 80, Tag = "sub", Font = Theme.Small,
                Text = "Free-hand layer. Draw with Pencil, shapes and Fill.\r\nFill follows the outlines of everything you can see (toggle \"Fill sees all layers\" on the toolbar).\r\nDouble-click a layer to rename it." };

            _props.Controls.Add(_paintInfo);
            _props.Controls.Add(_partBox);
            _props.Controls.Add(_propTitle);

            Controls.Add(_props);
            Controls.Add(buttons);
            Controls.Add(_list);
            Controls.Add(title);

            _list.DrawItem += DrawLayer;
            _list.MouseDown += ListMouseDown;
            _list.DoubleClick += (s, a) => Rename();
            _list.SelectedIndexChanged += (s, a) => { if (!_filling) E.SelectLayer(ToLayer(_list.SelectedIndex)); };

            _hue.ValueChanged += (s, a) => SetPartValue("hue", l => l.Hue = _hue.Value);
            _sat.ValueChanged += (s, a) => SetPartValue("sat", l => l.Sat = _sat.Value);
            _light.ValueChanged += (s, a) => SetPartValue("light", l => l.Light = _light.Value);
            _keepOutline.CheckedChanged += (s, a) => SetPartValue(null, l => l.KeepOutline = _keepOutline.Checked);
            _flip.CheckedChanged += (s, a) => SetPartValue(null, l => l.FlipH = _flip.Checked);
            _x.ValueChanged += (s, a) => SetPartValue("x", l => l.OffsetX = (int)_x.Value);
            _y.ValueChanged += (s, a) => SetPartValue("y", l => l.OffsetY = (int)_y.Value);
            _resetColors.Click += (s, a) => { if (E.CurrentPart == null) return; E.BeginChange(); E.CurrentPart.ResetColors(); E.Changed(); ShowProps(); };
            conv.Click += (s, a) => E.ConvertToPaint();

            E.LayersChanged += Fill;
            E.SelectionChanged += () => { SyncSelection(); ShowProps(); };
            E.DocChanged += () => { _list.Invalidate(); RefreshPropsLight(); };
            E.FrameChanged += () => _list.Invalidate();
        }

        Button Btn(string text, string tip, Action a, int w = 0)
        {
            var b = new Button { Text = text, Height = 26, Margin = new Padding(0, 0, 4, 0) };
            if (w > 0) b.Width = w; else b.AutoSize = true;
            b.Click += (s, e) => a();
            _tip.SetToolTip(b, tip);
            return b;
        }

        TrackBar Slider(int min, int max, ref int y, string label, out Label val)
        {
            var l = new Label { Text = label, Left = 0, Top = y + 6, Width = 74 };
            var t = new TrackBar { Left = 74, Top = y, Width = 180, Minimum = min, Maximum = max, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 10, AutoSize = false, Height = 30 };
            val = new Label { Left = 256, Top = y + 6, Width = 40, Text = "0" };
            var v = val;
            t.ValueChanged += (s, e) => v.Text = (t.Value > 0 ? "+" : "") + t.Value;
            t.MouseDoubleClick += (s, e) => t.Value = 0;
            _tip.SetToolTip(t, "Double-click to reset");
            _partBox.Controls.AddRange(new Control[] { l, t, val });
            y += 32;
            return t;
        }

        // the list shows the top layer first
        int ToLayer(int row) { return E.Doc == null || row < 0 ? -1 : E.Doc.Layers.Count - 1 - row; }
        int ToRow(int layer) { return E.Doc == null || layer < 0 ? -1 : E.Doc.Layers.Count - 1 - layer; }

        void Fill()
        {
            _filling = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            if (E.Doc != null) for (int i = E.Doc.Layers.Count - 1; i >= 0; i--) _list.Items.Add(E.Doc.Layers[i]);
            _list.EndUpdate();
            _filling = false;
            SyncSelection();
            ShowProps();
        }

        void SyncSelection()
        {
            _filling = true;
            int r = ToRow(E.LayerIndex);
            if (r >= 0 && r < _list.Items.Count) _list.SelectedIndex = r; else _list.ClearSelected();
            _filling = false;
            _list.Invalidate();
        }

        void DrawLayer(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || E.Doc == null) return;
            int li = ToLayer(e.Index);
            if (li < 0 || li >= E.Doc.Layers.Count) return;
            var l = E.Doc.Layers[li];
            var g = e.Graphics;
            bool sel = li == E.LayerIndex;
            using (var b = new SolidBrush(sel ? Theme.Selection : Theme.Input)) g.FillRectangle(b, e.Bounds);
            var r = e.Bounds;
            // eye
            var eye = new Rectangle(r.X + 6, r.Y + (r.Height - 16) / 2, 16, 16);
            using (var p = new Pen(l.Visible ? Theme.Text : Theme.SubText, 1.5f))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawEllipse(p, eye.X, eye.Y + 4, 15, 8);
                if (l.Visible) using (var br = new SolidBrush(Theme.Text)) g.FillEllipse(br, eye.X + 5, eye.Y + 5, 6, 6);
                else g.DrawLine(p, eye.X, eye.Bottom, eye.Right, eye.Y);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            }
            // thumbnail
            var tr = new Rectangle(r.X + 28, r.Y + 3, Row - 6, Row - 6);
            Pix.Checker(g, tr, 4);
            var img = E.Doc.LayerImage(l, E.Frame);
            Pix.Draw(g, img, Pix.Fit(img.W, img.H, tr, false));
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, tr.X, tr.Y, tr.Width - 1, tr.Height - 1);
            // text
            var pl = l as PartLayer;
            string sub = pl != null ? (pl.Part == null ? "missing: " + pl.Category + " / " + pl.PartName : pl.Category + ": " + pl.PartName + (pl.HasColorChanges ? " · recoloured" : "")) : "Paint (free-hand)";
            var tx = new Rectangle(tr.Right + 8, r.Y + 3, r.Width - tr.Right - 12, 18);
            TextRenderer.DrawText(g, l.Name, sel ? Theme.Bold : Theme.UiFont, tx, l.Visible ? Theme.Text : Theme.SubText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(tx.X, tx.Bottom, tx.Width, 16), pl != null && pl.Part == null ? Theme.Warn : Theme.SubText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        }

        void ListMouseDown(object sender, MouseEventArgs e)
        {
            int row = _list.IndexFromPoint(e.Location);
            if (row < 0 || E.Doc == null) return;
            int li = ToLayer(row);
            if (e.X < 26 && li >= 0)
            {
                E.BeginChange();
                E.Doc.Layers[li].Visible = !E.Doc.Layers[li].Visible;
                E.Changed();
                _list.Invalidate();
            }
            if (e.Button == MouseButtons.Right)
            {
                E.SelectLayer(li);
                var m = new ContextMenuStrip();
                m.Items.Add("Rename…", null, (s, a) => Rename());
                m.Items.Add("Duplicate", null, (s, a) => E.DuplicateLayer());
                if (E.CurrentPart != null) m.Items.Add("Convert to paint layer", null, (s, a) => E.ConvertToPaint());
                m.Items.Add("Merge down", null, (s, a) => E.MergeDown());
                m.Items.Add("Delete", null, (s, a) => E.DeleteLayer());
                Theme.Apply(m);
                m.Show(_list, e.Location);
            }
        }

        public void Rename()
        {
            var l = E.Current;
            if (l == null) return;
            var name = Prompt.Show(FindForm(), "Rename layer", "Layer name:", l.Name);
            if (string.IsNullOrWhiteSpace(name) || name == l.Name) return;
            E.BeginChange();
            l.Name = name.Trim();
            E.LayersEdited();
        }

        void SetPartValue(string key, Action<PartLayer> set)
        {
            if (_filling || E.CurrentPart == null) return;
            E.BeginChange(key == null ? null : key + E.LayerIndex);
            set(E.CurrentPart);
            E.Changed();
            if (key == "hue" || key == "sat" || key == "light" || key == null) FillSwatches();
        }

        void ShowProps()
        {
            var l = E.Current;
            _filling = true;
            _partBox.Visible = l is PartLayer;
            _paintInfo.Visible = l is PaintLayer;
            _propTitle.Text = l == null ? "" : l.Name + (l is PartLayer ? " settings" : "");
            var pl = l as PartLayer;
            if (pl != null)
            {
                _hue.Value = Clamp(pl.Hue, _hue); _sat.Value = Clamp(pl.Sat, _sat); _light.Value = Clamp(pl.Light, _light);
                _keepOutline.Checked = pl.KeepOutline; _flip.Checked = pl.FlipH;
                _x.Value = Math.Max(_x.Minimum, Math.Min(_x.Maximum, pl.OffsetX));
                _y.Value = Math.Max(_y.Minimum, Math.Min(_y.Maximum, pl.OffsetY));
                FillSwatches();
            }
            _filling = false;
        }

        static int Clamp(int v, TrackBar t) { return Math.Max(t.Minimum, Math.Min(t.Maximum, v)); }

        /// <summary>After a canvas edit, keep the numbers/swatches in step without rebuilding everything.</summary>
        void RefreshPropsLight()
        {
            var pl = E.CurrentPart;
            if (pl == null) return;
            _filling = true;
            if (_x.Value != pl.OffsetX) _x.Value = Math.Max(_x.Minimum, Math.Min(_x.Maximum, pl.OffsetX));
            if (_y.Value != pl.OffsetY) _y.Value = Math.Max(_y.Minimum, Math.Min(_y.Maximum, pl.OffsetY));
            _filling = false;
            var want = pl.SourcePalette().Select(c => pl.MapColor(c)).ToList();
            var have = _swatches.Controls.OfType<SwatchButton>().Select(s => s.Shown).ToList();
            if (!want.SequenceEqual(have)) FillSwatches();
        }

        void FillSwatches()
        {
            var pl = E.CurrentPart;
            _swatches.SuspendLayout();
            foreach (Control c in _swatches.Controls.Cast<Control>().ToList()) { _swatches.Controls.Remove(c); c.Dispose(); }
            if (pl != null)
            {
                foreach (var src in pl.SourcePalette())
                {
                    var b = new SwatchButton { Source = src, Shown = pl.MapColor(src), Changed = pl.ColorMap.ContainsKey(src) };
                    _tip.SetToolTip(b, (b.Changed ? "Was " + PixelImage.Hex(src) + ", now " : "") + PixelImage.Hex(b.Shown) + "  ·  click: change  ·  right-click: back to original  ·  Ctrl+click: use as drawing colour");
                    b.MouseUp += SwatchClick;
                    _swatches.Controls.Add(b);
                }
            }
            _swatches.ResumeLayout();
            int perRow = Math.Max(1, (_swatches.Width - 4) / 25), n = _swatches.Controls.Count;
            int rows = Math.Max(1, Math.Min(3, (n + perRow - 1) / perRow));
            int h = rows * 25 + 2, delta = h - 52;
            _swatches.Height = h;
            foreach (var kv in _below) kv.Key.Top = kv.Value + delta;
            _partBox.Height = _below.Max(kv => kv.Key.Bottom) + 8;
        }

        void SwatchClick(object sender, MouseEventArgs e)
        {
            var b = (SwatchButton)sender;
            var pl = E.CurrentPart;
            if (pl == null) return;
            if ((ModifierKeys & Keys.Control) != 0) { E.SetPrimary(b.Shown); return; }
            if (e.Button == MouseButtons.Right)
            {
                if (!pl.ColorMap.ContainsKey(b.Source)) return;
                E.BeginChange();
                pl.ColorMap.Remove(b.Source);
            }
            else
            {
                var c = ColorPicker.Pick(FindForm(), b.Shown, E);
                if (!c.HasValue) return;
                E.BeginChange();
                pl.ColorMap[b.Source] = c.Value;
            }
            E.Changed();
            FillSwatches();
            _list.Invalidate();
        }

        public void ApplyTheme() { _list.BackColor = Theme.Input; _list.Invalidate(); FillSwatches(); }
    }

    /// <summary>One colour of a part in the layer settings.</summary>
    public class SwatchButton : Control
    {
        public int Source, Shown;
        public bool Changed;
        public SwatchButton() { Size = new Size(22, 22); Margin = new Padding(0, 0, 3, 3); Cursor = Cursors.Hand; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Pix.Swatch(e.Graphics, ClientRectangle, Shown);
            if (Changed) using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillPolygon(b, new[] { new Point(Width - 8, 1), new Point(Width - 1, 1), new Point(Width - 1, 8) });
        }
    }
}
