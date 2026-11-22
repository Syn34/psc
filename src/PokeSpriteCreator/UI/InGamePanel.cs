using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// The side panel for a trainer's in-game walking sprites (a 4 × 4 sheet: rows Down, Left, Right, Up).
    /// A trainer front sprite keeps its walking sprites in the same file: upload a sheet (or start blank),
    /// click a frame to edit it on the big canvas, and copy a change from one frame to the others.
    /// For an overworld sheet opened on its own the panel works on that sheet.
    /// </summary>
    public class InGamePanel : Panel
    {
        readonly Editor E;
        readonly Label _status, _copyInfo;
        readonly Panel _empty, _set, _modeBar;
        readonly CheckBox _mainBtn, _setBtn;
        readonly WalkView _walk;
        readonly Button _copySteps, _copyAll, _upload, _export;
        readonly Timer _timer;
        readonly ToolTip _tip = new ToolTip();
        bool _syncing;

        public InGamePanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(8, 6, 8, 6);

            var title = new Label { Text = "In-game sprites", Font = Theme.Heading, Dock = DockStyle.Top, Height = 24, Tag = "accent" };
            _status = new Label { Dock = DockStyle.Top, Height = 34, Tag = "sub", Font = Theme.Small };

            // switch between the main sprite and its walking sprites
            _modeBar = new Panel { Dock = DockStyle.Top, Height = 34 };
            _mainBtn = new CheckBox { Text = "Main sprite", Appearance = Appearance.Button, Left = 0, Top = 2, Width = 148, Height = 28, TextAlign = ContentAlignment.MiddleCenter };
            _setBtn = new CheckBox { Text = "Walking sprites", Appearance = Appearance.Button, Left = 152, Top = 2, Width = 148, Height = 28, TextAlign = ContentAlignment.MiddleCenter };
            _modeBar.Controls.AddRange(new Control[] { _mainBtn, _setBtn });

            // no walking sprites yet
            _empty = new Panel { Dock = DockStyle.Fill };
            var emptyText = new Label { Left = 0, Top = 8, Width = 300, Height = 92, Font = Theme.UiFont,
                Text = "Add the trainer's walking sprites: a 4 × 4 sheet with rows Down, Left, Right, Up and the walking steps across " +
                       "(the overworld picture used in Graphics/Characters). They're saved with this sprite." };
            var uploadNew = new Button { Text = "Upload walking sheet…", Left = 0, Top = 104, Width = 180, Height = 30 };
            var blankNew = new Button { Text = "Start blank", Left = 186, Top = 104, Width = 112, Height = 30 };
            _empty.Controls.AddRange(new Control[] { emptyText, uploadNew, blankNew });

            // the walking sprites
            _set = new Panel { Dock = DockStyle.Fill };
            _walk = new WalkView { Left = 0, Top = 0, Width = 302, Height = 420 };
            _set.Resize += (s2, a2) => { _walk.Width = Math.Max(200, _set.ClientSize.Width - 2); };
            var copyLbl = new Label { Text = "Copy my change on this frame to:", Left = 0, Top = 426, Width = 300, Height = 18, Font = Theme.Bold };
            _copySteps = new Button { Text = "Its walking steps", Left = 0, Top = 446, Width = 146, Height = 28 };
            _copyAll = new Button { Text = "All directions", Left = 152, Top = 446, Width = 146, Height = 28 };
            _copyInfo = new Label { Left = 0, Top = 478, Width = 300, Height = 44, Tag = "sub", Font = Theme.Small };
            _upload = new Button { Text = "Replace sheet…", Left = 0, Top = 526, Width = 146, Height = 28 };
            _export = new Button { Text = "Export sheet…", Left = 152, Top = 526, Width = 146, Height = 28 };
            _tip.SetToolTip(_copySteps, "Copies what you changed on the selected frame (since you picked it) to the other frames facing the same way.");
            _tip.SetToolTip(_copyAll, "Copies what you changed on the selected frame to every other frame. Recolours carry over exactly; drawn pixels are lined up with each frame (and mirrored between left and right).");
            _set.Controls.AddRange(new Control[] { _walk, copyLbl, _copySteps, _copyAll, _copyInfo, _upload, _export });

            Controls.Add(_set);
            Controls.Add(_empty);
            Controls.Add(_modeBar);
            Controls.Add(_status);
            Controls.Add(title);

            _mainBtn.Click += (s, a) => { var r = E.Root; if (r != null) E.EditPart(r); Sync(); };
            _setBtn.Click += (s, a) => { var r = E.Root; if (r != null && r.Overworld != null) E.EditPart(r.Overworld, E.Doc == r.Overworld ? E.Frame : 0); Sync(); };
            uploadNew.Click += (s, a) => Upload();
            blankNew.Click += (s, a) => StartBlank();
            _upload.Click += (s, a) => Upload();
            _export.Click += (s, a) => Export();
            _copySteps.Click += (s, a) => { E.Say(E.CopyChange(false)); UpdateCopyInfo(); };
            _copyAll.Click += (s, a) => { E.Say(E.CopyChange(true)); UpdateCopyInfo(); };
            _walk.FramePicked += f => { var o = SetDoc; if (o != null) { E.EditPart(o, f); Sync(); } };

            _timer = new Timer { Interval = 170 };
            _timer.Tick += (s, a) => { _walk.Step++; _walk.Invalidate(); };
            _timer.Start();

            E.PartSwitched += Sync;
            E.LayersChanged += Sync;
            E.DocChanged += () => { _walk.Invalidate(); UpdateCopyInfo(); };
            E.FrameChanged += () => { _walk.Invalidate(); UpdateCopyInfo(); };
            E.SelectionChanged += UpdateCopyInfo;
        }

        public void Stop() { _timer.Stop(); }

        /// <summary>The walking-sprite sheet this panel works on (the front sprite's, or the overworld sheet itself).</summary>
        SpriteDocument SetDoc
        {
            get
            {
                var r = E.Root;
                if (r == null) return null;
                return r.Kind == SpriteKind.Overworld ? r : r.Kind == SpriteKind.Front ? r.Overworld : null;
            }
        }

        void Sync()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                var r = E.Root;
                var set = SetDoc;
                bool front = r != null && r.Kind == SpriteKind.Front;
                _modeBar.Visible = front && set != null;
                _empty.Visible = front && set == null;
                _set.Visible = set != null;
                _mainBtn.Checked = E.Doc == r;
                _setBtn.Checked = set != null && E.Doc == set;
                _walk.Doc = set;
                _walk.Editor = E;
                bool editingSet = set != null && E.Doc == set;
                _copySteps.Enabled = _copyAll.Enabled = editingSet;
                _upload.Visible = front;
                if (r == null) _status.Text = "";
                else if (r.Kind == SpriteKind.Back) _status.Text = "Walking sprites go with a trainer front sprite. Open or make a front sprite to add them.";
                else if (r.Kind == SpriteKind.Overworld) _status.Text = "Click a frame to edit it. Copy a change to the other frames below.";
                else if (set == null) _status.Text = "";
                else _status.Text = editingSet ? "Editing the walking sprites. Click a frame to edit it." : "Click a frame (or Walking sprites) to edit them on the big canvas.";
                _walk.Invalidate();
                UpdateCopyInfo();
            }
            finally { _syncing = false; }
        }

        void UpdateCopyInfo()
        {
            var set = SetDoc;
            if (set == null || E.Doc != set) { _copyInfo.Text = "Pick a frame first, then change it."; return; }
            var ch = E.PendingChange;
            string where = SpriteCanvas.FrameLabel(set, E.Frame);
            _copyInfo.Text = ch == null ? "Select a layer and frame to edit." :
                             ch.IsEmpty ? where + ": no change yet. Draw or recolour on it, then copy." :
                             where + ": " + ch.Describe() + ".";
        }

        /// <summary>Picks a 4 × 4 sheet picture and uses it as the front sprite's walking sprites.</summary>
        void Upload()
        {
            var r = E.Root;
            if (r == null || r.Kind != SpriteKind.Front) return;
            if (r.Overworld != null && MessageBox.Show(FindForm(), "Replace the walking sprites with a new sheet? (Undo won't bring the old ones back.)", "Replace sheet", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            using (var ofd = new OpenFileDialog { Filter = ImageImport.Filter, Title = "Upload the walking sprites (a 4 × 4 sheet)" })
            {
                if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
                List<PixelImage> pics;
                try { pics = ImageImport.LoadFrames(ofd.FileName); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Couldn't read that picture:\r\n" + ex.Message, "Upload", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                string note;
                var lay = ImageImport.Suggest(pics, SpriteKind.Overworld, r.Style, out note);
                var p0 = pics[0];
                if (!(p0.W % 4 == 0 && p0.H % 4 == 0 && p0.W >= 16 && p0.H >= 16))
                {
                    MessageBox.Show(FindForm(), "That picture isn't a 4 × 4 sheet (its width and height must split into 4 frames each).", "Upload", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var o = r.MakeOverworld(lay, ImageImport.FramesFor(pics, lay));
                E.EditPart(o, 0);
                E.PartsChanged();
                E.Say("Walking sprites added from " + Path.GetFileName(ofd.FileName) + " (" + note.Trim() + "). Click a frame to edit it; they're saved with this sprite.");
            }
        }

        void StartBlank()
        {
            var r = E.Root;
            if (r == null || r.Kind != SpriteKind.Front) return;
            var o = r.MakeOverworld(null, null);
            E.EditPart(o, 0);
            E.PartsChanged();
            E.Say("Blank walking sprites (" + o.Layout.FrameW + "×" + o.Layout.FrameH + " frames). Draw the down-facing frame first, then copy it on.");
        }

        void Export()
        {
            var set = SetDoc;
            if (set == null) return;
            using (var d = new ExportDialog(E, set))
                if (d.ShowDialog(FindForm()) == DialogResult.OK) E.Say("Exported " + d.ExportedPath);
        }

        public void ApplyTheme() { _walk.Invalidate(); Sync(); }
    }

    /// <summary>Four walking previews (Down, Left, Right, Up) on grass, plus the whole sheet; click a frame to edit it.</summary>
    public class WalkView : Control
    {
        public SpriteDocument Doc;
        public Editor Editor;
        public int Step;
        public event Action<int> FramePicked;
        static readonly string[] Names = { "Down", "Left", "Right", "Up" };
        const int CellH = 96;

        public WalkView() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; }

        Rectangle DirRect(int d)
        {
            int cellW = (Width - 10) / 2;
            return new Rectangle((d % 2) * (cellW + 10), (d / 2) * (CellH + 20) + 16, cellW, CellH);
        }

        int SheetTop { get { return 2 * (CellH + 20) + 18; } }

        Rectangle SheetRect(PixelImage sheet)
        {
            int sy = SheetTop;
            int ks = Math.Max(1, Math.Min(2, Math.Min(Width / Math.Max(1, sheet.W), (Height - sy) / Math.Max(1, sheet.H))));
            var r = new Rectangle(0, sy, sheet.W * ks, sheet.H * ks);
            return r.Height > Height - sy ? Pix.Fit(sheet.W, sheet.H, new Rectangle(0, sy, Width, Height - sy), false) : r;
        }

        List<PixelImage> Frames()
        {
            if (Doc == null) return null;
            return Editor != null && Editor.Doc == Doc ? Editor.Preview : Doc.FinalFrames(Doc.LimitPalette);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            var frames = Frames();
            if (Doc == null || frames == null || frames.Count < Doc.FrameCount) return;
            int cols = Doc.Layout.Cols;
            bool editing = Editor != null && Editor.Doc == Doc;
            for (int d = 0; d < Math.Min(4, Doc.Layout.Rows); d++)
            {
                var r = DirRect(d);
                TextRenderer.DrawText(g, Names[d], Theme.Small, new Point(r.X, r.Y - 16), editing && Editor.Frame / cols == d ? Theme.Accent : Theme.SubText);
                Grass(g, r);
                var f = frames[d * cols + Step % cols];
                int k = Math.Max(1, Math.Min((r.Width - 8) / f.W, (r.Height - 8) / f.H));
                Pix.Draw(g, f, new Rectangle(r.X + (r.Width - f.W * k) / 2, r.Y + (r.Height - f.H * k) / 2, f.W * k, f.H * k));
            }
            TextRenderer.DrawText(g, "Sheet (click a frame to edit it)", Theme.Small, new Point(0, SheetTop - 16), Theme.SubText);
            var sheet = SpriteDocument.MakeSheet(frames, Doc.Layout, 1);
            var sr = SheetRect(sheet);
            Pix.Checker(g, sr, 4);
            Pix.Draw(g, sheet, sr);
            if (editing)
            {
                double kx = sr.Width / (double)sheet.W, ky = sr.Height / (double)sheet.H;
                int i = Editor.Frame;
                var fr = new Rectangle(sr.X + (int)(Doc.Layout.FrameX(i) * kx), sr.Y + (int)(Doc.Layout.FrameY(i) * ky), (int)(Doc.Layout.FrameW * kx), (int)(Doc.Layout.FrameH * ky));
                using (var p = new Pen(Theme.Accent, 2)) g.DrawRectangle(p, fr.X, fr.Y, fr.Width - 1, fr.Height - 1);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Doc == null || FramePicked == null) return;
            int cols = Doc.Layout.Cols;
            for (int d = 0; d < Math.Min(4, Doc.Layout.Rows); d++)
                if (DirRect(d).Contains(e.Location)) { FramePicked(d * cols); return; }
            var frames = Frames();
            if (frames == null) return;
            var sheet = SpriteDocument.MakeSheet(frames, Doc.Layout, 1);
            var sr = SheetRect(sheet);
            if (!sr.Contains(e.Location)) return;
            int fx = (int)((e.X - sr.X) / (sr.Width / (double)sheet.W)) / Doc.Layout.FrameW;
            int fy = (int)((e.Y - sr.Y) / (sr.Height / (double)sheet.H)) / Doc.Layout.FrameH;
            int idx = fy * cols + fx;
            if (idx >= 0 && idx < Doc.FrameCount) FramePicked(idx);
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
