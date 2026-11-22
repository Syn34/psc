using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>New sprite: pick the generation style and the kind of sprite, then start blank or from a picture (PNG / GIF).</summary>
    public class NewSpriteDialog : Form
    {
        readonly ListBox _gens;
        readonly Label _desc, _sizeInfo, _picNote;
        readonly RadioButton _front, _back, _ow, _blank, _fromPic;
        readonly ComboBox _sheet;
        readonly TextBox _name, _picPath;
        readonly NumericUpDown _w, _h, _frames;
        readonly Label _framesLbl, _sheetLbl;
        readonly PreviewBox _preview;
        List<PixelImage> _pics;
        bool _filling;

        public SpriteDocument Result;

        public NewSpriteDialog(Settings s, string picture = null)
        {
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
            var kindBox = new Panel { Left = 14, Top = 238, Width = 340, Height = 72 };
            _front = new RadioButton { Text = KindNames.Label(SpriteKind.Front) + " (walking sprite made from it)", Left = 0, Top = 2, Width = 330, Checked = true };
            _back = new RadioButton { Text = KindNames.Label(SpriteKind.Back), Left = 0, Top = 26, Width = 330 };
            _ow = new RadioButton { Text = KindNames.Label(SpriteKind.Overworld) + " (walking, running, bike…)", Left = 0, Top = 50, Width = 330 };
            kindBox.Controls.AddRange(new Control[] { _front, _back, _ow });
            _sheetLbl = new Label { Text = "Sheet", Left = 34, Top = 318, Width = 50 };
            _sheet = new ComboBox { Left = 90, Top = 314, Width = 140, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var sh in OverworldSheets.Defaults) _sheet.Items.Add(sh);
            _sheet.Text = "Walk";

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
            var startBox = new Panel { Left = 364, Top = 36, Width = 222, Height = 50 };
            _blank = new RadioButton { Text = "A blank canvas (draw it yourself)", Left = 0, Top = 2, Width = 222, Checked = true };
            _fromPic = new RadioButton { Text = "A picture (PNG, GIF or BMP)", Left = 0, Top = 26, Width = 220 };
            startBox.Controls.AddRange(new Control[] { _blank, _fromPic });
            var browse = new Button { Text = "Choose picture…", Left = 590, Top = 59, Width = 130, Height = 26 };
            _picPath = new TextBox { Left = 382, Top = 90, Width = 402, ReadOnly = true };
            _preview = new PreviewBox { Left = 382, Top = 120, Width = 402, Height = 300 };
            _picNote = new Label { Left = 382, Top = 424, Width = 402, Height = 46, Tag = "sub", Font = Theme.Small };

            var ok = new Button { Text = "Create", Left = 600, Top = 480, Width = 90, Height = 28 };
            var cancel = new Button { Text = "Cancel", Left = 696, Top = 480, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            AcceptButton = ok; CancelButton = cancel;

            Controls.AddRange(new Control[] { l1, _gens, _desc, l2, kindBox, _sheetLbl, _sheet, ln, _name, nameHint, ls, _w, lx, _h, _framesLbl, _frames, _sizeInfo,
                l3, startBox, browse, _picPath, _preview, _picNote, ok, cancel });

            _gens.SelectedIndexChanged += (a, b) => Changed();
            _front.CheckedChanged += (a, b) => { if (_front.Checked) Changed(); };
            _back.CheckedChanged += (a, b) => { if (_back.Checked) Changed(); };
            _ow.CheckedChanged += (a, b) => { if (_ow.Checked) Changed(); };
            _blank.CheckedChanged += (a, b) => Changed();
            _w.ValueChanged += (a, b) => UpdateSizeInfo();
            _h.ValueChanged += (a, b) => UpdateSizeInfo();
            browse.Click += (a, b) => Browse();
            ok.Click += (a, b) => Create();

            _filling = true;
            _gens.SelectedIndex = Math.Max(0, Array.FindIndex(GenStyle.All, g => g.Gen == s.LastGen));
            if (s.LastKind == "Back") _back.Checked = true; else if (s.LastKind == "Overworld") _ow.Checked = true;
            _filling = false;
            Theme.Apply(this);
            if (picture != null) LoadPicture(picture); else Changed();
        }

        GenStyle Style { get { return (GenStyle)(_gens.SelectedItem ?? GenStyle.All[2]); } }
        SpriteKind Kind { get { return _back.Checked ? SpriteKind.Back : _ow.Checked ? SpriteKind.Overworld : SpriteKind.Front; } }
        string Sheet { get { var t = (_sheet.Text ?? "").Trim(); return t.Length == 0 ? "Walk" : t; } }
        bool UsePicture { get { return _fromPic.Checked && _pics != null; } }

        void Browse()
        {
            using (var ofd = new OpenFileDialog { Filter = ImageImport.Filter, Title = "Choose a sprite picture" })
                if (ofd.ShowDialog(this) == DialogResult.OK) LoadPicture(ofd.FileName);
        }

        void LoadPicture(string path)
        {
            try { _pics = ImageImport.LoadFrames(path); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't read that picture:\r\n" + ex.Message, Text); return; }
            _picPath.Text = path;
            _filling = true;
            _fromPic.Checked = true;
            var n = Path.GetFileNameWithoutExtension(path).Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
            if (n.StartsWith("SPR_")) n = n.Substring(4);
            _name.Text = n;
            _filling = false;
            Changed();
        }

        void Changed()
        {
            if (_filling) return;
            _filling = true;
            var st = Style;
            _desc.Text = st.Describe();
            bool ow = Kind == SpriteKind.Overworld;
            _sheet.Visible = _sheetLbl.Visible = ow;
            _frames.Visible = _framesLbl.Visible = Kind == SpriteKind.Back;
            var lay = st.DefaultLayout(Kind);
            string note = "";
            if (UsePicture) lay = ImageImport.Suggest(_pics, Kind, st, out note);
            _w.Value = Math.Max(_w.Minimum, Math.Min(_w.Maximum, lay.FrameW));
            _h.Value = Math.Max(_h.Minimum, Math.Min(_h.Maximum, lay.FrameH));
            _frames.Value = Math.Max(1, Math.Min(16, lay.Cols));
            if (UsePicture)
            {
                var frames = ImageImport.FramesFor(_pics, lay);
                _preview.Image = SpriteDocument.MakeSheet(frames, lay, 1);
                _preview.Caption = "Picture" + (_pics.Count > 1 ? " (" + _pics.Count + " frames)" : "");
                _picNote.Text = note.Length > 0 ? note : "The picture fits " + st.Name + "'s size as it is.";
            }
            else
            {
                _preview.Image = null; _preview.Caption = "";
                _picNote.Text = "Choose a picture to edit one you already have, e.g. a sprite from the games or your own art.";
            }
            _preview.Invalidate();
            UpdateSizeInfo();
            _filling = false;
        }

        void UpdateSizeInfo()
        {
            var def = Style.DefaultLayout(Kind);
            bool custom = _w.Value != def.FrameW || _h.Value != def.FrameH;
            _sizeInfo.Text = Kind == SpriteKind.Overworld
                ? "4 × 4 frames: rows are Down, Left, Right, Up; columns are the walking steps."
                : custom ? "Not the game's own size (" + def.FrameW + "×" + def.FrameH + ")." : "The game's own size. Export can scale it up for your game.";
        }

        void Create()
        {
            var lay = Kind == SpriteKind.Overworld ? new FrameLayout((int)_w.Value, (int)_h.Value, 4, 4)
                    : Kind == SpriteKind.Back ? new FrameLayout((int)_w.Value, (int)_h.Value, (int)_frames.Value, 1)
                    : new FrameLayout((int)_w.Value, (int)_h.Value, 1, 1);
            var d = new SpriteDocument(Style, Kind, Kind == SpriteKind.Overworld ? Sheet : "Walk", lay);
            d.Name = string.IsNullOrWhiteSpace(_name.Text) ? "NEWTRAINER" : _name.Text.Trim().Replace(' ', '_');
            if (UsePicture) d.Layers.Add(d.LayerFromFrames("Picture", ImageImport.FramesFor(_pics, lay)));
            else d.Layers.Add(d.NewLayer("Paint"));
            Result = d;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
