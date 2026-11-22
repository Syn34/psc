using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>Export the finished sprite as a PNG: to any file, or straight into a PokeCreator / Essentials game folder.</summary>
    public class ExportDialog : Form
    {
        readonly Editor E;
        readonly SpriteDocument D;
        readonly ComboBox _scale;
        readonly CheckBox _limit;
        readonly RadioButton _toFile, _toProject;
        readonly TextBox _file, _project, _name;
        readonly Label _target, _info;
        readonly PreviewBox _preview;

        public string ExportedPath;

        public ExportDialog(Editor e, SpriteDocument doc = null)
        {
            E = e;
            D = doc ?? e.Doc;
            var d = D;
            Text = "Export sprite";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            Font = Theme.UiFont;
            ClientSize = new Size(720, 420);

            _preview = new PreviewBox { Left = 14, Top = 14, Width = 260, Height = 300 };
            _info = new Label { Left = 14, Top = 318, Width = 260, Height = 50, Tag = "sub", Font = Theme.Small };

            int x = 292;
            var l1 = new Label { Text = "Size", Font = Theme.Heading, Left = x, Top = 12, Width = 300, Tag = "accent" };
            _scale = new ComboBox { Left = x, Top = 38, Width = 414, DropDownStyle = ComboBoxStyle.DropDownList };
            for (int k = 1; k <= 4; k++) _scale.Items.Add(k + "×  (" + d.Layout.SheetW * k + " × " + d.Layout.SheetH * k + ")" + (k == 1 ? "  game's own size" : k == 2 ? "  usual for Essentials / PokeCreator" : ""));
            _scale.SelectedIndex = Math.Max(0, Math.Min(3, e.Settings.ExportScale - 1));
            _limit = new CheckBox { Left = x, Top = 70, Width = 410, Checked = d.LimitPalette,
                Text = d.Style.Greyscale ? "Use Gen 1's 4 shades of grey" : "Limit to " + d.Style.MaxColors + " colours like " + d.Style.Name };

            var l2 = new Label { Text = "Save to", Font = Theme.Heading, Left = x, Top = 106, Width = 300, Tag = "accent" };
            _toFile = new RadioButton { Text = "A PNG file", Left = x, Top = 132, Width = 200, Checked = true };
            _file = new TextBox { Left = x + 18, Top = 156, Width = 320 };
            var browse = new Button { Text = "Browse…", Left = x + 344, Top = 154, Width = 70, Height = 25 };
            _toProject = new RadioButton { Text = "A game project folder (PokeCreator or Pokémon Essentials)", Left = x, Top = 192, Width = 410 };
            _project = new TextBox { Left = x + 18, Top = 216, Width = 320, Text = e.Settings.LastProjectFolder ?? "" };
            var browseP = new Button { Text = "Browse…", Left = x + 344, Top = 214, Width = 70, Height = 25 };
            var ln = new Label { Text = "Name", Left = x + 18, Top = 250, Width = 50 };
            _name = new TextBox { Left = x + 70, Top = 246, Width = 200, Text = d.Name, CharacterCasing = CharacterCasing.Upper };
            _target = new Label { Left = x + 18, Top = 274, Width = 396, Height = 48, Tag = "sub", Font = Theme.Small };

            var ok = new Button { Text = "Export", Left = 524, Top = 380, Width = 90, Height = 28 };
            var cancel = new Button { Text = "Cancel", Left = 620, Top = 380, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { _preview, _info, l1, _scale, _limit, l2, _toFile, _file, browse, _toProject, _project, browseP, ln, _name, _target, ok, cancel });

            string folder = e.Settings.LastExportFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = d.FilePath != null ? Path.GetDirectoryName(d.FilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            _file.Text = Path.Combine(folder, DefaultFileName(d, d.Name));
            if (!string.IsNullOrEmpty(_project.Text) && Directory.Exists(_project.Text)) _toProject.Checked = true;

            _scale.SelectedIndexChanged += (a, b) => UpdateAll();
            _limit.CheckedChanged += (a, b) => UpdateAll();
            _name.TextChanged += (a, b) => UpdateAll();
            _project.TextChanged += (a, b) => UpdateAll();
            _toFile.CheckedChanged += (a, b) => UpdateAll();
            browse.Click += (a, b) =>
            {
                using (var sfd = new SaveFileDialog { Filter = "PNG picture|*.png", FileName = Path.GetFileName(_file.Text) })
                {
                    try { sfd.InitialDirectory = Path.GetDirectoryName(_file.Text); } catch { }
                    if (sfd.ShowDialog(this) == DialogResult.OK) { _file.Text = sfd.FileName; _toFile.Checked = true; }
                }
            };
            browseP.Click += (a, b) =>
            {
                using (var fbd = new FolderBrowserDialog { Description = "Choose your game's project folder (the one with the Graphics folder in it)", SelectedPath = _project.Text })
                    if (fbd.ShowDialog(this) == DialogResult.OK) { _project.Text = fbd.SelectedPath; _toProject.Checked = true; }
            };
            ok.Click += (a, b) => DoExport();

            Theme.Apply(this);
            UpdateAll();
        }

        int ExportScale { get { return _scale.SelectedIndex + 1; } }

        /// <summary>The file name PokeCreator and Essentials look for.</summary>
        public static string DefaultFileName(SpriteDocument d, string name)
        {
            switch (d.Kind)
            {
                case SpriteKind.Back: return name + "_back.png";
                case SpriteKind.Overworld:
                    return string.Equals(d.Sheet, "Walk", StringComparison.OrdinalIgnoreCase) ? "trainer_" + name + ".png" : name + "_" + d.Sheet.ToLowerInvariant() + ".png";
                default: return name + ".png";
            }
        }

        public static string ProjectSubfolder(SpriteDocument d) { return d.Kind == SpriteKind.Overworld ? Path.Combine("Graphics", "Characters") : Path.Combine("Graphics", "Trainers"); }

        string TargetPath()
        {
            if (_toFile.Checked) return _file.Text.Trim();
            var name = string.IsNullOrWhiteSpace(_name.Text) ? D.Name : _name.Text.Trim().Replace(' ', '_');
            return Path.Combine(_project.Text.Trim(), ProjectSubfolder(D), DefaultFileName(D, name));
        }

        void UpdateAll()
        {
            var d = D;
            var sheet = d.BuildSheet(_limit.Checked, 1);
            _preview.Image = sheet;
            _preview.Caption = "Preview";
            _preview.Invalidate();
            int colours = sheet.Colors().Count;
            _info.Text = d.Description + "\r\nExport: " + d.Layout.SheetW * ExportScale + " × " + d.Layout.SheetH * ExportScale + " px, " + colours + " colour" + (colours == 1 ? "" : "s") +
                (colours > d.Style.MaxColors ? " (more than " + d.Style.Name + " allows)" : "");
            _file.Enabled = _toFile.Checked;
            _project.Enabled = _name.Enabled = _toProject.Checked;
            if (_toProject.Checked)
            {
                bool looks = Directory.Exists(_project.Text) && (Directory.Exists(Path.Combine(_project.Text, "Graphics")) || File.Exists(Path.Combine(_project.Text, "project.json")) || File.Exists(Path.Combine(_project.Text, "Game.rxproj")));
                _target.Text = "Saves as " + Path.Combine(ProjectSubfolder(d), DefaultFileName(d, string.IsNullOrWhiteSpace(_name.Text) ? d.Name : _name.Text.Trim())) +
                    (Directory.Exists(_project.Text) && !looks ? "\r\nThat folder has no Graphics folder - is it the game's project folder?" : "");
                _target.ForeColor = Directory.Exists(_project.Text) && !looks ? Theme.Warn : Theme.SubText;
            }
            else { _target.Text = ""; }
        }

        void DoExport()
        {
            var path = TargetPath();
            if (_toProject.Checked && !Directory.Exists(_project.Text.Trim())) { MessageBox.Show(this, "Choose the game's project folder first.", Text); return; }
            if (string.IsNullOrEmpty(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) { MessageBox.Show(this, "Choose where to save the picture.", Text); return; }
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            if (File.Exists(path) && MessageBox.Show(this, Path.GetFileName(path) + " already exists. Replace it?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                D.BuildSheet(_limit.Checked, ExportScale).Save(path);
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save the picture:\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            E.Settings.ExportScale = ExportScale;
            if (_toFile.Checked) E.Settings.LastExportFolder = Path.GetDirectoryName(path);
            else E.Settings.LastProjectFolder = _project.Text.Trim();
            if (_toProject.Checked && !string.IsNullOrWhiteSpace(_name.Text) && _name.Text.Trim() != D.Name) { D.Name = _name.Text.Trim(); D.Dirty = true; }
            ExportedPath = path;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
