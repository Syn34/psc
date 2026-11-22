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
        public const string Version = "0.4.1";

        readonly Editor E;
        readonly CanvasView _single, _left, _right;
        readonly SplitContainer _split;
        readonly Panel _canvasHost;
        readonly TabBar _tabBar;
        bool _linkZoom = true, _zoomSyncing;
        ToolStripMenuItem _compareMenu, _stopCompareItem, _linkZoomItem;
        readonly InGamePanel _dirs;
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

            // the editing area: tabs on top, one canvas (or two side by side when comparing), frames below
            _tabBar = new TabBar(E) { Dock = DockStyle.Top };
            _single = new CanvasView(E, false) { Dock = DockStyle.Fill };
            _left = new CanvasView(E, true) { Dock = DockStyle.Fill };
            _right = new CanvasView(E, true) { Dock = DockStyle.Fill };
            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6, Visible = false, Tag = "keep" };
            _split.Panel1.Controls.Add(_left);
            _split.Panel2.Controls.Add(_right);
            _canvasHost = new Panel { Dock = DockStyle.Fill, Tag = "keep" };
            _canvasHost.Controls.Add(_single);
            _canvasHost.Controls.Add(_split);
            _frames = new FrameStrip(E) { Dock = DockStyle.Bottom, Visible = false };
            var center = new Panel { Dock = DockStyle.Fill, Tag = "keep" };
            center.Controls.Add(_canvasHost);
            center.Controls.Add(_frames);
            center.Controls.Add(_tabBar);

            _dirs = new InGamePanel(E) { Dock = DockStyle.Right, Width = 322 };
            var left = new Panel { Dock = DockStyle.Left, Width = 318, Tag = "panel" };
            _layers = new LayersPanel(E) { Dock = DockStyle.Fill };
            _colors = new ColorPanel(E) { Dock = DockStyle.Bottom };
            left.Controls.Add(_layers);
            left.Controls.Add(_colors);

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
            E.PartSwitched += () => { UpdateTitle(); _frames.Visible = E.Doc != null && E.Doc.FrameCount > 1; _zoomLbl.Text = ActiveCanvas.Zoom * 100 + "%"; };
            E.TabsChanged += () => { UpdateTitle(); UpdateMenus(); };
            E.CompareChanged += ShowCompare;
            foreach (var v in new[] { _single, _left, _right })
            {
                var cv = v;
                cv.Canvas.HoverChanged += (p, c) => _pos.Text = p.HasValue ? p.Value.X + ", " + p.Value.Y + "   " + PixelImage.Hex(c) : "";
                cv.Canvas.ZoomChanged += () => { _zoomLbl.Text = cv.Canvas.Zoom * 100 + "%"; LinkZoom(cv); };
                cv.StopRequested += () => E.StopCompare();
            }
            _tabBar.NewRequested += () => NewSprite();
            _tabBar.CloseRequested += s => CloseTab(s);
            _tabBar.Dropped += (s, pt) =>
            {
                // a tab dragged onto the canvas: compare it with the tab being edited
                if (!_canvasHost.RectangleToScreen(_canvasHost.ClientRectangle).Contains(pt)) return;
                if (s == E.S) { E.Say("Drag a different tab onto the canvas to compare it with this one."); return; }
                E.StartCompare(E.S, s);
            };

            ApplyTheme();
            SyncToolButtons();
            NewBlank(GenStyle.Get(settings.LastGen), SpriteKind.Front);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveCanvas.FitToWindow();
            if (!string.IsNullOrEmpty(_openOnStart)) OpenFile(_openOnStart);
            else if (Environment.GetEnvironmentVariable("PSC_NO_NEW_DIALOG") == null) BeginInvoke((Action)(() => NewSprite()));
            E.Say("Draw a sprite or bring in a picture (File > Import picture). Add the trainer's walking sprites on the right.");
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
            file.DropDownItems.Add(Item("&Close tab", Keys.Control | Keys.W, () => CloseTab(E.S)));
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
            _framesMenu.DropDownItems.Add(Item("Copy my change to its &walking steps", Keys.Control | Keys.Shift | Keys.W, () => E.Say(E.CopyChange(false))));
            _framesMenu.DropDownItems.Add(Item("Copy my change to all &directions", Keys.Control | Keys.Shift | Keys.D, () => E.Say(E.CopyChange(true))));
            _framesMenu.DropDownItems.Add(Item("Copy frame to &all frames", Keys.None, () => E.CopyToAllFrames()));
            _framesMenu.DropDownItems.Add(Item("Mirror &Left → Right (overworld)", Keys.None, () => E.MirrorRows(true)));
            _framesMenu.DropDownItems.Add(Item("Mirror &Right → Left (overworld)", Keys.None, () => E.MirrorRows(false)));
            _framesMenu.DropDownItems.Add(new ToolStripSeparator());
            _framesMenu.DropDownItems.Add(Item("Add frame (back sprite)", Keys.None, () => { if (E.Doc != null) E.SetFrameCount(E.Doc.FrameCount + 1); }));
            _framesMenu.DropDownItems.Add(Item("Remove last frame (back sprite)", Keys.None, () => { if (E.Doc != null && E.Doc.FrameCount > 1) E.SetFrameCount(E.Doc.FrameCount - 1); }));
            _onionItem = Item("&Onion skin", Keys.None, () => { E.OnionSkin = !E.OnionSkin; _onionItem.Checked = E.OnionSkin; E.Changed(); });
            _framesMenu.DropDownItems.Add(_onionItem);

            var view = new ToolStripMenuItem("&View");
            view.DropDownItems.Add(Item("Zoom &in", Keys.Control | Keys.Oemplus, () => ActiveCanvas.ZoomBy(1)));
            view.DropDownItems.Add(Item("Zoom &out", Keys.Control | Keys.OemMinus, () => ActiveCanvas.ZoomBy(-1)));
            view.DropDownItems.Add(Item("&Fit to window", Keys.Control | Keys.D0, () => ActiveCanvas.FitToWindow()));
            view.DropDownItems.Add(new ToolStripSeparator());
            _compareMenu = new ToolStripMenuItem("&Compare side by side with");
            _stopCompareItem = Item("&Stop comparing", Keys.None, () => E.StopCompare());
            _linkZoomItem = Item("&Link zoom while comparing", Keys.None, () => { _linkZoom = !_linkZoom; _linkZoomItem.Checked = _linkZoom; if (_linkZoom && E.Comparing) LinkZoom(ActiveView); });
            view.DropDownItems.Add(_compareMenu);
            view.DropDownItems.Add(_stopCompareItem);
            view.DropDownItems.Add(_linkZoomItem);
            view.DropDownItems.Add(Item("Next tab", Keys.Control | Keys.PageDown, () => NextTab(1)));
            view.DropDownItems.Add(Item("Previous tab", Keys.Control | Keys.PageUp, () => NextTab(-1)));
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
            help.DropDownItems.Add(Item("&About", Keys.None, () => MessageBox.Show(this, AppName + " " + Version + "\r\nA sprite editor for Pokémon fan games (trainer front & back sprites and overworld sheets, Gen 1 to Gen 5 styles). Trainer front sprites keep their walking sprites in the same file.\r\nCompanion app for PokeCreator.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information)));

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

3. WALKING SPRITES (IN-GAME SPRITES PANEL, ON THE RIGHT)
   A trainer front sprite keeps its walking sprites in the same file: a 4 x 4
   sheet with rows Down, Left, Right, Up and the walking steps across.
   * Upload walking sheet (or Start blank) adds them; Replace sheet swaps them.
   * Click a frame (or a direction) to edit it on the big canvas; ""Main
     sprite"" / ""Walking sprites"" switch between the two.
   * Change one frame, then copy that change:
       Its walking steps - to the other frames facing the same way
       All directions    - to every other frame
     Recolours carry over exactly. Drawn or erased pixels are lined up with
     each frame (following the walking bob) and mirrored between left and
     right. The change is what you did since you picked the frame. Undo
     (Ctrl+Z) if it doesn't fit.
   * Export sheet saves it (trainer_NAME.png for Graphics/Characters).
   An overworld sheet opened on its own works the same way.

4. TABS AND COMPARING
   Every sprite you open or make gets its own tab above the canvas, with its own
   undo, zoom and scroll. Click a tab to switch (Ctrl+Tab), + for a new sprite,
   x or middle-click to close (Ctrl+W); you're asked to save changes first.
   The scroll bars (right and bottom) move around a zoomed-in sprite; the mouse
   wheel scrolls up/down, Shift + wheel sideways, Ctrl + wheel zooms.
   To compare two sprites side by side, drag a tab onto the canvas (or
   right-click a tab, or View > Compare side by side with). Click either side
   to edit it; the side you're editing says ""Editing"". Both sides zoom
   together (View > Link zoom while comparing). Clicking another tab while
   comparing puts it in the side you're editing. ""Stop comparing"" (above
   either side, or in the View menu) goes back to one sprite.

5. EXPORT
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
  Mouse wheel         scroll up / down     Shift + wheel  scroll sideways
  Ctrl + mouse wheel  zoom      Middle mouse / Space + drag  pan
  Ctrl+= / Ctrl+-     zoom      Ctrl+0  fit       Ctrl+G  grid

TABS
  Ctrl+Tab / Ctrl+Shift+Tab (or Ctrl+PgDn / Ctrl+PgUp)  next / previous tab
  Ctrl+W  close tab      Middle-click a tab  close it
  Drag a tab onto the canvas  compare it side by side

FRAMES
  Ctrl+Left / Ctrl+Right   previous / next frame
  Ctrl+C / Ctrl+V          copy / paste frame
  Ctrl+Shift+W             copy my change to its walking steps
  Ctrl+Shift+D             copy my change to all directions

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
            _mirrorBtn.CheckedChanged += (s, a) => { E.Mirror = _mirrorBtn.Checked; InvalidateCanvases(); };
            _mergedBtn = new ToolStripButton("Fill sees all layers") { CheckOnClick = true, Checked = true, ToolTipText = "On: Fill stops at the outlines of everything you can see (colour in line art on another layer). Off: Fill only looks at the selected layer." };
            _mergedBtn.CheckedChanged += (s, a) => E.SampleMerged = _mergedBtn.Checked;
            _gridBtn = new ToolStripButton("Grid") { CheckOnClick = true, Checked = E.Settings.ShowGrid, ToolTipText = "Pixel grid (Ctrl+G)" };
            _gridBtn.CheckedChanged += (s, a) => { E.Settings.ShowGrid = _gridBtn.Checked; InvalidateCanvases(); };
            t.Items.Add(_mirrorBtn);
            t.Items.Add(_mergedBtn);
            t.Items.Add(_gridBtn);
            t.Items.Add(new ToolStripSeparator());
            t.Items.Add(TextBtn("−", "Zoom out (Ctrl+-)", () => ActiveCanvas.ZoomBy(-1)));
            t.Items.Add(TextBtn("+", "Zoom in (Ctrl+=)", () => ActiveCanvas.ZoomBy(1)));
            t.Items.Add(TextBtn("Fit", "Fit to window (Ctrl+0)", () => ActiveCanvas.FitToWindow()));
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
            if (_linkZoomItem != null) _linkZoomItem.Checked = _linkZoom;
            if (_stopCompareItem != null) _stopCompareItem.Enabled = E.Comparing;
            if (_compareMenu != null)
            {
                _compareMenu.DropDownItems.Clear();
                foreach (var o in E.Sessions.Where(x => x != E.S))
                {
                    var other = o;
                    _compareMenu.DropDownItems.Add(other.TabName, null, (a, b) => E.StartCompare(E.S, other));
                }
                _compareMenu.Enabled = _compareMenu.DropDownItems.Count > 0;
                if (!_compareMenu.Enabled) _compareMenu.ToolTipText = "Open another sprite in a tab first.";
            }
        }

        void FillRecent()
        {
            _recentMenu.DropDownItems.Clear();
            foreach (var p in E.Settings.Recent.Where(File.Exists))
            {
                var path = p;
                _recentMenu.DropDownItems.Add(path, null, (s, a) => OpenFile(path));
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
            InvalidateCanvases(); _frames.Invalidate(true);
            UpdateMenus();
        }

        // ------------------------------------------------------------------ keyboard

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab)) { NextTab(1); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { NextTab(-1); return true; }
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
                if (focused is SpriteCanvas || focused == null || focused is ListBox)
                {
                    switch (keyData)
                    {
                        case Keys.Left: ActiveCanvas.Nudge(-1, 0); return true;
                        case Keys.Right: ActiveCanvas.Nudge(1, 0); return true;
                        case Keys.Up: if (focused is ListBox) break; ActiveCanvas.Nudge(0, -1); return true;
                        case Keys.Down: if (focused is ListBox) break; ActiveCanvas.Nudge(0, 1); return true;
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
            Text = E.Doc == null ? AppName : E.Doc.Title + " - " + E.Root.Name + (E.Doc != E.Root ? " (walking sprites)" : "") + " - " + AppName;
            _docLbl.Text = E.Doc == null ? "" : (E.Doc != E.Root ? "Walking sprites · " : "") + E.Doc.Description;
        }

        void NewBlank(GenStyle st, SpriteKind k)
        {
            var d = new SpriteDocument(st, k, "Walk", st.DefaultLayout(k));
            d.Layers.Add(d.NewLayer("Paint"));
            SetDoc(d);
        }

        /// <summary>Opens a sprite in a new tab (an untouched blank tab is replaced).</summary>
        void SetDoc(SpriteDocument d)
        {
            var blank = E.Sessions.Count == 1 && IsUntouchedBlank(E.Sessions[0]) ? E.Sessions[0] : null;
            E.OpenTab(d);
            if (blank != null) E.CloseTab(blank);
            _frames.Visible = d.FrameCount > 1;
            if (d.Layers.Count > 0)
            {
                // start with the top paint layer selected so drawing goes on top
                E.SelectLayer(d.Layers.Count - 1);
            }
            UpdateTitle();
            // sensible first colours for the style
            if (d.Style.Palette.Length > 1) { E.SetPrimary(d.Style.Palette.OrderBy(c => PixelImage.Luma(c)).First()); E.SetSecondary(d.Style.Palette.OrderByDescending(c => PixelImage.Luma(c)).First()); }
        }

        static bool IsUntouchedBlank(Session s)
        {
            return s.Root.FilePath == null && !s.Root.AnyDirty && s.Root.Overworld == null && !s.Root.UsedColors().Any();
        }

        /// <summary>Asks to save a tab's sprite if it has changes. False = cancel.</summary>
        bool ConfirmDiscard(Session s)
        {
            if (s == null || !s.Root.AnyDirty) return true;
            if (s != E.S) E.PickTab(s);
            var r = MessageBox.Show(this, "Save changes to " + s.TabName.TrimEnd(' ', '*') + "?", AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save(false);
            return true;
        }

        void CloseTab(Session s)
        {
            if (s == null || !ConfirmDiscard(s)) return;
            bool last = E.Sessions.Count == 1;
            if (last) NewBlank(s.Root.Style, SpriteKind.Front);   // always keep one tab open
            E.CloseTab(s);
        }

        void NextTab(int dir)
        {
            if (E.Sessions.Count < 2) return;
            int i = E.Sessions.IndexOf(E.S);
            E.PickTab(E.Sessions[(i + dir + E.Sessions.Count) % E.Sessions.Count]);
        }

        // ------------------------------------------------------------------ comparing two tabs

        SpriteCanvas ActiveCanvas { get { return ActiveView.Canvas; } }

        CanvasView ActiveView
        {
            get { return !E.Comparing ? _single : E.S == E.CompareRight ? _right : _left; }
        }

        void InvalidateCanvases() { _single.Canvas.Invalidate(); _left.Canvas.Invalidate(); _right.Canvas.Invalidate(); }

        void ShowCompare()
        {
            if (E.Comparing)
            {
                _left.Canvas.Bound = E.CompareLeft;
                _right.Canvas.Bound = E.CompareRight;
                _single.Visible = false;
                _split.Visible = true;
                _split.SplitterDistance = Math.Max(100, _split.Width / 2);
                foreach (var v in new[] { _left, _right }) { v.Canvas.Bound.NeedsFit = true; v.Canvas.FitToWindow(); v.UpdateHeader(); v.SyncBars(); }
                E.Say("Comparing \"" + E.CompareLeft.TabName.TrimEnd(' ', '*') + "\" and \"" + E.CompareRight.TabName.TrimEnd(' ', '*') + "\" side by side. Click either one to edit it. View > Stop comparing (or the button above) to go back to one.");
            }
            else
            {
                _left.Canvas.Bound = _right.Canvas.Bound = null;
                _split.Visible = false;
                _single.Visible = true;
                if (E.S != null) E.S.NeedsFit = true;
                _single.Canvas.FitToWindow();
                _single.SyncBars();
                if (E.S != null) E.Say("Stopped comparing. Editing \"" + E.S.TabName.TrimEnd(' ', '*') + "\".");
            }
            UpdateMenus();
            InvalidateCanvases();
        }

        /// <summary>While comparing, the other side follows the zoom (so both are seen at the same size).</summary>
        void LinkZoom(CanvasView from)
        {
            if (!_linkZoom || !E.Comparing || _zoomSyncing || (from != _left && from != _right)) return;
            var other = from == _left ? _right : _left;
            _zoomSyncing = true;
            try { other.Canvas.SetZoom(from.Canvas.Zoom); }
            finally { _zoomSyncing = false; }
        }

        void NewSprite(string picture = null)
        {
            using (var d = new NewSpriteDialog(E.Settings, picture))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                E.Settings.LastGen = d.Result.Style.Gen;
                E.Settings.LastKind = d.Result.Kind.ToString();
                SetDoc(d.Result);
                E.Say("New " + d.Result.Description + "." + (d.Result.Kind == SpriteKind.Front ? " Add its walking sprites on the right." : ""));
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

        void Open()
        {
            using (var ofd = new OpenFileDialog { Filter = "Sprite (*" + SpriteDocument.Extension + ")|*" + SpriteDocument.Extension + "|All files|*.*" })
                if (ofd.ShowDialog(this) == DialogResult.OK) OpenFile(ofd.FileName);
        }

        public void OpenFile(string path)
        {
            // already open? go to its tab
            var open = E.Sessions.FirstOrDefault(x => x.Root.FilePath != null && string.Equals(Path.GetFullPath(x.Root.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (open != null) { E.PickTab(open); E.Say("That sprite is already open."); return; }
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
            var root = E.Root;
            string path = root.FilePath;
            if (saveAs || path == null)
            {
                using (var sfd = new SaveFileDialog { Filter = "Sprite (*" + SpriteDocument.Extension + ")|*" + SpriteDocument.Extension, FileName = (path != null ? Path.GetFileNameWithoutExtension(path) : root.Name) + SpriteDocument.Extension })
                {
                    if (sfd.ShowDialog(this) != DialogResult.OK) return false;
                    path = sfd.FileName;
                }
            }
            try
            {
                root.Save(path);
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
            var n = Prompt.Show(this, "Sprite name", "Name used for exported files (e.g. ACE_TRAINER_M):", E.Root.Name);
            if (string.IsNullOrWhiteSpace(n)) return;
            var root = E.Root;
            root.Name = n.Trim().ToUpperInvariant().Replace(' ', '_');
            if (root.Overworld != null) root.Overworld.Name = root.Name;
            root.Dirty = true;
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
            foreach (var s in E.Sessions.ToList())
                if (!ConfirmDiscard(s)) { e.Cancel = true; return; }
            _frames.Stop();
            _dirs.Stop();
            E.Settings.Save();
            base.OnFormClosing(e);
        }
    }
}
