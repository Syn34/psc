using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>The Windows colour dialog, remembering the user's custom colours between runs.</summary>
    public static class ColorPicker
    {
        public static int? Pick(IWin32Window owner, int initial, Editor e)
        {
            using (var d = new ColorDialog { FullOpen = true, AnyColor = true, Color = initial == 0 ? Color.White : PixelImage.ToColor(initial) })
            {
                var custom = e.Settings.CustomColors.Select(h => { try { int c = PixelImage.ParseHex(h); return (PixelImage.B(c) << 16) | (PixelImage.G(c) << 8) | PixelImage.R(c); } catch { return 0xFFFFFF; } }).ToArray();
                if (custom.Length > 0) d.CustomColors = custom;
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                e.Settings.CustomColors = d.CustomColors.Select(c => PixelImage.Hex(PixelImage.Rgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF))).ToList();
                return PixelImage.FromColor(d.Color);
            }
        }
    }

    /// <summary>Drawing colours: left/right-click colours, the style's palette and the colours used in the sprite.</summary>
    public class ColorPanel : Panel
    {
        readonly Editor E;
        readonly ColorWell _primary, _secondary;
        readonly SwatchGrid _style, _used;
        readonly Label _count, _usedLbl, _styleLbl;
        readonly CheckBox _limit;
        readonly ToolTip _tip = new ToolTip();

        public ColorPanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(6, 4, 8, 6);
            var title = new Label { Text = "Colours", Font = Theme.Heading, Left = 6, Top = 4, Width = 200, Height = 22, Tag = "accent" };
            _secondary = new ColorWell { Left = 30, Top = 46, Size = new Size(34, 34) };
            _primary = new ColorWell { Left = 8, Top = 30, Size = new Size(38, 38) };
            var swap = new Button { Text = "⇄", Left = 70, Top = 30, Width = 28, Height = 24 };
            var clear = new Button { Text = "Erase", Left = 70, Top = 56, Width = 52, Height = 24 };
            _tip.SetToolTip(_primary, "Left-click colour (click to choose)");
            _tip.SetToolTip(_secondary, "Right-click colour (click to choose)");
            _tip.SetToolTip(swap, "Swap colours (X)");
            _tip.SetToolTip(clear, "Make the left-click colour transparent (draws as an eraser)");
            var help = new Label { Left = 130, Top = 30, Width = 170, Height = 52, Font = Theme.Small, Tag = "sub", Text = "Left-click a swatch for the left mouse colour, right-click for the right mouse colour." };

            _styleLbl = new Label { Left = 6, Top = 88, Width = 290, Height = 16, Font = Theme.Small, Tag = "sub" };
            _style = new SwatchGrid { Left = 6, Top = 104, Width = 290, Height = 40 };
            _usedLbl = new Label { Left = 6, Top = 148, Width = 180, Height = 16, Font = Theme.Small, Tag = "sub", Text = "In this sprite (right-click: replace everywhere)" };
            _count = new Label { Left = 190, Top = 148, Width = 106, Height = 16, Font = Theme.Bold, TextAlign = ContentAlignment.TopRight };
            _used = new SwatchGrid { Left = 6, Top = 164, Width = 290, Height = 40 };
            _limit = new CheckBox { Left = 6, Top = 208, Width = 290, Height = 22 };
            _tip.SetToolTip(_limit, "Shows (and exports) the sprite with the generation's colour limit, the way the real games would.");

            Controls.AddRange(new Control[] { title, _primary, _secondary, swap, clear, help, _styleLbl, _style, _usedLbl, _count, _used, _limit });
            Height = 236;

            _primary.Click += (s, a) => { var c = ColorPicker.Pick(FindForm(), E.Primary, E); if (c.HasValue) E.SetPrimary(c.Value); };
            _secondary.Click += (s, a) => { var c = ColorPicker.Pick(FindForm(), E.Secondary, E); if (c.HasValue) E.SetSecondary(c.Value); };
            swap.Click += (s, a) => E.SwapColors();
            clear.Click += (s, a) => E.SetPrimary(0);
            _style.Picked += (c, b) => { if (b == MouseButtons.Left) E.SetPrimary(c); else E.SetSecondary(c); };
            _used.Picked += (c, b) => { if (b == MouseButtons.Left) E.SetPrimary(c); else ReplaceEverywhere(c); };
            _limit.CheckedChanged += (s, a) =>
            {
                if (E.Doc == null || E.Doc.LimitPalette == _limit.Checked) return;
                E.Doc.LimitPalette = _limit.Checked; E.Doc.Dirty = true; E.InvalidatePreview(); E.Changed();
            };

            E.ColorsChanged += () => { _primary.Value = E.Primary; _secondary.Value = E.Secondary; _style.Selected = E.Primary; _used.Selected = E.Primary; };
            E.DocChanged += RefreshUsed;
            E.LayersChanged += RefreshStyle;
            _primary.Value = E.Primary; _secondary.Value = E.Secondary;
        }

        void ReplaceEverywhere(int c)
        {
            var to = ColorPicker.Pick(FindForm(), c, E);
            if (!to.HasValue || to.Value == c) return;
            E.BeginChange();
            E.Doc.ReplaceColorEverywhere(c, to.Value);
            E.Changed();
            E.Say("Replaced " + PixelImage.Hex(c) + " with " + PixelImage.Hex(to.Value) + " on every layer.");
        }

        void RefreshStyle()
        {
            if (E.Doc == null) return;
            var s = E.Doc.Style;
            _styleLbl.Text = s.Name + " palette";
            _style.Colors = s.Palette.ToList();
            _limit.Text = s.Greyscale ? "Show as Gen 1 (4 shades of grey)" : "Limit to " + s.MaxColors + " colours like " + s.Name;
            _limit.Checked = E.Doc.LimitPalette;
            RefreshUsed();
        }

        void RefreshUsed()
        {
            if (E.Doc == null) return;
            if (_limit.Checked != E.Doc.LimitPalette) _limit.Checked = E.Doc.LimitPalette;
            var used = E.Doc.UsedColors().OrderBy(c => PixelImage.Luma(c)).ToList();
            _used.Colors = used;
            int max = E.Doc.Style.MaxColors;
            _count.Text = used.Count + " / " + max;
            _count.ForeColor = used.Count > max ? Theme.Warn : Theme.Good;
            _tip.SetToolTip(_count, used.Count > max
                ? (E.Doc.LimitPalette ? "More colours than " + E.Doc.Style.Name + " allows; the view and export merge similar colours." : "More colours than " + E.Doc.Style.Name + " allows. Tick the limit box to merge them.")
                : "Within " + E.Doc.Style.Name + "'s colour limit.");
        }

        public void ApplyTheme() { _style.Invalidate(); _used.Invalidate(); RefreshUsed(); }
    }

    /// <summary>A big colour square.</summary>
    public class ColorWell : Control
    {
        int _v;
        public int Value { get { return _v; } set { _v = value; Invalidate(); } }
        public ColorWell() { Cursor = Cursors.Hand; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Pix.Swatch(e.Graphics, ClientRectangle, _v);
            using (var p = new Pen(Theme.Dark ? Color.Black : Color.White)) e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
    }

    /// <summary>A grid of colour swatches; Picked(colour, mouse button).</summary>
    public class SwatchGrid : Control
    {
        List<int> _colors = new List<int>();
        public int Selected;
        public int Cell = 18;
        public event Action<int, MouseButtons> Picked;
        readonly ToolTip _tip = new ToolTip();
        int _tipIdx = -1;

        public List<int> Colors { get { return _colors; } set { _colors = value ?? new List<int>(); Invalidate(); } }
        public SwatchGrid() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; }

        int PerRow { get { return Math.Max(1, Width / (Cell + 2)); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            for (int i = 0; i < _colors.Count; i++)
            {
                var r = new Rectangle((i % PerRow) * (Cell + 2), (i / PerRow) * (Cell + 2), Cell, Cell);
                if (r.Bottom > Height) break;
                Pix.Swatch(e.Graphics, r, _colors[i], _colors[i] == Selected);
            }
            if (_colors.Count == 0) TextRenderer.DrawText(e.Graphics, "—", Theme.Small, new Point(2, 2), Theme.SubText);
        }

        int Hit(Point p)
        {
            int i = (p.Y / (Cell + 2)) * PerRow + p.X / (Cell + 2);
            return p.X / (Cell + 2) < PerRow && i >= 0 && i < _colors.Count ? i : -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = Hit(e.Location);
            if (i >= 0 && Picked != null) Picked(_colors[i], e.Button);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = Hit(e.Location);
            if (i == _tipIdx) return;
            _tipIdx = i;
            _tip.SetToolTip(this, i >= 0 ? PixelImage.Hex(_colors[i]) : "");
        }
    }
}
