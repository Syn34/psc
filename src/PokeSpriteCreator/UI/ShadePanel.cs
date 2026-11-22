using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// The side panel for the shading helper tool: shows the picked part now and with the suggested shading,
    /// the colours it uses, what it would change and why, and Apply.
    /// </summary>
    public class ShadePanel : Panel
    {
        readonly Editor E;
        readonly ShadePreviewBox _now, _after;
        readonly RampView _ramp;
        readonly Label _notes, _help;
        readonly ComboBox _light, _outline;
        readonly CheckBox _under, _soft, _existing, _preview;
        readonly Button _apply, _clear;
        readonly ToolTip _tip = new ToolTip();
        bool _syncing;

        static readonly LightFrom[] Lights = { LightFrom.Auto, LightFrom.Left, LightFrom.TopLeft, LightFrom.Top, LightFrom.TopRight, LightFrom.Right };

        public ShadePanel(Editor e)
        {
            E = e;
            Tag = "panel";
            Padding = new Padding(8, 6, 8, 6);
            AutoScroll = true;

            var title = new Label { Text = "Shading helper", Font = Theme.Heading, Left = 8, Top = 6, Width = 300, Height = 22, Tag = "accent" };
            _help = new Label { Left = 8, Top = 30, Width = 300, Height = 44, Font = Theme.Small, Tag = "sub",
                Text = "Drag a box over one part (a leg, an arm, a sleeve), or click it. You'll get shading suggested with your own colours." };

            var nowLbl = new Label { Text = "Now", Left = 8, Top = 76, Width = 146, Height = 16, Font = Theme.Bold };
            var afterLbl = new Label { Text = "Suggested", Left = 160, Top = 76, Width = 146, Height = 16, Font = Theme.Bold };
            _now = new ShadePreviewBox { Left = 8, Top = 94, Width = 146, Height = 176 };
            _after = new ShadePreviewBox { Left = 160, Top = 94, Width = 146, Height = 176 };

            var lightLbl = new Label { Text = "Light from", Left = 8, Top = 282, Width = 80, Height = 18 };
            _light = new ComboBox { Left = 92, Top = 278, Width = 214, DropDownStyle = ComboBoxStyle.DropDownList };
            _light.Items.AddRange(new object[] { "Automatic", "Left", "Top left", "Top", "Top right", "Right" });
            _light.SelectedIndex = 0;
            var outLbl = new Label { Text = "Outline", Left = 8, Top = 312, Width = 80, Height = 18 };
            _outline = new ComboBox { Left = 92, Top = 308, Width = 214, DropDownStyle = ComboBoxStyle.DropDownList };
            _outline.Items.AddRange(new object[] { "Add around it (outside)", "On its edge pixels", "No outline" });
            _outline.SelectedIndex = 0;

            _under = new CheckBox { Text = "Shadow under the part above it", Left = 8, Top = 338, Width = 300, Height = 22, Checked = true };
            _soft = new CheckBox { Text = "Softer outline on the lit side", Left = 8, Top = 360, Width = 300, Height = 22 };
            _existing = new CheckBox { Text = "Only colours already in the sprite", Left = 8, Top = 382, Width = 300, Height = 22, Checked = true };
            _preview = new CheckBox { Text = "Show the suggestion on the canvas", Left = 8, Top = 404, Width = 300, Height = 22, Checked = true };

            _ramp = new RampView { Left = 8, Top = 432, Width = 300, Height = 46 };

            _apply = new Button { Text = "Apply", Left = 8, Top = 484, Width = 146, Height = 30, Enabled = false };
            _clear = new Button { Text = "Clear box", Left = 160, Top = 484, Width = 146, Height = 30, Enabled = false };

            _notes = new Label { Left = 8, Top = 522, Width = 300, Height = 220, Font = Theme.UiFont };

            _tip.SetToolTip(_light, "Which side the light comes from. Automatic works it out from how the rest of the sprite is shaded.");
            _tip.SetToolTip(_outline, "Add around it: a 1-pixel outline outside the part, where it meets the background.\nOn its edge pixels: keeps the part the same size.");
            _tip.SetToolTip(_under, "A darker row just under anything that overlaps the part from above (like a leg under shorts).");
            _tip.SetToolTip(_soft, "Uses a lighter brown instead of the dark outline on the side facing the light (often seen in Gen 5 sprites).");
            _tip.SetToolTip(_existing, "On: never adds colours, so you stay within the generation's colour limit. Off: makes a darker or lighter shade if the sprite doesn't have one.");
            _tip.SetToolTip(_apply, "Puts the suggested shading on the selected layer. Ctrl+Z undoes it.");

            Controls.AddRange(new Control[] { title, _help, nowLbl, afterLbl, _now, _after, lightLbl, _light, outLbl, _outline, _under, _soft, _existing, _preview, _ramp, _apply, _clear, _notes });

            EventHandler opts = (s, a) => { if (!_syncing) ReadOptions(); };
            _light.SelectedIndexChanged += opts; _outline.SelectedIndexChanged += opts;
            _under.CheckedChanged += opts; _soft.CheckedChanged += opts; _existing.CheckedChanged += opts;
            _preview.CheckedChanged += (s, a) => { E.ShadePreview = _preview.Checked; E.RecomputeShade(); };
            _apply.Click += (s, a) => E.ApplyShade();
            _clear.Click += (s, a) => E.SetShadeArea(null);

            E.ShadeChanged += Sync;
            // keep the suggestion up to date with the sprite; a different sprite or part clears the box
            E.DocChanged += () => { if (E.ShadeArea.HasValue) E.RecomputeShade(); };
            E.FrameChanged += () => { if (E.ShadeArea.HasValue) E.RecomputeShade(); };
            E.SelectionChanged += () => { if (E.ShadeArea.HasValue) E.RecomputeShade(); };
            E.PartSwitched += () => { if (E.ShadeArea.HasValue) E.SetShadeArea(null); };
            Sync();
        }

        void ReadOptions()
        {
            var o = E.ShadeOptions;
            o.Light = Lights[Math.Max(0, _light.SelectedIndex)];
            o.Outline = (OutlineMode)Math.Max(0, _outline.SelectedIndex);
            o.CastShadow = _under.Checked;
            o.SoftLitOutline = _soft.Checked;
            o.OnlyExisting = _existing.Checked;
            E.RecomputeShade();
        }

        void Sync()
        {
            var s = E.Shade;
            _now.Set(s == null ? null : s.Before, s == null ? Rectangle.Empty : s.Region);
            _after.Set(s == null ? null : s.After, s == null ? Rectangle.Empty : s.Region);
            _ramp.Ramp = s == null || s.Pixels == 0 ? null : s.Ramp;
            _apply.Enabled = s != null && s.HasChanges;
            _clear.Enabled = E.ShadeArea.HasValue;
            if (s == null) _notes.Text = "";
            else _notes.Text = string.Join("\n", s.Notes.Select(n => "• " + n).ToArray());
            // fit the notes' height to the text so the panel scrolls when needed
            var sz = TextRenderer.MeasureText(_notes.Text.Length == 0 ? " " : _notes.Text, _notes.Font, new Size(_notes.Width, 10000), TextFormatFlags.WordBreak);
            _notes.Height = sz.Height + 8;
        }

        public void ApplyTheme() { _now.Invalidate(); _after.Invalidate(); _ramp.Invalidate(); }
    }

    /// <summary>The picked area of a frame, zoomed to fit, on a checkerboard.</summary>
    public class ShadePreviewBox : Control
    {
        PixelImage _img; Rectangle _area;

        public ShadePreviewBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Set(PixelImage img, Rectangle area) { _img = img; _area = area; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.CanvasBack);
            if (_img == null || _area.Width <= 0 || _area.Height <= 0)
            {
                TextRenderer.DrawText(g, "No box yet", Theme.Small, ClientRectangle, Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                var crop = _img.Crop(_area.X, _area.Y, _area.Width, _area.Height);
                var r = Pix.Fit(crop.W, crop.H, new Rectangle(4, 4, Width - 8, Height - 8));
                Pix.Checker(g, r, Math.Max(3, r.Width / crop.W / 2));
                Pix.Draw(g, crop, r);
            }
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>The colours a suggestion uses: outline, under, shadow, base, highlight.</summary>
    public class RampView : Control
    {
        ShadeRamp _ramp;
        public ShadeRamp Ramp { get { return _ramp; } set { _ramp = value; Invalidate(); } }

        public RampView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            if (_ramp == null) return;
            var items = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("Outline", _ramp.Outline),
                new KeyValuePair<string, int>("Under", _ramp.Under),
                new KeyValuePair<string, int>("Shadow", _ramp.Shadow),
                new KeyValuePair<string, int>("Base", _ramp.Base),
                new KeyValuePair<string, int>("Highlight", _ramp.Highlight),
            };
            int w = Width / items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                var sw = new Rectangle(i * w + (w - 26) / 2, 2, 26, 22);
                Pix.Swatch(g, sw, items[i].Value);
                if (items[i].Value != 0 && _ramp.New.Contains(items[i].Value))
                    TextRenderer.DrawText(g, "new", Theme.Small, new Rectangle(sw.X, sw.Y, sw.Width, sw.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, items[i].Key, Theme.Small, new Rectangle(i * w, 26, w, 16), Theme.SubText, TextFormatFlags.HorizontalCenter);
            }
        }
    }
}
