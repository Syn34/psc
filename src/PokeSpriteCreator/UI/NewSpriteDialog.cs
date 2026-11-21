using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>New sprite: pick the generation style, the kind of sprite and a pose outline to start from (or a blank canvas).</summary>
    public class NewSpriteDialog : Form
    {
        readonly PartLibrary _lib;
        readonly ListBox _gens;
        readonly Label _desc, _sizeInfo;
        readonly RadioButton _front, _back, _ow;
        readonly ComboBox _sheet;
        readonly TextBox _name;
        readonly NumericUpDown _w, _h, _frames;
        readonly Label _framesLbl, _sheetLbl;
        readonly ListView _poses;
        readonly ImageList _thumbs;
        readonly Label _poseHint;
        bool _filling;
        List<Part> _poseParts = new List<Part>();

        public SpriteDocument Result;

        public NewSpriteDialog(PartLibrary lib, Settings s)
        {
            _lib = lib;
            Text = "New sprite";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            Font = Theme.UiFont;
            ClientSize = new Size(800, 520);

            var l1 = new Label { Text = "Style", Font = Theme.Heading, Left = 14, Top = 12, Width = 300, Tag = "accent" };
            _gens = new ListBox { Left = 14, Top = 36, Width = 330, Height = 96, IntegralHeight = false };
            foreach (var g in GenStyle.All) _gens.Items.Add(g);
            _desc = new Label { Left = 14, Top = 136, Width = 330, Height = 74, Tag = "sub", Font = Theme.Small };

            var l2 = new Label { Text = "Sprite", Font = Theme.Heading, Left = 14, Top = 214, Width = 300, Tag = "accent" };
            _front = new RadioButton { Text = KindNames.Label(SpriteKind.Front), Left = 14, Top = 240, Width = 330, Checked = true };
            _back = new RadioButton { Text = KindNames.Label(SpriteKind.Back), Left = 14, Top = 264, Width = 330 };
            _ow = new RadioButton { Text = KindNames.Label(SpriteKind.Overworld) + " (walking, running, bike…)", Left = 14, Top = 288, Width = 330 };
            _sheetLbl = new Label { Text = "Sheet", Left = 34, Top = 318, Width = 50 };
            _sheet = new ComboBox { Left = 90, Top = 314, Width = 140, DropDownStyle = ComboBoxStyle.DropDown };

            var ln = new Label { Text = "Name", Left = 14, Top = 352, Width = 70 };
            _name = new TextBox { Left = 90, Top = 348, Width = 200, Text = "NEWTRAINER", CharacterCasing = CharacterCasing.Upper };
            var nameHint = new Label { Left = 90, Top = 372, Width = 254, Height = 30, Text = "Used for exported file names, e.g. Graphics/Trainers/NEWTRAINER.png", Tag = "sub", Font = Theme.Small };

            var ls = new Label { Text = "Frame size", Left = 14, Top = 410, Width = 76 };
            _w = new NumericUpDown { Left = 90, Top = 406, Width = 60, Minimum = 8, Maximum = 512 };
            var lx = new Label { Text = "×", Left = 154, Top = 410, Width = 14 };
            _h = new NumericUpDown { Left = 170, Top = 406, Width = 60, Minimum = 8, Maximum = 512 };
            _framesLbl = new Label { Text = "Frames", Left = 240, Top = 410, Width = 50 };
            _frames = new NumericUpDown { Left = 292, Top = 406, Width = 52, Minimum = 1, Maximum = 16 };
            _sizeInfo = new Label { Left = 14, Top = 434, Width = 330, Height = 34, Tag = "sub", Font = Theme.Small };

            var l3 = new Label { Text = "Start from", Font = Theme.Heading, Left = 364, Top = 12, Width = 300, Tag = "accent" };
            _thumbs = new ImageList { ImageSize = new Size(96, 96), ColorDepth = ColorDepth.Depth32Bit };
            _poses = new ListView { Left = 364, Top = 36, Width = 422, Height = 400, View = View.LargeIcon, LargeImageList = _thumbs, MultiSelect = false, HideSelection = false };
            _poseHint = new Label { Left = 364, Top = 440, Width = 422, Height = 34, Tag = "sub", Font = Theme.Small };

            var ok = new Button { Text = "Create", Left = 600, Top = 480, Width = 90, Height = 28, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Cancel", Left = 696, Top = 480, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            AcceptButton = ok; CancelButton = cancel;

            Controls.AddRange(new Control[] { l1, _gens, _desc, l2, _front, _back, _ow, _sheetLbl, _sheet, ln, _name, nameHint, ls, _w, lx, _h, _framesLbl, _frames, _sizeInfo, l3, _poses, _poseHint, ok, cancel });

            _gens.SelectedIndexChanged += (a, b) => Changed(true);
            _front.CheckedChanged += (a, b) => { if (_front.Checked) Changed(true); };
            _back.CheckedChanged += (a, b) => { if (_back.Checked) Changed(true); };
            _ow.CheckedChanged += (a, b) => { if (_ow.Checked) Changed(true); };
            _sheet.SelectedIndexChanged += (a, b) => Changed(true);
            _sheet.Leave += (a, b) => Changed(true);
            _poses.SelectedIndexChanged += (a, b) => { if (!_filling) PoseChosen(); };
            _poses.DoubleClick += (a, b) => Create();
            ok.Click += (a, b) => Create();

            _filling = true;
            _gens.SelectedIndex = Math.Max(0, Array.FindIndex(GenStyle.All, g => g.Gen == s.LastGen));
            if (s.LastKind == "Back") _back.Checked = true; else if (s.LastKind == "Overworld") _ow.Checked = true;
            _filling = false;
            Changed(true);
            Theme.Apply(this);
            _poses.BackColor = Theme.Input; _poses.ForeColor = Theme.Text;
        }

        GenStyle Style { get { return (GenStyle)(_gens.SelectedItem ?? GenStyle.All[2]); } }
        SpriteKind Kind { get { return _back.Checked ? SpriteKind.Back : _ow.Checked ? SpriteKind.Overworld : SpriteKind.Front; } }
        string Sheet { get { var t = (_sheet.Text ?? "").Trim(); return t.Length == 0 ? "Walk" : t; } }

        void Changed(bool resetSize)
        {
            if (_filling) return;
            _filling = true;
            var st = Style;
            _desc.Text = st.Describe();
            bool ow = Kind == SpriteKind.Overworld;
            _sheet.Visible = _sheetLbl.Visible = ow;
            if (ow && _sheet.Items.Count == 0 || ow && !_lib.Sheets(st.Gen).SequenceEqual(_sheet.Items.Cast<string>()))
            {
                var keep = _sheet.Text;
                _sheet.Items.Clear();
                foreach (var sh in _lib.Sheets(st.Gen)) _sheet.Items.Add(sh);
                _sheet.Text = string.IsNullOrEmpty(keep) ? "Walk" : keep;
            }
            _frames.Visible = _framesLbl.Visible = Kind == SpriteKind.Back;
            if (resetSize)
            {
                var lay = st.DefaultLayout(Kind);
                _w.Value = lay.FrameW; _h.Value = lay.FrameH; _frames.Value = lay.Cols;
            }
            UpdateSizeInfo();
            FillPoses();
            _filling = false;
        }

        void UpdateSizeInfo()
        {
            var st = Style;
            var def = st.DefaultLayout(Kind);
            bool custom = _w.Value != def.FrameW || _h.Value != def.FrameH;
            _sizeInfo.Text = Kind == SpriteKind.Overworld
                ? "4 × 4 frames: rows are Down, Left, Right, Up; columns are the walking steps." + (custom ? " (Size set by the pose.)" : "")
                : custom ? "Size taken from the pose (the game's own size is " + def.FrameW + "×" + def.FrameH + ")." : "The game's own size. Export can scale it up for your game.";
        }

        void FillPoses()
        {
            _poses.BeginUpdate();
            _poses.Items.Clear();
            _thumbs.Images.Clear();
            _thumbs.Images.Add(BlankThumb());
            _poses.Items.Add(new ListViewItem("Blank canvas", 0) { Tag = null, Selected = true });
            _poseParts = _lib.Parts(Style.Gen, Kind, Kind == SpriteKind.Overworld ? Sheet : null, PartLibrary.PoseCategory);
            for (int i = 0; i < _poseParts.Count; i++)
            {
                var p = _poseParts[i];
                PixelImage first = null;
                var lay = LayoutFor(p);
                if (p.Sheet != null && lay != null) first = p.FramesFor(lay).Front[0];
                _thumbs.Images.Add(Pix.Thumb(first, 96, 96));
                _poses.Items.Add(new ListViewItem(p.Name, i + 1) { Tag = p });
            }
            _poses.EndUpdate();
            _poseHint.Text = _poseParts.Count == 0
                ? "No poses for this style yet. Put outline pictures in Parts\\" + Style.Name + "\\" + KindNames.Folder(Kind) + (Kind == SpriteKind.Overworld ? "\\" + Sheet : "") + "\\Poses."
                : "A pose is a blank outline to fill in. It also sets the canvas size.";
        }

        Bitmap BlankThumb()
        {
            var b = Pix.Thumb(null, 96, 96);
            using (var g = Graphics.FromImage(b))
            using (var p = new Pen(Color.FromArgb(150, 150, 160)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                g.DrawRectangle(p, 20, 20, 55, 55);
            return b;
        }

        /// <summary>The layout a pose picture implies (its own frame size, shrinking pixel-doubled art).</summary>
        FrameLayout LayoutFor(Part p)
        {
            var img = p.Sheet;
            if (img == null) return null;
            var def = Style.DefaultLayout(Kind);
            int w = img.W, h = img.H;
            int k = 1;
            for (int t = 4; t >= 2; t--)
                if ((w > def.SheetW || h > def.SheetH) && img.IsBlocky(t)) { k = t; break; }
            w /= k; h /= k;
            switch (Kind)
            {
                case SpriteKind.Overworld: return new FrameLayout(Math.Max(1, w / 4), Math.Max(1, h / 4), 4, 4);
                case SpriteKind.Back: { int f = Math.Max(1, w / Math.Max(1, h)); return new FrameLayout(h, h, f, 1); }
                default: return new FrameLayout(w, h, 1, 1);
            }
        }

        Part SelectedPose { get { return _poses.SelectedItems.Count > 0 ? _poses.SelectedItems[0].Tag as Part : null; } }

        void PoseChosen()
        {
            var p = SelectedPose;
            _filling = true;
            if (p == null) { var def = Style.DefaultLayout(Kind); _w.Value = def.FrameW; _h.Value = def.FrameH; _frames.Value = def.Cols; }
            else
            {
                var lay = LayoutFor(p);
                if (lay != null)
                {
                    _w.Value = Math.Max(_w.Minimum, Math.Min(_w.Maximum, lay.FrameW));
                    _h.Value = Math.Max(_h.Minimum, Math.Min(_h.Maximum, lay.FrameH));
                    if (Kind == SpriteKind.Back) _frames.Value = Math.Max(1, Math.Min(16, lay.Cols));
                }
            }
            UpdateSizeInfo();
            _filling = false;
        }

        void Create()
        {
            var lay = Kind == SpriteKind.Overworld ? new FrameLayout((int)_w.Value, (int)_h.Value, 4, 4)
                    : Kind == SpriteKind.Back ? new FrameLayout((int)_w.Value, (int)_h.Value, (int)_frames.Value, 1)
                    : new FrameLayout((int)_w.Value, (int)_h.Value, 1, 1);
            var d = new SpriteDocument(Style, Kind, Kind == SpriteKind.Overworld ? Sheet : "Walk", lay);
            d.Name = string.IsNullOrWhiteSpace(_name.Text) ? "NEWTRAINER" : _name.Text.Trim().Replace(' ', '_');
            var pose = SelectedPose;
            if (pose != null) d.Layers.Add(d.MakePartLayer(pose));
            d.Layers.Add(d.NewPaintLayer("Paint"));
            Result = d;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
