using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// The side panel showing how the trainer looks walking in game, facing down, left, right and up.
    /// For a front sprite it's made automatically (OverworldGenerator) whenever the sprite is saved
    /// or Update is clicked; for an overworld sheet it plays the sheet itself.
    /// </summary>
    public class DirectionsPanel : Panel
    {
        readonly Editor E;
        readonly Label _title, _status, _upLbl;
        readonly WalkView _walk;
        readonly CheckBox _symmetric;
        readonly Button _update, _export, _edit, _upPick, _upClear;
        readonly ComboBox _scale;
        readonly Panel _frontTools;
        readonly Timer _timer;
        readonly ToolTip _tip = new ToolTip();
        OverworldResult _result;
        PixelImage _madeFrom;               // the front sprite the walking sprite was made from
        bool _stale = true, _filling;

        /// <summary>"Edit as overworld sprite": open this sheet as a new sprite.</summary>
        public event Action<OverworldResult> EditRequested;

        public DirectionsPanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(8, 6, 8, 6);

            _title = new Label { Text = "In-game directions", Font = Theme.Heading, Dock = DockStyle.Top, Height = 24, Tag = "accent" };
            _status = new Label { Dock = DockStyle.Top, Height = 46, Tag = "sub", Font = Theme.Small };
            _walk = new WalkView { Dock = DockStyle.Top, Height = 430 };

            _frontTools = new Panel { Dock = DockStyle.Fill };
            int y = 8;
            _update = new Button { Text = "Update now", Left = 0, Top = y, Width = 110, Height = 28 };
            _tip.SetToolTip(_update, "Makes the walking sprite from the sprite as it is now (it's also made every time you save).");
            _symmetric = new CheckBox { Text = "Symmetric front and back", Left = 120, Top = y + 4, Width = 180, Checked = true };
            _tip.SetToolTip(_symmetric, "Makes the down and up views the same on both sides (battle sprites are often drawn at an angle).");
            y += 38;
            var upHead = new Label { Text = "Up view hair:", Left = 0, Top = y + 5, Width = 82 };
            _upPick = new Button { Text = "From back sprite…", Left = 84, Top = y, Width = 130, Height = 26 };
            _upClear = new Button { Text = "Clear", Left = 218, Top = y, Width = 60, Height = 26 };
            _tip.SetToolTip(_upPick, "Use a back sprite (a PNG, GIF or .psc file) for the hair shape seen from behind.");
            y += 30;
            _upLbl = new Label { Left = 0, Top = y, Width = 290, Height = 18, Tag = "sub", Font = Theme.Small };
            y += 26;
            var scaleLbl = new Label { Text = "Export size", Left = 0, Top = y + 5, Width = 82 };
            _scale = new ComboBox { Left = 84, Top = y + 1, Width = 194, DropDownStyle = ComboBoxStyle.DropDownList };
            y += 34;
            _export = new Button { Text = "Export sheet…", Left = 0, Top = y, Width = 136, Height = 28 };
            _edit = new Button { Text = "Edit as overworld sprite", Left = 142, Top = y, Width = 160, Height = 28 };
            _tip.SetToolTip(_edit, "Opens the walking sprite as its own overworld sheet so you can touch it up.");
            _frontTools.Controls.AddRange(new Control[] { _update, _symmetric, upHead, _upPick, _upClear, _upLbl, scaleLbl, _scale, _export, _edit });

            Controls.Add(_frontTools);
            Controls.Add(_walk);
            Controls.Add(_status);
            Controls.Add(_title);

            _update.Click += (s, a) => Generate();
            _symmetric.CheckedChanged += (s, a) =>
            {
                if (_filling || E.Doc == null) return;
                E.Doc.OwSymmetric = _symmetric.Checked; E.Doc.Dirty = true; Generate();
            };
            _upPick.Click += (s, a) => PickBack();
            _upClear.Click += (s, a) => { if (E.Doc == null) return; E.Doc.UpSource = null; E.Doc.Dirty = true; Generate(); };
            _export.Click += (s, a) => Export();
            _edit.Click += (s, a) => { if (_result != null && EditRequested != null) EditRequested(_result); };

            _timer = new Timer { Interval = 170 };
            _timer.Tick += (s, a) => { _walk.Step++; _walk.Invalidate(); };
            _timer.Start();

            E.LayersChanged += DocSwitched;
            E.Saved += Generate;
            E.DocChanged += () =>
            {
                if (E.Doc == null) return;
                if (E.Doc.Kind == SpriteKind.Overworld) ShowSheet();
                else if (_result != null && !_stale && E.Doc.Kind == SpriteKind.Front && (_madeFrom == null || !_madeFrom.SameAs(E.Doc.Composite(0)))) { _stale = true; UpdateStatus(); }
            };
        }

        public void Stop() { _timer.Stop(); }

        void DocSwitched()
        {
            if (E.Doc == null) return;
            _filling = true;
            _symmetric.Checked = E.Doc.OwSymmetric;
            _filling = false;
            int fw = E.Doc.Kind == SpriteKind.Overworld ? E.Doc.Layout.FrameW : E.Doc.Style.OwW, fh = E.Doc.Kind == SpriteKind.Overworld ? E.Doc.Layout.FrameH : E.Doc.Style.OwH;
            var keep = _scale.SelectedIndex;
            _scale.Items.Clear();
            for (int k = 1; k <= 4; k++) _scale.Items.Add(k + "×  (" + fw * 4 * k + " × " + fh * 4 * k + ")" + (k == 1 ? "  game's own size" : k == 2 ? "  usual for Essentials" : ""));
            _scale.SelectedIndex = Math.Max(0, Math.Min(3, E.Settings.ExportScale - 1));
            _frontTools.Visible = E.Doc.Kind == SpriteKind.Front;
            if (E.Doc.Kind == SpriteKind.Overworld) { _result = null; ShowSheet(); return; }
            if (E.Doc.Kind == SpriteKind.Back)
            {
                _result = null; _walk.Result = null; _walk.Invalidate();
                _status.Text = "The walking sprite is made from a trainer front sprite. Open or make a front sprite to see it here.";
                return;
            }
            // a front sprite: make it now if the sprite came from a file, otherwise wait for the first save
            _result = null;
            if (E.Doc.FilePath != null || !E.Doc.UsedColors().Any()) Generate();
            else { _walk.Result = null; _walk.Invalidate(); _stale = true; UpdateStatus(); }
        }

        void ShowSheet()
        {
            var d = E.Doc;
            _walk.Result = new OverworldResult { Layout = d.Layout, Frames = E.Preview.ToList() };
            _walk.Invalidate();
            _status.Text = "Your " + d.Sheet.ToLowerInvariant() + " sheet walking in each direction (rows Down, Left, Right, Up).";
            _status.ForeColor = Theme.SubText;
        }

        /// <summary>Makes the walking sprite from the front sprite as it is now.</summary>
        public void Generate()
        {
            var d = E.Doc;
            if (d == null) return;
            if (d.Kind == SpriteKind.Overworld) { ShowSheet(); return; }
            if (d.Kind != SpriteKind.Front) return;
            PixelImage back = null;
            string backNote = "";
            if (!string.IsNullOrEmpty(d.UpSource))
            {
                back = LoadBack(d.UpSource);
                if (back == null) backNote = " (Couldn't read the back sprite " + Path.GetFileName(d.UpSource) + ".)";
            }
            var front = d.FinalFrames(d.LimitPalette)[0];
            _madeFrom = d.Composite(0);
            _result = OverworldGenerator.Make(front, d.Style, d.OwSymmetric, back);
            _result.Note += backNote;
            _walk.Result = _result;
            _walk.Invalidate();
            _stale = false;
            _upLbl.Text = string.IsNullOrEmpty(d.UpSource) ? "Guessed from the front sprite." : Path.GetFileName(d.UpSource);
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (E.Doc == null || E.Doc.Kind != SpriteKind.Front) return;
            if (_result == null)
            {
                _status.Text = "Save the sprite (Ctrl+S) or click Update now to see the trainer walking in game.";
                _status.ForeColor = Theme.SubText;
            }
            else if (_stale)
            {
                _status.Text = "The sprite has changed since this was made. Save or click Update now.";
                _status.ForeColor = Theme.Warn;
            }
            else
            {
                _status.Text = _result.Note;
                _status.ForeColor = Theme.SubText;
            }
            _export.Enabled = _edit.Enabled = _result != null;
        }

        static PixelImage LoadBack(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                if (path.EndsWith(SpriteDocument.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    var bd = SpriteDocument.Load(path);
                    return bd.FinalFrames(false)[0];
                }
                var frames = ImageImport.LoadFrames(path);
                if (frames.Count == 0) return null;
                var f = frames[0];
                // a strip of square frames: use the first
                if (f.W > f.H && f.W % f.H == 0) f = f.Crop(0, 0, f.H, f.H);
                return f;
            }
            catch { return null; }
        }

        void PickBack()
        {
            if (E.Doc == null) return;
            using (var ofd = new OpenFileDialog { Title = "Choose the back sprite", Filter = "Back sprite (*.png;*.gif;*.bmp;*" + SpriteDocument.Extension + ")|*.png;*.gif;*.bmp;*" + SpriteDocument.Extension + "|All files|*.*" })
            {
                if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
                E.Doc.UpSource = ofd.FileName;
                E.Doc.Dirty = true;
                Generate();
            }
        }

        void Export()
        {
            if (_result == null || E.Doc == null) return;
            int scale = _scale.SelectedIndex + 1;
            var folder = E.Settings.LastExportFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = E.Doc.FilePath != null ? Path.GetDirectoryName(E.Doc.FilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            using (var sfd = new SaveFileDialog { Filter = "PNG picture|*.png", FileName = "trainer_" + E.Doc.Name + ".png", Title = "Export the walking sprite sheet" })
            {
                try { sfd.InitialDirectory = folder; } catch { }
                if (sfd.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    SpriteDocument.MakeSheet(_result.Frames, _result.Layout, scale).Save(sfd.FileName);
                    E.Settings.LastExportFolder = Path.GetDirectoryName(sfd.FileName);
                    E.Settings.ExportScale = scale;
                    E.Say("Exported the walking sprite to " + sfd.FileName + "  (for a game folder: Graphics/Characters)");
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Couldn't save the picture:\r\n" + ex.Message, "Export sheet", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        public void ApplyTheme() { _walk.Invalidate(); UpdateStatus(); }
    }

    /// <summary>Four walking previews (Down, Left, Right, Up) on grass, plus the whole sheet.</summary>
    public class WalkView : Control
    {
        public OverworldResult Result;
        public int Step;
        static readonly string[] Names = { "Down", "Left", "Right", "Up" };

        public WalkView() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            int cellW = (Width - 10) / 2, cellH = 112;
            for (int d = 0; d < 4; d++)
            {
                var r = new Rectangle((d % 2) * (cellW + 10), (d / 2) * (cellH + 22) + 16, cellW, cellH);
                TextRenderer.DrawText(g, Names[d], Theme.Small, new Point(r.X, r.Y - 16), Theme.SubText);
                Grass(g, r);
                if (Result == null || Result.Frames.Count < 16) continue;
                var f = Result.Frames[d * Result.Layout.Cols + Step % Result.Layout.Cols];
                int k = Math.Max(1, Math.Min((r.Width - 8) / f.W, (r.Height - 8) / f.H));
                Pix.Draw(g, f, new Rectangle(r.X + (r.Width - f.W * k) / 2, r.Y + (r.Height - f.H * k) / 2, f.W * k, f.H * k));
            }
            // the whole sheet at its own size (doubled if it fits)
            int sy = 2 * (cellH + 22) + 18;
            TextRenderer.DrawText(g, "Sheet", Theme.Small, new Point(0, sy - 16), Theme.SubText);
            if (Result == null || Result.Frames.Count < 16)
            {
                TextRenderer.DrawText(g, "Nothing yet", Theme.Small, new Rectangle(0, sy, Width, Height - sy), Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                return;
            }
            var sheet = SpriteDocument.MakeSheet(Result.Frames, Result.Layout, 1);
            int ks = Math.Max(1, Math.Min(2, Math.Min(Width / sheet.W, (Height - sy) / Math.Max(1, sheet.H))));
            var sr = new Rectangle(0, sy, sheet.W * ks, sheet.H * ks);
            if (sr.Height > Height - sy) sr = Pix.Fit(sheet.W, sheet.H, new Rectangle(0, sy, Width, Height - sy), false);
            Pix.Checker(g, sr, 4);
            Pix.Draw(g, sheet, sr);
        }

        static void Grass(Graphics g, Rectangle r)
        {
            using (var b = new SolidBrush(Color.FromArgb(136, 200, 112))) g.FillRectangle(b, r);
            using (var b = new SolidBrush(Color.FromArgb(112, 176, 96)))
                for (int y = r.Y + 6; y < r.Bottom - 4; y += 14)
                    for (int x = r.X + ((y / 14) % 2 == 0 ? 6 : 13); x < r.Right - 6; x += 14)
                    { g.FillRectangle(b, x, y, 2, 4); g.FillRectangle(b, x + 3, y + 1, 2, 3); }
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
    }
}
