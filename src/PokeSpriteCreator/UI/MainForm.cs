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
    public class MainForm : Form
    {
        public const string AppName = "Poke Sprite Creator";
        public const string Version = "0.2.0";

        readonly Editor E;
        readonly SpriteCanvas _canvas;
        readonly DirectionsPanel _dirs;
        readonly LayersPanel _layers;
        readonly ColorPanel _colors;
        readonly FrameStrip _frames;
        readonly MenuStrip _menu;
        readonly ToolStrip _tools;
        readonly StatusStrip _status;
        readonly ToolStripStatusLabel _msg, _pos, _zoomLbl, _docLbl;
        readonly Dictionary<Tool, ToolStripButton> _toolButtons = new Dictionary<Tool, ToolStripButton>();
        ToolStripButton _mirrorBtn, _mergedBtn, _gridBtn;
        ToolStripMenuItem _recentMenu, _undoItem, _redoItem, _darkItem, _gridItem, _limitItem, _onionItem, _framesMenu;
        readonly string _openOnStart;

        public MainForm(Settings settings, string openFile)
        {
            _openOnStart = openFile;
            Theme.Dark = settings.DarkMode;
            E = new Editor(settings);

            Text = AppName;
            Font = Theme.UiFont;
            Size = new Size(1360, 860);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            _canvas = new SpriteCanvas(E) { Dock = DockStyle.Fill };
            _frames = new FrameStrip(E) { Dock = DockStyle.Bottom, Visible = false };
            var center = new Panel { Dock = DockStyle.Fill, Tag = "keep" };
            center.Controls.Add(_canvas);
            center.Controls.Add(_frames);

            _dirs = new DirectionsPanel(E) { Dock = DockStyle.Right, Width = 318 };
            var left = new Panel { Dock = DockStyle.Left, Width = 318, Tag = "panel" };
            _layers = new LayersPanel(E) { Dock = DockStyle.Fill };
            _colors = new ColorPanel(E) { Dock = DockStyle.Bottom };
            left.Controls.Add(_layers);
            left.Controls.Add(_colors);
            _dirs.EditRequested += EditAsOverworld;

            var splitL = new Splitter { Dock = DockStyle.Left, Width = 4 };
            var splitR = new Splitter { Dock = DockStyle.Right, Width = 4 };

            _menu = BuildMenu();
            _tools = BuildToolbar();
            _status = new StatusStrip { SizingGrip = true };
            _msg = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready" };
            _docLbl = new ToolStripStatusLabel { BorderSides = ToolStripStatusLabelBorderSides.Left };
            _pos = new ToolStripStatusLabel { Width = 170, AutoSize = false, BorderSides = ToolStripStatusLabelBorderSides.Left };
            _zoomLbl = new ToolStripStatusLabel { Width = 70, AutoSize = false, BorderSides = ToolStripStatusLabelBorderSides.Left };
            _status.Items.AddRange(new ToolStripItem[] { _msg, _docLbl, _pos, _zoomLbl });

            Controls.Add(center);
            Controls.Add(splitR);
            Controls.Add(_dirs);
            Controls.Add(splitL);
            Controls.Add(left);
            Controls.Add(_tools);
            Controls.Add(_menu);
            Controls.Add(_status);
            MainMenuStrip = _menu;

            E.Status += m => _msg.Text = m;
            E.DocChanged += UpdateTitle;
            E.LayersChanged += () => { UpdateTitle(); _frames.Visible = E.Doc != null && E.Doc.FrameCount > 1; UpdateMenus(); };
            E.ToolChanged += SyncToolButtons;
            _canvas.HoverChanged += (p, c) => _pos.Text = p.HasValue ? p.Value.X + ", " + p.Value.Y + "   " + PixelImage.Hex(c) : "";
            _canvas.ZoomChanged += () => _zoomLbl.Text = _canvas.Zoom * 100 + "%";

            ApplyTheme();
            SyncToolButtons();
            NewBlank(GenStyle.Get(settings.LastGen), SpriteKind.Front);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _canvas.FitToWindow();
            if (!string.IsNullOrEmpty(_openOnStart)) OpenFile(_openOnStart);
            else if (Environment.GetEnvironmentVariable("PSC_NO_NEW_DIALOG") == null) BeginInvoke((Action)(() => NewSprite()));
            E.Say("Draw a sprite or bring in a picture (File > Import picture). Save a trainer front sprite to see it walking in game.");
        }

        // ------------------------------------------------------------------ menu & toolbar

        MenuStrip BuildMenu()
        {
            var m = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Item("&New sprite…", Keys.Control | Keys.N, () => NewSprite()));
            file.DropDownItems.Add(Item("&Open…", Keys.Control | Keys.O, () => Open()));
            file.DropDownItems.Add(Item("&Import picture as a new sprite…", Keys.Control | Keys.I, () => ImportAsNew()));
            file.DropDownItems.Add(Item("Import picture as a &layer…", Keys.Control | Keys.Shift | Keys.I, () => ImportAsLayer()));
            _recentMenu = new ToolStripMenuItem("Open &recent");
            file.DropDownItems.Add(_recentMenu);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("&Save", Keys.Control | Keys.S, () => Save(false)));
            file.DropDownItems.Add(Item("Save &as…", Keys.Control | Keys.Shift | Keys.S, () => Save(true)));
            file.DropDownItems.Add(Item("&Export PNG…", Keys.Control | Keys.E, () => Export()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("E&xit", Keys.Alt | Keys.F4, () => Close()));
            file.DropDownOpening += (s, a) => FillRecent();

            var edit = new ToolStripMenuItem("&Edit");
            _undoItem = Item("&Undo", Keys.Control | Keys.Z, () => E.Undo());
            _redoItem = Item("&Redo", Keys.Control | Keys.Y, () => E.Redo());
            edit.DropDownItems.Add(_undoItem);
            edit.DropDownItems.Add(_redoItem);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("&Copy frame", Keys.Control | Keys.C, () => E.CopyFrame()));
            edit.DropDownItems.Add(Item("&Paste frame", Keys.Control | Keys.V, () => E.PasteFrame()));
            edit.DropDownItems.Add(Item("Clear frame (paint layer)", Keys.Delete, () => E.ClearFrame()));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Sprite &name…", Keys.None, () => RenameSprite()));
            edit.DropDownOpening += (s, a) => { _undoItem.Enabled = E.CanUndo; _redoItem.Enabled = E.CanRedo; };

            var layer = new ToolStripMenuItem("&Layer");
            layer.DropDownItems.Add(Item("&New layer", Keys.Control | Keys.Shift | Keys.N, () => E.AddLayer()));
            layer.DropDownItems.Add(Item("&Duplicate", Keys.Control | Keys.J, () => E.DuplicateLayer()));
            layer.DropDownItems.Add(Item("&Rename…", Keys.F2, () => _layers.Rename()));
            layer.DropDownItems.Add(Item("Delete", Keys.None, () => E.DeleteLayer()));
            layer.DropDownItems.Add(new ToolStripSeparator());
            layer.DropDownItems.Add(Item("Move &up", Keys.Control | Keys.OemCloseBrackets, () => E.MoveLayer(1)));
            layer.DropDownItems.Add(Item("Move do&wn", Keys.Control | Keys.OemOpenBrackets, () => E.MoveLayer(-1)));
            layer.DropDownItems.Add(new ToolStripSeparator());
            layer.DropDownItems.Add(Item("&Merge down", Keys.Control | Keys.M, () => E.MergeDown()));

            _framesMenu = new ToolStripMenuItem("F&rames");
            _framesMenu.DropDownItems.Add(Item("&Next frame", Keys.Control | Keys.Right, () => { if (E.Doc != null) E.SelectFrame((E.Frame + 1) % E.Doc.FrameCount); }));
            _framesMenu.DropDownItems.Add(Item("&Previous frame", Keys.Control | Keys.Left, () => { if (E.Doc != null) E.SelectFrame((E.Frame + E.Doc.FrameCount - 1) % E.Doc.FrameCount); }));
            _framesMenu.DropDownItems.Add(new ToolStripSeparator());
            _framesMenu.DropDownItems.Add(Item("Copy frame to &all frames", Keys.None, () => E.CopyToAllFrames()));
            _framesMenu.DropDownItems.Add(Item("Mirror &Left → Right (overworld)", Keys.None, () => E.MirrorRows(true)));
            _framesMenu.DropDownItems.Add(Item("Mirror &Right → Left (overworld)", Keys.None, () => E.MirrorRows(false)));
            _framesMenu.DropDownItems.Add(new ToolStripSeparator());
            _framesMenu.DropDownItems.Add(Item("Add frame (back sprite)", Keys.None, () => { if (E.Doc != null) E.SetFrameCount(E.Doc.FrameCount + 1); }));
            _framesMenu.DropDownItems.Add(Item("Remove last frame (back sprite)", Keys.None, () => { if (E.Doc != null && E.Doc.FrameCount > 1) E.SetFrameCount(E.Doc.FrameCount - 1); }));
            _onionItem = Item("&Onion skin", Keys.None, () => { E.OnionSkin = !E.OnionSkin; _onionItem.Checked = E.OnionSkin; E.Changed(); });
            _framesMenu.DropDownItems.Add(_onionItem);

            var view = new ToolStripMenuItem("&View");
            view.DropDownItems.Add(Item("Zoom &in", Keys.Control | Keys.Oemplus, () => _canvas.ZoomBy(1)));
            view.DropDownItems.Add(Item("Zoom &out", Keys.Control | Keys.OemMinus, () => _canvas.ZoomBy(-1)));
            view.DropDownItems.Add(Item("&Fit to window", Keys.Control | Keys.D0, () => _canvas.FitToWindow()));
            view.DropDownItems.Add(new ToolStripSeparator());
            _gridItem = Item("Pixel &grid", Keys.Control | Keys.G, () => ToggleGrid());
            _limitItem = Item("Show with the generation's &colour limit", Keys.None, () => { if (E.Doc == null) return; E.Doc.LimitPalette = !E.Doc.LimitPalette; E.InvalidatePreview(); E.LayersEdited(); });
            _darkItem = Item("&Dark mode", Keys.None, () => { Theme.Dark = !Theme.Dark; E.Settings.DarkMode = Theme.Dark; ApplyTheme(); });
            view.DropDownItems.Add(_gridItem);
            view.DropDownItems.Add(_limitItem);
            view.DropDownItems.Add(_darkItem);
            view.DropDownOpening += (s, a) => UpdateMenus();

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Item("&How it works", Keys.F1, () => ShowText("How it works", HowItWorks)));
            help.DropDownItems.Add(Item("&Keyboard shortcuts", Keys.None, () => ShowText("Keyboard shortcuts", Shortcuts)));
            help.DropDownItems.Add(Item("&About", Keys.None, () => MessageBox.Show(this, AppName + " " + Version + "\r\nA sprite editor for Pokémon fan games (trainer front & back sprites and overworld sheets, Gen 1 to Gen 5 styles). Saving a trainer front sprite makes its overworld walking sprite.\r\nCompanion app for PokeCreator.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information)));

            m.Items.AddRange(new ToolStripItem[] { file, edit, layer, _framesMenu, view, help });
            return m;
        }

        const string HowItWorks =
@"POKE SPRITE CREATOR

1. MAKE OR BRING IN A SPRITE
   File > New sprite: pick the style (Gen 1 to Gen 5) and the kind of sprite
   (trainer front, trainer back or overworld sheet). Start from a blank canvas
   or a picture (PNG, GIF or BMP).
   File > Import picture as a new sprite does the same with the picture chosen.
   File > Import picture as a layer adds a picture to the sprite you're editing.
   Pictures are fitted to the canvas: empty space is trimmed and the feet are
   kept on the bottom row; pixel-doubled art is shrunk; animated GIFs and strips
   become frames of a back sprite; a 4 x 4 sheet becomes an overworld sheet.

2. EDIT IT
   Pencil, eraser, fill, colour picker, line, rectangle, ellipse, move,
   recolour and mirror drawing, on as many layers as you like.
   ""Limit to 15 colours"" shows the sprite the way the games would.

3. SEE IT IN GAME
   When you save a trainer front sprite, the panel on the right makes its
   overworld walking sprite: facing down, left, right and up, with walking
   steps, in the style's overworld size (Gen 1-2: 16x16, Gen 3: 16x32,
   Gen 4-5: 32x32). The head keeps the trainer's hair shape and the body
   uses the trainer's own colours (hair, skin, shirt, trousers, shoes and a
   headband or visor if there is one).
   * Symmetric front and back: both sides of the down and up views match.
   * Up view hair from a back sprite: uses the back sprite's hair shape.
   * Export sheet: saves the 4 x 4 sheet (trainer_NAME.png for
     Graphics/Characters).
   * Edit as overworld sprite: opens it as its own sheet to touch up.
   For an overworld sheet the panel plays the sheet itself.

4. EXPORT
   File > Export PNG saves the sprite at 1x to 4x, to any file or straight into
   a PokeCreator / Essentials game folder with the right file name.";

        const string Shortcuts =
@"TOOLS
  B  Pencil            E  Eraser           G  Fill
  I  Colour picker     L  Line             R  Rectangle
  O  Ellipse           M  Move             C  Recolour
  X  Swap colours      S  Mirror drawing on/off

DRAWING
  Left mouse = left colour, right mouse = right colour
  Alt + click         pick a colour with any drawing tool
  Shift (Rectangle / Ellipse)   filled shape
  Shift (Line)        straight / 45° lines
  Shift (Recolour)    change the colour on every layer
  Alt (Move, paint layer)  move only the current frame
  Arrow keys          nudge the selected layer 1 pixel

VIEW
  Ctrl + mouse wheel  zoom      Middle mouse / Space + drag  pan
  Ctrl+= / Ctrl+-     zoom      Ctrl+0  fit       Ctrl+G  grid

FRAMES
  Ctrl+Left / Ctrl+Right   previous / next frame
  Ctrl+C / Ctrl+V          copy / paste frame

FILE
  Ctrl+N new   Ctrl+O open   Ctrl+I import picture   Ctrl+S save   Ctrl+E export";

        ToolStripMenuItem Item(string text, Keys keys, Action a)
        {
            var i = new ToolStripMenuItem(text);
            if (keys != Keys.None)
            {
                i.ShortcutKeys = keys;
                // friendlier names than "Oemplus" / "OemOpenBrackets"
                string k = null;
                switch (keys & Keys.KeyCode)
                {
                    case Keys.Oemplus: k = "="; break;
                    case Keys.OemMinus: k = "-"; break;
                    case Keys.D0: k = "0"; break;
                    case Keys.OemOpenBrackets: k = "["; break;
                    case Keys.OemCloseBrackets: k = "]"; break;
                    case Keys.Left: k = "Left"; break;
                    case Keys.Right: k = "Right"; break;
                }
                if (k != null) i.ShortcutKeyDisplayString = ((keys & Keys.Control) != 0 ? "Ctrl+" : "") + ((keys & Keys.Shift) != 0 ? "Shift+" : "") + k;
            }
            i.Click += (s, e) => a();
            return i;
        }

        ToolStrip BuildToolbar()
        {
            var t = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2), ImageScalingSize = new Size(16, 16) };
            t.Items.Add(TextBtn("New", "New sprite (Ctrl+N)", () => NewSprite()));
            t.Items.Add(TextBtn("Open", "Open a sprite (Ctrl+O)", () => Open()));
            t.Items.Add(TextBtn("Save", "Save (Ctrl+S)", () => Save(false)));
            t.Items.Add(TextBtn("Export", "Export PNG (Ctrl+E)", () => Export()));
            t.Items.Add(new ToolStripSeparator());
            AddTool(t, Tool.Pencil, "Pencil", "B");
            AddTool(t, Tool.Eraser, "Eraser", "E");
            AddTool(t, Tool.Fill, "Fill", "G");
            AddTool(t, Tool.Picker, "Picker", "I");
            AddTool(t, Tool.Line, "Line", "L");
            AddTool(t, Tool.Rectangle, "Rectangle", "R");
            AddTool(t, Tool.Ellipse, "Ellipse", "O");
            AddTool(t, Tool.Move, "Move", "M");
            AddTool(t, Tool.Recolor, "Recolor", "C");
            t.Items.Add(new ToolStripSeparator());
            _mirrorBtn = new ToolStripButton("Mirror") { CheckOnClick = true, Image = Icons.Get("Mirror"), DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ToolTipText = "Mirror drawing left/right (S)" };
            _mirrorBtn.CheckedChanged += (s, a) => { E.Mirror = _mirrorBtn.Checked; _canvas.Invalidate(); };
            _mergedBtn = new ToolStripButton("Fill sees all layers") { CheckOnClick = true, Checked = true, ToolTipText = "On: Fill stops at the outlines of everything you can see (colour in line art on another layer). Off: Fill only looks at the selected layer." };
            _mergedBtn.CheckedChanged += (s, a) => E.SampleMerged = _mergedBtn.Checked;
            _gridBtn = new ToolStripButton("Grid") { CheckOnClick = true, Checked = E.Settings.ShowGrid, ToolTipText = "Pixel grid (Ctrl+G)" };
            _gridBtn.CheckedChanged += (s, a) => { E.Settings.ShowGrid = _gridBtn.Checked; _canvas.Invalidate(); };
            t.Items.Add(_mirrorBtn);
            t.Items.Add(_mergedBtn);
            t.Items.Add(_gridBtn);
            t.Items.Add(new ToolStripSeparator());
            t.Items.Add(TextBtn("−", "Zoom out (Ctrl+-)", () => _canvas.ZoomBy(-1)));
            t.Items.Add(TextBtn("+", "Zoom in (Ctrl+=)", () => _canvas.ZoomBy(1)));
            t.Items.Add(TextBtn("Fit", "Fit to window (Ctrl+0)", () => _canvas.FitToWindow()));
            return t;
        }

        ToolStripButton TextBtn(string text, string tip, Action a)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += (s, e) => a();
            return b;
        }

        static readonly Dictionary<Tool, string> ToolHelp = new Dictionary<Tool, string>
        {
            { Tool.Pencil, "Pencil: draw pixels (left/right mouse = your two colours). Alt+click picks a colour." },
            { Tool.Eraser, "Eraser: clears pixels on the selected paint layer." },
            { Tool.Fill, "Fill: fills an area. With \"Fill sees all layers\" it stops at outlines on any layer, so you can colour in line art." },
            { Tool.Picker, "Colour picker: click to take a colour from the sprite." },
            { Tool.Line, "Line: drag to draw a line (Shift = straight / 45°)." },
            { Tool.Rectangle, "Rectangle: drag to draw (Shift = filled)." },
            { Tool.Ellipse, "Ellipse: drag to draw (Shift = filled)." },
            { Tool.Move, "Move: drag the selected layer's pixels (Alt: this frame only). Arrow keys nudge 1 pixel." },
            { Tool.Recolor, "Recolour: click a colour to change it to your left colour on the selected layer. Shift+click changes it on every layer." },
        };

        void AddTool(ToolStrip t, Tool tool, string name, string key)
        {
            var b = new ToolStripButton(tool == Tool.Recolor ? "Recolour" : name) { Image = Icons.Get(name), DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = (tool == Tool.Recolor ? "Recolour" : name) + " (" + key + ")" };
            b.Click += (s, e) => E.SetTool(tool);
            _toolButtons[tool] = b;
            t.Items.Add(b);
        }

        void SyncToolButtons()
        {
            foreach (var kv in _toolButtons) kv.Value.Checked = kv.Key == E.Tool;
            string h;
            if (ToolHelp.TryGetValue(E.Tool, out h)) E.Say(h);
        }

        void ToggleGrid() { _gridBtn.Checked = !_gridBtn.Checked; }

        void UpdateMenus()
        {
            if (_gridItem != null) _gridItem.Checked = E.Settings.ShowGrid;
            if (_darkItem != null) _darkItem.Checked = Theme.Dark;
            if (_limitItem != null) _limitItem.Checked = E.Doc != null && E.Doc.LimitPalette;
            if (_onionItem != null) _onionItem.Checked = E.OnionSkin;
        }

        void FillRecent()
        {
            _recentMenu.DropDownItems.Clear();
            foreach (var p in E.Settings.Recent.Where(File.Exists))
            {
                var path = p;
                _recentMenu.DropDownItems.Add(path, null, (s, a) => { if (ConfirmDiscard()) OpenFile(path); });
            }
            _recentMenu.Enabled = _recentMenu.DropDownItems.Count > 0;
        }

        void ApplyTheme()
        {
            BackColor = Theme.Panel;
            Theme.Apply(this);
            foreach (var kv in _toolButtons) kv.Value.Image = Icons.Get(kv.Value.Text == "Recolour" ? "Recolor" : kv.Value.Text);
            if (_mirrorBtn != null) _mirrorBtn.Image = Icons.Get("Mirror");
            _dirs.ApplyTheme(); _layers.ApplyTheme(); _colors.ApplyTheme();
            _canvas.Invalidate(); _frames.Invalidate(true);
            UpdateMenus();
        }

        // ------------------------------------------------------------------ keyboard

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var focused = ActiveControlDeep(this);
            bool typing = focused is TextBoxBase || focused is NumericUpDown || focused is ComboBox || focused is UpDownBase;
            if (!typing)
            {
                switch (keyData)
                {
                    case Keys.B: E.SetTool(Tool.Pencil); return true;
                    case Keys.E: E.SetTool(Tool.Eraser); return true;
                    case Keys.G: E.SetTool(Tool.Fill); return true;
                    case Keys.I: E.SetTool(Tool.Picker); return true;
                    case Keys.L: E.SetTool(Tool.Line); return true;
                    case Keys.R: E.SetTool(Tool.Rectangle); return true;
                    case Keys.O: E.SetTool(Tool.Ellipse); return true;
                    case Keys.M: case Keys.V: E.SetTool(Tool.Move); return true;
                    case Keys.C: E.SetTool(Tool.Recolor); return true;
                    case Keys.X: E.SwapColors(); return true;
                    case Keys.S: _mirrorBtn.Checked = !_mirrorBtn.Checked; return true;
                }
                if (focused == _canvas || focused == null || focused is ListBox)
                {
                    switch (keyData)
                    {
                        case Keys.Left: _canvas.Nudge(-1, 0); return true;
                        case Keys.Right: _canvas.Nudge(1, 0); return true;
                        case Keys.Up: if (focused is ListBox) break; _canvas.Nudge(0, -1); return true;
                        case Keys.Down: if (focused is ListBox) break; _canvas.Nudge(0, 1); return true;
                    }
                }
            }
            else if (keyData == Keys.Delete || keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.V) || keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y))
                return false; // leave text editing keys to the text box
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static Control ActiveControlDeep(ContainerControl c)
        {
            Control a = c.ActiveControl;
            while (a is ContainerControl && ((ContainerControl)a).ActiveControl != null) a = ((ContainerControl)a).ActiveControl;
            return a;
        }

        // ------------------------------------------------------------------ documents

        void UpdateTitle()
        {
            Text = E.Doc == null ? AppName : E.Doc.Title + " - " + E.Doc.Name + " - " + AppName;
            _docLbl.Text = E.Doc == null ? "" : E.Doc.Description;
        }

        void NewBlank(GenStyle st, SpriteKind k)
        {
            var d = new SpriteDocument(st, k, "Walk", st.DefaultLayout(k));
            d.Layers.Add(d.NewLayer("Paint"));
            SetDoc(d);
        }

        void SetDoc(SpriteDocument d)
        {
            E.SetDocument(d);
            _frames.Visible = d.FrameCount > 1;
            if (d.Layers.Count > 0)
            {
                // start with the top paint layer selected so drawing goes on top
                E.SelectLayer(d.Layers.Count - 1);
            }
            _canvas.FitToWindow();
            UpdateTitle();
            // sensible first colours for the style
            if (d.Style.Palette.Length > 1) { E.SetPrimary(d.Style.Palette.OrderBy(c => PixelImage.Luma(c)).First()); E.SetSecondary(d.Style.Palette.OrderByDescending(c => PixelImage.Luma(c)).First()); }
        }

        bool ConfirmDiscard()
        {
            if (E.Doc == null || !E.Doc.Dirty) return true;
            var r = MessageBox.Show(this, "Save changes to " + E.Doc.Title.TrimEnd(' ', '*') + "?", AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save(false);
            return true;
        }

        void NewSprite(string picture = null)
        {
            if (!ConfirmDiscard()) return;
            using (var d = new NewSpriteDialog(E.Settings, picture))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                E.Settings.LastGen = d.Result.Style.Gen;
                E.Settings.LastKind = d.Result.Kind.ToString();
                SetDoc(d.Result);
                E.Say("New " + d.Result.Description + "." + (d.Result.Kind == SpriteKind.Front ? " Save it (Ctrl+S) to see the trainer walking in game, on the right." : ""));
            }
        }

        /// <summary>File > Import picture as a new sprite: the New sprite dialog with the picture already chosen.</summary>
        void ImportAsNew()
        {
            using (var ofd = new OpenFileDialog { Filter = ImageImport.Filter, Title = "Import a picture as a new sprite" })
                if (ofd.ShowDialog(this) == DialogResult.OK) NewSprite(ofd.FileName);
        }

        /// <summary>File > Import picture as a layer: adds the picture to the current sprite on a new layer.</summary>
        void ImportAsLayer()
        {
            if (E.Doc == null) return;
            using (var ofd = new OpenFileDialog { Filter = ImageImport.Filter, Title = "Import a picture as a layer" })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var pics = ImageImport.LoadFrames(ofd.FileName);
                    E.AddLayerFromFrames(Path.GetFileNameWithoutExtension(ofd.FileName), ImageImport.FramesFor(pics, E.Doc.Layout));
                    E.Say("Added " + Path.GetFileName(ofd.FileName) + " as a layer" + (pics.Count > 1 ? " (" + Math.Min(pics.Count, E.Doc.FrameCount) + " frames)" : "") + ". Use the Move tool to line it up.");
                }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't read that picture:\r\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        /// <summary>Directions panel > Edit as overworld sprite: the walking sprite becomes its own overworld sheet.</summary>
        void EditAsOverworld(OverworldResult r)
        {
            if (E.Doc == null || r == null || !ConfirmDiscard()) return;
            var src = E.Doc;
            var d = new SpriteDocument(src.Style, SpriteKind.Overworld, "Walk", r.Layout) { Name = src.Name, LimitPalette = src.LimitPalette };
            d.Layers.Add(d.LayerFromFrames("Walking sprite", r.Frames));
            d.Dirty = true;
            SetDoc(d);
            E.Say("This is the walking sprite as its own overworld sheet: touch it up, then save it or export it (Ctrl+E).");
        }

        void Open()
        {
            if (!ConfirmDiscard()) return;
            using (var ofd = new OpenFileDialog { Filter = "Sprite (*" + SpriteDocument.Extension + ")|*" + SpriteDocument.Extension + "|All files|*.*" })
                if (ofd.ShowDialog(this) == DialogResult.OK) OpenFile(ofd.FileName);
        }

        public void OpenFile(string path)
        {
            try
            {
                var d = SpriteDocument.Load(path);
                SetDoc(d);
                E.Settings.AddRecent(path);
                E.Say(d.Warnings.Count > 0 ? string.Join("  ", d.Warnings) : "Opened " + path);
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't open " + path + ":\r\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        bool Save(bool saveAs)
        {
            if (E.Doc == null) return false;
            string path = E.Doc.FilePath;
            if (saveAs || path == null)
            {
                using (var sfd = new SaveFileDialog { Filter = "Sprite (*" + SpriteDocument.Extension + ")|*" + SpriteDocument.Extension, FileName = (path != null ? Path.GetFileNameWithoutExtension(path) : E.Doc.Name) + SpriteDocument.Extension })
                {
                    if (sfd.ShowDialog(this) != DialogResult.OK) return false;
                    path = sfd.FileName;
                }
            }
            try
            {
                E.Doc.Save(path);
                E.Settings.AddRecent(path);
                UpdateTitle();
                E.Say("Saved " + path);
                E.NotifySaved();
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save:\r\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }

        void Export()
        {
            if (E.Doc == null) return;
            using (var d = new ExportDialog(E))
                if (d.ShowDialog(this) == DialogResult.OK) { E.Say("Exported " + d.ExportedPath); UpdateTitle(); }
        }

        void RenameSprite()
        {
            if (E.Doc == null) return;
            var n = Prompt.Show(this, "Sprite name", "Name used for exported files (e.g. ACE_TRAINER_M):", E.Doc.Name);
            if (string.IsNullOrWhiteSpace(n)) return;
            E.Doc.Name = n.Trim().ToUpperInvariant().Replace(' ', '_');
            E.Doc.Dirty = true;
            UpdateTitle();
        }

        void ShowText(string title, string text)
        {
            using (var f = new Form { Text = title, Size = new Size(720, 560), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, Font = Theme.UiFont })
            {
                var t = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9.5f), Text = text, BorderStyle = BorderStyle.None };
                f.Controls.Add(t);
                Theme.Apply(f);
                t.Select(0, 0);
                f.ShowDialog(this);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            _frames.Stop();
            _dirs.Stop();
            E.Settings.Save();
            base.OnFormClosing(e);
        }
    }
}
