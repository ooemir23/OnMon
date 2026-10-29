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
        private string _prevLayout;
        private bool StripOn { get { return !_hideStrip && (_audioBar || _micBar || _volBar); } }
        private const float VolW = 300f;
        private float _lastVol; private bool _lastVolMuted; private RectangleF _volRect; private bool _volDrag;
        private float _lastMic = -1; private bool _lastMuted;
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
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "layout" && (v == "classic" || v == "hud" || v == "cockpit" || v == "aida" || v == "studio")) _layout = v;
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
                    else if (k == "micfav") { _micFavs.Clear(); foreach (string x in v.Split(new[] { '|' })) if (x.Length > 0) _micFavs.Add(x); }
                    else if (k == "soundfav") { _favs.Clear(); foreach (string x in v.Split(new[] { '|' })) if (x.Length > 0) _favs.Add(x); }
                    else if (k == "theme") _th = Themes.Get(v);
                    else if (k == "margin") int.TryParse(v, out _margin);
                    else if (k == "city" && v.Length > 0) _city = v;
                }
            }
            catch { }
        }

        internal void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, new[] { "layout=" + _layout, "theme=" + _th.Id, "margin=" + _margin, "city=" + _city, "audiobar=" + (_audioBar ? "1" : "0"), "micbar=" + (_micBar ? "1" : "0"), "volbar=" + (_volBar ? "1" : "0"), "hidestrip=" + (_hideStrip ? "1" : "0"), "autogame=" + (_autoGame ? "1" : "0"), "topbar=" + (_topBar ? "1" : "0"), "lowcpu2=" + (_lowCpu ? "1" : "0"), "fanmap=" + string.Join(",", TelemetryCollector.FanMap.Select(x => x.ToString()).ToArray()), "topmost=" + (TopMost ? "1" : "0"), "soundfav=" + string.Join("|", _favs), "micfav=" + string.Join("|", _micFavs) });
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
            ShowInTaskbar = true;
            MinimizeBox = true;
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

            GameSession.LoadHistory(); AudioProfiles.Load(); Peripherals.StartBackground();
            BuildMenu();
            _timer.Interval = 500;
            _timer.Tick += delegate
            {
                if (WindowState == FormWindowState.Minimized || !Visible) return;
                _tickN++;
                if (_micBar || _layout == "studio" || _layout == "hud")
                {
                    AudioSwitch.PollMic();
                    bool mu = AudioSwitch.MicMuted;
                    if (mu != _lastMuted) { _lastMuted = mu; _dirty = true; }
                }
                if ((_volBar || _layout == "studio" || _layout == "hud") && (_tickN & 3) == 0)
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
            if (_target.Width > 0) { Bounds = _target; Invalidate(); }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _lastMouse = DateTime.UtcNow;
            if (_volDrag && _sc > 0 && _volRect.Width > 0 && e.Button == MouseButtons.Left) SetVolumeTo(((e.X - _ox) / _sc - _volRect.X) / _volRect.Width);
            if (_curHidden) { _curHidden = false; Cursor.Show(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.D1) SetLayout("classic");
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
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
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
                if (ev == 1 && _layout != "hud") { _prevLayout = _layout; _layout = "hud"; }
                else if (ev == -1 && _prevLayout != null) { _layout = _prevLayout; _prevLayout = null; }
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
            var it = new ToolStripMenuItem(text.ToUpper(new CultureInfo("tr-TR")));
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
            m.Opening += delegate { colors.Accent = _th.A1; PopulateMenu(m); };
            ContextMenuStrip = m;
        }

        private SettingsForm _settings;

        internal void OpenSettings()
        {
            if (_settings != null && !_settings.IsDisposed) { _settings.Show(); _settings.WindowState = FormWindowState.Normal; _settings.Activate(); return; }
            _settings = new SettingsForm(this);
            _settings.Show();
        }

        private void PopulateMenu(ContextMenuStrip m)
        {
            foreach (ToolStripItem old in new List<ToolStripItem>(m.Items.Cast<ToolStripItem>())) old.Dispose();
            m.Items.Clear();
            AudioSwitch.PollMic();

            m.Items.Add(new ToolStripMenuItem("ROG PRO SYSTEM MONITOR") { Tag = "hdr", Enabled = false, Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
            m.Items.Add(new ToolStripSeparator());
            var set = Mi("Ayarlar…", "F2", false, delegate { OpenSettings(); });
            set.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            m.Items.Add(set);
            m.Items.Add(Mi(AudioSwitch.MicMuted ? "Mikrofonu aç" : "Mikrofonu sustur", "M", false, delegate { ToggleMic(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Mi("Küçült", null, false, delegate { WindowState = FormWindowState.Minimized; }));
            m.Items.Add(Mi("Panoyu kapat", "", false, delegate { Close(); }));
            m.Items.Add(Mi("Uygulamadan çık", null, false, delegate { Program.ExitApp(); }));
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
            foreach (var h in _hits)
                if (h.Key.Contains(cx, cy))
                {
                    if (h.Value == "mic:toggle") ToggleMic();
                    else if (h.Value == "vol:mute") ToggleVolMute();
                    else if (h.Value == "out:next") NextAudio();
                    else if (h.Value.StartsWith("prof:")) { int pi; if (int.TryParse(h.Value.Substring(5), out pi) && pi >= 0 && pi < AudioProfiles.All.Count) { _profIdx = pi; ApplyProfile(AudioProfiles.All[pi]); } }
                    else if (h.Value == "vol:bar") { _volRect = h.Key; _volDrag = true; SetVolumeTo((cx - h.Key.X) / h.Key.Width); }
                    else SwitchAudio(h.Value);
                    return;
                }
        }

        internal void SetLayout(string id) { _prevLayout = null; _layout = id; SaveSettings(); _dirty = true; Invalidate(); }
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
            G = gr;
            G.SmoothingMode = SmoothingMode.AntiAlias;
            G.TextRenderingHint = TextRenderingHint.AntiAlias;
            G.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Theme t = _th;
            G.Clear(t.Bg);
            _marqActive = false; _marqRects.Clear(); _marqItems.Clear(); _hits.Clear();

            float W = ClientSize.Width, Hh = ClientSize.Height;
            float aw = W - _margin * 2, ah = Hh - _margin * 2;
            bool strip = StripOn;
            float chh = (strip ? 1112f : 1080f) + (_topBar ? InfoH : 0);
            float sc = Math.Max(0.1f, Math.Min(aw / 1920f, ah / chh));
            DrawBackground(W, Hh, sc);

            _ox = _margin + (aw - 1920 * sc) / 2f; _oy = _margin + (ah - chh * sc) / 2f; _sc = sc;
            G.TranslateTransform(_ox, _oy);
            G.ScaleTransform(sc, sc);

            Snap s = _snap;
            if (s == null)
            {
                Txt("Veriler okunuyor…", F(48, false, true), t.Dim, 0, 0, 1920, 1080, 1);
            }
            else
            {
                if (_layout == "studio") DrawStudio(s);
                else if (_layout == "aida") DrawAida(s);
                else if (_layout == "hud") DrawHud(s);
                else if (_layout == "cockpit") DrawCockpit(s);
                else DrawClassic(s);

                if (!string.IsNullOrEmpty(s.Throttle))
                {
                    Font f = F(20, true, false);
                    string m = "⚠ " + s.Throttle;
                    float w = TW(m, f) + 28;
                    var r = new RectangleF(1920 - w - 14 - RightW(), StripOn ? 1084 : 1054, w, 26);
                    using (GraphicsPath p = RR(r, 8)) { G.FillPath(Br(Al(t.Crit, 235)), p); }
                    Txt(m, f, Color.White, r.X, r.Y, r.Width, r.Height, 1);
                }
            }

            DrawAudioBar();
            if (_topBar) DrawInfoBar(_snap, StripOn ? 1112f : 1080f);

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
            RectangleF c = Card(r, "GPU", ShortHw(s.GpuModel), Tc(s.GpuTemp));
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
            Dial(c.X + dsz / 2, c.Y + dsz / 2 + (big ? 0 : 0), dsz, Ratio(s.Fps, 240), s.Fps > 0 ? I0(s.Fps) : "0", "", "FPS", dsz * 0.34f);
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
                L[i] = nm;
                bool hasSpace = d.TotalGb > 0;
                V[i] = hasSpace ? D1(d.FreeGb) + " GB boş" : (double.IsNaN(d.TempC) ? "--" : Tc(d.TempC));
                P[i] = hasSpace ? d.UsedPct : (double.IsNaN(d.TempC) ? 0 : Ratio(d.TempC, 100)); Hh[i] = hasSpace ? d.UsedPct > 92 : Hot(d.TempC, 70);
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

        private void DrawInfoBar(Snap s, float top)
        {
            Theme t = _th;
            var tr = new CultureInfo("tr-TR");
            DateTime now = DateTime.Now;
            float y = top + 2, h = 30;
            Font fb = F(20, true, false), fd = F(20, false, true);
            string brand = (s == null || string.IsNullOrEmpty(s.Brand)) ? "PC" : s.Brand.ToUpper(tr);
            G.FillRectangle(Br(t.A1), 14, y + 4, 4, h - 8);
            Txt(brand, fb, t.Tx, 26, y, 300, h, 0);
            if (Extras.WxTemp != "--") Txt(Extras.WxTemp + "  " + Extras.WxDesc, fd, t.Dim, 26 + TW(brand, fb) + 18, y, 500, h, 0);
            string clock = now.ToString("HH:mm", tr);
            string date = now.ToString("d MMMM dddd", tr);
            string pr = PrayerLeft(now);
            float x = 1920 - 14;
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
            }
        }

        // Sonraki vakit (Güneş hariç): "İkindiye 1sa 23dk"
        private static string PrayerLeft(DateTime now)
        {
            int[] idx = { 0, 2, 3, 4, 5 };
            string[] nm = { "Sabaha", "Öğleye", "İkindiye", "Akşama", "Yatsıya" };
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
                        return nm[k] + " " + (mins >= 60 ? (mins / 60) + "sa " + (mins % 60) + "dk" : mins + "dk");
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

        private static string ShortHw(string m)
        {
            if (string.IsNullOrEmpty(m)) return "";
            m = m.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "");
            string[] cut = { "NVIDIA GeForce ", "NVIDIA ", "GeForce ", "AMD Radeon ", "AMD ", "Intel Core ", "Intel ", " 8-Core Processor", " 6-Core Processor", " 12-Core Processor", " 16-Core Processor", " 4-Core Processor", " Processor", " CPU" };
            foreach (string c in cut)
            {
                int i = m.IndexOf(c, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) m = m.Remove(i, c.Length);
            }
            if (m.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) < 0 && m.StartsWith("RX ")) m = "Radeon " + m;
            return m.Trim().Replace("  ", " ");
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
            float x = 10, y = 1084, h = 26, maxX = 1920 - 340 - RightW();
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
            float w = VolW, h = 26, x = 1920 - 14 - (_micBar ? MicW + 12 : 0) - w, y = 1084;
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
            if (!avail) { Txt("Ses yok", F(16, true, false), t.Dim, x + 40, y, w - 50, h, 0); return; }
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
            Txt(muted ? "KAPALI" : I0(lv * 100) + "%", F(16, true, false), muted ? Color.White : t.Tx, x + w - 58, y, 50, h, 2);
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(x, y, 36, h), "vol:mute"));
            _hits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(bx - 6, y, bw + 12, h), "vol:bar"));
        }

        private void DrawMic()
        {
            Theme t = _th;
            float w = MicW, h = 26, x = 1920 - 14 - w, y = 1084;
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
            if (!avail) { Txt("Mikrofon yok", f, t.Dim, x + 32, y, w - 40, h, 0); }
            else
            {
                string lab = muted ? "KAPALI" : "AÇIK";
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
            string brand = string.IsNullOrEmpty(s.Brand) ? "PC" : s.Brand.ToUpper(new CultureInfo("tr-TR"));
            float fs = 56; Font fb = F(fs, true, false);
            while (fs > 24 && TW(brand, fb) > r.Width - 40) { fs -= 4; fb = F(fs, true, false); }
            Txt(brand, fb, t.Tx, r.X, r.Y + 8, r.Width, 70, 1);
            Txt(now.ToString("HH:mm", CultureInfo.InvariantCulture), F(64, false, true), t.Tx, r.X, r.Y + 76, r.Width, 76, 1);
            var pill = new RectangleF(r.X + 50, r.Y + 156, r.Width - 100, 36);
            using (GraphicsPath p = RR(pill, 8)) G.FillPath(Br(t.Tx), p);
            Txt(now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), F(26, true, false), t.Bg, pill.X, pill.Y, pill.Width, pill.Height, 1);
            Font fs2 = F(21, false, true), fv = F(21, true, false);
            Txt("Çalışma", fs2, t.Dim, r.X + 20, r.Y + 202, 90, 30, 0);
            Txt(s.Uptime, fv, t.Tx, r.X + 112, r.Y + 202, 170, 30, 0);
            Txt("Ses", fs2, t.Dim, r.X + r.Width - 150, r.Y + 202, 60, 30, 0);
            Txt(double.IsNaN(s.Sound) ? "--" : I0(s.Sound), fv, t.Tx, r.X + r.Width - 100, r.Y + 202, 80, 30, 2);
        }

        private void PAMem(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "Bellek", null, null);
            float rh = c.Height / 4f;
            MiniRow(c.X, c.Y, c.Width, rh, "Yük", s.MemLoad, Pct(s.MemLoad), s.MemLoad > 90);
            MiniRow(c.X, c.Y + rh, c.Width, rh, "Hız", 100, string.IsNullOrEmpty(s.MemSpeed) ? "--" : s.MemSpeed, false);
            MiniRow(c.X, c.Y + rh * 2, c.Width, rh, "Kullanım", s.MemLoad, Gb(s.MemUsed / 1024.0), false);
            MiniRow(c.X, c.Y + rh * 3, c.Width, rh, "Boş", 100 - s.MemLoad, Gb(s.MemFree / 1024.0), false);
        }

        private void PAMobo(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "ANAKART", s.MoboModel, null);
            Theme t = _th;
            string[] k = { "Yonga seti", "BIOS", "Yol tipi", "İşlemler" };
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
            Txt("Sıcaklık", F(26, false, true), t.Tx, rx, c.Y + 50, rw * 0.5f, 44, 0);
            Txt(Tc(s.MoboTemp), F(38, true, false), t.Tx, rx + rw * 0.3f, c.Y + 50, rw * 0.7f, 44, 2);
        }

        private void PACpu(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "CPU", ShortHw(s.CpuModel), null);
            Theme t = _th;
            Dial(c.X + 160, c.Y + 150, 290, s.CpuLoad, I0(s.CpuLoad), "%", "YÜK", 96);
            Graph(new RectangleF(c.X, c.Y + 316, 320, c.Height - 316), "cpuTemp", 60, 0, true, true);
            float rx = c.X + 350, rw = c.Width - 350;
            bool pump = !double.IsNaN(s.Pump);
            StatColumn(rx, c.Y, rw, 300,
                new[] { "Sıcaklık", "Saat", "Fan", pump ? "AIO Pompa" : "Kasa Fan" },
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
                Txt((i + 1) + ". CPU", fl, t.Tx, x, y, 90, ch - 8, 0);
                Txt(I0(k.ClockMhz) + " MHz", fv, t.Tx, x + cw - 130, y, 130, ch - 8, 2);
                BarAt(x, y + ch - 14, cw, k.LoadPct, k.LoadPct > 92, 7);
            }
        }

        private void PAGpu(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "GPU", ShortHw(s.GpuModel), null);
            Theme t = _th;
            Dial(c.X + 160, c.Y + 150, 290, s.GpuLoad, I0(s.GpuLoad), "%", "YÜK", 96);
            var gr = new RectangleF(c.X, c.Y + 316, 320, c.Height - 316);
            Graph(gr, "fps", 60, 0, true, true);
            Txt("FPS " + (s.Fps > 0 ? I0(s.Fps) : "--"), F(46, true, false), Al(t.Tx, 70), gr.X, gr.Y, gr.Width, gr.Height, 1);
            float rx = c.X + 350, rw = c.Width - 350;
            StatColumn(rx, c.Y, rw, 300,
                new[] { "Sıcaklık", "Güç", "Saat", "VRAM", "Fan" },
                new[] { Tc(s.GpuTemp), Wt(s.GpuW), Mhz(s.GpuClock), s.VramTotal > 0 ? I0(s.VramUsed) + " / " + I0(s.VramTotal) + " MB" : Mb(s.VramUsed), Rpm(s.GpuFan) },
                new[] { Ratio(s.GpuTemp, 100), Ratio(s.GpuW, 450), Ratio(s.GpuClock, 3000), Ratio(s.VramUsed, s.VramTotal), Ratio(s.GpuFan, 3500) },
                new[] { Hot(s.GpuTemp, 85), false, false, Ratio(s.VramUsed, s.VramTotal) > 92, false });
            Graph(new RectangleF(rx, c.Y + 316, rw, c.Height - 316), "gpuLoad", 100, 100, true, true);
        }

        private void PAStorage(RectangleF r, Snap s)
        {
            RectangleF c = Card(r, "Depolama", null, null);
            Theme t = _th;
            int n = Math.Min(4, s.Drives.Length);
            if (n == 0) { Txt("Disk bilgisi yok", F(24, false, true), t.Dim, c.X, c.Y, c.Width, 40, 0); return; }
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
            RectangleF c = Card(r, "Güç", null, null);
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
            RectangleF c = Card(r, "Ağ", null, s.Ip);
            Theme t = _th;
            string[] k = { "Gönderim", "Toplam", "İndirme", "Toplam" };
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
            RectangleF c = Card(r, "SES", AudioSwitch.DefaultName, AudioSwitch.VolAvailable ? (vm ? "KAPALI" : I0(AudioSwitch.VolLevel * 100) + "%") : null);
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
            MTxt(AudioSwitch.MicAvailable ? AudioSwitch.MicName : "Mikrofon yok", f, tc, mr.X + 34, mr.Y, nw, mr.Height, 0);
            if (AudioSwitch.MicAvailable)
            {
                if (!mm)
                {
                    float lvm = (float)Math.Sqrt(Math.Max(0, Math.Min(1, AudioSwitch.MicLevel)));
                    BarAt(mr.X + 34 + nw + 14, mr.Y + mr.Height / 2f - 4, mr.Width - 34 - nw - 14 - 84, lvm * 100, lvm > 0.92f, 8);
                }
                Txt(mm ? "KAPALI" : "AÇIK", f, tc, mr.Right - 84, mr.Y, 74, mr.Height, 2);
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
                Txt(col == 0 ? "ÇIKIŞ" : "GİRİŞ", fl, t.Dim, cx, y, colW, 18, 0);
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
                FlowRow("PROFİL", profs, c.X, c.Bottom - 32, c.Width, c.Bottom + 4, 1);
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
            // durum noktası + oyun adı
            Color dot = on ? t.A1 : t.Dim;
            if (on && DateTime.Now.Millisecond < 500) G.FillEllipse(Br(Al(t.A1, 70)), r.X + 16, r.Y + 26, 34, 34);
            G.FillEllipse(Br(dot), r.X + 24, r.Y + 34, 18, 18);
            string game = on ? GameSession.Game : "Oyun bekleniyor";
            Font fg = F(40, true, false);
            Txt(Fit(game, fg, 430), fg, t.Tx, r.X + 56, r.Y + 6, 440, 50, 0);
            Txt(on ? "OYUNDA  " + Dur(GameSession.Elapsed) : "BEKLEMEDE", F(19, true, false), t.A1, r.X + 58, r.Y + 54, 440, 26, 0);
            float x = r.X + 480, w = (r.Right - 20 - x) / 7f;
            string avg = on ? I0(GameSession.AvgFps) : "--";
            string low = double.IsNaN(s.Low1) ? "--" : I0(s.Low1);
            Color hc = Hot(s.CpuTemp, 85) ? t.Crit : t.Tx, hg = Hot(s.GpuTemp, 80) ? t.Crit : t.Tx;
            double pw = (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW);
            string[] lab = { "ORT FPS", "%1 DÜŞÜK", "KARE", "PING", "CPU", "GPU", "GÜÇ" };
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
            RectangleF c = Card(r, "FPS", on ? Shorten(GameSession.Game, 26) : "Oyun algılanmadı", s.FrameMs > 0 ? D1(s.FrameMs) + " ms" : null);
            float dsz = Math.Min(c.Width * 0.5f, 330f);
            Dial(c.X + dsz / 2, c.Y + dsz / 2 + 6, dsz, Ratio(s.Fps, 240), s.Fps > 0 ? I0(s.Fps) : "0", "", "FPS", dsz * 0.40f);
            float sx = c.X + dsz + 26, sw = c.Right - sx;
            string[] lb = { "Ortalama", "%1 Düşük", "En düşük", "En yüksek", "Süre" };
            string[] vl = { on ? I0(GameSession.AvgFps) : "--", double.IsNaN(s.Low1) ? "--" : I0(s.Low1), on && !double.IsNaN(GameSession.MinFps) ? I0(GameSession.MinFps) : "--", on ? I0(GameSession.MaxFps) : "--", on ? Dur(GameSession.Elapsed) : "--" };
            double[] pc = { on ? Ratio(GameSession.AvgFps, 240) : 0, Ratio(s.Low1, 240), on ? Ratio(GameSession.MinFps, 240) : 0, on ? Ratio(GameSession.MaxFps, 240) : 0, 0 };
            StatColumn(sx, c.Y, sw, dsz + 6, lb, vl, pc, null);
            float gy = c.Y + dsz + 62, gh = c.Bottom - gy;
            float half = (c.Width - 20) / 2f;
            Txt("FPS GEÇMİŞİ", F(17, true, false), t.Dim, c.X, gy - 28, half, 24, 0);
            Txt("KARE SÜRESİ", F(17, true, false), t.Dim, c.X + half + 20, gy - 28, half, 24, 0);
            Graph(new RectangleF(c.X, gy, half, gh), "fps", 60, 0, true, true);
            Graph(new RectangleF(c.X + half + 20, gy, half, gh), "frame", 20, 0, true, true);
        }

        private void PApps(RectangleF r, Snap s)
        {
            Theme t = _th;
            RectangleF c = Card(r, "UYGULAMALAR", null, null);
            Font f = F(20, true, false), fd = F(15, true, false), fv = F(20, true, false);
            float rowH = Math.Min(30f, (c.Height - 56f) / 7f), y = c.Y - 4;
            // en çok CPU
            Txt("EN ÇOK CPU", fd, t.Dim, c.X, y, 200, 20, 0); y += 20;
            for (int i = 0; i < 3; i++, y += rowH)
            {
                ProcItem p = i < s.TopCpu.Length ? s.TopCpu[i] : null;
                if (p == null) continue;
                MTxt(p.Name, f, t.Tx, c.X, y, c.Width * 0.5f, rowH, 0);
                BarAt(c.X + c.Width * 0.5f + 8, y + rowH / 2f - 4, c.Width * 0.5f - 78, Math.Min(100, p.Cpu * 2), p.Cpu > 40, 8);
                Txt(p.Cpu.ToString("0.0", CultureInfo.InvariantCulture) + "%", fv, t.A1, c.Right - 66, y, 66, rowH, 2);
            }
            y += 4;
            // en çok RAM
            Txt("EN ÇOK RAM", fd, t.Dim, c.X, y, 200, 20, 0); y += 20;
            for (int i = 0; i < 3 && i < s.TopProcs.Length; i++, y += rowH)
            {
                ProcItem p = s.TopProcs[i];
                MTxt(p.Name, f, t.Tx, c.X, y, c.Width * 0.5f, rowH, 0);
                BarAt(c.X + c.Width * 0.5f + 8, y + rowH / 2f - 4, c.Width * 0.5f - 112, Ratio(p.MemMb, Math.Max(1, s.MemTotal)) * 4, false, 8);
                Txt(I0(p.MemMb) + " MB", fv, t.A1, c.Right - 90, y, 90, rowH, 2);
            }
            // bu uygulama
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
    }
}
