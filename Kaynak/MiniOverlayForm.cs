using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>Tek monitör veya oyun içi için tıklamayı geçiren (click-through) mini köşe HUD çubuğu.</summary>
    internal sealed class MiniOverlayForm : Form
    {
        private Snap _snap;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_TRANSPARENT (0x20) | WS_EX_TOOLWINDOW (0x80) | WS_EX_LAYERED (0x80000) | WS_EX_NOACTIVATE (0x08000000)
                cp.ExStyle |= 0x080800A0;
                return cp;
            }
        }

        public MiniOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Size = new Size(460, 34);
            BackColor = Color.FromArgb(14, 16, 22);
            ApplyLayout();
        }

        public void ApplyLayout()
        {
            if (IsDisposed) return;
            int op = Math.Max(30, Math.Min(100, AppPrefs.OverlayOpacity));
            Opacity = op / 100.0;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int pad = 14;
            int x = wa.Left + pad, y = wa.Top + pad;
            switch (AppPrefs.OverlayCorner)
            {
                case 1: x = wa.Right - Width - pad; y = wa.Top + pad; break;
                case 2: x = wa.Left + pad; y = wa.Bottom - Height - pad; break;
                case 3: x = wa.Right - Width - pad; y = wa.Bottom - Height - pad; break;
                default: x = wa.Left + pad; y = wa.Top + pad; break;
            }
            Location = new Point(x, y);
            Invalidate();
        }

        public void UpdateSnap(Snap s)
        {
            if (IsDisposed) return;
            _snap = s;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) Invalidate(); }); }
                catch { }
            }
            else
            {
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Titreşimi önlemek için boyamayı OnPaint içinde yapıyoruz.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(14, 16, 22));

            Theme th = Program.Dash != null && !Program.Dash.IsDisposed ? Program.Dash._th : Themes.All[0];
            Color acc = th.A1;
            Color a2 = th.A2;

            using (var pn = new Pen(ModernUi.Alpha(acc, 180), 1.5f))
                g.DrawRectangle(pn, 0, 0, Width - 1, Height - 1);
            using (var topBar = new SolidBrush(acc))
                g.FillRectangle(topBar, 0, 0, 4, Height);

            Snap s = _snap;
            if (s == null)
            {
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "OnMon HUD · " + L.T("veri bekleniyor…", "waiting…"), f, new Rectangle(12, 0, Width - 16, Height), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }

            string fpsTxt = s.Fps >= 5
                ? (Math.Round(s.Fps).ToString(CultureInfo.InvariantCulture) + " FPS" + (!double.IsNaN(s.Low1) && s.Low1 >= 1 ? " (" + Math.Round(s.Low1) + ")" : ""))
                : "-- FPS";
            string cpuTxt = "CPU " + AppPrefs.FmtTemp(s.CpuTemp) + " " + Math.Round(s.CpuLoad) + "%";
            string gpuTxt = "GPU " + AppPrefs.FmtTemp(s.GpuTemp) + " " + Math.Round(s.GpuLoad) + "%";
            string ramTxt = "RAM " + Math.Round(s.MemLoad) + "%";

            int x = 12;
            using (var fBold = new Font("Consolas", 9.5f, FontStyle.Bold))
            using (var fNorm = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                Color fpsCol = s.Fps >= 5 ? a2 : Color.FromArgb(140, 148, 165);
                x = DrawChip(g, fpsTxt, fBold, fpsCol, x);
                x = DrawChip(g, cpuTxt, fNorm, TempColor(s.CpuTemp, acc), x);
                x = DrawChip(g, gpuTxt, fNorm, TempColor(s.GpuTemp, Color.FromArgb(0, 200, 255)), x);
                DrawChip(g, ramTxt, fNorm, Color.FromArgb(200, 210, 228), x);
            }
        }

        private int DrawChip(Graphics g, string txt, Font f, Color col, int x)
        {
            Size sz = TextRenderer.MeasureText(txt, f);
            TextRenderer.DrawText(g, txt, f, new Point(x, (Height - sz.Height) / 2), col, TextFormatFlags.NoPadding);
            int next = x + sz.Width + 10;
            using (var p = new Pen(Color.FromArgb(52, 58, 74), 1f))
                g.DrawLine(p, next - 5, 8, next - 5, Height - 8);
            return next;
        }

        private static Color TempColor(double c, Color normal)
        {
            if (double.IsNaN(c)) return Color.FromArgb(148, 155, 172);
            if (c >= AppPrefs.AlertCpu - 5) return ModernColors.Danger;
            if (c >= 75) return ModernColors.Warn;
            return normal;
        }
    }
}
