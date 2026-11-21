using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// The parts browser: categories on the left, the parts of the chosen category on the right,
    /// each shown "tried on" the current sprite. Click a part to wear it; Ctrl+click adds it as an extra layer.
    /// </summary>
    public class PartsPanel : Panel
    {
        readonly Editor E;
        readonly Label _title, _hint;
        readonly ListBox _cats;
        readonly ListView _list;
        readonly ImageList _images;
        readonly Button _open, _reload, _newCat;
        readonly Timer _refreshTimer;
        List<Part> _parts = new List<Part>();
        bool _filling;
        const int ThumbSize = 72;

        public PartsPanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(8, 6, 4, 6);

            _title = new Label { Text = "Parts", Font = Theme.Heading, Dock = DockStyle.Top, Height = 24, Tag = "accent" };
            _hint = new Label { Dock = DockStyle.Top, Height = 34, Font = Theme.Small, Tag = "sub", Text = "Click to wear · Ctrl+click adds another layer" };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
            _open = new Button { Text = "Open folder", AutoSize = true, Height = 26 };
            _newCat = new Button { Text = "New category…", AutoSize = true, Height = 26 };
            _reload = new Button { Text = "Reload", AutoSize = true, Height = 26 };
            bottom.Controls.AddRange(new Control[] { _open, _newCat, _reload });

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 4 };
            _cats = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, Font = Theme.UiFont };
            _images = new ImageList { ImageSize = new Size(ThumbSize, ThumbSize), ColorDepth = ColorDepth.Depth32Bit };
            _list = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = _images, MultiSelect = false, BorderStyle = BorderStyle.None, HideSelection = false };
            split.Panel1.Controls.Add(_cats);
            split.Panel2.Controls.Add(_list);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(_hint);
            Controls.Add(_title);
            split.SplitterDistance = 104;

            _cats.DrawItem += DrawCategory;
            _cats.SelectedIndexChanged += (s, a) => { if (!_filling) FillParts(); };
            _list.MouseClick += (s, a) => { var it = _list.GetItemAt(a.X, a.Y); if (it != null) Choose(it, (ModifierKeys & Keys.Control) != 0); };
            _list.KeyDown += (s, a) => { if (a.KeyCode == Keys.Enter && _list.SelectedItems.Count > 0) Choose(_list.SelectedItems[0], a.Control); };
            _open.Click += (s, a) => OpenFolder();
            _reload.Click += (s, a) => Reload();
            _newCat.Click += (s, a) => NewCategory();

            _refreshTimer = new Timer { Interval = 350 };
            _refreshTimer.Tick += (s, a) => { _refreshTimer.Stop(); RefreshThumbs(); };

            E.LayersChanged += () => { FillCategories(); };
            E.DocChanged += () => { _refreshTimer.Stop(); _refreshTimer.Start(); };
            E.SelectionChanged += SyncToSelection;
        }

        string Cat { get { return _cats.SelectedItem as string; } }

        public void Reload()
        {
            E.Lib.Refresh();
            if (E.Doc != null)
                foreach (var pl in E.Doc.Layers.OfType<PartLayer>())
                {
                    var fresh = E.Lib.Find(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet, pl.Category, pl.PartName);
                    if (fresh != null) pl.Part = fresh;
                }
            FillCategories();
            E.InvalidatePreview();
            E.Changed();
            E.Say("Parts reloaded from " + E.Lib.Root);
        }

        void OpenFolder()
        {
            string dir = E.Doc != null ? E.Lib.KindFolder(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet) : E.Lib.Root;
            if (E.Doc != null && Cat != null) dir = Path.Combine(dir, Cat);
            try { Directory.CreateDirectory(dir); Process.Start(dir); }
            catch (Exception ex) { E.Say("Couldn't open " + dir + ": " + ex.Message); }
        }

        void NewCategory()
        {
            if (E.Doc == null) return;
            var name = Prompt.Show(FindForm(), "New category", "Name of the new part category (a folder in " + E.Lib.KindFolder(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet) + "):", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show(FindForm(), "That name can't be used for a folder.", "New category"); return; }
            Directory.CreateDirectory(Path.Combine(E.Lib.KindFolder(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet), name));
            E.Lib.Refresh();
            FillCategories();
            _cats.SelectedItem = name;
        }

        public void FillCategories()
        {
            if (E.Doc == null) { _cats.Items.Clear(); _list.Items.Clear(); return; }
            _title.Text = "Parts · " + E.Doc.Style.Name + " " + (E.Doc.Kind == SpriteKind.Overworld ? E.Doc.Sheet : E.Doc.Kind.ToString());
            var keep = Cat;
            var cats = E.Lib.Categories(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet);
            bool same = cats.SequenceEqual(_cats.Items.Cast<string>());
            _filling = true;
            if (!same)
            {
                _cats.BeginUpdate();
                _cats.Items.Clear();
                foreach (var c in cats) _cats.Items.Add(c);
                _cats.EndUpdate();
            }
            if (keep != null && cats.Contains(keep)) _cats.SelectedItem = keep;
            else if (cats.Count > 0)
            {
                // start on the first category that has parts (but not Poses when a pose is already on)
                var first = cats.FirstOrDefault(c => E.Lib.Parts(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet, c).Count > 0 &&
                    !(c == PartLibrary.PoseCategory && E.Doc.Layers.OfType<PartLayer>().Any(l => l.Category == c)));
                _cats.SelectedItem = first ?? cats[0];
            }
            _filling = false;
            _cats.Invalidate();
            int total = cats.Sum(c => E.Lib.Parts(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet, c).Count);
            _hint.Text = total == 0
                ? "No parts here yet. Put PNG files in the category folders (Open folder), then Reload. You can still draw free-hand."
                : "Click to wear · Ctrl+click adds another layer · (None) takes it off";
            _hint.ForeColor = total == 0 ? Theme.Warn : Theme.SubText;
            FillParts();
        }

        void DrawCategory(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var name = (string)_cats.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Selection : Theme.Input)) e.Graphics.FillRectangle(b, e.Bounds);
            int n = E.Doc == null ? 0 : E.Lib.Parts(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet, name).Count;
            bool worn = E.Doc != null && E.Doc.Layers.OfType<PartLayer>().Any(l => l.Category == name);
            if (worn) using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 4, 3, e.Bounds.Height - 8);
            TextRenderer.DrawText(e.Graphics, name, worn ? Theme.Bold : Theme.UiFont, new Rectangle(e.Bounds.X + 7, e.Bounds.Y, e.Bounds.Width - 30, e.Bounds.Height), n == 0 ? Theme.SubText : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, n.ToString(), Theme.Small, new Rectangle(e.Bounds.Right - 28, e.Bounds.Y, 24, e.Bounds.Height), Theme.SubText, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        void FillParts()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Image im in _images.Images) im.Dispose();
            _images.Images.Clear();
            _parts = E.Doc == null || Cat == null ? new List<Part>() : E.Lib.Parts(E.Doc.Style.Gen, E.Doc.Kind, E.Doc.Sheet, Cat);
            if (E.Doc != null && Cat != null)
            {
                _images.Images.Add(Thumb(null));
                _list.Items.Add(new ListViewItem("(None)", 0) { Tag = null });
                for (int i = 0; i < _parts.Count; i++)
                {
                    _images.Images.Add(Thumb(_parts[i]));
                    _list.Items.Add(new ListViewItem(_parts[i].Name, i + 1) { Tag = _parts[i], ToolTipText = _parts[i].Path });
                }
            }
            _list.EndUpdate();
            _list.ShowItemToolTips = true;
            MarkWorn();
        }

        void RefreshThumbs()
        {
            if (E.Doc == null || Cat == null || _list.Items.Count != _parts.Count + 1) return;
            for (int i = 0; i <= _parts.Count; i++)
            {
                var old = _images.Images[i];
                _images.Images[i] = Thumb(i == 0 ? null : _parts[i - 1]);
                old.Dispose();
            }
            _list.Invalidate();
            MarkWorn();
        }

        /// <summary>The current sprite (frame 0) wearing this part instead of whatever it wears in this category.</summary>
        Bitmap Thumb(Part p)
        {
            var d = E.Doc;
            var cur = E.CurrentPart != null && E.CurrentPart.Category == Cat ? E.CurrentPart : d.Layers.OfType<PartLayer>().FirstOrDefault(l => l.Category == Cat);
            int frame = 0;
            PixelImage img;
            if (p != null && p.Sheet == null) img = null;
            else
            {
                var saved = d.Layers;
                var layers = d.Layers.ToList();
                if (cur != null)
                {
                    int i = layers.IndexOf(cur);
                    if (p == null) layers.RemoveAt(i);
                    else
                    {
                        var t = (PartLayer)cur.Clone();
                        t.Part = p;
                        if (!p.FramesFor(d.Layout).Loose) { t.OffsetX = 0; t.OffsetY = 0; }
                        else if (!cur.IsLoose(d.Layout)) { var f = d.MakePartLayer(p); t.OffsetX = f.OffsetX; t.OffsetY = f.OffsetY; }
                        layers[i] = t;
                    }
                }
                else if (p != null)
                {
                    var t = d.MakePartLayer(p);
                    layers.Insert(d.InsertIndexFor(p.Category, E.Lib.OrderFor(d.Style.Gen, d.Kind, d.Sheet)), t);
                }
                d.Layers = layers;
                try { img = d.Composite(frame); }
                finally { d.Layers = saved; }
            }
            var bmp = Pix.Thumb(img, ThumbSize, ThumbSize);
            if (p != null && p.Sheet == null)
                using (var g = Graphics.FromImage(bmp)) TextRenderer.DrawText(g, "can't read", Theme.Small, new Rectangle(0, 0, ThumbSize, ThumbSize), Theme.Warn, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return bmp;
        }

        void MarkWorn()
        {
            if (E.Doc == null) return;
            var worn = new HashSet<string>(E.Doc.Layers.OfType<PartLayer>().Where(l => l.Category == Cat).Select(l => l.PartName), StringComparer.OrdinalIgnoreCase);
            _filling = true;
            foreach (ListViewItem it in _list.Items)
            {
                var p = it.Tag as Part;
                bool on = p == null ? worn.Count == 0 : worn.Contains(p.Name);
                it.Font = on ? Theme.Bold : Theme.UiFont;
                it.ForeColor = on ? Theme.Accent : Theme.Text;
            }
            _filling = false;
        }

        void SyncToSelection()
        {
            var pl = E.CurrentPart;
            if (pl == null || E.Doc == null || _cats.Items.Count == 0) return;
            if (Cat != pl.Category && _cats.Items.Contains(pl.Category)) _cats.SelectedItem = pl.Category;
            else RefreshThumbs();
        }

        void Choose(ListViewItem it, bool extra)
        {
            if (E.Doc == null || Cat == null) return;
            var p = it.Tag as Part;
            if (p == null) E.RemoveCategory(Cat);
            else E.SetPart(p, extra);
            _cats.Invalidate();
        }

        public void ApplyTheme()
        {
            _list.BackColor = Theme.Input; _list.ForeColor = Theme.Text;
            _cats.BackColor = Theme.Input;
            FillParts();
        }
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
