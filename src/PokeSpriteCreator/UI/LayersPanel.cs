using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>The layer stack (top layer first): show/hide, order, rename, duplicate, merge.</summary>
    public class LayersPanel : Panel
    {
        readonly Editor E;
        readonly ListBox _list;
        bool _filling;
        const int Row = 40;
        readonly ToolTip _tip = new ToolTip();

        public LayersPanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(8, 6, 6, 6);

            var title = new Label { Text = "Layers", Font = Theme.Heading, Dock = DockStyle.Top, Height = 24, Tag = "accent" };
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Row, BorderStyle = BorderStyle.FixedSingle };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
            buttons.Controls.Add(Btn("+ Layer", "New layer (Ctrl+Shift+N)", () => E.AddLayer()));
            buttons.Controls.Add(Btn("Copy", "Duplicate layer (Ctrl+J)", () => E.DuplicateLayer()));
            buttons.Controls.Add(Btn("▲", "Move up (Ctrl+])", () => E.MoveLayer(1), 30));
            buttons.Controls.Add(Btn("▼", "Move down (Ctrl+[)", () => E.MoveLayer(-1), 30));
            buttons.Controls.Add(Btn("Delete", "Delete layer", () => E.DeleteLayer()));
            var hint = new Label { Dock = DockStyle.Bottom, Height = 30, Tag = "sub", Font = Theme.Small, Text = "Click the eye to show or hide. Double-click to rename. Right-click for more." };

            Controls.Add(_list);
            Controls.Add(hint);
            Controls.Add(buttons);
            Controls.Add(title);

            _list.DrawItem += DrawLayer;
            _list.MouseDown += ListMouseDown;
            _list.DoubleClick += (s, a) => Rename();
            _list.SelectedIndexChanged += (s, a) => { if (!_filling) E.SelectLayer(ToLayer(_list.SelectedIndex)); };

            E.LayersChanged += Fill;
            E.SelectionChanged += SyncSelection;
            E.DocChanged += () => _list.Invalidate();
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
            var eye = new Rectangle(r.X + 6, r.Y + (r.Height - 16) / 2, 16, 16);
            using (var p = new Pen(l.Visible ? Theme.Text : Theme.SubText, 1.5f))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawEllipse(p, eye.X, eye.Y + 4, 15, 8);
                if (l.Visible) using (var br = new SolidBrush(Theme.Text)) g.FillEllipse(br, eye.X + 5, eye.Y + 5, 6, 6);
                else g.DrawLine(p, eye.X, eye.Bottom, eye.Right, eye.Y);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            }
            var tr = new Rectangle(r.X + 28, r.Y + 3, Row - 6, Row - 6);
            Pix.Checker(g, tr, 4);
            if (E.Frame < l.Frames.Count) { var img = l.Frames[E.Frame]; Pix.Draw(g, img, Pix.Fit(img.W, img.H, tr, false)); }
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, tr.X, tr.Y, tr.Width - 1, tr.Height - 1);
            var tx = new Rectangle(tr.Right + 8, r.Y, r.Width - tr.Right - 12, r.Height);
            TextRenderer.DrawText(g, l.Name, sel ? Theme.Bold : Theme.UiFont, tx, l.Visible ? Theme.Text : Theme.SubText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
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

        public void ApplyTheme() { _list.BackColor = Theme.Input; _list.Invalidate(); }
    }

    /// <summary>A one-line text question.</summary>
    public static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string text, string value)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, 120), Font = Theme.UiFont, ShowInTaskbar = false })
            {
                var l = new Label { Text = text, Left = 12, Top = 10, Width = 396, Height = 36 };
                var t = new TextBox { Left = 12, Top = 50, Width = 396, Text = value ?? "" };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 252, Top = 84, Width = 75 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 333, Top = 84, Width = 75 };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                Theme.Apply(f);
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }
    }
}
