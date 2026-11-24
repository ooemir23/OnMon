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
    public sealed partial class DashboardForm : Form
    {
        private const int HN = 90;
        private Snap _snap;
        private bool _dirty = true;
        private int _lastSecond = -1;
        internal string _layout = "classic";
        internal Theme _th = Themes.All[0];
        internal int _margin;
        internal string _city = "istanbul";
        private DateTime _lastMouse = DateTime.UtcNow;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly Dictionary<string, float[]> _hist = new Dictionary<string, float[]>();
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        private readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        private readonly StringFormat[] _sf = new StringFormat[3];
        private Graphics G;
        private string _fontMain = "Segoe UI", _fontLight = "Segoe UI", _fontMono = "Consolas";
        private bool _curHidden;
        private const float MicW = 440f;
        internal bool _audioBar = true, _micBar = true, _volBar = true, _hideStrip = false, _autoGame = true, _topBar = true, _lowCpu = true;
        private const float InfoH = 36f;
        private string _prevLayout; // kullanılmıyor
        private bool StripOn { get { return !_hideStrip && (_audioBar || _micBar || _volBar); } }
        private const float VolW = 300f;
        private float _lastVol; private bool _lastVolMuted; private RectangleF _volRect; private bool _volDrag;
        private float _lastMic = -1; private bool _lastMuted;
        private RectangleF _infoBtn, _infoPanel; private bool _infoOpen;
        private void ToggleInfo()
        {
            _infoOpen = !_infoOpen;
            if (_infoOpen) SysInfo.LoadAsync(delegate { _dirty = true; try { Invalidate(); } catch { } });
            _dirty = true; Invalidate();
        }
        private void DrawInfoPanel(Snap s, float barTop)
        {
            if (!_infoOpen) return;
            Theme t = _th;
            var rows = new List<KeyValuePair<string, string>>();
            if (s != null)
            {
                rows.Add(new KeyValuePair<string, string>(L.T("Sistem", "System"), (s.Brand + " " + s.MoboModel).Trim()));
                if (s.Bios.Length > 0 && SysInfo.Rows.Count == 0) rows.Add(new KeyValuePair<string, string>("BIOS", s.Bios));
            }
            foreach (var kv in SysInfo.Rows) rows.Add(kv);
            if (s != null)
            {
                if (s.Chipset.Length > 0) rows.Add(new KeyValuePair<string, string>(L.T("Yonga seti", "Chipset"), s.Chipset));
                if (s.CpuModel.Length > 0) rows.Add(new KeyValuePair<string, string>(L.T("İşlemci", "Processor"), s.CpuModel));
                if (s.GpuModel.Length > 0) rows.Add(new KeyValuePair<string, string>(L.T("Ekran kartı", "Graphics Card"), s.GpuModel + (s.GpuBus.Length > 0 ? "  (" + s.GpuBus + ")" : "")));
                if (s.MemSpeed.Length > 0) rows.Add(new KeyValuePair<string, string>(L.T("Bellek hızı", "Memory Speed"), s.MemSpeed));
                if (!double.IsNaN(s.SysBat))
                {
                    string bStr = I0(s.SysBat) + "%" + (s.SysBatChg ? L.T(" (Şarj oluyor)", " (Charging)") : "");
                    if (s.SysBatMin > 0 && !s.SysBatChg) bStr += " · " + AppPrefs.FmtMinutes(s.SysBatMin);
                    if (!double.IsNaN(s.SysBatW)) bStr += " · " + (s.SysBatW > 0 ? "+" : "") + s.SysBatW.ToString("0.0", CultureInfo.InvariantCulture) + " W";
                    rows.Add(new KeyValuePair<string, string>(L.T("Pil durumu", "Battery"), bStr));
                }
                if (!string.IsNullOrEmpty(s.WifiSsid))
                {
                    string wStr = s.WifiSsid + (s.WifiSignal >= 0 ? " (" + s.WifiSignal + "%)" : "");
                    rows.Add(new KeyValuePair<string, string>("Wi-Fi", wStr));
                }
            }
            if (!double.IsNaN(PeakStats.CpuTempMax) || !double.IsNaN(PeakStats.GpuTempMax))
            {
                string pk = "CPU " + AppPrefs.FmtTemp(PeakStats.CpuTempMin) + "–" + AppPrefs.FmtTemp(PeakStats.CpuTempMax) +
                            " · GPU " + AppPrefs.FmtTemp(PeakStats.GpuTempMin) + "–" + AppPrefs.FmtTemp(PeakStats.GpuTempMax) +
                            (!double.IsNaN(PeakStats.FpsMax) ? " · " + I0(PeakStats.FpsMax) + " FPS" : "");
                rows.Add(new KeyValuePair<string, string>(L.T("Oturum Peak", "Session Peak"), pk));
            }
            if (SysInfo.Boot != DateTime.MinValue)
            {
                TimeSpan up = DateTime.Now - SysInfo.Boot;
                string upTxt = L.IsEn
                    ? ((up.Days > 0 ? up.Days + "d " : "") + up.Hours + "h " + up.Minutes + "m")
                    : ((up.Days > 0 ? up.Days + " g " : "") + up.Hours + " sa " + up.Minutes + " dk");
                rows.Add(new KeyValuePair<string, string>(L.T("Açık kalma", "Uptime"), upTxt));
            }
            rows.Add(new KeyValuePair<string, string>("OnMon", "v" + Updater.AppVersion));
            float rh = 30, w = 760, h = 64 + rows.Count * rh + (SysInfo.Loading ? 30 : 0);
            _infoPanel = new RectangleF(14, barTop - h - 8, w, h);
            using (GraphicsPath gp = RR(_infoPanel, 14))
            {
                G.FillPath(Br(Color.FromArgb(252, 16, 18, 24)), gp);
                G.DrawPath(Pn(t.A1, 1.6f), gp);
            }
            Txt(L.T("SİSTEM BİLGİSİ", "SYSTEM INFORMATION"), F(18, true, false), t.A1, _infoPanel.X + 20, _infoPanel.Y + 10, 400, 30, 0);
            Txt(L.T("kapatmak için tıkla", "click to close"), F(14, false, true), t.Dim, _infoPanel.Right - 220, _infoPanel.Y + 12, 200, 26, 2);
            float yy = _infoPanel.Y + 50;
            Font fk = F(17, false, false), fv = F(17, true, false);
            foreach (var kv in rows)
            {
                Txt(kv.Key, fk, t.Dim, _infoPanel.X + 20, yy, 170, rh, 0);
                Txt(Fit(kv.Value, fv, w - 230), fv, t.Tx, _infoPanel.X + 200, yy, w - 220, rh, 0);
                yy += rh;
            }
            if (SysInfo.Loading) Txt(L.T("okunuyor…", "reading…"), fk, t.Dim, _infoPanel.X + 20, yy, 300, rh, 0);
        }
        internal int _bright = 100;          // uygulama parlaklığı %
        private RectangleF _brRect;
        private static readonly int[] BrLevels = { 100, 80, 60, 40, 25 };
        internal int EffectiveBright()
        {
            return AppPrefs.IsNightNow() ? AppPrefs.NightBright : _bright;
        }

        internal void NextBright()
        {
            int i = Array.IndexOf(BrLevels, _bright);
            _bright = BrLevels[(i + 1) % BrLevels.Length];
            SaveSettings(); _dirty = true; Invalidate();
        }
        private void DrawBrightBtn()
        {
            if (_brRect.Width <= 0) return;
            int eb = EffectiveBright();
            Theme t = _th; RectangleF r = _brRect;
            using (GraphicsPath bp = RR(r, r.Height / 2f)) { G.FillPath(Br(Color.FromArgb(255, 24, 26, 32)), bp); G.DrawPath(Pn(eb < 100 ? t.A1 : Color.FromArgb(255, 120, 124, 134), 1.4f), bp); }
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            Color c = eb < 100 ? t.A1 : Color.FromArgb(255, 200, 204, 214);
            G.FillEllipse(Br(c), cx - 4.5f, cy - 4.5f, 9, 9);
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                G.DrawLine(Pn(c, 1.6f), cx + (float)Math.Cos(a) * 7f, cy + (float)Math.Sin(a) * 7f, cx + (float)Math.Cos(a) * 10f, cy + (float)Math.Sin(a) * 10f);
            }
        }
        internal bool _desk = true;          // arka planda kal: diğer pencereler üstüne gelebilir, Win+D küçültmez
        private bool _userMin;
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);

        // Masaüstü pencereleri (Progman + WorkerW). "Masaüstünü göster" bunları öne çıkarır; pano onların hemen üstünde durarak görünür kalır.
        private static List<IntPtr> DeskWins()
        {
            var l = new List<IntPtr>();
            IntPtr pm = FindWindow("Progman", null); if (pm != IntPtr.Zero) l.Add(pm);
            IntPtr w = IntPtr.Zero;
            for (int i = 0; i < 8; i++) { w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null); if (w == IntPtr.Zero) break; l.Add(w); }
            return l;
        }

        private bool DeskAboveMe()
        {
            List<IntPtr> d = DeskWins(); if (d.Count == 0) return false;
            IntPtr cur = GetWindow(Handle, 3);   // GW_HWNDPREV: üstümdeki pencereler
            int guard = 0;
            while (cur != IntPtr.Zero && guard++ < 4000) { if (d.Contains(cur)) return true; cur = GetWindow(cur, 3); }
            return false;
        }

        // Panoyu masaüstünün hemen üstüne, diğer tüm pencerelerin altına yerleştirir.
        internal void SendToBottom()
        {
            try
            {
                if (!_desk || !IsHandleCreated || TopMost) return;
                List<IntPtr> d = DeskWins();
                IntPtr highest = IntPtr.Zero, h = GetWindow(Handle, 5);   // GW_HWNDLAST: en alttaki
                int guard = 0;
                while (h != IntPtr.Zero && guard++ < 4000) { if (d.Contains(h)) highest = h; h = GetWindow(h, 3); }
                IntPtr after = highest == IntPtr.Zero ? new IntPtr(1) : GetWindow(highest, 3);
                if (after == Handle) return;
                IntPtr ins;
                if (highest == IntPtr.Zero) ins = new IntPtr(1);                                  // masaüstü bulunamadı: en alta
                else if (after == IntPtr.Zero || (GetWindowLong(after, -20) & 0x8) != 0) ins = new IntPtr(-2);   // üstte yalnız topmost'lar var
                else ins = after;
                SetWindowPos(Handle, ins, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        private readonly System.Windows.Forms.Timer _deskTimer = new System.Windows.Forms.Timer { Interval = 500 };
        private bool _deskLogged, _deskTop;
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        private static string ClsOf(IntPtr h) { var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, 64); return sb.ToString(); }
        // Masaüstü odaktayken (Win+D) pano geçici olarak en üste çıkar; başka bir uygulama öne gelince yeniden arkaya iner.
        private void DeskFocusCheck()
        {
            IntPtr fg = GetForegroundWindow(); if (fg == IntPtr.Zero) return;
            string c = ClsOf(fg);
            bool desk = c == "Progman" || c == "WorkerW";
            if (desk && !_deskTop) { SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); _deskTop = true; }
            else if (_deskTop && !desk && fg != Handle && c != "Shell_TrayWnd" && c != "Shell_SecondaryTrayWnd" && c != "TopLevelWindowForOverflowXamlIsland" && c != "XamlExplorerHostIslandWindow")
            {
                uint pid; GetWindowThreadProcessId(fg, out pid);
                if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return;   // ayarlar penceresi vb.
                SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); _deskTop = false; SendToBottom();
            }
        }
        private void StartDeskWatch()
        {
            _deskTimer.Tick += delegate
            {
                if (!_desk || _userMin || !IsHandleCreated || IsDisposed) return;
                try
                {
                    DeskFocusCheck();
                    if (IsIconic(Handle) || !IsWindowVisible(Handle))
                    {
                        if (!_deskLogged) { _deskLogged = true; Program.Log("pano", new Exception("pano sistem tarafından küçültüldü/gizlendi, geri getiriliyor")); }
                        ShowWindow(Handle, 4);   // SW_SHOWNOACTIVATE
                        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                        SendToBottom(); Invalidate();
                    }
                }
                catch { }
            };
            _deskTimer.Start();
        }
        internal void UserMinimize() { _userMin = true; WindowState = FormWindowState.Minimized; }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; if (_desk) cp.ExStyle |= 0x80; return cp; }  // WS_EX_TOOLWINDOW: Alt+Tab'ta görünmez
        }
        protected override void WndProc(ref Message m)
        {
            // WM_SYSCOMMAND / SC_MINIMIZE: Win+D ve "tümünü küçült" panoyu küçültemesin (kullanıcı menüden küçültürse izin ver)
            if (m.Msg == 0x0112 && (m.WParam.ToInt64() & 0xFFF0) == 0xF020 && _desk && !_userMin) return;
            base.WndProc(ref m);
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; DwmSetWindowAttribute(Handle, 3, ref on, 4); } catch { }   // DWMWA_TRANSITIONS_FORCEDISABLED: küçült/aç animasyonu yok, Win+D'de titreme azalır
        }
        internal void ApplyDeskMode() { MinimizeBox = !_desk; }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_desk) BeginInvoke((Action)SendToBottom);
        }
        internal readonly HashSet<string> _favs = new HashSet<string>();
        internal readonly HashSet<string> _micFavs = new HashSet<string>();
        private readonly List<KeyValuePair<RectangleF, string>> _hits = new List<KeyValuePair<RectangleF, string>>();
        private float _ox, _oy, _sc = 1f;

        // ---------------- ayarlar ----------------
        private static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "ui.txt"); }
        }

        private void LoadSettings()
        {
            try
            {
                AppPrefs.LoadFromDisk();
                if (!File.Exists(SettingsPath)) return;
                bool hasDesk = false;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "layout" && v.Length > 0) _layout = v;
                    else if (k == "cw") { int q; if (int.TryParse(v, out q) && q >= 200 && q <= 8000) _cw = q; }
                    else if (k == "ch") { int q; if (int.TryParse(v, out q) && q >= 120 && q <= 8000) _ch = q; }
                    else if (k == "grid") { float q; if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out q) && q >= 1 && q <= 200) _grid = q; }
                    else if (k == "showgrid") _showGrid = v != "0";
                    else if (k == "snap") _snapOn = v != "0";
                    else if (k == "bgcol") { int q; int.TryParse(v, out q); _bgCol = q; }
                    else if (k == "bgimg") _bgImg = v;
                    else if (k == "gfs") { float q; if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out q) && q >= 0.3f && q <= 3f) _gfs = q; }
                    else if (k == "gzoom") { float q; if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out q) && q >= 0.2f && q <= 4f) _gzoom = q; }
                    else if (k == "rcedit") _rcEdit = v == "1";
                    else if (k == "audiobar") _audioBar = v != "0";
                    else if (k == "micbar") _micBar = v != "0";
                    else if (k == "volbar") _volBar = v != "0";
                    else if (k == "hidestrip") _hideStrip = v == "1";
                    else if (k == "autogame") _autoGame = v != "0";
                    else if (k == "topbar") _topBar = v != "0";
                    else if (k == "lowcpu2") { _lowCpu = v == "1"; Program.PollIntervalMs = _lowCpu ? 2000 : 1000; }
                    else if (k == "fanmap")
                    {
                        string[] fp = v.Split(new[] { ',' });
                        var fm = new int[4];
                        for (int q = 0; q < 4 && q < fp.Length; q++) int.TryParse(fp[q], out fm[q]);
                        TelemetryCollector.FanMap = fm;
                    }
                    else if (k == "topmost") TopMost = v == "1";
                    else if (k == "bright") { int bv; if (int.TryParse(v, out bv) && bv >= 20 && bv <= 100) _bright = bv; }
                    else if (k == "desk") { hasDesk = true; _desk = v == "1"; }
                    else if (k == "micfav") { _micFavs.Clear(); foreach (string x in v.Split(new[] { '|' })) if (x.Length > 0) _micFavs.Add(x); }
                    else if (k == "soundfav") { _favs.Clear(); foreach (string x in v.Split(new[] { '|' })) if (x.Length > 0) _favs.Add(x); }
                    else if (k == "theme") _th = Themes.Get(v);
                    else if (k == "margin") int.TryParse(v, out _margin);
                    else if (k == "city" && v.Length > 0) _city = v;
                    else if (k == "lang" && (v == "en" || v == "tr")) L.Lang = v;
                }
                if (!hasDesk) { _desk = true; TopMost = false; }
                if (_desk) TopMost = false;
            }
            catch { }
        }

        internal void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, new[]
                {
                    "lang=" + L.Lang, "layout=" + _layout, "theme=" + _th.Id, "margin=" + _margin, "city=" + _city,
                    "audiobar=" + (_audioBar ? "1" : "0"), "micbar=" + (_micBar ? "1" : "0"), "volbar=" + (_volBar ? "1" : "0"),
                    "hidestrip=" + (_hideStrip ? "1" : "0"), "autogame=" + (_autoGame ? "1" : "0"), "topbar=" + (_topBar ? "1" : "0"),
                    "lowcpu2=" + (_lowCpu ? "1" : "0"), "fanmap=" + string.Join(",", TelemetryCollector.FanMap.Select(x => x.ToString()).ToArray()),
                    "topmost=" + (TopMost ? "1" : "0"), "desk=" + (_desk ? "1" : "0"), "bright=" + _bright,
                    "soundfav=" + string.Join("|", _favs), "micfav=" + string.Join("|", _micFavs),
                    "cw=" + _cw, "ch=" + _ch, "grid=" + _grid.ToString(CultureInfo.InvariantCulture),
                    "showgrid=" + (_showGrid ? "1" : "0"), "snap=" + (_snapOn ? "1" : "0"), "bgcol=" + _bgCol,
                    "bgimg=" + _bgImg, "gfs=" + _gfs.ToString(CultureInfo.InvariantCulture), "gzoom=" + _gzoom.ToString(CultureInfo.InvariantCulture),
                    "rcedit=" + (_rcEdit ? "1" : "0"),
                    "tempf=" + (AppPrefs.TempFahrenheit ? "1" : "0"),
                    "clock12h=" + (AppPrefs.Clock12h ? "1" : "0"),
                    "showprayer=" + (AppPrefs.ShowPrayer ? "1" : "0"),
                    "nightmode=" + (AppPrefs.NightMode ? "1" : "0"),
                    "nightstart=" + AppPrefs.NightStartHour,
                    "nightend=" + AppPrefs.NightEndHour,
                    "nightbright=" + AppPrefs.NightBright,
                    "overlay=" + (AppPrefs.OverlayEnabled ? "1" : "0"),
                    "overlaycorner=" + AppPrefs.OverlayCorner,
                    "overlayop=" + AppPrefs.OverlayOpacity,
                    "traymode=" + AppPrefs.TrayIconMode,
                    "alcpu=" + AppPrefs.AlertCpu,
                    "algpu=" + AppPrefs.AlertGpu,
                    "aldisk=" + AppPrefs.AlertDisk,
                    "alram=" + AppPrefs.AlertRam,
                    "albat=" + AppPrefs.AlertBat,
                    "alping=" + AppPrefs.AlertPing,
                    "albeep=" + (AppPrefs.AlertBeep ? "1" : "0"),
                    "onboarded=" + (AppPrefs.Onboarded ? "1" : "0")
                });
            }
            catch { }
        }

        internal void ReloadFromDisk()
        {
            LoadSettings();
            WLoad();
            Program.SyncOverlay();
            Program.RebuildTrayMenu();
            _dirty = true;
            Invalidate();
        }

        // ---------------- yaşam döngüsü ----------------
        public DashboardForm()
        {
            LoadSettings();
            WLoad();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = "OnMon";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = !_desk;   // küçültme düğmesi yoksa "masaüstünü göster" panoyu küçültmez
            MaximizeBox = false;
            try { Icon = Program.TrayIcon(); } catch { }
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

            ShutdownTimer.Load(); GameSession.LoadHistory(); AudioProfiles.Load(); Peripherals.StartBackground();
            AllowDrop = true;
            DragEnter += delegate (object s, DragEventArgs e)
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] fs = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (fs != null && fs.Any(x => x.EndsWith(".onmon", StringComparison.OrdinalIgnoreCase)))
                        e.Effect = DragDropEffects.Copy;
                }
            };
            DragDrop += delegate (object s, DragEventArgs e)
            {
                try
                {
                    string[] fs = e.Data != null ? (e.Data.GetData(DataFormats.FileDrop) as string[]) : null;
                    if (fs == null) return;
                    foreach (string f in fs)
                    {
                        if (f.EndsWith(".onmon", StringComparison.OrdinalIgnoreCase))
                        {
                            ImportAndApplyExternalTemplate(f);
                            break;
                        }
                    }
                }
                catch { }
            };
            BuildMenu();
            _timer.Interval = 500;
            _timer.Tick += delegate
            {
                if (_desk && !_userMin && WindowState == FormWindowState.Minimized) { WindowState = FormWindowState.Normal; SendToBottom(); }   // Win+D sonrası geri getir
                else if (_desk && !TopMost && WindowState != FormWindowState.Minimized && DeskAboveMe()) SendToBottom();   // masaüstü öne çıktıysa (Win+D) yine üstüne çık
                if (WindowState == FormWindowState.Minimized || !Visible) return;
                _tickN++;
                if (_micBar || WVis("sound"))
                {
                    AudioSwitch.PollMic();
                    bool mu = AudioSwitch.MicMuted;
                    if (mu != _lastMuted) { _lastMuted = mu; _dirty = true; }
                }
                if ((_volBar || WVis("sound")) && (_tickN & 3) == 0)
                {
                    AudioSwitch.PollVolume();
                    float vl = AudioSwitch.VolLevel; bool vm = AudioSwitch.VolMuted;
                    if (vm != _lastVolMuted || Math.Abs(vl - _lastVol) > 0.004f) { _lastVol = vl; _lastVolMuted = vm; _dirty = true; }
                }
                int min = DateTime.Now.Minute;
                if (min != _lastSecond) { _lastSecond = min; _dirty = true; }
                if (!_curHidden && (DateTime.UtcNow - _lastMouse).TotalSeconds > 3 && ContextMenuStrip != null && !ContextMenuStrip.Visible && (_settings == null || _settings.IsDisposed)) { _curHidden = true; Cursor.Hide(); }
                if (_dirty) { _dirty = false; Invalidate(); }
            };
            _timer.Start();
            StartDeskWatch();
            Extras.Start(_city);
        }

        private Rectangle _target;
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (WindowState == FormWindowState.Normal && _target.Width > 0 && Bounds != _target) Bounds = _target;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_desk) BeginInvoke((Action)SendToBottom);
            if (_target.Width > 0) { Bounds = _target; Invalidate(); }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _lastMouse = DateTime.UtcNow;
            if (_edit && _drag != 0 && _sc > 0) EditMouseMove((e.X - _ox) / _sc, (e.Y - _oy) / _sc);
            if (_volDrag && _sc > 0 && _volRect.Width > 0 && e.Button == MouseButtons.Left) SetVolumeTo(((e.X - _ox) / _sc - _volRect.X) / _volRect.Width);
            if (_curHidden) { _curHidden = false; Cursor.Show(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_edit) { EditKey(e); return; }
            if (e.KeyCode == Keys.E) ToggleEdit();
            else if (e.KeyCode == Keys.D1) SetLayout("classic");
            else if (e.KeyCode == Keys.D2) SetLayout("hud");
            else if (e.KeyCode == Keys.D3) SetLayout("cockpit");
            else if (e.KeyCode == Keys.D4) SetLayout("aida");
            else if (e.KeyCode == Keys.D5) SetLayout("studio");
            else if (e.KeyCode == Keys.Up) ChangeVolume(0.05f);
            else if (e.KeyCode == Keys.Down) ChangeVolume(-0.05f);
            else if (e.KeyCode == Keys.V) ToggleVolMute();
            else if (e.KeyCode == Keys.P) NextProfile();
            else if (e.KeyCode == Keys.T) NextTheme();
            else if (e.KeyCode == Keys.S) NextAudio();
            else if (e.KeyCode == Keys.M) ToggleMic();
            else if (e.KeyCode == Keys.F1 || e.KeyCode == Keys.F2) OpenSettings();
            else if (e.KeyCode == Keys.B) ToggleStrip();
            else if (e.KeyCode == Keys.L) NextBright();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_desk && WindowState == FormWindowState.Minimized && !_userMin)
            {   // Win+D / "tüm pencereleri küçült": pano masaüstünde kalsın
                BeginInvoke((Action)delegate { if (!IsDisposed && WindowState == FormWindowState.Minimized) { WindowState = FormWindowState.Normal; SendToBottom(); } });
                return;
            }
            if (WindowState != FormWindowState.Minimized) _userMin = false;
            Program.DashVisible = WindowState != FormWindowState.Minimized && Visible;
            if (WindowState == FormWindowState.Minimized && _frame != null) { _frame.Dispose(); _frame = null; _twCache.Clear(); Program.TrimProcessMemory(); }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            Program.DashVisible = WindowState != FormWindowState.Minimized && Visible;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Program.DashVisible = false;
            if (_frame != null) { _frame.Dispose(); _frame = null; }
            base.OnFormClosed(e);
            _timer.Stop();
            try { if (_settings != null && !_settings.IsDisposed) _settings.Close(); } catch { }
            try { if (_studioForm != null && !_studioForm.IsDisposed) _studioForm.Close(); } catch { }
            try { if (_galleryForm != null && !_galleryForm.IsDisposed) _galleryForm.Close(); } catch { }
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
            int ev = GameSession.Update(s);
            if (_autoGame)
            {
                GameLayout(ev);
            }
            Add("fps", s.Fps);
            Add("frame", s.FrameMs);
            Add("cpuTemp", s.CpuTemp);
            Add("gpuLoad", s.GpuLoad);
            Add("netUp", s.NetUp);
            Add("netDown", s.NetDown);
            Add("ping", s.Ping);
            Add("totalW", (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW));
            _source = s.Source;
            if (WindowState != FormWindowState.Minimized) _dirty = true;
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
        private sealed class MenuColors : ProfessionalColorTable
        {
            public Color Accent = Color.FromArgb(255, 74, 0);
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 26, 31); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 26, 31); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 26, 31); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 26, 31); } }
            public override Color MenuBorder { get { return Color.FromArgb(58, 62, 72); } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(44, 48, 58); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(44, 48, 58); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(44, 48, 58); } }
            public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(52, 56, 68); } }
            public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(52, 56, 68); } }
            public override Color SeparatorDark { get { return Color.FromArgb(52, 56, 66); } }
            public override Color SeparatorLight { get { return Color.FromArgb(24, 26, 31); } }
            public override Color CheckBackground { get { return Color.Transparent; } }
            public override Color CheckSelectedBackground { get { return Color.Transparent; } }
            public override Color CheckPressedBackground { get { return Color.Transparent; } }
        }

        private sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly MenuColors _c;
            public MenuRenderer(MenuColors c) : base(c) { _c = c; RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                string tag = e.Item.Tag as string;
                if (tag == "hdr") e.TextColor = _c.Accent;
                else if (tag == "sec") e.TextColor = Color.FromArgb(120, 126, 140);
                else if (tag == "dim") e.TextColor = Color.FromArgb(130, 136, 150);
                else e.TextColor = e.Item.Enabled ? Color.FromArgb(232, 234, 238) : Color.FromArgb(110, 114, 124);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Color.FromArgb(170, 176, 190);
                base.OnRenderArrow(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var r = e.ImageRectangle;
                var g = e.Graphics;
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(_c.Accent))
                {
                    float d = 8;
                    g.FillEllipse(br, r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
                }
                g.SmoothingMode = old;
            }
        }

        private ToolStripMenuItem Mi(string text, string key, bool check, EventHandler click)
        {
            var it = new ToolStripMenuItem(text);
            it.Padding = new Padding(2, 5, 2, 5);
            it.Checked = check;
            if (!string.IsNullOrEmpty(key)) it.ShortcutKeyDisplayString = key;
            if (click != null) it.Click += click;
            return it;
        }

        private ToolStripMenuItem Sec(string text)
        {
            var it = new ToolStripMenuItem((text ?? "").ToUpper(L.Culture));
            it.Tag = "sec";
            it.Enabled = false;
            it.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            return it;
        }

        internal string _source = "";

        private void BuildMenu()
        {
            var colors = new MenuColors();
            var m = new ContextMenuStrip();
            m.Renderer = new MenuRenderer(colors);
            m.Font = new Font("Segoe UI", 10f);
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Opening += delegate (object so, System.ComponentModel.CancelEventArgs ce) { if (_edit) { ce.Cancel = true; ShowEditMenu(); } else if (_rcEdit) { ce.Cancel = true; ToggleEdit(); } else { colors.Accent = _th.A1; PopulateMenu(m); } };
            ContextMenuStrip = m;
        }

        private SettingsForm _settings;
        internal StudioForm _studioForm;
        internal GalleryForm _galleryForm;

        internal void RefreshLanguage()
        {
            _dirty = true; Invalidate();
            Action rebuild = delegate
            {
                if (IsDisposed) return;
                bool reopenSet = _settings != null && !_settings.IsDisposed && _settings.Visible;
                bool reopenStu = _studioForm != null && !_studioForm.IsDisposed && _studioForm.Visible;
                bool reopenGal = _galleryForm != null && !_galleryForm.IsDisposed && _galleryForm.Visible;
                try { if (_settings != null && !_settings.IsDisposed) { _settings.Close(); _settings.Dispose(); } } catch { }
                try { if (_studioForm != null && !_studioForm.IsDisposed) { _studioForm.Close(); _studioForm.Dispose(); } } catch { }
                try { if (_galleryForm != null && !_galleryForm.IsDisposed) { _galleryForm.Close(); _galleryForm.Dispose(); } } catch { }
                _settings = null; _studioForm = null; _galleryForm = null;
                CloseEditorDialogs();
                if (reopenSet) OpenSettings();
                if (reopenStu) OpenStudio();
                if (reopenGal) OpenGallery();
                _dirty = true; Invalidate();
            };
            if (IsHandleCreated) BeginInvoke(rebuild); else rebuild();
        }

        internal void OpenSettings()
        {
            if (_settings != null && !_settings.IsDisposed) { _settings.Show(); _settings.WindowState = FormWindowState.Normal; _settings.Activate(); return; }
            _settings = new SettingsForm(this);
            _settings.Show();
        }

        internal void OpenStudio()
        {
            if (_studioForm != null && !_studioForm.IsDisposed)
            {
                _studioForm.ReloadFromDashboard();
                _studioForm.Show();
                if (_studioForm.WindowState == FormWindowState.Minimized) _studioForm.WindowState = FormWindowState.Normal;
                _studioForm.Activate();
                return;
            }
            _studioForm = new StudioForm(this);
            _studioForm.Show();
        }

        internal void OpenGallery()
        {
            if (_galleryForm != null && !_galleryForm.IsDisposed)
            {
                _galleryForm.RefreshCards();
                _galleryForm.Show();
                if (_galleryForm.WindowState == FormWindowState.Minimized) _galleryForm.WindowState = FormWindowState.Normal;
                _galleryForm.Activate();
                return;
            }
            _galleryForm = new GalleryForm(this);
            _galleryForm.Show();
        }

        private void PopulateMenu(ContextMenuStrip m)
        {
            foreach (ToolStripItem old in new List<ToolStripItem>(m.Items.Cast<ToolStripItem>())) old.Dispose();
            m.Items.Clear();
            AudioSwitch.PollMic();

            string hdr = (_snap != null && !string.IsNullOrEmpty(_snap.Brand)) ? (_snap.Brand.ToUpper(L.Culture) + " · ONMON") : "ONMON SYSTEM MONITOR";
            m.Items.Add(new ToolStripMenuItem(hdr) { Tag = "hdr", Enabled = false, Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
            m.Items.Add(new ToolStripSeparator());
            var set = Mi(L.T("Ayarlar…", "Settings…"), "F2", false, delegate { OpenSettings(); });
            set.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            m.Items.Add(set);
            m.Items.Add(Mi(L.T("🎨 Düzen Stüdyosu (görsel editör)…", "🎨 Layout Studio (Visual Editor)…"), null, false, delegate { OpenStudio(); }));
            m.Items.Add(Mi(L.T("📁 Düzen Galerisi / Düzen Ekle…", "📁 Layout Gallery / Import…"), null, false, delegate { OpenGallery(); }));
            m.Items.Add(Mi(L.T("Pano üstü hızlı düzenle", "Quick On-Screen Edit"), "E", false, delegate { ToggleEdit(); }));
            m.Items.Add(Mi(AudioSwitch.MicMuted ? L.T("Mikrofonu aç", "Unmute Microphone") : L.T("Mikrofonu sustur", "Mute Microphone"), "M", false, delegate { ToggleMic(); }));

            var langMenu = new ToolStripMenuItem("🌐 Dil / Language");
            langMenu.Padding = new Padding(2, 5, 2, 5);
            langMenu.DropDownItems.Add(Mi("Türkçe", null, !L.IsEn, delegate { L.Set("tr", this); }));
            langMenu.DropDownItems.Add(Mi("English", null, L.IsEn, delegate { L.Set("en", this); }));
            m.Items.Add(langMenu);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Mi(L.T("Küçült", "Minimize"), null, false, delegate { UserMinimize(); }));
            m.Items.Add(Mi(L.T("Panoyu kapat", "Close Dashboard"), "", false, delegate { Close(); }));
            m.Items.Add(Mi(L.T("Uygulamadan çık", "Exit Application"), null, false, delegate { Program.ExitAppAsk(); }));
        }

        internal void MoveToScreen(Screen sc)
        {
            _target = sc.Bounds;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Bounds = _target;
            Invalidate();
        }

        private void MoveToNextScreen()
        {
            Screen[] all = Screen.AllScreens;
            if (all.Length < 2) return;
            Screen cur = Screen.FromControl(this);
            int i = Array.FindIndex(all, s => s.DeviceName == cur.DeviceName);
            _target = all[(i + 1) % all.Length].Bounds;
            Bounds = _target;
            Invalidate();
        }

        private void NextAudio()
        {
            var list = new List<AudioDevice>();
            foreach (AudioDevice d in AudioSwitch.Devices) if (_favs.Count == 0 || _favs.Contains(d.Id) || d.IsDefault) list.Add(d);
            if (list.Count < 2) return;
            int i = list.FindIndex(d => d.IsDefault);
            SwitchAudio(list[(i + 1) % list.Count].Id);
        }

        private int _profIdx = -1;
        internal void NextProfile()
        {
            if (AudioProfiles.All.Count == 0) return;
            _profIdx = (_profIdx + 1) % AudioProfiles.All.Count;
            ApplyProfile(AudioProfiles.All[_profIdx]);
        }

        internal void ApplyProfile(AudioProfile p)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { AudioProfiles.Apply(p); _dirty = true; });
        }

        internal void ToggleStrip() { _hideStrip = !_hideStrip; SaveSettings(); _dirty = true; Invalidate(); }

        internal void ChangeVolume(float d)
        {
            float target = Math.Max(0f, Math.Min(1f, AudioSwitch.VolLevel + d));
            AudioSwitch.VolLevel = target; _dirty = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { AudioSwitch.SetVolume(target); });
        }

        internal void SetVolumeTo(float v)
        {
            v = Math.Max(0f, Math.Min(1f, v));
            AudioSwitch.VolLevel = v; AudioSwitch.VolMuted = false; _dirty = true; Invalidate();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { AudioSwitch.SetVolume(v); });
        }

        internal void ToggleVolMute()
        {
            AudioSwitch.VolMuted = !AudioSwitch.VolMuted; _dirty = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { AudioSwitch.ToggleVolMute(); AudioSwitch.PollVolume(); _dirty = true; });
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _volDrag = false;
            if (_edit) EditMouseUp();
        }

        internal void ToggleMic()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { AudioSwitch.ToggleMicMute(); AudioSwitch.PollMic(); _dirty = true; });
        }

        internal void SwitchAudio(string id)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool wasMic = false;
                foreach (AudioDevice md in AudioSwitch.MicDevices) if (md.Id == id) wasMic = true;
                AudioSwitch.SetDefault(id);
                if (!wasMic)
                {
                    // Yeni ses kartından kısa bir bildirim sesi çal (geçişin çalıştığını duyarsın).
                    System.Threading.Thread.Sleep(350);
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
                _dirty = true;
            });
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _sc <= 0) return;
            float cx = (e.X - _ox) / _sc, cy = (e.Y - _oy) / _sc;
            if (_edit) { EditMouseDown(e.X, e.Y, cx, cy); return; }
            if (_infoOpen)
            {
                if (_infoBtn.Contains(cx, cy) || _infoPanel.Contains(cx, cy)) { ToggleInfo(); return; }
                _infoOpen = false; _dirty = true; Invalidate(); return;
            }
            foreach (var h in _hits)
                if (h.Key.Contains(cx, cy))
                {
                    if (h.Value == "edit:toggle") ToggleEdit();
                    else if (h.Value == "info:toggle") ToggleInfo();
                    else if (h.Value == "mic:toggle") ToggleMic();
                    else if (h.Value == "vol:mute") ToggleVolMute();
                    else if (h.Value == "out:next") NextAudio();
                    else if (h.Value == "bright:next") NextBright();
                    else if (h.Value == "media:prev") { MediaControl.Prev(); _dirty = true; Invalidate(); }
                    else if (h.Value == "media:play") { MediaControl.PlayPause(); _dirty = true; Invalidate(); }
                    else if (h.Value == "media:next") { MediaControl.Next(); _dirty = true; Invalidate(); }
                    else if (h.Value.StartsWith("prof:")) { int pi; if (int.TryParse(h.Value.Substring(5), out pi) && pi >= 0 && pi < AudioProfiles.All.Count) { _profIdx = pi; ApplyProfile(AudioProfiles.All[pi]); } }
                    else if (h.Value == "vol:bar") { _volRect = h.Key; _volDrag = true; SetVolumeTo((cx - h.Key.X) / h.Key.Width); }
                    else SwitchAudio(h.Value);
                    return;
                }
        }

        internal void SetLayout(string id) { if (_edit && _wSaved != null) _wSaved = null; ApplyTemplate(id); }
        internal void SetTheme(Theme t) { _th = t; SaveSettings(); _dirty = true; Invalidate(); }
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
            if (_fs != 1f) size = (float)Math.Round(size * _fs * 2f) / 2f;
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

        private bool _marqActive;
        private readonly List<Rectangle> _marqRects = new List<Rectangle>();
        internal double _paintMs;
        private int _tickN;
        // Sığmayan metni kayan yazı olarak çizer (sola hizalı); sığıyorsa normal çizer.
        private void MTxt(string s, Font f, Color c, float x, float y, float w, float h, int al)
        {
            if (string.IsNullOrEmpty(s) || w < 10) return;
            Txt(Fit(s, f, w), f, c, x, y, w, h, al);
        }

        private readonly Dictionary<string, float> _twCache = new Dictionary<string, float>();
        private float TW(string s, Font f)
        {
            string key = f.Name + "|" + f.Size + "|" + (f.Bold ? "b" : "r") + "|" + s;
            float w;
            if (_twCache.TryGetValue(key, out w)) return w;
            w = G.MeasureString(s, f, 100000, _sf[0]).Width;
            if (_twCache.Count > 4000) _twCache.Clear();
            _twCache[key] = w;
            return w;
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
        private static string Tc(double v) { return AppPrefs.FmtTemp(v); }
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
            if (st == "none") return;
            if (st == "brutal")
            {
                G.FillRectangle(Br(t.A1), r.X + 7, r.Y + 7, r.Width, r.Height);
                G.FillRectangle(Br(t.Panel), r.X, r.Y, r.Width, r.Height);
                G.DrawRectangle(Pn(t.Line, FW(3)), r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3);
                return;
            }
            if (st == "clip")
            {
                using (GraphicsPath p = Clip(r, 26))
                {
                    G.FillPath(Br(t.Panel), p);
                    G.DrawPath(Pn(t.Line, FW(1)), p);
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
                    G.DrawPath(Pn(Al(t.Tx, 36), FW(1)), p);
                }
                else if (st == "neon")
                {
                    G.FillPath(Br(t.Panel), p);
                    G.DrawPath(Pn(Al(t.A1, 30), 8), p);
                    G.DrawPath(Pn(Al(t.A1, 60), 4), p);
                    G.DrawPath(Pn(Al(t.A1, 140), FW(1.5f)), p);
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
                    G.DrawPath(Pn(t.A1, FW(1)), p);
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
                    G.DrawPath(Pn(t.Line, FW(1)), p);
                }
            }
        }

        /// <summary>Panel + başlık çizer, içerik alanını döndürür.</summary>
        private RectangleF Card(RectangleF r, string title, string sub, string extra)
        {
            Panel(r);
            Theme t = _th;
            if (_wsNoTitle) return new RectangleF(r.X + 20, r.Y + 60, r.Width - 40, r.Height - 74);
            float x = r.X + 20, y = r.Y + 14;
            G.FillRectangle(Br(t.A1), x, y + 3, 5, 30);
            Font ft = F(30, true, false), fs = F(30, false, true);
            string T = (title ?? "").ToUpper(L.Culture);
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
            if (!string.IsNullOrEmpty(sub))
            {
                float sz = 30;
                while (sz > 20 && TW(sub, F(sz, false, true)) > avail) sz -= 2;
                Font fz = F(sz, false, true);
                Txt(Fit(sub, fz, avail), fz, t.Tx, sx, y, Math.Max(10, avail), 36, 0);
            }
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

        private float StatFont(float w, string label, string val, float item)
        {
            float fz = Math.Min(29f, item * 0.52f);
            while (fz > 14 && TW(label, F(fz, false, true)) + TW(val, F(fz, true, false)) + 14 > w) fz -= 2;
            return fz;
        }

        private void Stat(float x, float y, float w, string label, string val, double pct, bool hot, float item, float fz)
        {
            Theme t = _th;
            float bh = Math.Min(15f, item * 0.27f);
            BarAt(x, y, w, pct, hot, bh);
            Font fl = F(fz, false, true), fv = F(fz, true, false);
            float ty = y + bh + 4;
            Txt(label, fl, t.Tx, x, ty, w, item - bh - 4, 0);
            Txt(val, fv, t.Tx, x, ty, w, item - bh - 4, 2);
        }

        /// <summary>Dikeyde eşit dağıtımla n adet stat çizer (tüm satırlarda aynı yazı boyutu).</summary>
        private void StatColumn(float x, float y, float w, float h, string[] labels, string[] vals, double[] pcts, bool[] hots)
        {
            int n = labels.Length;
            float item = Math.Min(58f, h / n);
            float gap = (h - n * item) / n;
            float fz = 29f;
            for (int i = 0; i < n; i++) fz = Math.Min(fz, StatFont(w, labels[i], vals[i], item));
            for (int i = 0; i < n; i++)
                Stat(x, y + gap / 2f + i * (item + gap), w, labels[i], vals[i], pcts[i], hots != null && hots[i], item, fz);
        }

        private void Dial(float cx, float cy, float size, double pct, string big, string unit, string label, float bigSize)
        {
            Theme t = _th;
            pct = Math.Max(0, Math.Min(100, pct));
            float k = size / 100f, rad = 40 * k, sw = 9 * k;
            RectangleF box = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
            var cap = (t.Style == "blueprint" || t.Style == "crt" || t.Style == "brutal" || t.Style == "hud") ? LineCap.Flat : LineCap.Round;
            if (t.Style == "brutal") sw = 12 * k;
            if (_wsGw > 0) sw *= _wsGw;
            if (_wsCap == "round") cap = LineCap.Round; else if (_wsCap == "flat") cap = LineCap.Flat;
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
        private bool _loggedPaint;
        private Bitmap _frame;
        private bool _frameDirty = true;
        public new void Invalidate() { _frameDirty = true; base.Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) return;
            try
            {
                Size cs = ClientSize;
                if (cs.Width <= 0 || cs.Height <= 0) return;
                if (_frame == null || _frame.Width != cs.Width || _frame.Height != cs.Height)
                {
                    if (_frame != null) _frame.Dispose();
                    _frame = new Bitmap(cs.Width, cs.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    _frameDirty = true;
                }
                if (_frameDirty)
                {
                    _frameDirty = false;
                    var swp = System.Diagnostics.Stopwatch.StartNew();
                    using (Graphics fg = Graphics.FromImage(_frame)) PaintAll(fg);
                    _paintMs = _paintMs * 0.8 + swp.Elapsed.TotalMilliseconds * 0.2;
                }
                e.Graphics.DrawImageUnscaled(_frame, 0, 0);
                DrawMarquees(e.Graphics);
            }
            catch (Exception ex) { if (!_loggedPaint) { _loggedPaint = true; Program.Log("paint", ex); } }
        }

        private sealed class MItem { public string S; public Font F; public Color C; public float X, Y, W, H, Tw; public System.Drawing.Drawing2D.Matrix M; }
        private readonly List<MItem> _marqItems = new List<MItem>();

        private void DrawMarquees(Graphics g)
        {
            if (_marqItems.Count == 0) return;
            G = g;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            foreach (MItem it in _marqItems)
            {
                var st = g.Save();
                g.Transform = it.M;
                float scroll = it.Tw - it.W + 8;
                double sec = DateTime.UtcNow.Ticks / 1e7, period = scroll / 28.0 + 2.5, p = sec % period;
                float off = p < 1.0 ? 0 : (float)Math.Min(scroll, (p - 1.0) * 28.0);
                float need = it.F.Size * 1.45f, ch = Math.Max(it.H, need), cy = it.Y - (ch - it.H) / 2f;
                g.SetClip(new RectangleF(it.X, cy, it.W, ch), CombineMode.Intersect);
                Txt(it.S, it.F, it.C, it.X - off, it.Y, it.Tw + 24, it.H, 0);
                g.Restore(st);
            }
        }

        private void PaintAll(Graphics gr)
        {
            PaintViewport(gr, ClientSize.Width, ClientSize.Height, true);
        }

        private void PaintViewport(Graphics gr, float W, float Hh, bool allowOverlay)
        {
            G = gr;
            G.SmoothingMode = SmoothingMode.AntiAlias;
            G.TextRenderingHint = TextRenderingHint.AntiAlias;
            G.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Theme t = _th;
            G.Clear(_bgCol != 0 ? Color.FromArgb(255, Color.FromArgb(_bgCol)) : t.Bg);
            _marqActive = false; _marqRects.Clear(); _marqItems.Clear(); _hits.Clear();

            float aw = Math.Max(10f, W - _margin * 2), ah = Math.Max(10f, Hh - _margin * 2);
            bool strip = StripOn;
            float chh = (strip ? _ch + 32f : _ch) + (_topBar ? InfoH : 0);
            _chh = chh;
            float sc = Math.Max(0.05f, Math.Min(aw / _cw, ah / chh));
            Image bgi = string.IsNullOrEmpty(_bgImg) ? null : GetImg(_bgImg, 2560);
            if (bgi != null)
            {
                float k = Math.Max(W / bgi.Width, Hh / bgi.Height);
                float sw2 = W / k, sh2 = Hh / k;
                G.InterpolationMode = InterpolationMode.HighQualityBilinear;
                G.DrawImage(bgi, new RectangleF(0, 0, W, Hh), new RectangleF((bgi.Width - sw2) / 2f, (bgi.Height - sh2) / 2f, sw2, sh2), GraphicsUnit.Pixel);
            }
            else DrawBackground(W, Hh, sc);

            _ox = _margin + (aw - _cw * sc) / 2f; _oy = _margin + (ah - chh * sc) / 2f; _sc = sc;
            G.TranslateTransform(_ox, _oy);
            G.ScaleTransform(sc, sc);

            Snap s = _snap;
            if (s == null)
            {
                Txt(L.T("Veriler okunuyor…", "Reading sensors…"), F(48, false, true), t.Dim, 0, 0, _cw, _ch, 1);
            }
            else
            {
                DrawWidgets(s);

                if (allowOverlay && !string.IsNullOrEmpty(s.Throttle))
                {
                    Font f = F(20, true, false);
                    string m = "⚠ " + s.Throttle;
                    float w = TW(m, f) + 28;
                    var r = new RectangleF(_cw - w - 14 - RightW(), StripOn ? _ch + 4 : _ch - 26, w, 26);
                    using (GraphicsPath p = RR(r, 8)) { G.FillPath(Br(Al(t.Crit, 235)), p); }
                    Txt(m, f, Color.White, r.X, r.Y, r.Width, r.Height, 1);
                }
            }

            if (strip) DrawAudioBar();
            _brRect = RectangleF.Empty;
            if (_topBar) DrawInfoBar(_snap, StripOn ? _ch + 32f : _ch);
            if (allowOverlay && _topBar) DrawInfoPanel(_snap, StripOn ? _ch + 32f : _ch);
            int effBr = EffectiveBright();
            if (allowOverlay && effBr < 100)
            {
                G.ResetTransform();
                G.FillRectangle(Br(Color.FromArgb((int)(255 * (100 - effBr) / 100f), 0, 0, 0)), 0, 0, W, Hh);
                G.TranslateTransform(_ox, _oy); G.ScaleTransform(sc, sc);
            }
            if (allowOverlay && _edit) DrawEditor();
            if (allowOverlay && !_edit) DrawBrightBtn();

            if (t.Style == "crt")
            {
                G.ResetTransform();
                G.SmoothingMode = SmoothingMode.None;
                Pen sp = Pn(Color.FromArgb(70, 0, 0, 0), 1);
                for (float y = 0; y < Hh; y += 3) G.DrawLine(sp, 0, y, W, y);
            }
        }

        private static Snap _demoSnap;
        internal static Snap DemoSnap()
        {
            if (_demoSnap == null)
            {
                var cores = new CoreTelemetry[8];
                for (int i = 0; i < 8; i++)
                    cores[i] = new CoreTelemetry { Index = i + 1, ClockMhz = 4850 + (i % 3) * 100, LoadPct = 22 + (i * 9) % 55 };
                _demoSnap = new Snap
                {
                    Brand = "SYSTEM", MoboModel = "PRO GAMING", Chipset = "PCIe 5.0", Bios = "3024",
                    CpuModel = "Ryzen 7 7800X3D", GpuModel = "GeForce RTX 4080", GpuBus = "PCI-E 4.0 x16", MemSpeed = "DDR5-6000",
                    Uptime = "04:12:30", Ip = "192.168.1.42", Game = "", Throttle = "", Source = "Preview",
                    Sound = 64, Procs = 185, Ping = 14, SysBat = double.NaN, SysBatChg = false,
                    MemTotal = 32768, MemUsed = 15728, MemFree = 17040, MemLoad = 48, MoboTemp = 34, DimmTemp = 31,
                    CpuLoad = 36, CpuTemp = 54, CpuDiode = 51, CpuClock = 5050, CpuFan = 1180, Cha1 = 1050, Cha2 = 1020, Pump = 2400,
                    CpuW = 62.5, CpuV = 1.142, SocV = 1.18, V12 = 12.04,
                    GpuLoad = 78, GpuTemp = 63, GpuHot = 74, GpuClock = 2745, VramUsed = 6420, VramTotal = 16376, GpuFan = 1450, GpuW = 248.0, GpuV = 1.035,
                    Fps = 144, FrameMs = 6.9, Low1 = 118,
                    NetUp = 420, NetDown = 3850, NetUpTot = 1240, NetDownTot = 8420, DiskR = 145.2, DiskW = 32.8,
                    Cores = cores,
                    Drives = new[] { new DriveTelemetry { Name = "NVMe SSD", Mount = "C:", TempC = 36, TotalGb = 1000, FreeGb = 540, UsedPct = 46 } },
                    TopProcs = new[] { new ProcItem { Name = "Game.exe", MemMb = 4200, Cpu = 28 }, new ProcItem { Name = "Browser", MemMb = 920, Cpu = 4 }, new ProcItem { Name = "Discord", MemMb = 310, Cpu = 2 } },
                    TopCpu = new[] { new ProcItem { Name = "Game.exe", MemMb = 4200, Cpu = 28 }, new ProcItem { Name = "obs64", MemMb = 450, Cpu = 6 }, new ProcItem { Name = "Browser", MemMb = 920, Cpu = 4 } },
                    SelfMb = 38, SelfCpu = 0.2
                };
            }
            _demoSnap.Source = L.T("Önizleme", "Preview");
            return _demoSnap;
        }

        internal void RenderSceneTo(Graphics gr, int targetW, int targetH, List<WInst> widgets, int cw, int ch, Theme th, int bgCol, string bgImg, float gfs, float gzoom, bool topBar, bool hideStrip, int margin, Snap snap)
        {
            var sWd = _wd; int sCw = _cw, sCh = _ch, sBgCol = _bgCol, sMargin = _margin, sBright = _bright;
            Theme sTh = _th; string sBgImg = _bgImg; float sGfs = _gfs, sGzoom = _gzoom;
            bool sTopBar = _topBar, sHideStrip = _hideStrip, sEdit = _edit, sInfo = _infoOpen;
            Snap sSnap = _snap; Graphics sG = G; float sOx = _ox, sOy = _oy, sSc = _sc, sChh = _chh;
            var sHits = new List<KeyValuePair<RectangleF, string>>(_hits);
            var sMarq = new List<MItem>(_marqItems);
            try
            {
                _wd = widgets ?? sWd;
                _cw = Math.Max(200, cw); _ch = Math.Max(120, ch);
                _th = th ?? Themes.All[0];
                _bgCol = bgCol; _bgImg = bgImg ?? "";
                _gfs = gfs; _gzoom = gzoom;
                _topBar = topBar; _hideStrip = hideStrip; _margin = margin;
                _edit = false; _bright = 100; _infoOpen = false;
                _snap = snap ?? DemoSnap();
                PaintViewport(gr, Math.Max(16, targetW), Math.Max(16, targetH), false);
            }
            finally
            {
                _wd = sWd; _cw = sCw; _ch = sCh; _th = sTh; _bgCol = sBgCol; _bgImg = sBgImg;
                _gfs = sGfs; _gzoom = sGzoom; _topBar = sTopBar; _hideStrip = sHideStrip; _margin = sMargin;
                _edit = sEdit; _bright = sBright; _infoOpen = sInfo; _snap = sSnap;
                G = sG; _ox = sOx; _oy = sOy; _sc = sSc; _chh = sChh;
                _hits.Clear(); _hits.AddRange(sHits);
                _marqItems.Clear(); _marqItems.AddRange(sMarq);
            }
        }

        internal Bitmap RenderLayoutThumbnail(List<WInst> widgets, int cw, int ch, Theme th, int bgCol, string bgImg, float gfs, float gzoom, int thumbW, int thumbH)
        {
            var bmp = new Bitmap(Math.Max(32, thumbW), Math.Max(24, thumbH), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics fg = Graphics.FromImage(bmp))
                    RenderSceneTo(fg, bmp.Width, bmp.Height, widgets, cw, ch, th, bgCol, bgImg, gfs, gzoom, false, true, 0, DemoSnap());
            }
            catch { }
            return bmp;
        }

        private void DrawBackground(float W, float Hh, float sc)
        {
            Theme t = _th;
            string fx = t.Fx ?? "";
            if (fx.StartsWith("grid"))
            {
                string[] p = fx.Split(new[] { ':' });
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
                string[] p = fx.Split(new[] { ':' });
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
            h = h.TrimStart(new[] { '#' });
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
            RectangleF c = Card(r, "CPU", ShortHw(s.CpuModel), Tc(s.CpuTemp));
            float dsz = Math.Min(c.Height, c.Width * 0.42f);
            Dial(c.X + dsz / 2, c.Y + c.Height / 2, dsz, s.CpuLoad, I0(s.CpuLoad), "%", L.T("YÜK", "LOAD"), dsz * 0.30f);
            float sx = c.X + dsz + 24, sw = c.Right - sx;
            StatColumn(sx, c.Y, sw, c.Height,
                new[] { L.T("Saat", "Clock"), L.T("Güç", "Power"), "Fan" },
                new[] { Mhz(s.CpuClock), Wt(s.CpuW), Rpm(s.CpuFan) },
                new[] { Ratio(s.CpuClock, 6000), Ratio(s.CpuW, 250), Ratio(s.CpuFan, 3000) },
                new[] { false, false, false });
        }

        private void PGpu(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "GPU", ShortHw(s.GpuModel), Tc(s.GpuTemp));
            float dsz = Math.Min(c.Height, c.Width * 0.42f);
            Dial(c.X + dsz / 2, c.Y + c.Height / 2, dsz, s.GpuLoad, I0(s.GpuLoad), "%", L.T("YÜK", "LOAD"), dsz * 0.30f);
            float sx = c.X + dsz + 24, sw = c.Right - sx;
            StatColumn(sx, c.Y, sw, c.Height,
                new[] { L.T("Saat", "Clock"), L.T("Güç", "Power"), "VRAM" },
                new[] { Mhz(s.GpuClock), Wt(s.GpuW), s.VramTotal > 0 ? I0(s.VramUsed) + " / " + I0(s.VramTotal) + " MB" : Mb(s.VramUsed) },
                new[] { Ratio(s.GpuClock, 3000), Ratio(s.GpuW, 450), Ratio(s.VramUsed, s.VramTotal) },
                new[] { false, false, Ratio(s.VramUsed, s.VramTotal) > 92 });
        }

        private void PMem(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "RAM", s.MemSpeed, Gb(s.MemUsed / 1024.0));
            float dsz = Math.Min(c.Height, c.Width * 0.42f);
            Dial(c.X + dsz / 2, c.Y + c.Height / 2, dsz, s.MemLoad, I0(s.MemLoad), "%", L.T("DOLU", "USED"), dsz * 0.30f);
            float sx = c.X + dsz + 24, sw = c.Right - sx;
            StatColumn(sx, c.Y, sw, c.Height,
                new[] { L.T("Kullanılan", "Used"), L.T("Boş", "Free"), L.T("Toplam", "Total") },
                new[] { Gb(s.MemUsed / 1024.0), Gb(s.MemFree / 1024.0), Gb(s.MemTotal / 1024.0) },
                new[] { s.MemLoad, 100 - s.MemLoad, 100.0 },
                new[] { s.MemLoad > 90, false, false });
        }

        private void PFps(RectangleF r, Snap s, bool big)
        {
            string g = string.IsNullOrEmpty(s.Game) ? L.T("Oyun algılanmadı", "No game detected") : s.Game;
            RectangleF c = Card(r, "FPS", g, s.FrameMs > 0 ? D1(s.FrameMs) + " ms" : null);
            float dsz = Math.Min(c.Height * (big ? 1f : 0.62f), c.Width * 0.5f);
            Dial(c.X + dsz / 2, c.Y + dsz / 2, dsz, Ratio(s.Fps, 240), s.Fps > 0 ? I0(s.Fps) : "0", "", "FPS", dsz * 0.34f);
            float sx = c.X + dsz + 20, sw = c.Right - sx;
            string lowLbl = L.T("%1 Düşük ", "1% Low ") + (double.IsNaN(s.Low1) ? "--" : I0(s.Low1));
            if (big)
            {
                Txt(lowLbl, F(30, true, false), _th.Tx, sx, c.Y + 8, sw, 38, 0);
                Graph(new RectangleF(sx, c.Y + 54, sw, c.Height - 54), "fps", 60, 0, true, true);
            }
            else
            {
                Txt(lowLbl, F(26, true, false), _th.Tx, sx, c.Y, sw, 34, 0);
                Graph(new RectangleF(sx, c.Y + 40, sw, c.Height - 40), "fps", 60, 0, true, true);
            }
        }

        private void PNet(RectangleF r, Snap s)
        {
            string sub = !string.IsNullOrEmpty(s.WifiSsid)
                ? ("Wi-Fi " + Shorten(s.WifiSsid, 16) + (s.WifiSignal >= 0 ? " (" + s.WifiSignal + "%)" : "") + (string.IsNullOrEmpty(s.Ip) ? "" : " · " + s.Ip))
                : s.Ip;
            RectangleF c = Card(r, L.T("AĞ", "NETWORK"), sub, double.IsNaN(s.Ping) ? null : I0(s.Ping) + " ms");
            float half = (c.Width - 20) / 2;
            Caption(c.X, c.Y, half, "▲", Spd(s.NetUp), null, null);
            Caption(c.X + half + 20, c.Y, half, "▼", Spd(s.NetDown), null, null);
            float gh = c.Height - 62;
            Graph(new RectangleF(c.X, c.Y + 30, half, gh), "netUp", 100, 0, false, true);
            Graph(new RectangleF(c.X + half + 20, c.Y + 30, half, gh), "netDown", 100, 0, false, true);
            Txt(L.T("Toplam ▲ ", "Total ▲ ") + Tot(s.NetUpTot) + "   ▼ " + Tot(s.NetDownTot), F(20, false, false), _th.Dim, c.X, c.Bottom - 26, c.Width, 26, 0);
        }

        private void PDisks(RectangleF r, Snap s)
        {
            string rw = (double.IsNaN(s.DiskR) ? "--" : D1(s.DiskR)) + " / " + (double.IsNaN(s.DiskW) ? "--" : D1(s.DiskW)) + " MB/s";
            RectangleF c = Card(r, L.T("DİSK", "DISKS"), L.T("O/Y", "R/W"), rw);
            int n = Math.Min(s.Drives.Length, 5);
            if (n == 0) { Txt(L.T("Disk bilgisi yok", "No disk data"), F(26, false, true), _th.Dim, c.X, c.Y, c.Width, 40, 0); return; }
            string[] Lb = new string[n], V = new string[n]; double[] P = new double[n]; bool[] Hh = new bool[n];
            for (int i = 0; i < n; i++)
            {
                DriveTelemetry d = s.Drives[i];
                string nm = !string.IsNullOrEmpty(d.Mount) ? d.Mount : (!string.IsNullOrEmpty(d.Letter) ? d.Letter : d.Name);
                string hlth = !double.IsNaN(d.HealthPct) ? " · ♥" + I0(d.HealthPct) + "%" : "";
                Lb[i] = nm;
                bool hasSpace = d.TotalGb > 0;
                V[i] = (hasSpace ? D1(d.FreeGb) + L.T(" GB boş", " GB free") : (double.IsNaN(d.TempC) ? "--" : Tc(d.TempC))) + hlth;
                P[i] = hasSpace ? d.UsedPct : (double.IsNaN(d.TempC) ? 0 : Ratio(d.TempC, 100)); Hh[i] = hasSpace ? d.UsedPct > 92 : Hot(d.TempC, AppPrefs.AlertDisk);
            }
            StatColumn(c.X, c.Y, c.Width, c.Height, Lb, V, P, Hh);
        }

        private void PCores(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("ÇEKİRDEKLER", "CORES"), s.Cores.Length + " " + L.T("iş parçacığı", "threads"), null);
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
            RectangleF c = Card(r, L.T("İŞLEMLER", "PROCESSES"), double.IsNaN(s.Procs) ? null : I0(s.Procs) + L.T(" adet", " running"), null);
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
            CultureInfo ci = L.Culture;
            RectangleF c = Card(r, L.T("SAAT", "CLOCK"), now.ToString("d MMMM dddd", ci), AppPrefs.FmtWx(Extras.WxTemp));
            bool showP = AppPrefs.ShowPrayer;
            bool narrow = c.Width < 700;
            string tm = AppPrefs.FmtClock(now);
            float areaW = showP ? (narrow ? c.Width * 0.62f : c.Width * 0.5f) : c.Width;
            float fs = Math.Min(150, showP ? (narrow ? c.Height * 0.3f : c.Height * 0.62f) : c.Height * 0.55f);
            Font ft = F(fs, true, false);
            while (fs > 30 && TW(tm, ft) > areaW) { fs -= 4; ft = F(fs, true, false); }
            float th = showP ? (narrow ? c.Height * 0.42f : c.Height * 0.8f) : c.Height * 0.72f;
            Txt(tm, ft, _th.Tx, c.X, c.Y, areaW + 10, th, showP ? 0 : 1);
            Txt(Extras.WxDesc, F(24, false, true), _th.Dim, c.X, narrow || !showP ? c.Y + th : c.Bottom - 40, showP ? c.Width * 0.5f : c.Width, 36, showP ? 0 : 1);
            if (!showP) return;
            int next = NextPrayer(now);
            if (narrow)
            {
                float py = c.Y + th + 36, ph = (c.Bottom - py) / 3f, cw = c.Width / 2f;
                for (int i = 0; i < 6; i++)
                {
                    float x = c.X + (i % 2) * cw, y = py + (i / 2) * ph;
                    Color col = i == next ? _th.A1 : _th.Tx;
                    Txt(Extras.GetPrayerName(i), F(22, i == next, false), col, x, y, cw * 0.5f, ph, 0);
                    Txt(Extras.Prayer[i], F(22, true, false), col, x + cw * 0.35f, y, cw * 0.55f, ph, 2);
                }
            }
            else
            {
                float px = c.X + c.Width * 0.58f, pw = c.Width * 0.42f, ph = c.Height / 6f;
                for (int i = 0; i < 6; i++)
                {
                    Color col = i == next ? _th.A1 : _th.Tx;
                    Txt(Extras.GetPrayerName(i), F(24, i == next, false), col, px, c.Y + i * ph, pw / 2, ph, 0);
                    Txt(Extras.Prayer[i], F(24, true, false), col, px + pw / 2, c.Y + i * ph, pw / 2, ph, 2);
                }
            }
        }

        private void DrawInfoBar(Snap s, float top)
        {
            Theme t = _th;
            CultureInfo ci = L.Culture;
            DateTime now = DateTime.Now;
            float y = top + 2, h = 30;
            Font fb = F(20, true, false), fd = F(20, false, true);
            string brand = (s == null || string.IsNullOrEmpty(s.Brand)) ? "PC" : s.Brand.ToUpper(ci);
            _brRect = new RectangleF(10, y, 30, 30);
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(6, y - 4, 38, 38), "bright:next"));
            G.FillRectangle(Br(t.A1), 52, y + 4, 4, h - 8);
            Txt(brand, fb, t.Tx, 64, y, 300, h, 0);
            _infoBtn = new RectangleF(50, y - 2, TW(brand, fb) + 26, h + 4);
            _hits.Add(new KeyValuePair<RectangleF, string>(_infoBtn, "info:toggle"));
            float bx = 64 + TW(brand, fb) + 18;
            if (Extras.WxTemp != "--")
            {
                string wx = AppPrefs.FmtWx(Extras.WxTemp) + "  " + Extras.WxDesc;
                Txt(wx, fd, t.Dim, bx, y, 500, h, 0);
                bx += TW(wx, fd) + 22;
            }
            Font fbat = F(18, true, false);
            if (s != null && !double.IsNaN(s.SysBat))
            {
                string btxt = L.T("Pil ", "Bat ") + I0(s.SysBat) + "%" + (s.SysBatChg ? " ⚡" : (s.SysBatMin > 0 ? " " + AppPrefs.FmtMinutes(s.SysBatMin) : ""));
                float bbw = TW(btxt, fbat) + 22;
                if (bx + bbw <= 1120)
                {
                    var bbr = new RectangleF(bx, y + 2, bbw, h - 4);
                    bool lowB = !s.SysBatChg && s.SysBat <= AppPrefs.AlertBat;
                    using (GraphicsPath bp = RR(bbr, (h - 4) / 2f))
                    {
                        G.FillPath(Br(lowB ? Al(t.Crit, 90) : (s.SysBatChg ? Al(t.A1, 65) : t.Tile)), bp);
                        G.DrawPath(Pn(lowB ? t.Crit : (s.SysBatChg ? t.A1 : t.Line), 1.4f), bp);
                    }
                    Txt(btxt, fbat, lowB ? Color.White : t.Tx, bbr.X, bbr.Y, bbr.Width, bbr.Height, 1);
                    bx += bbw + 8;
                }
            }
            KeyValuePair<string, int>[] bat = Peripherals.Items;
            if (bat.Length > 0)
            {
                foreach (var kv in bat)
                {
                    string nm = kv.Key; int sp = nm.IndexOf('(');
                    if (sp > 3) nm = nm.Substring(0, sp).Trim();
                    string txt = Shorten(nm, 16) + " " + kv.Value + "%";
                    float bw = TW(txt, fbat) + 22;
                    if (bx + bw > 1120) break;
                    var br = new RectangleF(bx, y + 2, bw, h - 4);
                    bool off = Peripherals.RogOffline && kv.Key.StartsWith("Cetra");
                    bool low = !off && kv.Value <= 15;
                    bool live = !off && kv.Key.StartsWith("Cetra");
                    using (GraphicsPath bp = RR(br, (h - 4) / 2f)) { G.FillPath(Br(low ? Al(t.Crit, 90) : (live ? Al(t.A1, 70) : t.Tile)), bp); G.DrawPath(Pn(low ? t.Crit : (live ? t.A1 : t.Line), live ? 1.8f : 1.2f), bp); }
                    Txt(txt, fbat, low ? Color.White : (off ? Al(t.Dim, 140) : t.Tx), br.X, br.Y, br.Width, br.Height, 1);
                    bx += bw + 8;
                }
            }
            string clock = AppPrefs.FmtClock(now);
            string date = now.ToString("d MMMM dddd", ci);
            string pr = AppPrefs.ShowPrayer ? PrayerLeft(now) : "";
            float x = _cw - 14;
            Font fc = F(28, true, false);
            float cw = TW(clock, fc) + 6;
            Txt(clock, fc, t.Tx, x - cw, y - 2, cw + 4, h + 4, 2); x -= cw + 20;
            float dw = TW(date, fd) + 6;
            Txt(date, fd, t.Dim, x - dw, y, dw + 4, h, 2); x -= dw + 20;
            if (pr.Length > 0)
            {
                float pw = TW(pr, fb) + 28;
                var pr2 = new RectangleF(x - pw, y + 1, pw, h - 2);
                using (GraphicsPath pp = RR(pr2, (h - 2) / 2f)) { G.FillPath(Br(Al(t.A1, 40)), pp); G.DrawPath(Pn(t.A1, 1.5f), pp); }
                Txt(pr, fb, t.Tx, pr2.X, pr2.Y, pr2.Width, pr2.Height, 1);
                x -= pw + 12;
            }
            if (ShutdownTimer.Active)
            {
                TimeSpan rem = ShutdownTimer.Remaining;
                string sp2 = (ShutdownTimer.Action == 2 ? L.T("Yeniden başlatma ", "Restart ") : L.T("Kapanış ", "Shutdown ")) + ShutdownTimer.Format(rem);
                bool soon = rem.TotalMinutes <= 10;
                float sw2 = TW(sp2, fb) + 28;
                var sr = new RectangleF(x - sw2, y + 1, sw2, h - 2);
                using (GraphicsPath spp = RR(sr, (h - 2) / 2f)) { G.FillPath(Br(Al(soon ? t.Crit : t.A1, soon ? 120 : 40)), spp); G.DrawPath(Pn(soon ? t.Crit : t.A1, 1.5f), spp); }
                Txt(sp2, fb, soon ? Color.White : t.Tx, sr.X, sr.Y, sr.Width, sr.Height, 1);
            }
        }

        private static string PrayerLeft(DateTime now)
        {
            int[] idx = { 0, 1, 2, 3, 4, 5 };
            string[] nmTr = { "Sabaha", "Güneşe", "Öğleye", "İkindiye", "Akşama", "Yatsıya" };
            string[] nmEn = { "Fajr in", "Sunrise in", "Dhuhr in", "Asr in", "Maghrib in", "Isha in" };
            for (int day = 0; day < 2; day++)
                for (int k = 0; k < idx.Length; k++)
                {
                    string[] hm = Extras.Prayer[idx[k]].Split(new[] { ':' });
                    int h, mi;
                    if (hm.Length != 2 || !int.TryParse(hm[0], out h) || !int.TryParse(hm[1], out mi)) continue;
                    DateTime at = now.Date.AddDays(day).AddHours(h).AddMinutes(mi);
                    if (at > now)
                    {
                        int mins = (int)Math.Ceiling((at - now).TotalMinutes);
                        if (L.IsEn)
                            return nmEn[k] + " " + (mins >= 60 ? (mins / 60) + "h " + (mins % 60) + "m" : mins + "m");
                        return nmTr[k] + " " + (mins >= 60 ? (mins / 60) + "sa " + (mins % 60) + "dk" : mins + "dk");
                    }
                }
            return "";
        }

        private static int NextPrayer(DateTime now)
        {
            int m = now.Hour * 60 + now.Minute;
            for (int i = 0; i < 6; i++)
            {
                string[] hm = Extras.Prayer[i].Split(new[] { ':' });
                int h, mi;
                if (hm.Length == 2 && int.TryParse(hm[0], out h) && int.TryParse(hm[1], out mi) && h * 60 + mi > m) return i;
            }
            return 0;
        }

        private void PSys(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("SİSTEM", "SYSTEM"), s.Uptime, s.Brand);
            string[] k = { L.T("Anakart", "Motherboard"), L.T("Yonga seti", "Chipset"), "BIOS", L.T("Ses", "Volume"), L.T("Anakart ısı", "Board Temp") };
            string[] v = { s.MoboModel, s.Chipset, s.Bios, double.IsNaN(s.Sound) ? "--" : I0(s.Sound) + "%", Tc(s.MoboTemp) };
            float rh = c.Height / k.Length;
            for (int i = 0; i < k.Length; i++)
            {
                Txt(k[i], F(24, false, true), _th.Dim, c.X, c.Y + i * rh, c.Width * 0.34f, rh, 0);
                Txt(Fit(v[i], F(24, true, false), c.Width * 0.66f), F(24, true, false), _th.Tx, c.X + c.Width * 0.34f, c.Y + i * rh, c.Width * 0.66f, rh, 0);
            }
        }

        private void PPower(RectangleF r, Snap s)
        {
            double tot = (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW);
            RectangleF c = Card(r, L.T("GÜÇ", "POWER"), "CPU + GPU", I0(tot) + " W");
            Graph(new RectangleF(c.X, c.Y, c.Width, c.Height - 4), "totalW", 100, 0, true, true);
        }

        private void PTemps(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("SICAKLIK", "TEMPS"), L.T("geçmiş", "history"), Tc(s.CpuTemp));
            Graph(new RectangleF(c.X, c.Y, c.Width, c.Height - 4), "cpuTemp", 60, 0, true, true);
        }

        private void PGpuGraph(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("GPU YÜK", "GPU LOAD"), L.T("geçmiş", "history"), Pct(s.GpuLoad));
            Graph(new RectangleF(c.X, c.Y, c.Width, c.Height - 4), "gpuLoad", 100, 100, true, true);
        }

        private void PFans(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("FANLAR", "FANS"), "RPM", null);
            StatColumn(c.X, c.Y, c.Width, c.Height,
                new[] { "CPU", L.T("Kasa 1", "Chassis 1"), L.T("Kasa 2", "Chassis 2"), L.T("Pompa", "Pump"), "GPU" },
                new[] { Rpm(s.CpuFan), Rpm(s.Cha1), Rpm(s.Cha2), Rpm(s.Pump), Rpm(s.GpuFan) },
                new[] { Ratio(s.CpuFan, 3000), Ratio(s.Cha1, 3000), Ratio(s.Cha2, 3000), Ratio(s.Pump, 4000), Ratio(s.GpuFan, 3500) }, null);
        }

        private void PVolts(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("GERİLİM", "VOLTAGES"), null, null);
            var lbs = new List<string> { "CPU Vcore", "GPU Volt" };
            var vls = new List<string> { Vt(s.CpuV), Vt(s.GpuV) };
            var pcs = new List<double> { Ratio(s.CpuV, 1.6), Ratio(s.GpuV, 1.2) };
            if (!double.IsNaN(s.SocV)) { lbs.Add("SoC / SA"); vls.Add(Vt(s.SocV)); pcs.Add(Ratio(s.SocV, 1.5)); }
            if (!double.IsNaN(s.V12)) { lbs.Add("+12V"); vls.Add(s.V12.ToString("0.00", CultureInfo.InvariantCulture) + " V"); pcs.Add(Ratio(s.V12, 12.6)); }
            StatColumn(c.X, c.Y, c.Width, c.Height, lbs.ToArray(), vls.ToArray(), pcs.ToArray(), null);
        }

        private static string ShortHw(string m)
        {
            if (string.IsNullOrEmpty(m)) return "";
            m = m.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "");
            string[] cut = {
                "11th Gen ", "12th Gen ", "13th Gen ", "14th Gen ", "15th Gen ",
                "NVIDIA GeForce ", "NVIDIA ", "GeForce ", "AMD Radeon ", "AMD ",
                "Intel Core ", "Intel ",
                " 32-Core Processor", " 24-Core Processor", " 16-Core Processor", " 12-Core Processor", " 8-Core Processor", " 6-Core Processor", " 4-Core Processor",
                " w/ Radeon 780M Graphics", " w/ Radeon 760M Graphics", " w/ Radeon 890M Graphics", " w/ Radeon 880M Graphics",
                " with Radeon Graphics", " Processor", " CPU"
            };
            foreach (string c in cut)
            {
                int i = m.IndexOf(c, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) m = m.Remove(i, c.Length);
            }
            m = m.Trim();
            while (m.Contains("  ")) m = m.Replace("  ", " ");
            if (m.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) < 0 && m.StartsWith("RX ", StringComparison.OrdinalIgnoreCase)) m = "Radeon " + m;
            return m;
        }

        private static string Shorten(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        // ---------------- ses cihazı çubuğu ----------------
        private void DrawAudioBar()
        {
            Theme t = _th;
            if (_hideStrip) return;
            if (_micBar) DrawMic();
            if (_volBar) DrawVol();
            if (!_audioBar) return;
            AudioDevice[] all = AudioSwitch.Devices;
            if (all == null || all.Length == 0) return;
            var show = new List<AudioDevice>();
            foreach (AudioDevice d in all) if (_favs.Count == 0 || _favs.Contains(d.Id) || d.IsDefault) show.Add(d);
            Font f = F(18, true, false);
            float x = 10, y = _ch + 4, h = 26, maxX = _cw - 340 - RightW();
            Txt("♪", F(20, true, false), t.A1, x, y, 20, h, 1);
            x += 24;
            foreach (AudioDevice d in show)
            {
                string nm = d.Name;
                float w = Math.Min(TW(nm, f), 330f) + 26;
                if (x + w > maxX) break;
                var r = new RectangleF(x, y, w, h);
                using (GraphicsPath p = RR(r, h / 2f))
                {
                    if (d.IsDefault) G.FillPath(Br(t.A1), p);
                    else { G.FillPath(Br(t.Tile), p); G.DrawPath(Pn(t.Line, 1), p); }
                }
                MTxt(nm, f, d.IsDefault ? t.Bg : t.Tx, x + 13, y, w - 26, h, 1);
                _hits.Add(new KeyValuePair<RectangleF, string>(r, d.Id));
                x += w + 8;
            }
        }

        private float RightW() { return (_micBar ? MicW + 12 : 0) + (_volBar ? VolW + 12 : 0); }

        private void SpeakerIcon(float x, float y, Color ic, bool muted, float lvl)
        {
            G.FillPolygon(Br(ic), new[] { new PointF(x, y + 8), new PointF(x + 5, y + 8), new PointF(x + 11, y + 3), new PointF(x + 11, y + 19), new PointF(x + 5, y + 14), new PointF(x, y + 14) });
            if (muted) { G.DrawLine(Pn(ic, 2.2f), x + 14, y + 7, x + 22, y + 15); G.DrawLine(Pn(ic, 2.2f), x + 22, y + 7, x + 14, y + 15); return; }
            if (lvl > 0.01f) G.DrawArc(Pn(ic, 1.8f), x + 7, y + 6, 10, 10, -50, 100);
            if (lvl > 0.40f) G.DrawArc(Pn(ic, 1.8f), x + 5, y + 3, 16, 16, -55, 110);
            if (lvl > 0.75f) G.DrawArc(Pn(ic, 1.8f), x + 3, y + 0, 22, 22, -55, 110);
        }

        private void DrawVol()
        {
            Theme t = _th;
            float w = VolW, h = 26, x = _cw - 14 - (_micBar ? MicW + 12 : 0) - w, y = _ch + 4;
            var r = new RectangleF(x, y, w, h);
            bool avail = AudioSwitch.VolAvailable, muted = AudioSwitch.VolMuted;
            float lv = Math.Max(0f, Math.Min(1f, AudioSwitch.VolLevel));
            using (GraphicsPath p = RR(r, h / 2f))
            {
                if (muted) G.FillPath(Br(t.Crit), p);
                else { G.FillPath(Br(t.Tile), p); G.DrawPath(Pn(avail ? t.A1 : t.Line, 1.5f), p); }
            }
            Color ic = muted ? Color.White : (avail ? t.A1 : t.Dim);
            SpeakerIcon(x + 9, y + 2, ic, muted, lv);
            if (!avail) { Txt(L.T("Ses yok", "No audio"), F(16, true, false), t.Dim, x + 40, y, w - 50, h, 0); return; }
            float bx = x + 40, bw = w - 40 - 58;
            var track = new RectangleF(bx, y + h / 2f - 4, bw, 8);
            using (GraphicsPath tp = RR(track, 4)) G.FillPath(Br(muted ? Al(Color.White, 70) : t.Track), tp);
            if (lv > 0.005f && !muted)
            {
                var fillr = new RectangleF(track.X, track.Y, Math.Max(8, bw * lv), 8);
                using (GraphicsPath fp = RR(fillr, 4))
                using (var lb = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y - 1, bw + 2, 10), t.A1, t.A2, 0f)) G.FillPath(lb, fp);
            }
            float kx = bx + bw * (muted ? 0 : lv);
            G.FillEllipse(Br(muted ? Color.White : t.Tx), kx - 6, y + h / 2f - 6, 12, 12);
            Txt(muted ? L.T("KAPALI", "MUTE") : I0(lv * 100) + "%", F(16, true, false), muted ? Color.White : t.Tx, x + w - 58, y, 50, h, 2);
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(x, y, 36, h), "vol:mute"));
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(bx - 6, y, bw + 12, h), "vol:bar"));
        }

        private void DrawMic()
        {
            Theme t = _th;
            float w = MicW, h = 26, x = _cw - 14 - w, y = _ch + 4;
            var r = new RectangleF(x, y, w, h);
            bool avail = AudioSwitch.MicAvailable, muted = AudioSwitch.MicMuted;
            using (GraphicsPath p = RR(r, h / 2f))
            {
                if (!avail) { G.FillPath(Br(t.Tile), p); G.DrawPath(Pn(t.Line, 1), p); }
                else if (muted) G.FillPath(Br(t.Crit), p);
                else { G.FillPath(Br(t.Tile), p); G.DrawPath(Pn(t.A1, 1.5f), p); }
            }
            Color ic = muted ? Color.White : (avail ? t.A1 : t.Dim);
            // mikrofon simgesi
            using (GraphicsPath cap = RR(new RectangleF(x + 11, y + 4, 7, 12), 3.5f)) G.FillPath(Br(ic), cap);
            G.DrawArc(Pn(ic, 1.6f), x + 8, y + 7, 13, 11, 0, 180);
            G.DrawLine(Pn(ic, 1.6f), x + 14.5f, y + 18.5f, x + 14.5f, y + 21);
            G.DrawLine(Pn(ic, 1.6f), x + 11, y + 21, x + 18, y + 21);
            if (muted) G.DrawLine(Pn(Color.White, 2.4f), x + 7, y + 22, x + 22, y + 4);
            Font f = F(16, true, false);
            if (!avail) { Txt(L.T("Mikrofon yok", "No microphone"), f, t.Dim, x + 32, y, w - 40, h, 0); }
            else
            {
                string lab = muted ? L.T("KAPALI", "MUTED") : L.T("AÇIK", "LIVE");
                float lw = 66, nw = 200;
                Color tc = muted ? Color.White : t.Tx;
                MTxt(AudioSwitch.MicName, f, tc, x + 32, y, nw, h, 0);
                if (!muted)
                {
                    float bx = x + 32 + nw + 10, bw = w - 32 - nw - 10 - lw - 14;
                    float lv = (float)Math.Sqrt(Math.Max(0, Math.Min(1, AudioSwitch.MicLevel)));
                    BarAt(bx, y + h / 2f - 4, bw, lv * 100, lv > 0.92f, 8);
                }
                Txt(lab, f, tc, x + w - lw - 10, y, lw, h, 2);
            }
            _hits.Add(new KeyValuePair<RectangleF, string>(r, "mic:toggle"));
        }

        // ---------------- Sensör Paneli düzeni ----------------
        private void MiniRow(float x, float y, float w, float h, string label, double pct, string val, bool hot)
        {
            Theme t = _th;
            Font fl = F(22, false, true), fv = F(22, true, false);
            float lw = Math.Min(150, w * 0.3f), vw = Math.Min(150, w * 0.3f);
            Txt(Fit(label, fl, lw), fl, t.Tx, x, y, lw, h, 0);
            BarAt(x + lw + 6, y + h / 2f - 7, w - lw - vw - 18, pct, hot, 14);
            Txt(val, fv, t.Tx, x + w - vw, y, vw, h, 2);
        }

        private void PAClock(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            DateTime now = DateTime.Now;
            string brand = string.IsNullOrEmpty(s.Brand) ? "PC" : s.Brand.ToUpper(L.Culture);
            float fs = 56; Font fb = F(fs, true, false);
            while (fs > 24 && TW(brand, fb) > r.Width - 40) { fs -= 4; fb = F(fs, true, false); }
            Txt(brand, fb, t.Tx, r.X, r.Y + 8, r.Width, 70, 1);
            Txt(AppPrefs.FmtClock(now), F(64, false, true), t.Tx, r.X, r.Y + 76, r.Width, 76, 1);
            var pill = new RectangleF(r.X + 50, r.Y + 156, r.Width - 100, 36);
            using (GraphicsPath p = RR(pill, 8)) G.FillPath(Br(t.Tx), p);
            Txt(now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), F(26, true, false), t.Bg, pill.X, pill.Y, pill.Width, pill.Height, 1);
            Font fs2 = F(21, false, true), fv = F(21, true, false);
            Txt(L.T("Çalışma", "Uptime"), fs2, t.Dim, r.X + 20, r.Y + 202, 96, 30, 0);
            Txt(s.Uptime, fv, t.Tx, r.X + 116, r.Y + 202, 166, 30, 0);
            Txt(L.T("Ses", "Vol"), fs2, t.Dim, r.X + r.Width - 150, r.Y + 202, 60, 30, 0);
            Txt(double.IsNaN(s.Sound) ? "--" : I0(s.Sound), fv, t.Tx, r.X + r.Width - 100, r.Y + 202, 80, 30, 2);
        }

        private void PAMem(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("Bellek", "Memory"), null, null);
            float rh = c.Height / 4f;
            MiniRow(c.X, c.Y, c.Width, rh, L.T("Yük", "Load"), s.MemLoad, Pct(s.MemLoad), s.MemLoad > 90);
            MiniRow(c.X, c.Y + rh, c.Width, rh, L.T("Hız", "Speed"), 100, string.IsNullOrEmpty(s.MemSpeed) ? "--" : s.MemSpeed, false);
            MiniRow(c.X, c.Y + rh * 2, c.Width, rh, L.T("Kullanım", "Used"), s.MemLoad, Gb(s.MemUsed / 1024.0), false);
            MiniRow(c.X, c.Y + rh * 3, c.Width, rh, L.T("Boş", "Free"), 100 - s.MemLoad, Gb(s.MemFree / 1024.0), false);
        }

        private void PAMobo(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("ANAKART", "MOTHERBOARD"), s.MoboModel, null);
            Theme t = _th;
            string[] k = { L.T("Yonga seti", "Chipset"), "BIOS", L.T("Yol tipi", "Bus"), L.T("İşlemler", "Processes") };
            string[] v = { s.Chipset, s.Bios, s.GpuBus, double.IsNaN(s.Procs) ? "--" : I0(s.Procs) };
            float rh = c.Height / 4f, lw = 150, vw = c.Width * 0.62f - lw;
            Font fk = F(22, false, true), fv = F(22, true, false);
            for (int i = 0; i < 4; i++)
            {
                Txt(k[i], fk, t.Dim, c.X, c.Y + i * rh, lw, rh, 0);
                Txt(Fit(v[i], fv, vw), fv, t.Tx, c.X + lw, c.Y + i * rh, vw, rh, 0);
            }
            float rx = c.X + c.Width * 0.66f, rw = c.Width * 0.34f;
            BarAt(rx, c.Y + 22, rw, Ratio(s.MoboTemp, 100), false, 15);
            Txt(L.T("Sıcaklık", "Temp"), F(26, false, true), t.Tx, rx, c.Y + 50, rw * 0.5f, 44, 0);
            Txt(Tc(s.MoboTemp), F(38, true, false), t.Tx, rx + rw * 0.3f, c.Y + 50, rw * 0.7f, 44, 2);
        }

        private void PACpu(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "CPU", ShortHw(s.CpuModel), null);
            Theme t = _th;
            Dial(c.X + 160, c.Y + 150, 290, s.CpuLoad, I0(s.CpuLoad), "%", L.T("YÜK", "LOAD"), 96);
            Graph(new RectangleF(c.X, c.Y + 316, 320, c.Height - 316), "cpuTemp", 60, 0, true, true);
            float rx = c.X + 350, rw = c.Width - 350;
            bool pump = !double.IsNaN(s.Pump);
            StatColumn(rx, c.Y, rw, 300,
                new[] { L.T("Sıcaklık", "Temp"), L.T("Saat", "Clock"), "Fan", pump ? L.T("AIO Pompa", "AIO Pump") : L.T("Kasa Fan", "Case Fan") },
                new[] { Tc(s.CpuTemp), Mhz(s.CpuClock), Rpm(s.CpuFan), Rpm(pump ? s.Pump : s.Cha1) },
                new[] { Ratio(s.CpuTemp, 100), Ratio(s.CpuClock, 6000), Ratio(s.CpuFan, 3000), Ratio(pump ? s.Pump : s.Cha1, 3500) },
                new[] { Hot(s.CpuTemp, 90), false, false, false });
            int n = Math.Min(8, s.Cores.Length);
            float cw = rw / 2f - 8, ch = (c.Height - 316) / 4f;
            Font fl = F(20, false, true), fv = F(20, true, false);
            for (int i = 0; i < n; i++)
            {
                CoreTelemetry k = s.Cores[i];
                float x = rx + (i / 4) * (cw + 16), y = c.Y + 316 + (i % 4) * ch;
                Txt((L.IsEn ? "CPU #" + (i + 1) : (i + 1) + ". CPU"), fl, t.Tx, x, y, 90, ch - 8, 0);
                Txt(I0(k.ClockMhz) + " MHz", fv, t.Tx, x + cw - 130, y, 130, ch - 8, 2);
                BarAt(x, y + ch - 14, cw, k.LoadPct, k.LoadPct > 92, 7);
            }
        }

        private void PAGpu(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "GPU", ShortHw(s.GpuModel), null);
            Theme t = _th;
            Dial(c.X + 160, c.Y + 150, 290, s.GpuLoad, I0(s.GpuLoad), "%", L.T("YÜK", "LOAD"), 96);
            var gr = new RectangleF(c.X, c.Y + 316, 320, c.Height - 316);
            Graph(gr, "fps", 60, 0, true, true);
            Txt("FPS " + (s.Fps > 0 ? I0(s.Fps) : "--"), F(46, true, false), Al(t.Tx, 70), gr.X, gr.Y, gr.Width, gr.Height, 1);
            float rx = c.X + 350, rw = c.Width - 350;
            StatColumn(rx, c.Y, rw, 300,
                new[] { L.T("Sıcaklık", "Temp"), L.T("Güç", "Power"), L.T("Saat", "Clock"), "VRAM", "Fan" },
                new[] { Tc(s.GpuTemp), Wt(s.GpuW), Mhz(s.GpuClock), s.VramTotal > 0 ? I0(s.VramUsed) + " / " + I0(s.VramTotal) + " MB" : Mb(s.VramUsed), Rpm(s.GpuFan) },
                new[] { Ratio(s.GpuTemp, 100), Ratio(s.GpuW, 450), Ratio(s.GpuClock, 3000), Ratio(s.VramUsed, s.VramTotal), Ratio(s.GpuFan, 3500) },
                new[] { Hot(s.GpuTemp, 85), false, false, Ratio(s.VramUsed, s.VramTotal) > 92, false });
            Graph(new RectangleF(rx, c.Y + 316, rw, c.Height - 316), "gpuLoad", 100, 100, true, true);
        }

        private void PAStorage(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("Depolama", "Storage"), null, null);
            Theme t = _th;
            int n = Math.Min(4, s.Drives.Length);
            if (n == 0) { Txt(L.T("Disk bilgisi yok", "No disk data"), F(24, false, true), t.Dim, c.X, c.Y, c.Width, 40, 0); return; }
            float rh = Math.Min(46f, c.Height / n);
            Font fn = F(24, false, false), fv = F(24, true, false);
            for (int i = 0; i < n; i++)
            {
                DriveTelemetry d = s.Drives[i];
                float y = c.Y + i * rh;
                string nm = !string.IsNullOrEmpty(d.Name) ? d.Name : (d.Mount ?? d.Letter);
                Txt(Fit(nm, fn, c.Width * 0.5f), fn, t.Tx, c.X, y, c.Width * 0.5f, rh, 0);
                bool noData = double.IsNaN(d.TempC) && d.UsedPct <= 0.01;
                double pv = double.IsNaN(d.TempC) ? d.UsedPct : d.TempC;
                BarAt(c.X + c.Width * 0.52f, y + rh / 2f - 7, c.Width * 0.34f, double.IsNaN(d.TempC) ? d.UsedPct : Ratio(d.TempC, 100), pv > 90, 14);
                Txt(noData ? "--" : (double.IsNaN(d.TempC) ? Pct(d.UsedPct) : Tc(d.TempC)), fv, t.Tx, c.X + c.Width * 0.88f - 10, y, c.Width * 0.12f + 10, rh, 2);
            }
        }

        private void PAPower(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("Güç", "Power"), null, null);
            Theme t = _th;
            string[] k = { "CPU", "", "GPU", "" };
            string[] v = { Wt(s.CpuW), Vt(s.CpuV), Wt(s.GpuW), Vt(s.GpuV) };
            float rh = c.Height / 4f;
            for (int i = 0; i < 4; i++)
            {
                Txt(k[i], F(24, false, true), t.Dim, c.X, c.Y + i * rh, 90, rh, 0);
                Txt(v[i], F(26, true, false), t.Tx, c.X + 60, c.Y + i * rh, c.Width - 60, rh, 2);
            }
        }

        private void PANet(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("Ağ", "Network"), null, s.Ip);
            Theme t = _th;
            string[] k = { L.T("Gönderim", "Upload"), L.T("Toplam", "Total"), L.T("İndirme", "Download"), L.T("Toplam", "Total") };
            string[] v = { Spd(s.NetUp), Tot(s.NetUpTot), Spd(s.NetDown), Tot(s.NetDownTot) };
            float rh = c.Height / 4f, lw = 270;
            Font fk = F(20, false, true), fv = F(22, true, false);
            for (int i = 0; i < 4; i++)
            {
                Txt(k[i], fk, t.Dim, c.X, c.Y + i * rh, 110, rh, 0);
                Txt(v[i], fv, t.Tx, c.X + 100, c.Y + i * rh, lw - 100, rh, 2);
            }
            float gx = c.X + lw + 20, gw = c.Width - lw - 20, gh = c.Height / 2f - 4;
            Graph(new RectangleF(gx, c.Y, gw, gh), "netUp", 100, 0, false, true);
            Graph(new RectangleF(gx, c.Y + gh + 8, gw, gh), "netDown", 100, 0, false, true);
        }

        private void DrawAida(Snap s)
        {
            RectangleF[] a = Row(10, 245, 450, 470, 960);
            PAClock(a[0], s); PAMem(a[1], s); PAMobo(a[2], s);
            RectangleF[] b = Row(265, 550, 940, 940);
            PACpu(b[0], s); PAGpu(b[1], s);
            RectangleF[] c = Row(825, 245, 940, 310, 630);
            PAStorage(c[0], s); PAPower(c[1], s); PANet(c[2], s);
        }

        // ---------------- Komuta Merkezi düzeni ----------------
        // Aynı baz ada sahip cihazlarda parantez içini, aksi halde yalnızca baz adı gösterir.
        private static string DevShort(string name, AudioDevice[] all)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int p = name.IndexOf('(');
            if (p <= 0) return name;
            string bas = name.Substring(0, p).Trim();
            int same = 0;
            foreach (AudioDevice d in all) { int q = d.Name.IndexOf('('); string b = q > 0 ? d.Name.Substring(0, q).Trim() : d.Name; if (b == bas) same++; }
            if (same <= 1) return bas;
            string inner = name.Substring(p + 1);
            int e = inner.LastIndexOf(')');
            if (e > 0) inner = inner.Substring(0, e);
            inner = inner.Replace("(R)", "").Replace("Realtek ", "").Trim();
            return inner.Length > 0 ? inner : name;
        }

        private sealed class Chip { public string Text, Id; public bool Active, Prof; }

        // Etiket + sarmalanan çipler; sonraki satırın y değerini döndürür.
        private float FlowRow(string label, List<Chip> items, float x, float y, float w, float bottom, int maxLines)
        {
            Theme t = _th;
            Font fp = F(16, true, false);
            const float ch = 30f;
            float lw = 62f;
            Txt(label, F(13, true, false), t.Dim, x, y, lw, ch, 0);
            float cx = x + lw, line = 0;
            foreach (Chip c in items)
            {
                string nm = c.Text;
                float cw = Math.Min(TW(nm, fp), 170f) + 22;
                if (cx + cw > x + w)
                {
                    if (line + 1 >= maxLines || y + ch + 6 + ch > bottom + 2) return y + ch + 6;
                    line++; cx = x + lw; y += ch + 6;
                }
                var cr = new RectangleF(cx, y, cw, ch);
                using (GraphicsPath cp = RR(cr, ch / 2f))
                {
                    if (c.Prof) { G.FillPath(Br(Al(t.A2, 60)), cp); G.DrawPath(Pn(t.A2, 1.5f), cp); }
                    else if (c.Active) G.FillPath(Br(t.A1), cp);
                    else { G.FillPath(Br(t.Tile), cp); G.DrawPath(Pn(t.Line, 1), cp); }
                }
                MTxt(nm, fp, (c.Active && !c.Prof) ? t.Bg : t.Tx, cr.X + 11, cr.Y, cr.Width - 22, cr.Height, 1);
                _hits.Add(new KeyValuePair<RectangleF, string>(cr, c.Id));
                cx += cw + 6;
            }
            return y + ch + 6;
        }

        private void PSound(RectangleF r, Snap s)
        {
            Theme t = _th;
            bool vm = AudioSwitch.VolMuted, mm = AudioSwitch.MicMuted;
            RectangleF c = Card(r, L.T("SES", "AUDIO"), AudioSwitch.DefaultName, AudioSwitch.VolAvailable ? (vm ? L.T("KAPALI", "MUTED") : I0(AudioSwitch.VolLevel * 100) + "%") : null);
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(r.X, r.Y, r.Width * 0.75f, 56), "out:next"));
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(r.X + r.Width * 0.75f, r.Y, r.Width * 0.25f, 56), "vol:mute"));
            float y = c.Y + 2, rowH = 36;
            // çıkış ses seviyesi
            SpeakerIcon(c.X + 2, y + rowH / 2f - 11, vm ? t.Crit : t.A1, vm, AudioSwitch.VolLevel);
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(c.X, y, 40, rowH), "vol:mute"));
            var track = new RectangleF(c.X + 46, y + rowH / 2f - 6, c.Width - 46, 12);
            using (GraphicsPath tp = RR(track, 6)) G.FillPath(Br(t.Track), tp);
            float lv = Math.Max(0f, Math.Min(1f, AudioSwitch.VolLevel));
            if (lv > 0.005f && !vm)
            {
                var fr = new RectangleF(track.X, track.Y, Math.Max(12, track.Width * lv), 12);
                using (GraphicsPath fp = RR(fr, 6))
                using (var lb = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y - 1, track.Width + 2, 14), t.A1, t.A2, 0f)) G.FillPath(lb, fp);
            }
            G.FillEllipse(Br(t.Tx), track.X + track.Width * (vm ? 0 : lv) - 9, track.Y + 6 - 9, 18, 18);
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(track.X - 8, y, track.Width + 16, rowH), "vol:bar"));
            y += rowH + 4;
            // mikrofon (tıkla: aç/kapat)
            var mr = new RectangleF(c.X, y, c.Width, 36);
            using (GraphicsPath mp = RR(mr, 10))
            {
                if (mm) G.FillPath(Br(t.Crit), mp); else { G.FillPath(Br(t.Tile), mp); G.DrawPath(Pn(t.Line, 1), mp); }
            }
            Font f = F(20, true, false);
            Color tc = mm ? Color.White : t.Tx;
            Color ic = mm ? Color.White : t.A1;
            using (GraphicsPath cap = RR(new RectangleF(mr.X + 12, mr.Y + 7, 7, 14), 3.5f)) G.FillPath(Br(ic), cap);
            G.DrawArc(Pn(ic, 1.6f), mr.X + 9, mr.Y + 11, 13, 11, 0, 180);
            G.DrawLine(Pn(ic, 1.6f), mr.X + 15.5f, mr.Y + 22.5f, mr.X + 15.5f, mr.Y + 26);
            if (mm) G.DrawLine(Pn(Color.White, 2.4f), mr.X + 8, mr.Y + 27, mr.X + 24, mr.Y + 6);
            float nw = mr.Width * 0.42f;
            MTxt(AudioSwitch.MicAvailable ? AudioSwitch.MicName : L.T("Mikrofon yok", "No microphone"), f, tc, mr.X + 34, mr.Y, nw, mr.Height, 0);
            if (AudioSwitch.MicAvailable)
            {
                if (!mm)
                {
                    float lvm = (float)Math.Sqrt(Math.Max(0, Math.Min(1, AudioSwitch.MicLevel)));
                    BarAt(mr.X + 34 + nw + 14, mr.Y + mr.Height / 2f - 4, mr.Width - 34 - nw - 14 - 84, lvm * 100, lvm > 0.92f, 8);
                }
                Txt(mm ? L.T("KAPALI", "MUTED") : L.T("AÇIK", "LIVE"), f, tc, mr.Right - 84, mr.Y, 74, mr.Height, 2);
                _hits.Add(new KeyValuePair<RectangleF, string>(mr, "mic:toggle"));
            }
            y += mr.Height + 8;
            // iki sütun: ÇIKIŞ | GİRİŞ — her cihaz büyük, tıklanabilir bir satır
            bool profRow = AudioProfiles.All.Count > 0 && c.Height >= 230;
            float reserve = profRow ? 38f : 0f;
            float colW = (c.Width - 14) / 2f, rowH2 = 28f, gap = 4f;
            int maxRows = (int)Math.Floor((c.Bottom - (y + 20) - reserve + gap) / (rowH2 + gap));
            Font fl = F(13, true, false), frow = F(16, true, false);
            for (int col = 0; col < 2; col++)
            {
                AudioDevice[] list0 = col == 0 ? AudioSwitch.Devices : AudioSwitch.MicDevices;
                HashSet<string> fav = col == 0 ? _favs : _micFavs;
                AudioDevice[] list = list0 == null ? new AudioDevice[0] : list0.Where(d0 => fav.Count == 0 || fav.Contains(d0.Id) || d0.IsDefault).ToArray();
                float cx = c.X + col * (colW + 14);
                Txt(col == 0 ? L.T("ÇIKIŞ", "OUTPUT") : L.T("GİRİŞ", "INPUT"), fl, t.Dim, cx, y, colW, 18, 0);
                if (list == null || list.Length == 0 || maxRows < 1) continue;
                var order = new List<AudioDevice>(list);
                int di = order.FindIndex(d => d.IsDefault);
                if (di >= maxRows) { AudioDevice dd = order[di]; order.RemoveAt(di); order.Insert(maxRows - 1, dd); }
                float ry = y + 20;
                for (int k = 0; k < order.Count && k < maxRows; k++, ry += rowH2 + gap)
                {
                    AudioDevice d = order[k];
                    var rr = new RectangleF(cx, ry, colW, rowH2);
                    using (GraphicsPath rp = RR(rr, 8))
                    {
                        if (d.IsDefault) G.FillPath(Br(t.A1), rp);
                        else { G.FillPath(Br(t.Tile), rp); G.DrawPath(Pn(t.Line, 1), rp); }
                    }
                    string nm = DevShort(d.Name, list);
                    Txt(Fit(nm, frow, colW - 16), frow, d.IsDefault ? t.Bg : t.Tx, rr.X + 8, rr.Y, colW - 12, rr.Height, 0);
                    _hits.Add(new KeyValuePair<RectangleF, string>(rr, d.Id));
                }
            }
            if (profRow)
            {
                var profs = new List<Chip>();
                for (int i2 = 0; i2 < AudioProfiles.All.Count; i2++) profs.Add(new Chip { Text = AudioProfiles.All[i2].Name, Id = "prof:" + i2, Prof = true });
                FlowRow(L.T("PROFİL", "PROFILE"), profs, c.X, c.Bottom - 32, c.Width, c.Bottom + 4, 1);
            }
        }

        private void DrawStudio(Snap s)
        {
            RectangleF[] a = Row(10, 300, 560, 770, 560);
            PCpu(a[0], s, true); PFps(a[1], s, true); PGpu(a[2], s);
            RectangleF[] b = Row(320, 250, 650, 420, 420, 380);
            PCores(b[0], s); PTemps(b[1], s); PGpuGraph(b[2], s); PPower(b[3], s);
            RectangleF[] c = Row(580, 250, 450, 450, 500, 250, 200);
            PMem(c[0], s); PNet(c[1], s); PDisks(c[2], s); PFans(c[3], s); PVolts(c[4], s);
            RectangleF[] d = Row(840, 234, 540, 380, 530, 420);
            PSound(d[0], s); PProcs(d[1], s); PSys(d[2], s); PClock(d[3], s);
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
            PHudBar(new RectangleF(10, 10, 1900, 86), s);
            RectangleF[] a = Row(106, 620, 800, 560, 520);
            PHudFps(a[0], s);
            PCpu(new RectangleF(a[1].X, a[1].Y, a[1].Width, 305), s, true);
            PGpu(new RectangleF(a[1].X, a[1].Y + 315, a[1].Width, 305), s);
            PTemps(new RectangleF(a[2].X, a[2].Y, a[2].Width, 200), s);
            PGpuGraph(new RectangleF(a[2].X, a[2].Y + 210, a[2].Width, 200), s);
            PPower(new RectangleF(a[2].X, a[2].Y + 420, a[2].Width, 200), s);
            RectangleF[] b = Row(736, 338, 440, 360, 360, 340, 360);
            PSound(b[0], s); PNet(b[1], s); PDisks(b[2], s); PMem(b[3], s); PApps(b[4], s);
        }

        private static string Dur(TimeSpan t)
        {
            return ((int)t.TotalHours).ToString("0") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        private void HudMetric(float x, float y, float w, string label, string val, Color c)
        {
            Txt(label, F(17, true, false), _th.Dim, x, y + 6, w, 22, 0);
            Txt(val, F(36, true, false), c, x, y + 30, w, 46, 0);
        }

        private void PHudBar(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            bool on = GameSession.Active;
            Color dot = on ? t.A1 : t.Dim;
            if (on && DateTime.Now.Millisecond < 500) G.FillEllipse(Br(Al(t.A1, 70)), r.X + 16, r.Y + 26, 34, 34);
            G.FillEllipse(Br(dot), r.X + 24, r.Y + 34, 18, 18);
            string game = on ? GameSession.Game : L.T("Oyun bekleniyor", "Waiting for game");
            Font fg = F(40, true, false);
            Txt(Fit(game, fg, 430), fg, t.Tx, r.X + 56, r.Y + 6, 440, 50, 0);
            Txt(on ? L.T("OYUNDA  ", "IN GAME  ") + Dur(GameSession.Elapsed) : L.T("BEKLEMEDE", "STANDBY"), F(19, true, false), t.A1, r.X + 58, r.Y + 54, 440, 26, 0);
            float x = r.X + 480, w = (r.Right - 20 - x) / 7f;
            string avg = on ? I0(GameSession.AvgFps) : "--";
            string low = double.IsNaN(s.Low1) ? "--" : I0(s.Low1);
            Color hc = Hot(s.CpuTemp, 85) ? t.Crit : t.Tx, hg = Hot(s.GpuTemp, 80) ? t.Crit : t.Tx;
            double pw = (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW);
            string[] lab = { L.T("ORT FPS", "AVG FPS"), L.T("%1 DÜŞÜK", "1% LOW"), L.T("KARE", "FRAME"), "PING", "CPU", "GPU", L.T("GÜÇ", "POWER") };
            string[] val = { avg, low, s.FrameMs > 0 ? D1(s.FrameMs) + " ms" : "--", double.IsNaN(s.Ping) ? "--" : I0(s.Ping) + " ms", Tc(s.CpuTemp), Tc(s.GpuTemp), I0(pw) + " W" };
            Color[] col = { t.Tx, t.Tx, t.Tx, t.Tx, hc, hg, t.Tx };
            for (int i = 0; i < 7; i++)
            {
                if (i > 0) G.DrawLine(Pn(t.Line, 1), x + i * w - 10, r.Y + 16, x + i * w - 10, r.Bottom - 16);
                HudMetric(x + i * w, r.Y, w - 14, lab[i], val[i], col[i]);
            }
        }

        private void PHudFps(RectangleF r, Snap s)
        {
            Theme t = _th;
            bool on = GameSession.Active;
            RectangleF c = Card(r, "FPS", on ? Shorten(GameSession.Game, 26) : L.T("Oyun algılanmadı", "No game detected"), s.FrameMs > 0 ? D1(s.FrameMs) + " ms" : null);
            float dsz = Math.Min(c.Width * 0.5f, 330f);
            Dial(c.X + dsz / 2, c.Y + dsz / 2 + 6, dsz, Ratio(s.Fps, 240), s.Fps > 0 ? I0(s.Fps) : "0", "", "FPS", dsz * 0.40f);
            float sx = c.X + dsz + 26, sw = c.Right - sx;
            string[] lb = { L.T("Ortalama", "Average"), L.T("%1 Düşük", "1% Low"), L.T("En düşük", "Min"), L.T("En yüksek", "Max"), L.T("Süre", "Session") };
            string[] vl = { on ? I0(GameSession.AvgFps) : "--", double.IsNaN(s.Low1) ? "--" : I0(s.Low1), on && !double.IsNaN(GameSession.MinFps) ? I0(GameSession.MinFps) : "--", on ? I0(GameSession.MaxFps) : "--", on ? Dur(GameSession.Elapsed) : "--" };
            double[] pc = { on ? Ratio(GameSession.AvgFps, 240) : 0, Ratio(s.Low1, 240), on ? Ratio(GameSession.MinFps, 240) : 0, on ? Ratio(GameSession.MaxFps, 240) : 0, 0 };
            StatColumn(sx, c.Y, sw, dsz + 6, lb, vl, pc, null);
            float gy = c.Y + dsz + 62, gh = c.Bottom - gy;
            float half = (c.Width - 20) / 2f;
            Txt(L.T("FPS GEÇMİŞİ", "FPS HISTORY"), F(17, true, false), t.Dim, c.X, gy - 28, half, 24, 0);
            Txt(L.T("KARE SÜRESİ", "FRAMETIME"), F(17, true, false), t.Dim, c.X + half + 20, gy - 28, half, 24, 0);
            Graph(new RectangleF(c.X, gy, half, gh), "fps", 60, 0, true, true);
            Graph(new RectangleF(c.X + half + 20, gy, half, gh), "frame", 20, 0, true, true);
        }

        private void PApps(RectangleF r, Snap s)
        {
            Theme t = _th;
            RectangleF c = Card(r, L.T("UYGULAMALAR", "APPLICATIONS"), null, null);
            Font f = F(20, true, false), fd = F(15, true, false), fv = F(20, true, false);
            float rowH = Math.Min(30f, (c.Height - 56f) / 7f), y = c.Y - 4;
            Txt(L.T("EN ÇOK CPU", "TOP CPU"), fd, t.Dim, c.X, y, 200, 20, 0); y += 20;
            for (int i = 0; i < 3; i++, y += rowH)
            {
                ProcItem p = i < s.TopCpu.Length ? s.TopCpu[i] : null;
                if (p == null) continue;
                MTxt(p.Name, f, t.Tx, c.X, y, c.Width * 0.5f, rowH, 0);
                BarAt(c.X + c.Width * 0.5f + 8, y + rowH / 2f - 4, c.Width * 0.5f - 78, Math.Min(100, p.Cpu * 2), p.Cpu > 40, 8);
                Txt(p.Cpu.ToString("0.0", CultureInfo.InvariantCulture) + "%", fv, t.A1, c.Right - 66, y, 66, rowH, 2);
            }
            y += 4;
            Txt(L.T("EN ÇOK RAM", "TOP MEMORY"), fd, t.Dim, c.X, y, 200, 20, 0); y += 20;
            for (int i = 0; i < 3 && i < s.TopProcs.Length; i++, y += rowH)
            {
                ProcItem p = s.TopProcs[i];
                MTxt(p.Name, f, t.Tx, c.X, y, c.Width * 0.5f, rowH, 0);
                BarAt(c.X + c.Width * 0.5f + 8, y + rowH / 2f - 4, c.Width * 0.5f - 112, Ratio(p.MemMb, Math.Max(1, s.MemTotal)) * 4, false, 8);
                Txt(I0(p.MemMb) + " MB", fv, t.A1, c.Right - 90, y, 90, rowH, 2);
            }
            y += 6;
            var sr = new RectangleF(c.X, y, c.Width, Math.Min(34f, c.Bottom - y + 4));
            using (GraphicsPath sp = RR(sr, 10)) { G.FillPath(Br(Al(t.A1, 35)), sp); G.DrawPath(Pn(t.A1, 1.2f), sp); }
            Txt("OnMon", f, t.Tx, sr.X + 12, sr.Y, 100, sr.Height, 0);
            Txt(I0(s.SelfMb) + " MB   ·   CPU " + s.SelfCpu.ToString("0.0", CultureInfo.InvariantCulture) + "%", f, t.A1, sr.X + 100, sr.Y, sr.Width - 112, sr.Height, 2);
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

        // ---------------- yeni nesil modern & sade widget'lar ----------------
        private void PHero(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            DateTime now = DateTime.Now;
            CultureInfo ci = L.Culture;
            float pad = 22f, innerH = r.Height - 24f, y0 = r.Y + 12f;
            G.FillRectangle(Br(t.A1), r.X + pad, y0 + 4, 5f, Math.Max(16f, innerH - 8f));

            string brand = string.IsNullOrEmpty(s.Brand) ? "SYSTEM" : s.Brand.ToUpper(ci);
            string mobo = string.IsNullOrEmpty(s.MoboModel) ? s.Chipset : s.MoboModel;
            string topTitle = string.IsNullOrEmpty(mobo) ? brand : (brand + "  ·  " + mobo);
            string hwLine = ShortHw(s.CpuModel);
            string gShort = ShortHw(s.GpuModel);
            if (!string.IsNullOrEmpty(gShort)) hwLine = string.IsNullOrEmpty(hwLine) ? gShort : (hwLine + "   ·   " + gShort);
            if (!string.IsNullOrEmpty(s.MemSpeed)) hwLine += "   ·   " + s.MemSpeed;

            float leftW = r.Width * (r.Width >= 1100 ? 0.50f : 0.62f);
            Font fBrand = F(Math.Min(30f, r.Height * 0.34f), true, false);
            Font fHw = F(Math.Min(21f, r.Height * 0.24f), false, true);
            Txt(Fit(topTitle, fBrand, leftW - 24), fBrand, t.Tx, r.X + pad + 16, y0, leftW - 24, innerH * 0.54f, 0);
            Txt(Fit(hwLine, fHw, leftW - 24), fHw, t.Dim, r.X + pad + 16, y0 + innerH * 0.52f, leftW - 24, innerH * 0.48f, 0);

            if (r.Width >= 1100)
            {
                float mx = r.X + leftW + 12, mw = r.Width * 0.23f;
                G.DrawLine(Pn(Al(t.Line, 160), 1f), mx - 8, y0 + 6, mx - 8, r.Bottom - 18);
                bool inGame = GameSession.Active || !string.IsNullOrEmpty(s.Game);
                string midTop = inGame ? ("▶ " + Shorten(GameSession.Active ? GameSession.Game : s.Game, 22)) : (L.T("AÇIK SÜRE  ", "UPTIME  ") + s.Uptime);
                string midBot;
                if (!double.IsNaN(s.SysBat))
                    midBot = L.T("PİL ", "BATTERY ") + I0(s.SysBat) + "%" + (s.SysBatChg ? " ⚡" : (s.SysBatMin > 0 ? " · " + AppPrefs.FmtMinutes(s.SysBatMin) : ""));
                else if (s.Fps > 0)
                    midBot = I0(s.Fps) + " FPS" + (!double.IsNaN(s.Low1) ? "  ·  1% " + I0(s.Low1) : "");
                else if (!double.IsNaN(s.Ping))
                    midBot = "PING " + I0(s.Ping) + " ms  ·  " + s.Ip;
                else
                    midBot = "OnMon v" + Updater.AppVersion;
                Txt(Fit(midTop, F(21, true, false), mw - 12), F(21, true, false), inGame ? t.A1 : t.Tx, mx + 6, y0 + 2, mw - 12, innerH * 0.52f, 0);
                Txt(Fit(midBot, F(18, false, true), mw - 12), F(18, false, true), t.Dim, mx + 6, y0 + innerH * 0.52f, mw - 12, innerH * 0.46f, 0);
            }

            float rightW = r.Width * 0.25f;
            string clk = AppPrefs.FmtClock(now);
            string dt = now.ToString("d MMM dddd", ci) + (Extras.WxTemp != "--" ? "  ·  " + AppPrefs.FmtWx(Extras.WxTemp) : "");
            Font fClk = F(Math.Min(40f, r.Height * 0.44f), true, false);
            Font fDt = F(Math.Min(19f, r.Height * 0.22f), false, true);
            Txt(clk, fClk, t.A1, r.Right - pad - rightW, y0 - 2, rightW, innerH * 0.58f, 2);
            Txt(Fit(dt, fDt, rightW), fDt, t.Dim, r.Right - pad - rightW, y0 + innerH * 0.55f, rightW, innerH * 0.45f, 2);
        }

        private void PDualRing(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            float pad = 22f, halfW = (r.Width - pad * 2 - 28f) / 2f;
            float midX = r.X + r.Width / 2f;
            G.DrawLine(Pn(Al(t.Line, 170), 1.2f), midX, r.Y + 18, midX, r.Bottom - 18);

            for (int side = 0; side < 2; side++)
            {
                bool isCpu = side == 0;
                float x = isCpu ? (r.X + pad) : (midX + 14f);
                float y = r.Y + 16f, h = r.Height - 32f;
                string tag = isCpu ? "CPU" : "GPU";
                string model = ShortHw(isCpu ? s.CpuModel : s.GpuModel);
                double load = isCpu ? s.CpuLoad : s.GpuLoad;
                double temp = isCpu ? s.CpuTemp : s.GpuTemp;
                bool hot = Hot(temp, isCpu ? 85 : 83);

                G.FillRectangle(Br(t.A1), x, y + 4, 4f, 26f);
                Font fTag = F(26, true, false), fMod = F(20, false, true), fTmp = F(26, true, false);
                Txt(tag, fTag, t.Tx, x + 12, y, 80, 32, 0);
                float tmpW = TW(Tc(temp), fTmp) + 10;
                Txt(Tc(temp), fTmp, hot ? t.Crit : t.A1, x + halfW - tmpW, y, tmpW, 32, 2);
                float modX = x + 82, modW = Math.Max(20f, halfW - 88f - tmpW);
                Txt(Fit(model, fMod, modW), fMod, t.Dim, modX, y + 2, modW, 30, 0);

                float bodyY = y + 44f, bodyH = Math.Max(60f, h - 44f);
                string[] lbs = isCpu
                    ? new[] { L.T("Saat", "Clock"), L.T("Güç", "Power"), "Fan" }
                    : new[] { L.T("Saat", "Clock"), L.T("Güç", "Power"), "VRAM" };
                string[] vls = isCpu
                    ? new[] { Mhz(s.CpuClock), Wt(s.CpuW), Rpm(s.CpuFan) }
                    : new[] { Mhz(s.GpuClock), Wt(s.GpuW), s.VramTotal > 0 ? Gb(s.VramUsed / 1024.0) + " / " + Gb(s.VramTotal / 1024.0) : Mb(s.VramUsed) };
                double[] pcs = isCpu
                    ? new[] { Ratio(s.CpuClock, 6000), Ratio(s.CpuW, 250), Ratio(s.CpuFan, 3000) }
                    : new[] { Ratio(s.GpuClock, 3000), Ratio(s.GpuW, 450), Ratio(s.VramUsed, s.VramTotal) };

                if (halfW >= 380f && bodyH < halfW * 0.95f)
                {
                    float dsz = Math.Min(bodyH, halfW * 0.44f);
                    Dial(x + dsz / 2f, bodyY + bodyH / 2f, dsz, load, I0(load), "%", L.T("YÜK", "LOAD"), dsz * 0.30f);
                    float sx = x + dsz + 20f, sw = Math.Max(60f, x + halfW - sx);
                    StatColumn(sx, bodyY, sw, bodyH, lbs, vls, pcs, null);
                }
                else
                {
                    float dsz = Math.Min(halfW * 0.78f, bodyH * 0.54f);
                    Dial(x + halfW / 2f, bodyY + dsz / 2f, dsz, load, I0(load), "%", L.T("YÜK", "LOAD"), dsz * 0.30f);
                    float sy = bodyY + dsz + 12f, sh = Math.Max(50f, bodyH - dsz - 12f);
                    StatColumn(x, sy, halfW, sh, lbs, vls, pcs, null);
                }
            }
        }

        private void PMiniCpu(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            float pad = 20f, x = r.X + pad, w = r.Width - pad * 2;
            if (r.Height >= 150)
                Graph(new RectangleF(x, r.Bottom - r.Height * 0.42f, w, r.Height * 0.26f), "cpuTemp", 60, 0, false, true);

            Font fTag = F(22, true, false), fSub = F(18, false, true), fTmp = F(28, true, false);
            Txt("CPU", fTag, t.A1, x, r.Y + 14, 70, 28, 0);
            bool hot = Hot(s.CpuTemp, 85);
            string tmp = Tc(s.CpuTemp);
            float tw = TW(tmp, fTmp) + 8;
            Txt(tmp, fTmp, hot ? t.Crit : t.Tx, r.Right - pad - tw, r.Y + 12, tw, 32, 2);
            Txt(Fit(ShortHw(s.CpuModel), fSub, Math.Max(20f, w - 76 - tw)), fSub, t.Dim, x + 66, r.Y + 16, Math.Max(20f, w - 76 - tw), 26, 0);

            float bigSz = Math.Max(34f, Math.Min(68f, r.Height * 0.30f));
            Txt(Pct(s.CpuLoad), F(bigSz, true, false), t.Tx, x, r.Y + 46, w * 0.65f, bigSz * 1.15f, 0);
            BarAt(x, r.Bottom - 48, w, s.CpuLoad, s.CpuLoad > 92, 9f);
            Font fFoot = F(19, true, false);
            Txt(Mhz(s.CpuClock), fFoot, t.Dim, x, r.Bottom - 34, w * 0.55f, 24, 0);
            Txt(Wt(s.CpuW), fFoot, t.A1, x + w * 0.45f, r.Bottom - 34, w * 0.55f, 24, 2);
        }

        private void PMiniGpu(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            float pad = 20f, x = r.X + pad, w = r.Width - pad * 2;
            if (r.Height >= 150)
                Graph(new RectangleF(x, r.Bottom - r.Height * 0.42f, w, r.Height * 0.26f), "gpuLoad", 100, 100, false, true);

            Font fTag = F(22, true, false), fSub = F(18, false, true), fTmp = F(28, true, false);
            Txt("GPU", fTag, t.A1, x, r.Y + 14, 70, 28, 0);
            bool hot = Hot(s.GpuTemp, 83);
            string tmp = Tc(s.GpuTemp);
            float tw = TW(tmp, fTmp) + 8;
            Txt(tmp, fTmp, hot ? t.Crit : t.Tx, r.Right - pad - tw, r.Y + 12, tw, 32, 2);
            Txt(Fit(ShortHw(s.GpuModel), fSub, Math.Max(20f, w - 76 - tw)), fSub, t.Dim, x + 66, r.Y + 16, Math.Max(20f, w - 76 - tw), 26, 0);

            float bigSz = Math.Max(34f, Math.Min(68f, r.Height * 0.30f));
            Txt(Pct(s.GpuLoad), F(bigSz, true, false), t.Tx, x, r.Y + 46, w * 0.65f, bigSz * 1.15f, 0);
            BarAt(x, r.Bottom - 48, w, s.GpuLoad, s.GpuLoad > 95, 9f);
            Font fFoot = F(19, true, false);
            string vram = s.VramTotal > 0 ? Gb(s.VramUsed / 1024.0) + " VRAM" : Mhz(s.GpuClock);
            Txt(vram, fFoot, t.Dim, x, r.Bottom - 34, w * 0.60f, 24, 0);
            Txt(Wt(s.GpuW), fFoot, t.A1, x + w * 0.40f, r.Bottom - 34, w * 0.60f, 24, 2);
        }

        private void PMiniRam(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            float pad = 20f, x = r.X + pad, w = r.Width - pad * 2;
            Font fTag = F(22, true, false), fSub = F(18, false, true), fR = F(24, true, false);
            Txt("RAM", fTag, t.A1, x, r.Y + 14, 74, 28, 0);
            string rTop = !double.IsNaN(s.DimmTemp) ? Tc(s.DimmTemp) : Gb(s.MemTotal / 1024.0);
            float tw = TW(rTop, fR) + 8;
            Txt(rTop, fR, t.Tx, r.Right - pad - tw, r.Y + 14, tw, 28, 2);
            string spd = string.IsNullOrEmpty(s.MemSpeed) ? L.T("BELLEK", "MEMORY") : s.MemSpeed;
            Txt(Fit(spd, fSub, Math.Max(20f, w - 80 - tw)), fSub, t.Dim, x + 70, r.Y + 16, Math.Max(20f, w - 80 - tw), 26, 0);

            float bigSz = Math.Max(32f, Math.Min(60f, r.Height * 0.28f));
            Txt(Gb(s.MemUsed / 1024.0), F(bigSz, true, false), t.Tx, x, r.Y + 48, w * 0.72f, bigSz * 1.15f, 0);
            Txt(Pct(s.MemLoad), F(26, true, false), s.MemLoad > 90 ? t.Crit : t.A1, x + w * 0.65f, r.Y + 58, w * 0.35f, 34, 2);

            BarAt(x, r.Bottom - 48, w, s.MemLoad, s.MemLoad > 90, 9f);
            Font fFoot = F(19, true, false);
            Txt(L.T("Boş ", "Free ") + Gb(s.MemFree / 1024.0), fFoot, t.Dim, x, r.Bottom - 34, w * 0.52f, 24, 0);
            double io = (double.IsNaN(s.DiskR) ? 0 : s.DiskR) + (double.IsNaN(s.DiskW) ? 0 : s.DiskW);
            Txt("I/O " + (double.IsNaN(s.DiskR) && double.IsNaN(s.DiskW) ? "--" : D1(io) + " MB/s"), fFoot, t.A1, x + w * 0.45f, r.Bottom - 34, w * 0.55f, 24, 2);
        }

        private void PThermals(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, L.T("TERMAL & SOĞUTMA", "THERMALS & COOLING"), L.T("Sensör Matrisi", "Sensor Matrix"), Tc(s.CpuTemp) + " · " + Tc(s.GpuTemp));
            Theme t = _th;
            float colW = (c.Width - 28f) / 2f;
            G.DrawLine(Pn(Al(t.Line, 150), 1f), c.X + colW + 14f, c.Y + 4, c.X + colW + 14f, c.Bottom - 4);

            double auxTemp = !double.IsNaN(s.DimmTemp) ? s.DimmTemp : (s.Drives.Length > 0 ? s.Drives[0].TempC : double.NaN);
            string auxName = !double.IsNaN(s.DimmTemp) ? "RAM DIMM" : L.T("NVMe / Disk", "NVMe / Drive");

            string[] tL = { L.T("CPU Paket", "CPU Package"), L.T("CPU Diyot", "CPU Diode"), L.T("GPU Çekirdek", "GPU Core"), "GPU Hotspot", L.T("Anakart", "Motherboard"), auxName };
            string[] tV = { Tc(s.CpuTemp), Tc(s.CpuDiode), Tc(s.GpuTemp), Tc(s.GpuHot), Tc(s.MoboTemp), Tc(auxTemp) };
            double[] tP = { Ratio(s.CpuTemp, 100), Ratio(s.CpuDiode, 100), Ratio(s.GpuTemp, 100), Ratio(s.GpuHot, 110), Ratio(s.MoboTemp, 90), Ratio(auxTemp, 85) };
            bool[] tH = { Hot(s.CpuTemp, 85), Hot(s.CpuDiode, 88), Hot(s.GpuTemp, 83), Hot(s.GpuHot, 95), Hot(s.MoboTemp, 75), Hot(auxTemp, 70) };

            double totW = (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW);
            string[] fL = { "CPU Fan", L.T("Kasa 1", "Chassis 1"), L.T("Kasa 2", "Chassis 2"), L.T("AIO Pompa", "AIO Pump"), "GPU Fan", L.T("Toplam Güç", "Total Power") };
            string[] fV = { Rpm(s.CpuFan), Rpm(s.Cha1), Rpm(s.Cha2), Rpm(s.Pump), Rpm(s.GpuFan), I0(totW) + " W" };
            double[] fP = { Ratio(s.CpuFan, 3000), Ratio(s.Cha1, 3000), Ratio(s.Cha2, 3000), Ratio(s.Pump, 4000), Ratio(s.GpuFan, 3500), Ratio(totW, 650) };

            StatColumn(c.X, c.Y, colW, c.Height, tL, tV, tP, tH);
            StatColumn(c.X + colW + 28f, c.Y, colW, c.Height, fL, fV, fP, null);
        }

        private void PPerfStrip(RectangleF r, Snap s)
        {
            Theme t = _th;
            Panel(r);
            float pad = 18f, colW = (r.Width - pad * 2) / 5f;
            string[] tags = { "CPU", "GPU", "RAM", "FPS", L.T("AĞ", "NET") };
            string[] bigs = {
                Pct(s.CpuLoad),
                Pct(s.GpuLoad),
                Pct(s.MemLoad),
                s.Fps > 0 ? I0(s.Fps) : "--",
                Spd(s.NetDown)
            };
            string[] subs = {
                Tc(s.CpuTemp) + "  ·  " + Wt(s.CpuW),
                Tc(s.GpuTemp) + "  ·  " + Wt(s.GpuW),
                Gb(s.MemUsed / 1024.0) + " / " + Gb(s.MemTotal / 1024.0),
                s.FrameMs > 0 ? D1(s.FrameMs) + " ms" + (!double.IsNaN(s.Low1) ? " · 1% " + I0(s.Low1) : "") : L.T("Masaüstü", "Desktop"),
                "▲ " + Spd(s.NetUp) + (!double.IsNaN(s.Ping) ? " · " + I0(s.Ping) + " ms" : "")
            };
            double[] pcts = { s.CpuLoad, s.GpuLoad, s.MemLoad, Ratio(s.Fps, 240), Ratio(s.NetDown, 12800) };
            bool[] hots = { Hot(s.CpuTemp, 85), Hot(s.GpuTemp, 83), s.MemLoad > 90, false, false };

            for (int i = 0; i < 5; i++)
            {
                float x = r.X + pad + i * colW;
                if (i > 0) G.DrawLine(Pn(Al(t.Line, 160), 1f), x - 8, r.Y + 14, x - 8, r.Bottom - 14);
                float iw = colW - 18f;
                Txt(tags[i], F(18, true, false), t.A1, x, r.Y + 10, iw * 0.4f, 24, 0);
                Txt(Fit(bigs[i], F(30, true, false), iw * 0.65f), F(30, true, false), hots[i] ? t.Crit : t.Tx, x + iw * 0.35f, r.Y + 6, iw * 0.65f, 34, 2);
                BarAt(x, r.Y + r.Height * 0.54f, iw, pcts[i], hots[i], 7f);
                Txt(Fit(subs[i], F(17, false, true), iw), F(17, false, true), t.Dim, x, r.Bottom - 30, iw, 24, 0);
            }
        }

        private void PMedia(RectangleF r, Snap s)
        {
            Theme t = _th;
            MediaInfo mi = MediaControl.Read();
            string appTag = mi != null && mi.IsActive ? mi.AppName : L.T("MEDYA", "MEDIA");
            RectangleF c = Card(r, L.T("ŞİMDİ ÇALAN", "NOW PLAYING"), appTag, null);

            float pad = 4f;
            float btnSz = Math.Min(42f, Math.Max(30f, c.Height * 0.52f));
            float btnsW = btnSz * 3 + 16f;
            float textW = Math.Max(40f, c.Width - btnsW - 14f);

            string title = (mi != null && mi.IsActive && !string.IsNullOrEmpty(mi.Title))
                ? mi.Title
                : L.T("Aktif medya oynatıcı bulunamadı", "No active media playing");
            string artist = (mi != null && mi.IsActive && !string.IsNullOrEmpty(mi.Artist))
                ? mi.Artist
                : L.T("Spotify, YouTube veya VLC açın", "Open Spotify, YouTube or VLC");

            Font fT = F(Math.Min(26f, Math.Max(16f, c.Height * 0.26f)), true, false);
            Font fA = F(Math.Min(19f, Math.Max(14f, c.Height * 0.19f)), false, true);
            Txt(Fit(title, fT, textW), fT, (mi != null && mi.IsActive) ? t.Tx : t.Dim, c.X + pad, c.Y + 4, textW, c.Height * 0.52f, 0);
            Txt(Fit(artist, fA, textW), fA, (mi != null && mi.IsActive) ? t.A1 : t.Dim, c.X + pad, c.Y + c.Height * 0.52f, textW, c.Height * 0.42f, 0);

            float bx0 = c.Right - btnsW;
            float by = c.Y + (c.Height - btnSz) / 2f;
            string[] icons = { "⏮", "⏯", "⏭" };
            string[] acts = { "media:prev", "media:play", "media:next" };
            for (int i = 0; i < 3; i++)
            {
                var br = new RectangleF(bx0 + i * (btnSz + 8f), by, btnSz, btnSz);
                bool mid = i == 1;
                using (GraphicsPath gp = RR(br, btnSz / 2f))
                {
                    G.FillPath(Br(mid ? Al(t.A1, 55) : t.Tile), gp);
                    G.DrawPath(Pn(mid ? t.A1 : t.Line, mid ? 1.8f : 1.2f), gp);
                }
                Txt(icons[i], F(btnSz * 0.45f, true, false), mid ? t.A1 : t.Tx, br.X, br.Y, br.Width, br.Height, 1);
                _hits.Add(new KeyValuePair<RectangleF, string>(br, acts[i]));
            }
        }
    }
}
