using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>Light / dark colours and a pass that recolours a control tree.</summary>
    public static class Theme
    {
        public static bool Dark;
        public static readonly Color AccentBase = Color.FromArgb(200, 48, 48);
        public static Color Back { get { return Dark ? Color.FromArgb(30, 31, 34) : Color.White; } }
        public static Color Panel { get { return Dark ? Color.FromArgb(37, 38, 42) : Color.FromArgb(246, 247, 250); } }
        public static Color Input { get { return Dark ? Color.FromArgb(48, 50, 56) : Color.White; } }
        public static Color ButtonBack { get { return Dark ? Color.FromArgb(56, 58, 66) : Color.FromArgb(250, 250, 252); } }
        public static Color Border { get { return Dark ? Color.FromArgb(70, 72, 80) : Color.FromArgb(214, 216, 222); } }
        public static Color Text { get { return Dark ? Color.FromArgb(222, 224, 230) : Color.FromArgb(30, 30, 34); } }
        public static Color SubText { get { return Dark ? Color.FromArgb(140, 144, 154) : Color.FromArgb(110, 112, 120); } }
        public static Color Selection { get { return Dark ? Color.FromArgb(55, 70, 105) : Color.FromArgb(214, 230, 255); } }
        public static Color Hover { get { return Dark ? Color.FromArgb(46, 48, 54) : Color.FromArgb(236, 239, 245); } }
        public static Color CanvasBack { get { return Dark ? Color.FromArgb(24, 25, 28) : Color.FromArgb(222, 224, 230); } }
        public static Color Accent { get { return Dark ? ControlPaint.Light(AccentBase, 0.4f) : AccentBase; } }
        public static Color Warn { get { return Dark ? Color.FromArgb(255, 140, 120) : Color.FromArgb(200, 40, 30); } }
        public static Color Good { get { return Dark ? Color.FromArgb(120, 220, 130) : Color.FromArgb(30, 130, 50); } }

        public static readonly Font UiFont = new Font("Segoe UI", 9f);
        public static readonly Font Bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font Heading = new Font("Segoe UI", 10f, FontStyle.Bold);
        public static readonly Font Small = new Font("Segoe UI", 8f);

        /// <summary>Recolours a control and its children. Controls whose Tag is "keep" are left alone.</summary>
        public static void Apply(Control c)
        {
            if ("keep".Equals(c.Tag)) return;
            if (c is Form || c is UserControl || c is Panel || c is SplitContainer || c is TableLayoutPanel || c is FlowLayoutPanel || c is GroupBox || c is TabPage)
            {
                c.BackColor = "panel".Equals(c.Tag) ? Panel : (c is Form ? Panel : (c.Parent != null ? c.Parent.BackColor : Panel));
                if ("panel".Equals(c.Tag)) c.BackColor = Panel;
                if ("back".Equals(c.Tag)) c.BackColor = Back;
                c.ForeColor = Text;
            }
            else if (c is Button)
            {
                var b = (Button)c;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.BackColor = ButtonBack; b.ForeColor = Text;
            }
            else if (c is CheckBox || c is RadioButton)
            {
                c.ForeColor = Text; c.BackColor = c.Parent != null ? c.Parent.BackColor : Panel;
                if (c is CheckBox && ((CheckBox)c).Appearance == Appearance.Button)
                {
                    var cb = (CheckBox)c; cb.FlatStyle = FlatStyle.Flat; cb.FlatAppearance.BorderColor = Border; cb.FlatAppearance.CheckedBackColor = Selection; cb.BackColor = ButtonBack;
                }
            }
            else if (c is Label || c is TrackBar)
            {
                c.BackColor = c.Parent != null ? c.Parent.BackColor : Panel;
                if (!"sub".Equals(c.Tag) && !"accent".Equals(c.Tag)) c.ForeColor = Text;
                if ("sub".Equals(c.Tag)) c.ForeColor = SubText;
                if ("accent".Equals(c.Tag)) c.ForeColor = Accent;
            }
            else if (c is TextBox || c is ListBox || c is ComboBox || c is NumericUpDown || c is ListView)
            {
                c.BackColor = Input; c.ForeColor = Text;
            }
            else if (c is MenuStrip || c is ToolStrip || c is StatusStrip)
            {
                var ts = (ToolStrip)c;
                ts.Renderer = new ToolStripProfessionalRenderer(new Colors());
                ts.BackColor = Panel; ts.ForeColor = Text;
                foreach (ToolStripItem i in ts.Items) ApplyItem(i);
            }
            foreach (Control ch in c.Controls) Apply(ch);
        }

        static void ApplyItem(ToolStripItem i)
        {
            i.ForeColor = Text;
            var dd = i as ToolStripDropDownItem;
            if (dd != null) { dd.DropDown.BackColor = Panel; foreach (ToolStripItem s in dd.DropDownItems) ApplyItem(s); }
        }

        class Colors : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin { get { return Panel; } }
            public override Color MenuStripGradientEnd { get { return Panel; } }
            public override Color ToolStripGradientBegin { get { return Panel; } }
            public override Color ToolStripGradientMiddle { get { return Panel; } }
            public override Color ToolStripGradientEnd { get { return Panel; } }
            public override Color ToolStripBorder { get { return Panel; } }
            public override Color ToolStripDropDownBackground { get { return Panel; } }
            public override Color ImageMarginGradientBegin { get { return Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Panel; } }
            public override Color ImageMarginGradientEnd { get { return Panel; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Selection; } }
            public override Color MenuItemPressedGradientEnd { get { return Selection; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color ButtonSelectedHighlight { get { return Hover; } }
            public override Color ButtonSelectedGradientBegin { get { return Hover; } }
            public override Color ButtonSelectedGradientEnd { get { return Hover; } }
            public override Color ButtonCheckedGradientBegin { get { return Selection; } }
            public override Color ButtonCheckedGradientEnd { get { return Selection; } }
            public override Color ButtonCheckedHighlight { get { return Selection; } }
            public override Color ButtonPressedGradientBegin { get { return Selection; } }
            public override Color ButtonPressedGradientEnd { get { return Selection; } }
            public override Color ButtonSelectedBorder { get { return Border; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Panel; } }
            public override Color StatusStripGradientBegin { get { return Panel; } }
            public override Color StatusStripGradientEnd { get { return Panel; } }
        }
    }

    /// <summary>Drawing helpers for pixel art (no smoothing) and the transparency checkerboard.</summary>
    public static class Pix
    {
        public static void Checker(Graphics g, Rectangle r, int cell = 8)
        {
            var a = Theme.Dark ? Color.FromArgb(58, 60, 66) : Color.FromArgb(255, 255, 255);
            var b = Theme.Dark ? Color.FromArgb(46, 48, 54) : Color.FromArgb(228, 230, 236);
            using (var br = new SolidBrush(a)) g.FillRectangle(br, r);
            using (var br = new SolidBrush(b))
                for (int y = r.Top, yi = 0; y < r.Bottom; y += cell, yi++)
                    for (int x = r.Left + ((yi & 1) == 0 ? 0 : cell); x < r.Right; x += cell * 2)
                        g.FillRectangle(br, x, y, Math.Min(cell, r.Right - x), Math.Min(cell, r.Bottom - y));
        }

        public static void Draw(Graphics g, PixelImage img, Rectangle dest, float alpha = 1f)
        {
            if (img == null) return;
            using (var b = img.ToBitmap()) Draw(g, b, dest, alpha);
        }

        public static void Draw(Graphics g, Bitmap b, Rectangle dest, float alpha = 1f)
        {
            var im = g.InterpolationMode; var po = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (alpha >= 1f) g.DrawImage(b, dest);
            else
            {
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(b, dest, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
                }
            }
            g.InterpolationMode = im; g.PixelOffsetMode = po;
        }

        /// <summary>Largest whole-number zoom that fits w×h into the box, drawn centred.</summary>
        public static Rectangle Fit(int w, int h, Rectangle box, bool wholeSteps = true)
        {
            double k = Math.Min(box.Width / (double)w, box.Height / (double)h);
            if (wholeSteps && k >= 1) k = Math.Floor(k);
            int dw = Math.Max(1, (int)(w * k)), dh = Math.Max(1, (int)(h * k));
            return new Rectangle(box.X + (box.Width - dw) / 2, box.Y + (box.Height - dh) / 2, dw, dh);
        }

        /// <summary>A thumbnail bitmap: checkerboard + the picture scaled to fit.</summary>
        public static Bitmap Thumb(PixelImage img, int w, int h, bool checker = true)
        {
            var b = new Bitmap(w, h);
            using (var g = Graphics.FromImage(b))
            {
                if (checker) Checker(g, new Rectangle(0, 0, w, h), 4); else g.Clear(Color.Transparent);
                if (img != null)
                {
                    var bounds = img.Bounds();
                    Draw(g, img, Fit(img.W, img.H, new Rectangle(1, 1, w - 2, h - 2), img.W <= w && img.H <= h));
                }
            }
            return b;
        }

        /// <summary>Swatch: a colour square (or checkerboard for transparent) with a border.</summary>
        public static void Swatch(Graphics g, Rectangle r, int c, bool selected = false)
        {
            if (c == 0) Checker(g, r, 4);
            else using (var br = new SolidBrush(PixelImage.ToColor(c))) g.FillRectangle(br, r);
            using (var p = new Pen(selected ? Theme.Accent : Theme.Border, selected ? 2 : 1))
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
    }
}
