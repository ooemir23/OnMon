using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>
    /// Edge/tarayıcı olmadan, doğrudan GDI+ ile çizilen tam ekran pano.
    /// 1920x1080'lik sanal tuvale çizer ve pencereye ölçekler.
    /// </summary>
    public sealed class DashboardForm : Form
    {
        private const int HN = 90;
        private Snap _snap;
        private bool _dirty = true;
        private int _lastSecond = -1;
        private string _layout = "classic";
        private Theme _th = Themes.All[0];
        private int _margin;
        private string _city = "istanbul";
        private DateTime _lastMouse = DateTime.UtcNow;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly Dictionary<string, float[]> _hist = new Dictionary<string, float[]>();
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        private readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        private readonly StringFormat[] _sf = new StringFormat[3];
        private Graphics G;
        private string _fontMain = "Segoe UI", _fontLight = "Segoe UI", _fontMono = "Consolas";
        private ToolStripMenuItem _infoItem;
        private bool _curHidden;

        // ---------------- ayarlar ----------------
        private static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "ui.txt"); }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "layout" && (v == "classic" || v == "hud" || v == "cockpit")) _layout = v;
                    else if (k == "theme") _th = Themes.Get(v);
                    else if (k == "margin") int.TryParse(v, out _margin);
                    else if (k == "city" && v.Length > 0) _city = v;
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, new[] { "layout=" + _layout, "theme=" + _th.Id, "margin=" + _margin, "city=" + _city });
            }
            catch { }
        }

        // ---------------- yaşam döngüsü ----------------
        public DashboardForm()
        {
            LoadSettings();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = "ROG Pro System Monitor";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;

            foreach (FontFamily ff in FontFamily.Families)
            {
                if (ff.Name == "Bahnschrift") _fontMain = "Bahnschrift";
                else if (ff.Name == "Bahnschrift Light") _fontLight = "Bahnschrift Light";
            }
            if (_fontLight == "Segoe UI" && _fontMain == "Bahnschrift") _fontLight = "Bahnschrift";
            if (_fontLight == "Segoe UI") { foreach (FontFamily ff in FontFamily.Families) if (ff.Name == "Segoe UI Light") _fontLight = "Segoe UI Light"; }
            foreach (FontFamily ff in FontFamily.Families) if (ff.Name == "Cascadia Mono") _fontMono = "Cascadia Mono";

            for (int i = 0; i < 3; i++)
            {
                var f = new StringFormat(StringFormat.GenericTypographic);
                f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
                f.Trimming = StringTrimming.None;
                f.LineAlignment = StringAlignment.Center;
                f.Alignment = i == 0 ? StringAlignment.Near : (i == 1 ? StringAlignment.Center : StringAlignment.Far);
                _sf[i] = f;
            }

            BuildMenu();
            _timer.Interval = 500;
            _timer.Tick += delegate
            {
                int sec = DateTime.Now.Second;
                if (_dirty || sec != _lastSecond) { _dirty = false; _lastSecond = sec; Invalidate(); }
                if (!_curHidden && (DateTime.UtcNow - _lastMouse).TotalSeconds > 3 && ContextMenuStrip != null && !ContextMenuStrip.Visible) { _curHidden = true; Cursor.Hide(); }
            };
            _timer.Start();
            Extras.Start(_city);
        }

        private Rectangle _target;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_target.Width > 0) { Bounds = _target; Invalidate(); }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _lastMouse = DateTime.UtcNow;
            if (_curHidden) { _curHidden = false; Cursor.Show(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.D1) SetLayout("classic");
            else if (e.KeyCode == Keys.D2) SetLayout("hud");
            else if (e.KeyCode == Keys.D3) SetLayout("cockpit");
            else if (e.KeyCode == Keys.T) NextTheme();
            else if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _timer.Stop();
            if (_curHidden) { _curHidden = false; Cursor.Show(); }
            Extras.Stop();
            foreach (Font f in _fonts.Values) f.Dispose();
            foreach (SolidBrush b in _brushes.Values) b.Dispose();
            foreach (Pen p in _pens.Values) p.Dispose();
            foreach (StringFormat s in _sf) s.Dispose();
            _fonts.Clear(); _brushes.Clear(); _pens.Clear();
            Program.Dash = null;
            Program.TrimProcessMemory();
        }

        /// <summary>İkinci monitöre (yoksa birincil monitöre) tam ekran yerleştirir.</summary>
        public void PlaceOnSecondMonitor()
        {
            Screen[] screens = Screen.AllScreens;
            Screen target = screens.FirstOrDefault(s => !s.Primary) ?? screens.First();
            _target = target.Bounds;
            Bounds = _target;
        }

        // ---------------- veri ----------------
        public void Push(Snap s)
        {
            _snap = s;
            Add("fps", s.Fps);
            Add("frame", s.FrameMs);
            Add("cpuTemp", s.CpuTemp);
            Add("gpuLoad", s.GpuLoad);
            Add("netUp", s.NetUp);
            Add("netDown", s.NetDown);
            Add("ping", s.Ping);
            Add("totalW", (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW));
            if (_infoItem != null) _infoItem.Text = s.Source;
            _dirty = true;
        }

        private void Add(string key, double v)
        {
            float[] a;
            if (!_hist.TryGetValue(key, out a)) { a = new float[HN]; _hist[key] = a; for (int i = 0; i < HN; i++) a[i] = double.IsNaN(v) ? 0 : (float)v; }
            Array.Copy(a, 1, a, 0, HN - 1);
            a[HN - 1] = double.IsNaN(v) ? 0 : (float)v;
        }

        private float[] H(string k)
        {
            float[] a;
            return _hist.TryGetValue(k, out a) ? a : new float[HN];
        }

        // ---------------- menü ----------------
        private void BuildMenu()
        {
            var m = new ContextMenuStrip();
            _infoItem = new ToolStripMenuItem("Sensör kaynağı") { Enabled = false };
            m.Items.Add(_infoItem);
            m.Items.Add(new ToolStripSeparator());

            var lay = new ToolStripMenuItem("Düzen");
            lay.DropDownItems.Add("1 · Klasik", null, delegate { SetLayout("classic"); });
            lay.DropDownItems.Add("2 · Oyun HUD", null, delegate { SetLayout("hud"); });
            lay.DropDownItems.Add("3 · Pro Kokpit", null, delegate { SetLayout("cockpit"); });
            m.Items.Add(lay);

            var th = new ToolStripMenuItem("Tema (T)");
            foreach (Theme t in Themes.All)
            {
                Theme tt = t;
                th.DropDownItems.Add(t.Name, null, delegate { SetTheme(tt); });
            }
            m.Items.Add(th);

            var mar = new ToolStripMenuItem("Kenar boşluğu");
            foreach (int v in new[] { 0, 8, 16, 24, 32, 48 })
            {
                int vv = v;
                mar.DropDownItems.Add(v + " px", null, delegate { _margin = vv; SaveSettings(); Invalidate(); });
            }
            m.Items.Add(mar);

            var city = new ToolStripMenuItem("Şehir (hava & namaz)");
            foreach (string c in new[] { "istanbul", "ankara", "izmir", "bursa", "antalya", "adana", "konya", "trabzon", "kocaeli", "gaziantep" })
            {
                string cc = c;
                city.DropDownItems.Add(char.ToUpper(c[0]) + c.Substring(1), null, delegate { _city = cc; SaveSettings(); Extras.Start(_city); });
            }
            m.Items.Add(city);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Tarayıcıda aç (daha çok RAM kullanır)", null, delegate { Program.LaunchBrowserDashboard(); });
            m.Items.Add("Panoyu kapat (Esc)", null, delegate { Close(); });
            ContextMenuStrip = m;
        }

        private void SetLayout(string id) { _layout = id; SaveSettings(); _dirty = true; Invalidate(); }
        private void SetTheme(Theme t) { _th = t; SaveSettings(); _dirty = true; Invalidate(); }
        private void NextTheme()
        {
            int i = Array.IndexOf(Themes.All, _th);
            SetTheme(Themes.All[(i + 1) % Themes.All.Length]);
        }

        // ---------------- çizim yardımcıları ----------------
        private static Color Al(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }
        private static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        private SolidBrush Br(Color c)
        {
            SolidBrush b;
            int k = c.ToArgb();
            if (!_brushes.TryGetValue(k, out b)) { b = new SolidBrush(c); _brushes[k] = b; }
            return b;
        }

        private Pen Pn(Color c, float w)
        {
            Pen p;
            long k = ((long)c.ToArgb() << 16) ^ (long)(w * 10);
            if (!_pens.TryGetValue(k, out p)) { p = new Pen(c, w); p.LineJoin = LineJoin.Round; _pens[k] = p; }
            return p;
        }

        private Font F(float size, bool bold, bool light)
        {
            string name = _th.Mono ? _fontMono : (light ? _fontLight : _fontMain);
            string key = name + "|" + size + "|" + bold;
            Font f;
            if (!_fonts.TryGetValue(key, out f))
            {
                f = new Font(name, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                _fonts[key] = f;
            }
            return f;
        }

        // hizalama: 0 sol, 1 orta, 2 sağ
        private void Txt(string s, Font f, Color c, float x, float y, float w, float h, int al)
        {
            if (string.IsNullOrEmpty(s)) return;
            float need = f.Size * 1.45f;
            if (h < need) { y -= (need - h) / 2f; h = need; }
            G.DrawString(s, f, Br(c), new RectangleF(x, y, w, h), _sf[al]);
        }

        private string Fit(string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s) || maxW < 20) return "";
            if (TW(s, f) <= maxW) return s;
            while (s.Length > 1 && TW(s + "…", f) > maxW) s = s.Substring(0, s.Length - 1);
            return s.Length > 1 ? s.TrimEnd() + "…" : "";
        }

        private float TW(string s, Font f)
        {
            return G.MeasureString(s, f, 100000, _sf[0]).Width;
        }

        private GraphicsPath RR(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            if (rad < 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private GraphicsPath Clip(RectangleF r, float c)
        {
            var p = new GraphicsPath();
            p.AddPolygon(new[] {
                new PointF(r.X, r.Y), new PointF(r.Right - c, r.Y), new PointF(r.Right, r.Y + c), new PointF(r.Right, r.Bottom),
                new PointF(r.X + c, r.Bottom), new PointF(r.X, r.Bottom - c) });
            return p;
        }

        // ---------------- biçimlendirme ----------------
        private static string I0(double v) { return double.IsNaN(v) ? "--" : Math.Round(v).ToString("0", CultureInfo.InvariantCulture); }
        private static string D1(double v) { return double.IsNaN(v) ? "--" : v.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string Pct(double v) { return double.IsNaN(v) ? "--" : I0(v) + "%"; }
        private static string Tc(double v) { return double.IsNaN(v) ? "--" : I0(v) + "°C"; }
        private static string Mhz(double v) { return double.IsNaN(v) ? "--" : I0(v) + " MHz"; }
        private static string Rpm(double v) { return double.IsNaN(v) ? "--" : I0(v) + " RPM"; }
        private static string Wt(double v) { return double.IsNaN(v) ? "--" : D1(v) + " W"; }
        private static string Vt(double v) { return double.IsNaN(v) ? "--" : v.ToString("0.000", CultureInfo.InvariantCulture) + " V"; }
        private static string Mb(double v) { return double.IsNaN(v) ? "--" : I0(v) + " MB"; }
        private static string Gb(double v) { return double.IsNaN(v) ? "--" : D1(v) + " GB"; }
        private static string Spd(double kb) { return kb >= 1024 ? D1(kb / 1024) + " MB/s" : D1(kb) + " KB/s"; }
        private static string Tot(double mb) { return mb >= 10240 ? (mb / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " GB" : D1(mb) + " MB"; }
        private static double Ratio(double v, double max) { return double.IsNaN(v) || max <= 0 ? 0 : Math.Max(0, Math.Min(100, v / max * 100)); }

        // ---------------- paneller ve parçalar ----------------
        private void Panel(RectangleF r)
        {
            Theme t = _th;
            string st = t.Style;
            if (st == "brutal")
            {
                G.FillRectangle(Br(t.A1), r.X + 7, r.Y + 7, r.Width, r.Height);
                G.FillRectangle(Br(t.Panel), r.X, r.Y, r.Width, r.Height);
                G.DrawRectangle(Pn(t.Line, 3), r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3);
                return;
            }
            if (st == "clip")
            {
                using (GraphicsPath p = Clip(r, 26))
                {
                    G.FillPath(Br(t.Panel), p);
                    G.DrawPath(Pn(t.Line, 1), p);
                }
                G.FillRectangle(Br(t.A1), r.X, r.Y + 26, 4, r.Height - 52);
                return;
            }
            using (GraphicsPath p = RR(r, st == "mono" ? 4 : t.R))
            {
                if (st == "glass")
                {
                    using (var lg = new LinearGradientBrush(r, Al(Mix(t.Panel, t.Bg, 0.1f), 235), Al(t.Tile, 200), 55f))
                        G.FillPath(lg, p);
                    G.DrawPath(Pn(Al(t.Tx, 36), 1), p);
                }
                else if (st == "neon")
                {
                    G.FillPath(Br(t.Panel), p);
                    G.DrawPath(Pn(Al(t.A1, 30), 8), p);
                    G.DrawPath(Pn(Al(t.A1, 60), 4), p);
                    G.DrawPath(Pn(Al(t.A1, 140), 1.5f), p);
                }
                else if (st == "blueprint")
                {
                    G.FillPath(Br(Al(t.Panel, 210)), p);
                    using (var dp = new Pen(Al(t.Tx, 150), 1)) { dp.DashStyle = DashStyle.Dash; G.DrawPath(dp, p); }
                }
                else if (st == "crt")
                {
                    G.FillPath(Br(t.Panel), p);
                    G.DrawPath(Pn(Al(t.A1, 40), 6), p);
                    G.DrawPath(Pn(t.A1, 1), p);
                }
                else if (st == "hud")
                {
                    using (var lg = new LinearGradientBrush(r, Mix(t.Panel, t.A1, 0.07f), t.Panel, 90f))
                        G.FillPath(lg, p);
                    G.DrawPath(Pn(Al(t.A1, 97), 1), p);
                    float L = 22, o = 5;
                    Pen bp = Pn(t.A1, 2);
                    G.DrawLines(bp, new[] { new PointF(r.X + o, r.Y + o + L), new PointF(r.X + o, r.Y + o), new PointF(r.X + o + L, r.Y + o) });
                    G.DrawLines(bp, new[] { new PointF(r.Right - o - L, r.Y + o), new PointF(r.Right - o, r.Y + o), new PointF(r.Right - o, r.Y + o + L) });
                    G.DrawLines(bp, new[] { new PointF(r.X + o, r.Bottom - o - L), new PointF(r.X + o, r.Bottom - o), new PointF(r.X + o + L, r.Bottom - o) });
                    G.DrawLines(bp, new[] { new PointF(r.Right - o - L, r.Bottom - o), new PointF(r.Right - o, r.Bottom - o), new PointF(r.Right - o, r.Bottom - o - L) });
                }
                else
                {
                    G.FillPath(Br(t.Panel), p);
                    G.DrawPath(Pn(t.Line, 1), p);
                }
            }
        }

        /// <summary>Panel + başlık çizer, içerik alanını döndürür.</summary>
        private RectangleF Card(RectangleF r, string title, string sub, string extra)
        {
            Panel(r);
            Theme t = _th;
            float x = r.X + 20, y = r.Y + 14;
            G.FillRectangle(Br(t.A1), x, y + 3, 5, 30);
            Font ft = F(30, true, false), fs = F(30, false, true);
            string T = (title ?? "").ToUpper(new CultureInfo("tr-TR"));
            float tw = TW(T, ft);
            Txt(T, ft, t.Tx, x + 14, y, tw + 20, 36, 0);
            float sx = x + 14 + tw + 12;
            float ew = 0;
            if (!string.IsNullOrEmpty(extra))
            {
                Font fe = F(26, true, false);
                ew = TW(extra, fe) + 10;
                Txt(extra, fe, t.A1, r.Right - 20 - ew, y, ew, 36, 2);
            }
            float avail = r.Right - 20 - ew - 10 - sx;
            if (!string.IsNullOrEmpty(sub)) Txt(Fit(sub, fs, avail), fs, t.Tx, sx, y, Math.Max(10, avail), 36, 0);
            return new RectangleF(r.X + 20, r.Y + 60, r.Width - 40, r.Height - 74);
        }

        private void BarAt(float x, float y, float w, double pct, bool hot, float h)
        {
            Theme t = _th;
            pct = Math.Max(0, Math.Min(100, pct));
            float fw = (float)(w * pct / 100.0);
            Color c1 = hot ? t.Crit : t.A1, c2 = hot ? Color.FromArgb(255, 122, 0) : t.A2;
            string b = t.Bars;
            if (b == "thin") { y += (h - 8) / 2f; h = 8; }
            if (b == "chunk") { y -= 2; h = 20; }
            if (b == "seg")
            {
                for (float sx = 0; sx < w - 1; sx += 11)
                {
                    float sw = Math.Min(8, w - sx);
                    Color c = sx < fw ? Mix(c1, c2, sx / w) : t.Track;
                    G.FillRectangle(Br(c), x + sx, y, sw, h);
                }
                return;
            }
            if (b == "chunk")
            {
                G.FillRectangle(Br(t.Track), x, y, w, h);
                if (fw > 0.5f)
                    using (var lg = new LinearGradientBrush(new RectangleF(x, y, Math.Max(2, w), h), c1, c2, 0f))
                        G.FillRectangle(lg, x, y, fw, h);
                G.DrawRectangle(Pn(t.Line, 2), x + 1, y + 1, w - 2, h - 2);
                return;
            }
            float rad = b == "thin" ? 4 : 4;
            using (GraphicsPath tp = RR(new RectangleF(x, y, w, h), rad))
            {
                G.FillPath(Br(t.Track), tp);
                if (fw > 0.5f)
                {
                    var oldClip = G.Clip;
                    G.SetClip(tp, CombineMode.Intersect);
                    using (var lg = new LinearGradientBrush(new RectangleF(x, y, Math.Max(2, w), h), c1, c2, 0f))
                        G.FillRectangle(lg, x, y, fw, h);
                    G.Clip = oldClip;
                }
            }
        }

        private void Stat(float x, float y, float w, string label, string val, double pct, bool hot, float item)
        {
            Theme t = _th;
            float bh = Math.Min(15f, item * 0.27f);
            BarAt(x, y, w, pct, hot, bh);
            float fz = Math.Min(29f, item * 0.52f);
            Font fl = F(fz, false, true), fv = F(fz, true, false);
            while (fz > 14 && TW(label, fl) + TW(val, fv) + 14 > w) { fz -= 2; fl = F(fz, false, true); fv = F(fz, true, false); }
            float ty = y + bh + 4;
            Txt(label, fl, t.Tx, x, ty, w, item - bh - 4, 0);
            Txt(val, fv, t.Tx, x, ty, w, item - bh - 4, 2);
        }

        /// <summary>Dikeyde eşit dağıtımla n adet stat çizer.</summary>
        private void StatColumn(float x, float y, float w, float h, string[] labels, string[] vals, double[] pcts, bool[] hots)
        {
            int n = labels.Length;
            float item = Math.Min(58f, h / n);
            float gap = (h - n * item) / n;
            for (int i = 0; i < n; i++)
                Stat(x, y + gap / 2f + i * (item + gap), w, labels[i], vals[i], pcts[i], hots != null && hots[i], item);
        }

        private void Dial(float cx, float cy, float size, double pct, string big, string unit, string label, float bigSize)
        {
            Theme t = _th;
            pct = Math.Max(0, Math.Min(100, pct));
            float k = size / 100f, rad = 40 * k, sw = 9 * k;
            RectangleF box = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
            var cap = (t.Style == "blueprint" || t.Style == "crt" || t.Style == "brutal" || t.Style == "hud") ? LineCap.Flat : LineCap.Round;
            if (t.Style == "brutal") sw = 12 * k;
            using (var tp = new Pen(t.Track, sw)) { tp.StartCap = cap; tp.EndCap = cap; G.DrawArc(tp, box, 135, 270); }
            float sweep = (float)(270.0 * pct / 100.0);
            if (sweep > 0.5f)
            {
                using (var br = new LinearGradientBrush(new PointF(cx - 38 * k, cy + 38 * k), new PointF(cx + 38 * k, cy - 38 * k), t.A1, t.A2))
                {
                    if (t.Glow)
                        using (var gp = new Pen(Al(t.A1, 45), sw + 10 * k)) { gp.StartCap = cap; gp.EndCap = cap; G.DrawArc(gp, box, 135, sweep); }
                    using (var ap = new Pen(br, sw)) { ap.StartCap = cap; ap.EndCap = cap; G.DrawArc(ap, box, 135, sweep); }
                }
            }
            Font fb = F(bigSize, true, false), fu = F(bigSize * 0.38f, false, false);
            float bw = TW(big, fb), uw = string.IsNullOrEmpty(unit) ? 0 : TW(unit, fu) + 4;
            float x0 = cx - (bw + uw) / 2f, cyy = cy - size * 0.03f;
            Txt(big, fb, t.Tx, x0, cyy - bigSize * 0.55f, bw + 10, bigSize * 1.1f, 0);
            if (uw > 0) Txt(unit, fu, t.Tx, x0 + bw + 4, cyy - bigSize * 0.30f + bigSize * 0.16f, uw + 10, bigSize * 0.5f, 0);
            if (!string.IsNullOrEmpty(label)) Txt(label, F(28, false, true), t.Dim, cx - size / 2, cy + size * 0.5f - 20, size, 34, 1);
        }

        private void Graph(RectangleF r, string key, float floor, float fixedMax, bool labels, bool fill)
        {
            Theme t = _th;
            float[] a = H(key);
            if (r.Width < 10 || r.Height < 10) return;
            float mx = floor, mn = float.MaxValue, top = 0;
            foreach (float v in a) { if (v > mx) mx = v; if (v < mn) mn = v; if (v > top) top = v; }
            mx = fixedMax > 0 ? fixedMax : mx * 1.06f;
            float pad = 3, step = r.Width / (a.Length - 1);
            Func<float, float> Y = v => r.Bottom - pad - Math.Min(1f, v / mx) * (r.Height - pad * 2);

            using (var dp = new Pen(Al(t.Line, 140), 1)) { dp.DashStyle = DashStyle.Dot; for (int i = 1; i < 4; i++) { float yy = (float)Math.Round(r.Y + r.Height / 4f * i) + 0.5f; G.DrawLine(dp, r.X, yy, r.Right, yy); } }

            var pts = new List<PointF>(a.Length * 2);
            for (int i = 0; i < a.Length; i++)
            {
                float x = r.X + i * step, y = Y(a[i]);
                if (i > 0) pts.Add(new PointF(x, Y(a[i - 1])));
                pts.Add(new PointF(x, y));
            }
            if (fill)
            {
                var poly = new List<PointF>(pts);
                poly.Add(new PointF(r.Right, r.Bottom)); poly.Add(new PointF(r.X, r.Bottom));
                using (var lg = new LinearGradientBrush(r, Al(t.A1, 150), Al(t.A1, 24), 90f))
                    G.FillPolygon(lg, poly.ToArray());
            }
            G.DrawLines(Pn(t.A1, 2), pts.ToArray());
            if (labels)
            {
                Font f = F(17, true, false);
                Txt(I0(top), f, Al(t.Tx, 230), r.X + 6, r.Y + 2, 80, 22, 0);
                Txt(I0(mn == float.MaxValue ? 0 : mn), f, Al(t.Tx, 230), r.X + 6, r.Bottom - 24, 80, 22, 0);
            }
        }

        private void Caption(float x, float y, float w, string left, string leftVal, string right, string rightVal)
        {
            Theme t = _th;
            Font f = F(18, true, false);
            Txt(left + " " + leftVal, f, t.Dim, x, y, w / 2, 24, 0);
            if (right != null) Txt(right + " " + rightVal, f, t.Dim, x + w / 2, y, w / 2, 24, 2);
        }

        // ---------------- ana çizim ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            G = e.Graphics;
            G.SmoothingMode = SmoothingMode.AntiAlias;
            G.TextRenderingHint = TextRenderingHint.AntiAlias;
            G.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Theme t = _th;
            G.Clear(t.Bg);

            float W = ClientSize.Width, Hh = ClientSize.Height;
            float aw = W - _margin * 2, ah = Hh - _margin * 2;
            float sc = Math.Max(0.1f, Math.Min(aw / 1920f, ah / 1080f));
            DrawBackground(W, Hh, sc);

            G.TranslateTransform(_margin + (aw - 1920 * sc) / 2f, _margin + (ah - 1080 * sc) / 2f);
            G.ScaleTransform(sc, sc);

            Snap s = _snap;
            if (s == null)
            {
                Txt("Veriler okunuyor…", F(48, false, true), t.Dim, 0, 0, 1920, 1080, 1);
            }
            else
            {
                if (_layout == "hud") DrawHud(s);
                else if (_layout == "cockpit") DrawCockpit(s);
                else DrawClassic(s);

                if (!string.IsNullOrEmpty(s.Throttle))
                {
                    Font f = F(20, true, false);
                    string m = "⚠ " + s.Throttle;
                    float w = TW(m, f) + 28;
                    var r = new RectangleF(1920 - w - 24, 1080 - 26, w, 26);
                    using (GraphicsPath p = RR(r, 8)) { G.FillPath(Br(Al(t.Crit, 235)), p); }
                    Txt(m, f, Color.White, r.X, r.Y, r.Width, r.Height, 1);
                }
            }

            if (t.Style == "crt")
            {
                G.ResetTransform();
                G.SmoothingMode = SmoothingMode.None;
                Pen sp = Pn(Color.FromArgb(70, 0, 0, 0), 1);
                for (float y = 0; y < Hh; y += 3) G.DrawLine(sp, 0, y, W, y);
            }
        }

        private void DrawBackground(float W, float Hh, float sc)
        {
            Theme t = _th;
            string fx = t.Fx ?? "";
            if (fx.StartsWith("grid"))
            {
                string[] p = fx.Split(':');
                Color gc = Al(ColorFromHex(p[1]), t.Id == "blueprint" ? 24 : 18);
                float sp = (t.Id == "blueprint" ? 40 : 64) * sc;
                Pen gp = Pn(gc, 1);
                for (float x = 0; x < W; x += sp) G.DrawLine(gp, x, 0, x, Hh);
                for (float y = 0; y < Hh; y += sp) G.DrawLine(gp, 0, y, W, y);
                if (t.Id == "synth")
                {
                    using (var lg = new LinearGradientBrush(new RectangleF(0, Hh * 0.55f, W, Hh * 0.45f + 1), Color.Transparent, Al(ColorFromHex(p[2]), 60), 90f))
                        G.FillRectangle(lg, 0, Hh * 0.55f, W, Hh * 0.45f + 1);
                }
            }
            else if (fx.StartsWith("glow"))
            {
                string[] p = fx.Split(':');
                GlowAt(W * 0.1f, Hh * 0.05f, W * 0.55f, Al(ColorFromHex(p[1]), 52));
                GlowAt(W * 0.95f, Hh * 1.0f, W * 0.6f, Al(ColorFromHex(p[2]), 34));
            }
            else if (fx == "hatch")
            {
                Pen hp = Pn(Color.FromArgb(9, 255, 255, 255), 1);
                float st = 8 * sc + 2;
                for (float k = -Hh; k < W; k += st) { G.DrawLine(hp, k, 0, k + Hh, Hh); G.DrawLine(hp, k, Hh, k + Hh, 0); }
            }
        }

        private void GlowAt(float cx, float cy, float rad, Color c)
        {
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - rad, cy - rad * 0.75f, rad * 2, rad * 1.5f);
                using (var pg = new PathGradientBrush(gp))
                {
                    pg.CenterColor = c;
                    pg.SurroundColors = new[] { Color.Transparent };
                    G.FillPath(pg, gp);
                }
            }
        }

        private static Color ColorFromHex(string h)
        {
            h = h.TrimStart('#');
            if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
            return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }

        // ---------------- yerleşim yardımcıları ----------------
        private static RectangleF[] Row(float y, float h, params float[] ws)
        {
            var r = new RectangleF[ws.Length];
            float x = 10;
            for (int i = 0; i < ws.Length; i++) { r[i] = new RectangleF(x, y, ws[i], h); x += ws[i] + 10; }
            return r;
        }

        private static bool Hot(double v, double lim) { return !double.IsNaN(v) && v >= lim; }

        // ---------------- paneller ----------------
        private void PCpu(RectangleF r, Snap s, bool compact)
        {
            RectangleF c = Card(r, "CPU", Shorten(s.CpuModel, 34), Tc(s.CpuTemp));
            float dsz = Math.Min(c.Height, c.Width * 0.42f);
            Dial(c.X + dsz / 2, c.Y + c.Height / 2, dsz, s.CpuLoad, I0(s.CpuLoad), "%", "YÜK", dsz * 0.30f);
            float sx = c.X + dsz + 24, sw = c.Right - sx;
            StatColumn(sx, c.Y, sw, c.Height,
                new[] { "Saat", "Güç", "Fan" },
                new[] { Mhz(s.CpuClock), Wt(s.CpuW), Rpm(s.CpuFan) },
                new[] { Ratio(s.CpuClock, 6000), Ratio(s.CpuW, 250), Ratio(s.CpuFan, 3000) },
                new[] { false, false, false });
        }

        private void PGpu(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "GPU", Shorten(s.GpuModel, 34), Tc(s.GpuTemp));
            float dsz = Math.Min(c.Height, c.Width * 0.42f);
            Dial(c.X + dsz / 2, c.Y + c.Height / 2, dsz, s.GpuLoad, I0(s.GpuLoad), "%", "YÜK", dsz * 0.30f);
            float sx = c.X + dsz + 24, sw = c.Right - sx;
            StatColumn(sx, c.Y, sw, c.Height,
                new[] { "Saat", "Güç", "VRAM" },
                new[] { Mhz(s.GpuClock), Wt(s.GpuW), s.VramTotal > 0 ? I0(s.VramUsed) + " / " + I0(s.VramTotal) + " MB" : Mb(s.VramUsed) },
                new[] { Ratio(s.GpuClock, 3000), Ratio(s.GpuW, 450), Ratio(s.VramUsed, s.VramTotal) },
                new[] { false, false, Ratio(s.VramUsed, s.VramTotal) > 92 });
        }

        private void PMem(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "RAM", s.MemSpeed, Gb(s.MemUsed / 1024.0));
            float dsz = Math.Min(c.Height, c.Width * 0.42f);
            Dial(c.X + dsz / 2, c.Y + c.Height / 2, dsz, s.MemLoad, I0(s.MemLoad), "%", "DOLU", dsz * 0.30f);
            float sx = c.X + dsz + 24, sw = c.Right - sx;
            StatColumn(sx, c.Y, sw, c.Height,
                new[] { "Kullanılan", "Boş", "Toplam" },
                new[] { Gb(s.MemUsed / 1024.0), Gb(s.MemFree / 1024.0), Gb(s.MemTotal / 1024.0) },
                new[] { s.MemLoad, 100 - s.MemLoad, 100.0 },
                new[] { s.MemLoad > 90, false, false });
        }

        private void PFps(RectangleF r, Snap s, bool big)
        {
            string g = string.IsNullOrEmpty(s.Game) ? "Oyun algılanmadı" : s.Game;
            RectangleF c = Card(r, "FPS", g, s.FrameMs > 0 ? D1(s.FrameMs) + " ms" : null);
            float dsz = Math.Min(c.Height * (big ? 1f : 0.62f), c.Width * 0.5f);
            Dial(c.X + dsz / 2, c.Y + dsz / 2 + (big ? 0 : 0), dsz, Ratio(s.Fps, 240), s.Fps > 0 ? I0(s.Fps) : "--", "", "FPS", dsz * 0.34f);
            float sx = c.X + dsz + 20, sw = c.Right - sx;
            if (big)
            {
                Txt("%1 Düşük " + (double.IsNaN(s.Low1) ? "--" : I0(s.Low1)), F(30, true, false), _th.Tx, sx, c.Y + 8, sw, 38, 0);
                Graph(new RectangleF(sx, c.Y + 54, sw, c.Height - 54), "fps", 60, 0, true, true);
            }
            else
            {
                Txt("%1 Düşük " + (double.IsNaN(s.Low1) ? "--" : I0(s.Low1)), F(26, true, false), _th.Tx, sx, c.Y, sw, 34, 0);
                Graph(new RectangleF(sx, c.Y + 40, sw, c.Height - 40), "fps", 60, 0, true, true);
            }
        }

        private void PNet(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "AĞ", s.Ip, double.IsNaN(s.Ping) ? null : I0(s.Ping) + " ms");
            float half = (c.Width - 20) / 2;
            Caption(c.X, c.Y, half, "▲", Spd(s.NetUp), null, null);
            Caption(c.X + half + 20, c.Y, half, "▼", Spd(s.NetDown), null, null);
            float gh = c.Height - 62;
            Graph(new RectangleF(c.X, c.Y + 30, half, gh), "netUp", 100, 0, false, true);
            Graph(new RectangleF(c.X + half + 20, c.Y + 30, half, gh), "netDown", 100, 0, false, true);
            Txt("Toplam ▲ " + Tot(s.NetUpTot) + "   ▼ " + Tot(s.NetDownTot), F(20, false, false), _th.Dim, c.X, c.Bottom - 26, c.Width, 26, 0);
        }

        private void PDisks(RectangleF r, Snap s)
        {
            string rw = (double.IsNaN(s.DiskR) ? "--" : D1(s.DiskR)) + " / " + (double.IsNaN(s.DiskW) ? "--" : D1(s.DiskW)) + " MB/s";
            RectangleF c = Card(r, "DİSK", "O/Y", rw);
            int n = Math.Min(s.Drives.Length, 5);
            if (n == 0) { Txt("Disk bilgisi yok", F(26, false, true), _th.Dim, c.X, c.Y, c.Width, 40, 0); return; }
            string[] L = new string[n], V = new string[n]; double[] P = new double[n]; bool[] Hh = new bool[n];
            for (int i = 0; i < n; i++)
            {
                DriveTelemetry d = s.Drives[i];
                string nm = !string.IsNullOrEmpty(d.Mount) ? d.Mount : (!string.IsNullOrEmpty(d.Letter) ? d.Letter : d.Name);
                L[i] = nm + (double.IsNaN(d.TempC) ? "" : "  " + I0(d.TempC) + "°");
                V[i] = d.TotalGb > 0 ? D1(d.FreeGb) + " GB boş" : "--";
                P[i] = d.UsedPct; Hh[i] = d.UsedPct > 92;
            }
            StatColumn(c.X, c.Y, c.Width, c.Height, L, V, P, Hh);
        }

        private void PCores(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "ÇEKİRDEKLER", s.Cores.Length + " thread", null);
            int n = s.Cores.Length;
            if (n == 0) return;
            int cols = n > 16 ? 8 : (n > 8 ? 8 : 4);
            int rows = (n + cols - 1) / cols;
            float cw = c.Width / cols, ch = c.Height / rows;
            for (int i = 0; i < n; i++)
            {
                float x = c.X + (i % cols) * cw, y = c.Y + (i / cols) * ch;
                CoreTelemetry k = s.Cores[i];
                BarAt(x + 4, y + 4, cw - 14, k.LoadPct, k.LoadPct > 92, 12);
                Txt(I0(k.LoadPct) + "%", F(20, true, false), _th.Tx, x + 4, y + 20, cw / 2, 26, 0);
                if (cw >= 140) Txt(I0(k.ClockMhz), F(18, false, true), _th.Dim, x + cw / 2 - 6, y + 20, cw / 2 - 4, 26, 2);
            }
        }

        private void PProcs(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "İŞLEMLER", double.IsNaN(s.Procs) ? null : I0(s.Procs) + " adet", null);
            int n = Math.Min(s.TopProcs.Length, 6);
            float rh = Math.Min(48f, c.Height / Math.Max(1, n));
            for (int i = 0; i < n; i++)
            {
                ProcItem p = s.TopProcs[i];
                Txt(p.Name, F(26, false, false), _th.Tx, c.X, c.Y + i * rh, c.Width * 0.65f, rh, 0);
                Txt(I0(p.MemMb) + " MB", F(26, true, false), _th.A1, c.X + c.Width * 0.5f, c.Y + i * rh, c.Width * 0.5f, rh, 2);
            }
        }

        private void PClock(RectangleF r, Snap s)
        {
            DateTime now = DateTime.Now;
            var tr = new CultureInfo("tr-TR");
            RectangleF c = Card(r, "SAAT", now.ToString("d MMMM dddd", tr), Extras.WxTemp);
            bool narrow = c.Width < 700;
            string tm = now.ToString("HH:mm", tr);
            float areaW = narrow ? c.Width * 0.62f : c.Width * 0.5f;
            float fs = Math.Min(150, narrow ? c.Height * 0.3f : c.Height * 0.62f);
            Font ft = F(fs, true, false);
            while (fs > 30 && TW(tm, ft) > areaW) { fs -= 4; ft = F(fs, true, false); }
            float th = narrow ? c.Height * 0.42f : c.Height * 0.8f;
            Txt(tm, ft, _th.Tx, c.X, c.Y, areaW + 10, th, 0);
            float sw = TW(tm, ft);
            Txt(now.ToString("ss", tr), F(Math.Max(26, fs * 0.4f), false, true), _th.A1, c.X + sw + 8, c.Y, 80, th, 0);
            Txt(Extras.WxDesc, F(24, false, true), _th.Dim, c.X, narrow ? c.Y + th : c.Bottom - 40, c.Width * 0.5f, 36, 0);
            int next = NextPrayer(now);
            if (narrow)
            {
                float py = c.Y + th + 36, ph = (c.Bottom - py) / 3f, cw = c.Width / 2f;
                for (int i = 0; i < 6; i++)
                {
                    float x = c.X + (i % 2) * cw, y = py + (i / 2) * ph;
                    Color col = i == next ? _th.A1 : _th.Tx;
                    Txt(Extras.PrayerNames[i], F(22, i == next, false), col, x, y, cw * 0.5f, ph, 0);
                    Txt(Extras.Prayer[i], F(22, true, false), col, x + cw * 0.35f, y, cw * 0.55f, ph, 2);
                }
            }
            else
            {
                float px = c.X + c.Width * 0.58f, pw = c.Width * 0.42f, ph = c.Height / 6f;
                for (int i = 0; i < 6; i++)
                {
                    Color col = i == next ? _th.A1 : _th.Tx;
                    Txt(Extras.PrayerNames[i], F(24, i == next, false), col, px, c.Y + i * ph, pw / 2, ph, 0);
                    Txt(Extras.Prayer[i], F(24, true, false), col, px + pw / 2, c.Y + i * ph, pw / 2, ph, 2);
                }
            }
        }

        private static int NextPrayer(DateTime now)
        {
            int m = now.Hour * 60 + now.Minute;
            for (int i = 0; i < 6; i++)
            {
                string[] hm = Extras.Prayer[i].Split(':');
                int h, mi;
                if (hm.Length == 2 && int.TryParse(hm[0], out h) && int.TryParse(hm[1], out mi) && h * 60 + mi > m) return i;
            }
            return 0;
        }

        private void PSys(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "SİSTEM", s.Uptime, s.Brand);
            string[] k = { "Anakart", "Yonga seti", "BIOS", "Ses", "Anakart ısı" };
            string[] v = { s.MoboModel, s.Chipset, s.Bios, double.IsNaN(s.Sound) ? "--" : I0(s.Sound) + "%", Tc(s.MoboTemp) };
            float rh = c.Height / k.Length;
            for (int i = 0; i < k.Length; i++)
            {
                Txt(k[i], F(24, false, true), _th.Dim, c.X, c.Y + i * rh, c.Width * 0.3f, rh, 0);
                Txt(v[i], F(24, true, false), _th.Tx, c.X + c.Width * 0.3f, c.Y + i * rh, c.Width * 0.7f, rh, 0);
            }
        }

        private void PPower(RectangleF r, Snap s)
        {
            double tot = (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW);
            RectangleF c = Card(r, "GÜÇ", "CPU + GPU", I0(tot) + " W");
            Graph(new RectangleF(c.X, c.Y, c.Width, c.Height - 4), "totalW", 100, 0, true, true);
        }

        private void PTemps(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "SICAKLIK", "30 sn", Tc(s.CpuTemp));
            Graph(new RectangleF(c.X, c.Y, c.Width, c.Height - 4), "cpuTemp", 60, 0, true, true);
        }

        private void PGpuGraph(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "GPU YÜK", "geçmiş", Pct(s.GpuLoad));
            Graph(new RectangleF(c.X, c.Y, c.Width, c.Height - 4), "gpuLoad", 100, 100, true, true);
        }

        private void PFans(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "FANLAR", "RPM", null);
            StatColumn(c.X, c.Y, c.Width, c.Height,
                new[] { "CPU", "Kasa 1", "Kasa 2", "Pompa", "GPU" },
                new[] { Rpm(s.CpuFan), Rpm(s.Cha1), Rpm(s.Cha2), Rpm(s.Pump), Rpm(s.GpuFan) },
                new[] { Ratio(s.CpuFan, 3000), Ratio(s.Cha1, 3000), Ratio(s.Cha2, 3000), Ratio(s.Pump, 4000), Ratio(s.GpuFan, 3500) }, null);
        }

        private void PVolts(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "GERİLİM", null, null);
            StatColumn(c.X, c.Y, c.Width, c.Height,
                new[] { "CPU Vcore", "GPU Volt" },
                new[] { Vt(s.CpuV), Vt(s.GpuV) },
                new[] { Ratio(s.CpuV, 1.6), Ratio(s.GpuV, 1.2) }, null);
        }

        private static string Shorten(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        // ---------------- düzenler ----------------
        private void DrawClassic(Snap s)
        {
            RectangleF[] a = Row(10, 236, 470, 470, 940);
            PCpu(a[0], s, true); PGpu(a[1], s); PClock(a[2], s);
            RectangleF[] b = Row(256, 560, 945, 945);
            PCores(new RectangleF(b[0].X, b[0].Y, b[0].Width, 275), s);
            PMem(new RectangleF(b[0].X, b[0].Y + 285, b[0].Width, 275), s);
            PNet(new RectangleF(b[1].X, b[1].Y, b[1].Width, 275), s);
            PDisks(new RectangleF(b[1].X, b[1].Y + 285, b[1].Width, 275), s);
            RectangleF[] c = Row(826, 244, 950, 323, 607);
            PFps(c[0], s, false); PProcs(c[1], s); PSys(c[2], s);
        }

        private void DrawHud(Snap s)
        {
            RectangleF[] a = Row(10, 640, 836, 522, 522);
            PFps(a[0], s, true);
            PCpu(new RectangleF(a[1].X, a[1].Y, a[1].Width, 315), s, true);
            PGpu(new RectangleF(a[1].X, a[1].Y + 325, a[1].Width, 315), s);
            PTemps(new RectangleF(a[2].X, a[2].Y, a[2].Width, 315), s);
            PGpuGraph(new RectangleF(a[2].X, a[2].Y + 325, a[2].Width, 315), s);
            float w = 372;
            RectangleF[] b = Row(660, 410, w, w, w, w, w);
            PMem(b[0], s); PNet(b[1], s); PDisks(b[2], s); PPower(b[3], s); PClock(b[4], s);
        }

        private void DrawCockpit(Snap s)
        {
            RectangleF[] a = Row(10, 300, 570, 798, 512);
            PCpu(a[0], s, true); PCores(a[1], s); PGpu(a[2], s);
            RectangleF[] b = Row(320, 460, 596.8f, 686.3f, 596.8f);
            PFps(b[0], s, false); PTemps(new RectangleF(b[1].X, b[1].Y, b[1].Width, 225), s);
            PPower(new RectangleF(b[1].X, b[1].Y + 235, b[1].Width, 225), s);
            PMem(new RectangleF(b[2].X, b[2].Y, b[2].Width, 225), s);
            PNet(new RectangleF(b[2].X, b[2].Y + 235, b[2].Width, 225), s);
            RectangleF[] c = Row(790, 280, 561, 467.5f, 374, 467.5f);
            PDisks(c[0], s); PFans(c[1], s); PVolts(c[2], s); PClock(c[3], s);
        }
    }
}
