using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    internal static class ModernColors
    {
        public static readonly Color CanvasBg = Color.FromArgb(18, 20, 26);
        public static readonly Color SidebarBg = Color.FromArgb(13, 15, 20);
        public static readonly Color CardBg = Color.FromArgb(25, 28, 36);
        public static readonly Color CardHover = Color.FromArgb(32, 36, 46);
        public static readonly Color InputBg = Color.FromArgb(31, 35, 45);
        public static readonly Color Border = Color.FromArgb(46, 51, 64);
        public static readonly Color Divider = Color.FromArgb(38, 42, 54);
        public static readonly Color Text = Color.FromArgb(238, 240, 246);
        public static readonly Color Dim = Color.FromArgb(148, 155, 172);
        public static readonly Color Muted = Color.FromArgb(100, 107, 124);
        public static readonly Color Good = Color.FromArgb(34, 197, 94);
        public static readonly Color Warn = Color.FromArgb(245, 158, 11);
        public static readonly Color Danger = Color.FromArgb(239, 68, 68);
    }

    internal static class ModernUi
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static void ApplyDwm(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(handle, 20, ref dark, 4) != 0)
                    DwmSetWindowAttribute(handle, 19, ref dark, 4);
                Color c = ModernColors.SidebarBg;
                int cap = (c.B << 16) | (c.G << 8) | c.R;
                DwmSetWindowAttribute(handle, 35, ref cap, 4);
            }
            catch { }
        }

        public static void ApplyDarkTitleBar(Form f)
        {
            if (f == null) return;
            if (f.IsHandleCreated) ApplyDwm(f.Handle);
            f.HandleCreated += delegate { ApplyDwm(f.Handle); };
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var gp = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            if (rad < 0.5f) { gp.AddRectangle(r); return gp; }
            float d = rad * 2f;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        }

        public static Color ContrastText(Color bg)
        {
            double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            return lum > 0.58 ? Color.FromArgb(14, 16, 22) : Color.White;
        }

        public static Panel AddDivider(Control parent, int left, int top, int width)
        {
            var p = new Panel
            {
                Left = left,
                Top = top,
                Width = width,
                Height = 1,
                BackColor = ModernColors.Divider,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            parent.Controls.Add(p);
            return p;
        }
    }

    internal enum ModernButtonVariant
    {
        Primary,
        Secondary,
        Danger,
        Ghost
    }

    internal sealed class ModernCard : Panel
    {
        public int Radius = 12;
        public Color CardColor = ModernColors.CardBg;
        public Color BorderColor = ModernColors.Border;

        public ModernCard()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = ModernColors.CardBg;
            ForeColor = ModernColors.Text;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color outer = Parent != null ? Parent.BackColor : ModernColors.CanvasBg;
            e.Graphics.Clear(outer);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Math.Max(2, Width - 2), Math.Max(2, Height - 2));
            using (GraphicsPath gp = ModernUi.RoundRect(r, Radius))
            {
                using (var br = new SolidBrush(CardColor)) e.Graphics.FillPath(br, gp);
                using (var pn = new Pen(BorderColor, 1f)) e.Graphics.DrawPath(pn, gp);
            }
        }
    }

    internal sealed class ModernButton : Control
    {
        private bool _hover, _down, _checked;
        private ModernButtonVariant _variant = ModernButtonVariant.Secondary;
        private Color _accent = Color.FromArgb(0, 200, 255);
        private int _radius = 8;

        public ModernButtonVariant Variant
        {
            get { return _variant; }
            set { _variant = value; Invalidate(); }
        }

        public Color AccentColor
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        public bool Checked
        {
            get { return _checked; }
            set { _checked = value; Invalidate(); }
        }

        public ModernButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            ForeColor = ModernColors.Text;
            Height = 34;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left) { _down = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color outer = Parent != null ? Parent.BackColor : ModernColors.CardBg;
            if (outer == Color.Transparent) outer = ModernColors.CardBg;
            g.Clear(outer);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg, brd, txt;
            if (!Enabled)
            {
                bg = Color.FromArgb(29, 32, 42);
                brd = Color.FromArgb(42, 46, 58);
                txt = ModernColors.Muted;
            }
            else if (_variant == ModernButtonVariant.Primary)
            {
                int k = _down ? 215 : (_hover ? 255 : 235);
                bg = ModernUi.Alpha(_accent, k);
                brd = _accent;
                txt = ModernUi.ContrastText(_accent);
            }
            else if (_variant == ModernButtonVariant.Danger)
            {
                bg = _down ? ModernUi.Alpha(ModernColors.Danger, 75) : (_hover ? ModernUi.Alpha(ModernColors.Danger, 50) : ModernUi.Alpha(ModernColors.Danger, 28));
                brd = ModernUi.Alpha(ModernColors.Danger, _hover ? 220 : 140);
                txt = Color.FromArgb(255, 160, 160);
            }
            else if (_variant == ModernButtonVariant.Ghost)
            {
                if (_checked)
                {
                    bg = ModernUi.Alpha(_accent, 34);
                    brd = _accent;
                    txt = _accent;
                }
                else
                {
                    bg = _down ? ModernColors.CardHover : (_hover ? ModernColors.InputBg : outer);
                    brd = _hover ? ModernColors.Border : Color.Transparent;
                    txt = _hover ? ModernColors.Text : ModernColors.Dim;
                }
            }
            else
            {
                if (_checked)
                {
                    bg = ModernUi.Alpha(_accent, 32);
                    brd = _accent;
                    txt = _accent;
                }
                else
                {
                    bg = _down ? Color.FromArgb(44, 49, 62) : (_hover ? Color.FromArgb(39, 44, 56) : ModernColors.InputBg);
                    brd = _hover ? ModernUi.Alpha(_accent, 130) : ModernColors.Border;
                    txt = ModernColors.Text;
                }
            }

            var r = new RectangleF(0.5f, 0.5f, Math.Max(2, Width - 2), Math.Max(2, Height - 2));
            using (GraphicsPath gp = ModernUi.RoundRect(r, _radius))
            {
                using (var sb = new SolidBrush(bg)) g.FillPath(sb, gp);
                if (brd != Color.Transparent)
                    using (var pn = new Pen(brd, 1f)) g.DrawPath(pn, gp);
            }

            TextRenderer.DrawText(
                g,
                Text ?? "",
                Font,
                new Rectangle(6, 0, Math.Max(4, Width - 12), Height),
                txt,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ModernToggle : Control
    {
        private bool _checked, _hover;
        private Color _accent = Color.FromArgb(0, 200, 255);
        private string _title = "", _subtitle = "";

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public string Title
        {
            get { return _title; }
            set { _title = value ?? ""; Invalidate(); }
        }

        public string Subtitle
        {
            get { return _subtitle; }
            set { _subtitle = value ?? ""; Invalidate(); }
        }

        public Color AccentColor
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public void SetCheckedSilent(bool value)
        {
            if (_checked != value) { _checked = value; Invalidate(); }
        }

        public ModernToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.6f, FontStyle.Regular);
            Height = 46;
            BackColor = ModernColors.CardBg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color outer = Parent != null ? Parent.BackColor : ModernColors.CardBg;
            if (outer == Color.Transparent) outer = ModernColors.CardBg;
            g.Clear(outer);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_hover && Enabled)
            {
                using (GraphicsPath hp = ModernUi.RoundRect(new RectangleF(0, 1, Math.Max(4, Width - 1), Math.Max(4, Height - 3)), 8))
                using (var hb = new SolidBrush(ModernColors.CardHover))
                    g.FillPath(hb, hp);
            }

            int swW = 44, swH = 22;
            int swX = Width - swW - 8;
            int swY = (Height - swH) / 2;
            int textW = Math.Max(20, swX - 16);

            bool hasSub = !string.IsNullOrEmpty(_subtitle);
            if (hasSub)
            {
                TextRenderer.DrawText(g, _title, Font, new Rectangle(8, 5, textW, 20), Enabled ? ModernColors.Text : ModernColors.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                using (var sf = new Font("Segoe UI", 8.4f))
                    TextRenderer.DrawText(g, _subtitle, sf, new Rectangle(8, 24, textW, Height - 26), Enabled ? ModernColors.Dim : ModernColors.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, _title, Font, new Rectangle(8, 0, textW, Height), Enabled ? ModernColors.Text : ModernColors.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            var trackRect = new RectangleF(swX, swY, swW, swH);
            using (GraphicsPath tp = ModernUi.RoundRect(trackRect, swH / 2f))
            {
                if (_checked)
                {
                    Color fill = Enabled ? _accent : ModernColors.Muted;
                    using (var tb = new SolidBrush(fill)) g.FillPath(tb, tp);
                }
                else
                {
                    using (var tb = new SolidBrush(ModernColors.InputBg)) g.FillPath(tb, tp);
                    using (var tpen = new Pen(ModernColors.Border, 1.2f)) g.DrawPath(tpen, tp);
                }
            }

            float knobD = swH - 6;
            float knobX = _checked ? (swX + swW - knobD - 3) : (swX + 3);
            float knobY = swY + 3;
            Color knobC = _checked ? Color.White : Color.FromArgb(182, 188, 204);
            using (var kb = new SolidBrush(knobC))
                g.FillEllipse(kb, knobX, knobY, knobD, knobD);
        }
    }

    internal sealed class ModernSlider : Control
    {
        private int _min, _max = 100, _val;
        private bool _drag;
        private string _label = "", _suffix = "";
        private Color _accent = Color.FromArgb(0, 200, 255);

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get { return _min; }
            set { _min = value; if (_val < _min) _val = _min; Invalidate(); }
        }

        public int Maximum
        {
            get { return _max; }
            set { _max = Math.Max(_min + 1, value); if (_val > _max) _val = _max; Invalidate(); }
        }

        public int Value
        {
            get { return _val; }
            set
            {
                int cl = Math.Max(_min, Math.Min(_max, value));
                if (_val == cl) return;
                _val = cl;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public string Label
        {
            get { return _label; }
            set { _label = value ?? ""; Invalidate(); }
        }

        public string Suffix
        {
            get { return _suffix; }
            set { _suffix = value ?? ""; Invalidate(); }
        }

        public Color AccentColor
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public void SetValueSilent(int value)
        {
            int cl = Math.Max(_min, Math.Min(_max, value));
            if (_val != cl) { _val = cl; Invalidate(); }
        }

        public ModernSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            Height = 46;
            BackColor = ModernColors.CardBg;
        }

        private void UpdateFromMouse(int mx)
        {
            int pad = 10;
            int tw = Math.Max(10, Width - pad * 2);
            double ratio = Math.Max(0.0, Math.Min(1.0, (mx - pad) / (double)tw));
            Value = (int)Math.Round(_min + ratio * (_max - _min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left) { _drag = true; UpdateFromMouse(e.X); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag && Enabled) UpdateFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _drag = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color outer = Parent != null ? Parent.BackColor : ModernColors.CardBg;
            if (outer == Color.Transparent) outer = ModernColors.CardBg;
            g.Clear(outer);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (!string.IsNullOrEmpty(_label))
            {
                using (var lf = new Font("Segoe UI", 8.8f, FontStyle.Bold))
                    TextRenderer.DrawText(g, _label, lf, new Rectangle(2, 0, Width - 80, 20), ModernColors.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            string valTxt = _val + _suffix;
            TextRenderer.DrawText(g, valTxt, Font, new Rectangle(Width - 76, 0, 74, 20), _accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int pad = 10;
            float trackY = Height - 14;
            float trackW = Math.Max(20, Width - pad * 2);
            var tr = new RectangleF(pad, trackY, trackW, 6);
            using (GraphicsPath tp = ModernUi.RoundRect(tr, 3))
            using (var tb = new SolidBrush(ModernColors.InputBg))
                g.FillPath(tb, tp);

            float frac = (_val - _min) / (float)Math.Max(1, _max - _min);
            float fw = trackW * frac;
            if (fw > 2f)
            {
                using (GraphicsPath fp = ModernUi.RoundRect(new RectangleF(pad, trackY, fw, 6), 3))
                using (var fb = new SolidBrush(_accent))
                    g.FillPath(fb, fp);
            }

            float kx = pad + fw;
            float ky = trackY + 3;
            using (var ob = new SolidBrush(_accent)) g.FillEllipse(ob, kx - 8, ky - 8, 16, 16);
            using (var ib = new SolidBrush(Color.White)) g.FillEllipse(ib, kx - 4.5f, ky - 4.5f, 9, 9);
        }
    }

    internal sealed class ModernMeter : Control
    {
        private int _val;
        private Color _accent = Color.FromArgb(0, 200, 255);

        public int Value
        {
            get { return _val; }
            set
            {
                int cl = Math.Max(0, Math.Min(100, value));
                if (_val == cl) return;
                _val = cl;
                Invalidate();
            }
        }

        public Color AccentColor
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public ModernMeter()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 12;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color outer = Parent != null ? Parent.BackColor : ModernColors.CardBg;
            g.Clear(outer);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var tr = new RectangleF(0.5f, 0.5f, Math.Max(4, Width - 2), Math.Max(4, Height - 2));
            using (GraphicsPath tp = ModernUi.RoundRect(tr, Height / 2f))
            {
                using (var tb = new SolidBrush(ModernColors.InputBg)) g.FillPath(tb, tp);
                if (_val > 0)
                {
                    float fw = Math.Max(Height, (Width - 2) * (_val / 100f));
                    Color fill = _val > 90 ? ModernColors.Danger : _accent;
                    using (GraphicsPath fp = ModernUi.RoundRect(new RectangleF(0.5f, 0.5f, Math.Min(Width - 2, fw), Math.Max(4, Height - 2)), Height / 2f))
                    using (var fb = new SolidBrush(fill))
                        g.FillPath(fb, fp);
                }
                using (var pn = new Pen(ModernColors.Border, 1f)) g.DrawPath(pn, tp);
            }
        }
    }

    internal sealed class ModernBadge : Control
    {
        private Color _color = ModernColors.Good;

        public Color BadgeColor
        {
            get { return _color; }
            set { _color = value; Invalidate(); }
        }

        public ModernBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            Height = 24;
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color outer = Parent != null ? Parent.BackColor : ModernColors.CardBg;
            g.Clear(outer);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(0.5f, 0.5f, Math.Max(4, Width - 2), Math.Max(4, Height - 2));
            using (GraphicsPath gp = ModernUi.RoundRect(r, Height / 2f))
            {
                using (var bg = new SolidBrush(ModernUi.Alpha(_color, 34))) g.FillPath(bg, gp);
                using (var pn = new Pen(ModernUi.Alpha(_color, 150), 1f)) g.DrawPath(pn, gp);
            }
            TextRenderer.DrawText(g, Text ?? "", Font, new Rectangle(6, 0, Math.Max(4, Width - 12), Height), _color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ModernNavButton : Control
    {
        private bool _active, _hover;
        private string _icon = "", _keywords = "";
        private Color _accent = Color.FromArgb(0, 200, 255);

        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        public string IconGlyph
        {
            get { return _icon; }
            set { _icon = value ?? ""; Invalidate(); }
        }

        public string Keywords
        {
            get { return _keywords; }
            set { _keywords = value ?? ""; }
        }

        public Color AccentColor
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public ModernNavButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            Height = 36;
            BackColor = ModernColors.SidebarBg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ModernColors.SidebarBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(8, 1, Math.Max(10, Width - 16), Math.Max(10, Height - 3));
            if (_active || _hover)
            {
                using (GraphicsPath gp = ModernUi.RoundRect(r, 8))
                using (var br = new SolidBrush(_active ? Color.FromArgb(30, 35, 48) : Color.FromArgb(22, 26, 35)))
                    g.FillPath(br, gp);
            }

            if (_active)
            {
                using (GraphicsPath bar = ModernUi.RoundRect(new RectangleF(8, 8, 3.5f, Math.Max(8, Height - 17)), 2))
                using (var ab = new SolidBrush(_accent))
                    g.FillPath(ab, bar);
            }

            Color fg = _active ? _accent : (_hover ? ModernColors.Text : Color.FromArgb(198, 204, 218));
            if (!string.IsNullOrEmpty(_icon))
            {
                using (var icFont = new Font("Segoe UI Symbol", 10f))
                    TextRenderer.DrawText(g, _icon, icFont, new Rectangle(18, 0, 24, Height), _active ? _accent : ModernColors.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            using (var tf = new Font("Segoe UI", 9.5f, _active ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text ?? "", tf, new Rectangle(44, 0, Math.Max(10, Width - 54), Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
