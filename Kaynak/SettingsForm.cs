using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>Tüm ayarların yapıldığı pencere. Değişiklikler anında uygulanır.</summary>
    internal sealed class SettingsForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(22, 24, 29), Side = Color.FromArgb(17, 18, 22), Card = Color.FromArgb(30, 33, 40),
            Tx = Color.FromArgb(232, 234, 238), Dim = Color.FromArgb(140, 146, 160), Btn = Color.FromArgb(46, 50, 62), Line = Color.FromArgb(52, 56, 66);
        private Color _acc;
        private readonly DashboardForm _d;
        private readonly List<Panel> _pages = new List<Panel>();
        private readonly List<Button> _nav = new List<Button>();
        private Panel _content;
        private bool _loading;
        private ListBox _themeList, _layoutList;
        private ListView _outList, _micList;
        private ProgressBar _micBarCtl;
        private CheckBox _micMute;
        private TextBox _diag;
        private ComboBox _screenBox, _cityBox;
        private ComboBox[] _fanCombo = new ComboBox[4];
        private ListView _fanList, _sessList, _profList, _perList;
        private TextBox _profName;
        private readonly System.Windows.Forms.Timer _t = new System.Windows.Forms.Timer();

        public SettingsForm(DashboardForm d)
        {
            _d = d;
            _acc = d._th.A1;
            Text = "Ayarlar — ROG Pro System Monitor";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(960, 600);
            BackColor = Bg; ForeColor = Tx;
            Font = new Font("Segoe UI", 10f);
            TopMost = true;
            ShowInTaskbar = false;
            try { Icon = Program.TrayIcon(); } catch { }

            var side = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Side };
            Controls.Add(side);
            _content = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(24, 20, 24, 16) };
            Controls.Add(_content);
            _content.BringToFront();

            var title = new Label { Text = "AYARLAR", ForeColor = _acc, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Left = 20, Top = 20, Width = 170, Height = 28 };
            side.Controls.Add(title);

            AddPage(side, "Görünüm", BuildView());
            AddPage(side, "Ses ve mikrofon", BuildAudio());
            AddPage(side, "Konum ve uyarılar", BuildData());
            AddPage(side, "Pencere ve sistem", BuildSystem());
            AddPage(side, "Fan eşleme", BuildFans());
            AddPage(side, "Oyun oturumları", BuildSessions());
            AddPage(side, "Ses profilleri", BuildProfiles());
            AddPage(side, "Cihaz pilleri", BuildDevices());
            AddPage(side, "Güncelleme", BuildUpdate());
            AddPage(side, "Tanılama", BuildDiag());
            ShowPage(0);

            _t.Interval = 400;
            _t.Tick += delegate { Tick(); };
            _t.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _t.Stop();
            base.OnFormClosed(e);
        }

        // ---------------- iskelet ----------------
        private void AddPage(Panel side, string name, Panel page)
        {
            int idx = _pages.Count;
            var b = new Button
            {
                Text = "   " + name, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
                Left = 0, Top = 70 + idx * 48, Width = 200, Height = 44, ForeColor = Tx, BackColor = Side, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 37, 45);
            b.Click += delegate { ShowPage(idx); };
            side.Controls.Add(b);
            _nav.Add(b);
            page.Dock = DockStyle.Fill; page.Visible = false; page.BackColor = Bg;
            _content.Controls.Add(page);
            _pages.Add(page);
        }

        private void ShowPage(int i)
        {
            for (int k = 0; k < _pages.Count; k++)
            {
                _pages[k].Visible = k == i;
                _nav[k].BackColor = k == i ? Color.FromArgb(40, 44, 54) : Side;
                _nav[k].ForeColor = k == i ? _acc : Tx;
            }
            if (i == 1) RefreshAudio();
            if (i == 3) RefreshScreens();
            if (i == 4) RefreshFans();
            if (i == 5) RefreshSessions();
            if (i == 6) RefreshProfiles();
            if (i == 7) RefreshDevices();
            if (i == 8) RefreshUpdate();
            if (i == 9) RefreshDiag();
        }

        private Label H(string text, int top)
        {
            return new Label { Text = text, ForeColor = Tx, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Left = 0, Top = top, Width = 600, Height = 34 };
        }

        private Label Sub(string text, int left, int top, int w)
        {
            return new Label { Text = text, ForeColor = Dim, Left = left, Top = top, Width = w, Height = 22, Font = new Font("Segoe UI", 9f) };
        }

        private Button Bt(string text, int left, int top, int w, EventHandler click)
        {
            var b = new Button { Text = text, Left = left, Top = top, Width = w, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Btn, ForeColor = Tx, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 68, 84);
            b.Click += click;
            return b;
        }

        private CheckBox Ck(string text, int left, int top, int w, bool val, Action<bool> on)
        {
            var c = new CheckBox { Text = text, Left = left, Top = top, Width = w, Height = 28, ForeColor = Tx, Checked = val, FlatStyle = FlatStyle.Standard };
            c.CheckedChanged += delegate { if (!_loading) on(c.Checked); };
            return c;
        }

        private ListView LV(int left, int top, int w, int h, bool checks)
        {
            var lv = new ListView
            {
                Left = left, Top = top, Width = w, Height = h, View = View.Details, FullRowSelect = true, CheckBoxes = checks,
                BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.None, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.None
            };
            return lv;
        }

        // ---------------- Görünüm ----------------
        private Panel BuildView()
        {
            var p = new Panel();
            p.Controls.Add(H("Görünüm", 0));

            p.Controls.Add(Sub("DÜZEN", 0, 44, 200));
            _layoutList = new ListBox { Left = 0, Top = 68, Width = 260, Height = 176, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34 };
            string[] names = { "Klasik", "Oyun HUD", "Pro Kokpit", "Sensör Paneli", "Komuta Merkezi" };
            string[] ids = { "classic", "hud", "cockpit", "aida", "studio" };
            foreach (string n in names) _layoutList.Items.Add(n);
            _layoutList.DrawItem += delegate (object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var br = new SolidBrush(sel ? Color.FromArgb(48, 54, 68) : Card)) e.Graphics.FillRectangle(br, e.Bounds);
                if (sel) using (var br = new SolidBrush(_acc)) e.Graphics.FillRectangle(br, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, _layoutList.Items[e.Index] + "   (" + (e.Index + 1) + ")", Font, new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height), Tx, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            _layoutList.SelectedIndexChanged += delegate { if (!_loading && _layoutList.SelectedIndex >= 0) _d.SetLayout(ids[_layoutList.SelectedIndex]); };
            p.Controls.Add(_layoutList);

            p.Controls.Add(Sub("KENAR BOŞLUĞU", 0, 252, 200));
            var tb = new TrackBar { Left = 0, Top = 274, Width = 220, Minimum = 0, Maximum = 48, TickFrequency = 8, SmallChange = 4, LargeChange = 8, Value = Math.Max(0, Math.Min(48, _d._margin)) };
            var tl = new Label { Left = 226, Top = 282, Width = 60, ForeColor = Tx, Text = _d._margin + " px" };
            tb.Scroll += delegate { tl.Text = tb.Value + " px"; if (!_loading) { _d._margin = tb.Value; _d.SaveSettings(); _d.Invalidate(); } };
            p.Controls.Add(tb); p.Controls.Add(tl);
            { var ckb = Ck("Alt bilgi çubuğu: saat, tarih, sıradaki vakte kalan süre", 0, 330, 280, _d._topBar, v => { _d._topBar = v; _d.SaveSettings(); _d.Invalidate(); }); ckb.Height = 48; p.Controls.Add(ckb); }

            p.Controls.Add(Sub("TEMA (21 seçenek, listeyi kaydır)", 300, 44, 400));
            _themeList = new ListBox { Left = 300, Top = 68, Width = 440, Height = 480, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 40 };
            foreach (Theme t in Themes.All) _themeList.Items.Add(t.Name);
            _themeList.DrawItem += delegate (object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                Theme t = Themes.All[e.Index];
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var br = new SolidBrush(sel ? Color.FromArgb(48, 54, 68) : Card)) e.Graphics.FillRectangle(br, e.Bounds);
                if (sel) using (var br = new SolidBrush(_acc)) e.Graphics.FillRectangle(br, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
                int x = e.Bounds.X + 14, y = e.Bounds.Y + 8;
                Color[] sw = { t.Bg, t.Panel, t.A1, t.A2 };
                for (int i = 0; i < 4; i++)
                {
                    using (var br = new SolidBrush(sw[i])) e.Graphics.FillRectangle(br, x + i * 22, y, 22, 24);
                }
                e.Graphics.DrawRectangle(new Pen(Line), x, y, 88, 24);
                TextRenderer.DrawText(e.Graphics, t.Name, Font, new Rectangle(x + 104, e.Bounds.Y, e.Bounds.Width - 130, e.Bounds.Height), Tx, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            _themeList.SelectedIndexChanged += delegate { if (!_loading && _themeList.SelectedIndex >= 0) { _d.SetTheme(Themes.All[_themeList.SelectedIndex]); _acc = _d._th.A1; _themeList.Invalidate(); } };
            p.Controls.Add(_themeList);

            _loading = true;
            _layoutList.SelectedIndex = Math.Max(0, Array.IndexOf(ids, _d._layout));
            _themeList.SelectedIndex = Math.Max(0, Array.IndexOf(Themes.All, _d._th));
            _loading = false;
            return p;
        }

        // ---------------- Ses ----------------
        private Panel BuildAudio()
        {
            var p = new Panel { AutoScroll = true };
            p.Controls.Add(H("Ses ve mikrofon", 0));

            p.Controls.Add(Sub("ÇIKIŞ CİHAZLARI — işaretlediklerin alt çubukta, SES panelinde ve kısayolla değiştirmede kullanılır", 0, 40, 700));
            _outList = LV(0, 64, 700, 108, true);
            _outList.Columns.Add("", 40); _outList.Columns.Add("Cihaz", 520); _outList.Columns.Add("Durum", 120);
            _outList.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
            {
                if (_loading) return;
                string id = e.Item.Tag as string;
                if (e.Item.Checked) _d._favs.Add(id); else _d._favs.Remove(id);
                _d.SaveSettings(); _d.Invalidate();
            };
            _outList.DoubleClick += delegate { SetDefaultFrom(_outList, true); };
            p.Controls.Add(_outList);
            p.Controls.Add(Bt("Varsayılan yap", 0, 180, 150, delegate { SetDefaultFrom(_outList, true); }));
            p.Controls.Add(Bt("Test sesi çal", 160, 180, 150, delegate { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }));

            p.Controls.Add(Sub("MİKROFON / GİRİŞ CİHAZLARI — işaretlediklerin SES panelinde ve kısayolla değiştirmede kullanılır", 0, 226, 700));
            _micList = LV(0, 250, 700, 92, true);
            _micList.Columns.Add("", 40); _micList.Columns.Add("Cihaz", 520); _micList.Columns.Add("Durum", 120);
            _micList.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
            {
                if (_loading) return;
                string id = e.Item.Tag as string;
                if (e.Item.Checked) _d._micFavs.Add(id); else _d._micFavs.Remove(id);
                _d.SaveSettings(); _d.Invalidate();
            };
            _micList.DoubleClick += delegate { SetDefaultFrom(_micList, false); };
            p.Controls.Add(_micList);
            p.Controls.Add(Bt("Varsayılan yap", 0, 350, 150, delegate { SetDefaultFrom(_micList, false); }));
            _micMute = Ck("Mikrofonu sustur", 160, 352, 190, false, v => { if (v != AudioSwitch.MicMuted) _d.ToggleMic(); });
            p.Controls.Add(_micMute);
            p.Controls.Add(Sub("Anlık seviye", 370, 356, 90));
            _micBarCtl = new ProgressBar { Left = 460, Top = 358, Width = 240, Height = 14, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
            p.Controls.Add(_micBarCtl);

            p.Controls.Add(Sub("ALT ÇUBUK", 0, 400, 300));
            p.Controls.Add(Ck("Ses cihazlarını göster", 0, 424, 340, _d._audioBar, v => { _d._audioBar = v; _d.SaveSettings(); _d.Invalidate(); }));
            p.Controls.Add(Ck("Mikrofon göstergesi", 360, 424, 340, _d._micBar, v => { _d._micBar = v; _d.SaveSettings(); _d.Invalidate(); }));
            p.Controls.Add(Ck("Ses seviyesi kaydırıcısı", 0, 454, 340, _d._volBar, v => { _d._volBar = v; _d.SaveSettings(); _d.Invalidate(); }));
            p.Controls.Add(Ck("Alt çubuğu tamamen gizle (B tuşu)", 360, 454, 340, _d._hideStrip, v => { _d._hideStrip = v; _d.SaveSettings(); _d.Invalidate(); }));

            AddHotkeys(p, 508);
            return p;
        }

        private void SetDefaultFrom(ListView lv, bool output)
        {
            if (lv.SelectedItems.Count == 0) return;
            string id = lv.SelectedItems[0].Tag as string;
            _d.SwitchAudio(id);
            var t = new System.Windows.Forms.Timer { Interval = 900 };
            t.Tick += delegate { t.Stop(); t.Dispose(); RefreshAudio(); };
            t.Start();
        }

        private void RefreshAudio()
        {
            AudioSwitch.Refresh();
            _loading = true;
            try
            {
                _outList.Items.Clear();
                foreach (AudioDevice a in AudioSwitch.Devices)
                {
                    var it = new ListViewItem("") { Tag = a.Id, Checked = _d._favs.Contains(a.Id) };
                    it.SubItems.Add(a.Name); it.SubItems.Add(a.IsDefault ? "● Varsayılan" : "");
                    if (a.IsDefault) it.ForeColor = _acc;
                    _outList.Items.Add(it);
                }
                _micList.Items.Clear();
                foreach (AudioDevice a in AudioSwitch.MicDevices)
                {
                    var it = new ListViewItem("") { Tag = a.Id, Checked = _d._micFavs.Contains(a.Id) };
                    it.SubItems.Add(a.Name); it.SubItems.Add(a.IsDefault ? "● Varsayılan" : "");
                    if (a.IsDefault) it.ForeColor = _acc;
                    _micList.Items.Add(it);
                }
                AudioSwitch.PollMic();
                _micMute.Checked = AudioSwitch.MicMuted;
            }
            finally { _loading = false; }
        }

        // ---------------- Konum / uyarılar ----------------
        private Panel BuildData()
        {
            var p = new Panel();
            p.Controls.Add(H("Konum ve uyarılar", 0));
            p.Controls.Add(Sub("ŞEHİR (hava durumu ve namaz vakitleri)", 0, 44, 400));
            _cityBox = new ComboBox { Left = 0, Top = 68, Width = 260, DropDownStyle = ComboBoxStyle.DropDown, BackColor = Card, ForeColor = Tx, FlatStyle = FlatStyle.Flat };
            foreach (string c in new[] { "istanbul", "ankara", "izmir", "bursa", "antalya", "adana", "konya", "trabzon", "kocaeli", "gaziantep", "eskisehir", "samsun", "kayseri", "mersin", "diyarbakir" }) _cityBox.Items.Add(c);
            _cityBox.Text = _d._city;
            p.Controls.Add(_cityBox);
            p.Controls.Add(Bt("Uygula", 272, 66, 110, delegate
            {
                string c = (_cityBox.Text ?? "").Trim();
                if (c.Length == 0) return;
                _d._city = c; _d.SaveSettings(); Extras.Start(c);
            }));
            p.Controls.Add(Sub("Şehir adını Türkçe karaktersiz yazabilirsin (örn. izmir, sanliurfa).", 0, 104, 600));

            p.Controls.Add(Sub("UYARILAR", 0, 150, 300));
            p.Controls.Add(Ck("Isı uyarıları (CPU 90° / GPU 85° / Disk 70°) — bildirim olarak gösterilir", 0, 176, 700, Program.AlertsEnabled, v => Program.SetAlertsEnabled(v)));
            return p;
        }

        // ---------------- Pencere / sistem ----------------
        private Panel BuildSystem()
        {
            var p = new Panel();
            p.Controls.Add(H("Pencere ve sistem", 0));

            p.Controls.Add(Sub("MONİTÖR", 0, 44, 300));
            _screenBox = new ComboBox { Left = 0, Top = 68, Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Card, ForeColor = Tx, FlatStyle = FlatStyle.Flat };
            p.Controls.Add(_screenBox);
            p.Controls.Add(Bt("Panoyu bu monitöre taşı", 452, 66, 210, delegate
            {
                Screen[] all = Screen.AllScreens;
                if (_screenBox.SelectedIndex >= 0 && _screenBox.SelectedIndex < all.Length) _d.MoveToScreen(all[_screenBox.SelectedIndex]);
            }));

            p.Controls.Add(Sub("PENCERE", 0, 126, 300));
            CheckBox ckTop = null, ckDesk = null;
            ckTop = Ck("Her zaman üstte", 0, 152, 200, _d.TopMost, v => { _d.TopMost = v; if (v && _d._desk) { _d._desk = false; _loading = true; ckDesk.Checked = false; _loading = false; } _d.SaveSettings(); });
            ckDesk = Ck("Arka planda kal: başka pencere panonun üstüne getirilebilir, Win+D küçültmez", 210, 152, 490, _d._desk, v => { _d._desk = v; if (v) { _d.TopMost = false; _loading = true; ckTop.Checked = false; _loading = false; _d.SendToBottom(); } _d.SaveSettings(); });
            ckDesk.Height = 44;
            p.Controls.Add(ckTop); p.Controls.Add(ckDesk);
            p.Controls.Add(Bt("Panoyu küçült", 0, 202, 160, delegate { _d.UserMinimize(); }));
            p.Controls.Add(Bt("Panoyu kapat", 170, 202, 160, delegate { _d.Close(); }));
            { var note = Sub("Arka planda kal açıkken pano görev çubuğunda ve Alt+Tab'ta görünmez, yalnızca sistem tepsisi simgesinden yönetilir. Alt+Tab değişikliği için uygulamayı yeniden başlat.", 0, 246, 700); note.Height = 40; p.Controls.Add(note); }

            p.Controls.Add(Sub("SİSTEM", 0, 300, 300));
            bool auto = false; try { auto = Program.IsAutostartEnabled(); } catch { }
            p.Controls.Add(Ck("Windows ile birlikte başlat (yönetici olarak)", 0, 326, 400, auto, v => System.Threading.ThreadPool.QueueUserWorkItem(delegate { Program.SetAutostart(v); })));

            p.Controls.Add(Ck("Düşük CPU modu (veriler 2 sn'de bir yenilenir)", 0, 358, 500, _d._lowCpu, v => { _d._lowCpu = v; Program.PollIntervalMs = v ? 2000 : 1000; _d.SaveSettings(); }));
            p.Controls.Add(Sub("ESKİ ARAYÜZLER (daha çok RAM kullanır)", 0, 408, 500));
            p.Controls.Add(Bt("Tarayıcıda aç", 0, 434, 160, delegate { Program.LaunchBrowserDashboard(); }));
            p.Controls.Add(Bt("Edge uygulaması olarak aç", 170, 434, 220, delegate { Program.LaunchEdgeDashboard(); }));

            p.Controls.Add(Bt("Uygulamadan çık", 0, 500, 180, delegate { Program.ExitApp(); }));
            return p;
        }

        private void RefreshScreens()
        {
            _screenBox.Items.Clear();
            Screen[] all = Screen.AllScreens;
            Screen cur = Screen.FromControl(_d);
            for (int i = 0; i < all.Length; i++)
            {
                Screen sc = all[i];
                _screenBox.Items.Add("Monitör " + (i + 1) + (sc.Primary ? " (ana)" : "") + " — " + sc.Bounds.Width + "×" + sc.Bounds.Height);
                if (sc.DeviceName == cur.DeviceName) _screenBox.SelectedIndex = i;
            }
        }

        // ---------------- Fan eşleme ----------------
        private Panel BuildFans()
        {
            var p = new Panel();
            p.Controls.Add(H("Fan eşleme", 0));
            p.Controls.Add(Sub("Anakartın fan girişleri aşağıda canlı görünür. Hangisinin CPU fanı, kasa fanı veya pompa olduğunu seç.", 0, 40, 740));
            _fanList = LV(0, 68, 740, 190, false);
            _fanList.Columns.Add("Fan girişi", 300); _fanList.Columns.Add("Devir (RPM)", 200);
            p.Controls.Add(_fanList);

            string[] roles = { "CPU fanı", "Kasa fanı 1", "Kasa fanı 2", "AIO pompa" };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                p.Controls.Add(new Label { Text = roles[i], Left = 0, Top = 282 + i * 44, Width = 160, Height = 28, ForeColor = Tx });
                var cb = new ComboBox { Left = 170, Top = 278 + i * 44, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Card, ForeColor = Tx, FlatStyle = FlatStyle.Flat };
                cb.Items.Add("Otomatik");
                for (int f = 1; f <= 7; f++) cb.Items.Add("Fan #" + f);
                cb.SelectedIndex = Math.Max(0, Math.Min(7, TelemetryCollector.FanMap[i]));
                cb.SelectedIndexChanged += delegate
                {
                    if (_loading) return;
                    var m = (int[])TelemetryCollector.FanMap.Clone();
                    m[k] = cb.SelectedIndex;
                    TelemetryCollector.FanMap = m;
                    _d.SaveSettings();
                };
                p.Controls.Add(cb);
                _fanCombo[i] = cb;
            }
            p.Controls.Add(Sub("0 RPM görünen girişler boştur (fan takılı değil). Pano bir sonraki ölçümde yeni eşlemeyi kullanır.", 0, 462, 740));
            return p;
        }

        private void RefreshFans()
        {
            double[] raw = TelemetryCollector.FanRaw;
            if (raw == null) return;
            _fanList.BeginUpdate();
            _fanList.Items.Clear();
            for (int i = 1; i < raw.Length; i++)
            {
                if (double.IsNaN(raw[i])) continue;
                var it = new ListViewItem("Fan #" + i);
                it.SubItems.Add(raw[i] > 0 ? raw[i].ToString("0", CultureInfo.InvariantCulture) : "0 (boş)");
                if (raw[i] > 0) it.ForeColor = _acc;
                _fanList.Items.Add(it);
            }
            _fanList.EndUpdate();
        }

        // ---------------- Oyun oturumları ----------------
        private Panel BuildSessions()
        {
            var p = new Panel();
            p.Controls.Add(H("Oyun oturumları", 0));
            p.Controls.Add(Ck("Oyun algılanınca Oyun HUD düzenine geç, oyun bitince önceki düzene dön", 0, 40, 760, _d._autoGame, v => { _d._autoGame = v; _d.SaveSettings(); }));
            p.Controls.Add(Sub("Her oyun oturumu (en az 1 dk) bitince ortalama FPS, %1 düşük, zirve sıcaklık ve güç kaydedilir.", 0, 72, 760));
            _sessList = LV(0, 100, 740, 380, false);
            _sessList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            string[] cols = { "Başlangıç", "Oyun", "Süre dk", "Ort FPS", "%1 Düşük", "Min FPS", "CPU °C", "GPU °C", "Güç W" };
            int[] ws = { 120, 150, 60, 66, 76, 66, 56, 56, 56 };
            for (int i = 0; i < cols.Length; i++) _sessList.Columns.Add(cols[i], ws[i]);
            p.Controls.Add(_sessList);
            p.Controls.Add(Bt("Yenile", 0, 492, 110, delegate { RefreshSessions(); }));
            p.Controls.Add(Bt("Kayıt dosyasını aç (CSV)", 120, 492, 210, delegate { try { if (File.Exists(GameSession.CsvPath)) Process.Start(GameSession.CsvPath); } catch { } }));
            p.Controls.Add(Bt("Klasörü aç", 340, 492, 130, delegate { try { Process.Start(AppData.Dir); } catch { } }));
            return p;
        }

        private void RefreshSessions()
        {
            GameSession.LoadHistory();
            _sessList.BeginUpdate(); _sessList.Items.Clear();
            for (int i = GameSession.History.Count - 1; i >= 0; i--) _sessList.Items.Add(new ListViewItem(GameSession.History[i].Take(9).ToArray()));
            _sessList.EndUpdate();
        }

        // ---------------- Ses profilleri ----------------
        private readonly Dictionary<string, TextBox> _hkBoxes = new Dictionary<string, TextBox>();
        private Label _hkStatus;

        private void AddHotkeys(Panel p, int y0)
        {
            p.Controls.Add(Sub("KISAYOL TUŞLARI — her uygulamada ve oyunda çalışır", 0, y0, 690));
            p.Controls.Add(Sub("Kutuya tıkla, sonra tuş kombinasyonuna bas (ör. Alt + NumPad0). Cihaz değiştirme yukarıda işaretli cihazlar arasında döner.", 0, y0 + 22, 700));
            for (int i = 0; i < Hotkeys.Ids.Length; i++)
            {
                string id = Hotkeys.Ids[i]; int top = y0 + 54 + i * 38;
                p.Controls.Add(new Label { Text = Hotkeys.Names[i], ForeColor = Tx, Left = 0, Top = top + 4, Width = 380, Height = 24, Font = new Font("Segoe UI", 9.5f) });
                var tb = new TextBox { Left = 390, Top = top, Width = 200, ReadOnly = true, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.FixedSingle, ShortcutsEnabled = false, Font = new Font("Segoe UI", 10.5f) };
                int[] cur; if (Hotkeys.Map.TryGetValue(id, out cur)) tb.Text = Hotkeys.Describe(cur);
                string cid = id;
                tb.KeyDown += delegate (object s2, KeyEventArgs e)
                {
                    e.SuppressKeyPress = true; e.Handled = true;
                    Keys k = e.KeyCode;
                    if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;
                    int mod = (e.Alt ? 1 : 0) | (e.Control ? 2 : 0) | (e.Shift ? 4 : 0);
                    Hotkeys.Set(cid, mod, (int)k);
                    foreach (var kv in _hkBoxes) { int[] o; kv.Value.Text = Hotkeys.Map.TryGetValue(kv.Key, out o) ? Hotkeys.Describe(o) : ""; }
                    _hkStatus.Text = Hotkeys.Status;
                };
                _hkBoxes[id] = tb;
                p.Controls.Add(tb);
                p.Controls.Add(Bt("Temizle", 600, top - 2, 90, delegate { Hotkeys.Set(cid, 0, 0); tb.Text = ""; _hkStatus.Text = Hotkeys.Status; }));
            }
            _hkStatus = new Label { Text = Hotkeys.Status, ForeColor = Color.FromArgb(255, 140, 120), Left = 0, Top = y0 + 54 + Hotkeys.Ids.Length * 38, Width = 690, Height = 24, Font = new Font("Segoe UI", 9.5f) };
            p.Controls.Add(_hkStatus);
        }

        private Panel BuildProfiles()
        {
            var p = new Panel();
            p.Controls.Add(H("Ses profilleri", 0));
            { var pn = Sub("Şu anki çıkış cihazı, ses seviyesi ve mikrofon durumunu bir profil olarak kaydet; tek tıkla (HUD'daki PROFİL çipleri), P tuşuyla veya Ses ve mikrofon sayfasındaki kısayolla uygula.", 0, 40, 720); pn.Height = 34; p.Controls.Add(pn); }
            _profList = LV(0, 80, 740, 252, false);
            _profList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _profList.Columns.Add("Profil", 150); _profList.Columns.Add("Çıkış cihazı", 330); _profList.Columns.Add("Ses", 70); _profList.Columns.Add("Mikrofon", 100);
            p.Controls.Add(_profList);
            p.Controls.Add(Sub("Profil adı", 0, 348, 90));
            _profName = new TextBox { Left = 90, Top = 344, Width = 240, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.FixedSingle };
            p.Controls.Add(_profName);
            p.Controls.Add(Bt("Şu anki ayarı kaydet", 340, 342, 200, delegate
            {
                string n = (_profName.Text ?? "").Trim();
                if (n.Length == 0) return;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    AudioProfiles.CaptureCurrent(n);
                    try { BeginInvoke((MethodInvoker)delegate { RefreshProfiles(); _profName.Text = ""; }); } catch { }
                });
            }));
            p.Controls.Add(Bt("Seçileni uygula", 0, 392, 160, delegate
            {
                if (_profList.SelectedIndices.Count == 0) return;
                int i = _profList.SelectedIndices[0];
                if (i < AudioProfiles.All.Count) _d.ApplyProfile(AudioProfiles.All[i]);
            }));
            p.Controls.Add(Bt("Seçileni sil", 170, 392, 160, delegate
            {
                if (_profList.SelectedIndices.Count == 0) return;
                int i = _profList.SelectedIndices[0];
                if (i < AudioProfiles.All.Count) { AudioProfiles.Remove(AudioProfiles.All[i].Name); RefreshProfiles(); }
            }));
            p.Controls.Add(Sub("Örnek: kulaklığı seç, sesi %40 yap, mikrofonu aç → \"Oyun\" olarak kaydet. Hoparlöre geçip \"Müzik\" kaydet.", 0, 440, 760));
            return p;
        }

        private void RefreshProfiles()
        {
            AudioProfiles.Load();
            _profList.BeginUpdate(); _profList.Items.Clear();
            foreach (AudioProfile ap in AudioProfiles.All)
                _profList.Items.Add(new ListViewItem(new[] { ap.Name, ap.OutName, ap.Volume >= 0 ? ap.Volume + "%" : "-", ap.MicMute < 0 ? "-" : (ap.MicMute == 1 ? "Kapalı" : "Açık") }));
            _profList.EndUpdate();
        }

        // ---------------- Cihazlar ve hız ----------------
        private Panel BuildDevices()
        {
            var p = new Panel();
            p.Controls.Add(H("Cihaz pilleri", 0));
            p.Controls.Add(Sub("BLUETOOTH CİHAZ PİLLERİ (klavye, fare, kulaklık)", 0, 44, 600));
            _perList = LV(0, 70, 740, 260, false);
            _perList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _perList.Columns.Add("Cihaz", 480); _perList.Columns.Add("Pil", 100);
            p.Controls.Add(_perList);
            p.Controls.Add(Bt("Şimdi oku", 0, 342, 130, delegate { Peripherals.RefreshAsync(); }));
            p.Controls.Add(Bt("Kulaklık/cihaz bilgisini dosyaya yaz (piller.txt)", 140, 342, 360, delegate { Peripherals.DumpAsync(); }));
            p.Controls.Add(Bt("ROG Cetra HID dökümü (hid.txt) — kulaklığın tuşuna basıp bekle", 0, 384, 480, delegate { Peripherals.HidDumpAsync(); }));
            p.Controls.Add(Sub("Yalnızca Windows'un pil bilgisi verdiği (çoğunlukla Bluetooth) cihazlar görünür. USB alıcılı (Logitech/Razer) cihazlar çıkmayabilir.", 0, 432, 760));
            return p;
        }

        private string _lastPer = "";
        private void RefreshDevices()
        {
            string key = string.Join(";", Peripherals.Items.Select(x => x.Key + x.Value).ToArray()) + Peripherals.Status;
            if (key == _lastPer) return;
            _lastPer = key;
            _perList.BeginUpdate(); _perList.Items.Clear();
            foreach (var kv in Peripherals.Items) _perList.Items.Add(new ListViewItem(new[] { kv.Key, kv.Value + "%" }));
            if (Peripherals.Items.Length == 0) _perList.Items.Add(new ListViewItem(new[] { Peripherals.Status, "" }));
            _perList.EndUpdate();
        }

        // ---------------- Güncelleme ----------------
        private Label _updVer, _updStatus;
        private TextBox _updNotes;
        private Button _updGo, _updCheck;

        private Panel BuildUpdate()
        {
            var p = new Panel();
            p.Controls.Add(H("Güncelleme", 0));
            p.Controls.Add(Sub("Güncellemeler GitHub'daki ooemir23/OnMon deposundan gelir.", 0, 40, 740));
            _updVer = new Label { Left = 0, Top = 76, Width = 740, Height = 28, ForeColor = Tx, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
            _updStatus = new Label { Left = 0, Top = 108, Width = 740, Height = 26, ForeColor = Dim };
            p.Controls.Add(_updVer); p.Controls.Add(_updStatus);
            _updNotes = new TextBox { Left = 0, Top = 146, Width = 740, Height = 220, Multiline = true, ReadOnly = true, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
            p.Controls.Add(_updNotes);
            _updCheck = Bt("Güncellemeleri denetle", 0, 380, 220, delegate
            {
                _updStatus.Text = "Denetleniyor…";
                _updCheck.Enabled = false;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    Updater.Check();
                    try { BeginInvoke((MethodInvoker)delegate { _updCheck.Enabled = true; RefreshUpdate(); }); } catch { }
                });
            });
            p.Controls.Add(_updCheck);
            _updGo = Bt("Şimdi güncelle ve yeniden başlat", 232, 380, 280, delegate
            {
                if (MessageBox.Show(this, "Uygulama güncellenip yeniden başlatılacak. Devam edilsin mi?", "Güncelleme", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _updGo.Enabled = false; _updStatus.Text = "İndiriliyor…";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = Updater.ApplyUpdate();
                    if (err != null) try { BeginInvoke((MethodInvoker)delegate { _updGo.Enabled = true; RefreshUpdate(); }); } catch { }
                });
            });
            p.Controls.Add(_updGo);
            p.Controls.Add(Ck("Açılışta ve 6 saatte bir otomatik denetle (yeni sürümde bildirim göster)", 0, 424, 740, Updater.AutoCheck, v => Updater.AutoCheck = v));
            return p;
        }

        private void RefreshUpdate()
        {
            _updVer.Text = "Yüklü sürüm: v" + Updater.AppVersion + (Updater.LatestVersion > 0 ? "     Son sürüm: v" + Updater.LatestVersion : "");
            _updStatus.Text = Updater.Status;
            _updNotes.Text = (Updater.LatestNotes.Length > 0 && Updater.UpdateAvailable ? "YENİ: " + Updater.LatestNotes + "\r\n\r\n" : "") + (Updater.Changelog.Length > 0 ? "SON DEĞİŞİKLİKLER\r\n" + Updater.Changelog : Updater.LatestNotes);
            _updGo.Enabled = Updater.UpdateAvailable && !Updater.Busy;
            _updGo.ForeColor = Updater.UpdateAvailable ? _acc : Dim;
        }

        // ---------------- Tanılama ----------------
        private Panel BuildDiag()
        {
            var p = new Panel();
            p.Controls.Add(H("Tanılama", 0));
            _diag = new TextBox { Left = 0, Top = 48, Width = 740, Height = 330, Multiline = true, ReadOnly = true, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10f), ScrollBars = ScrollBars.Vertical };
            p.Controls.Add(_diag);
            p.Controls.Add(Bt("Yenile", 0, 392, 120, delegate { RefreshDiag(); }));
            p.Controls.Add(Bt("Sensör listesini kaydet", 130, 392, 220, delegate { Program.RequestSensorDump(); }));
            p.Controls.Add(Bt("Kayıt klasörünü aç", 360, 392, 200, delegate
            {
                try { Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory); } catch { }
            }));
            return p;
        }

        private void RefreshDiag()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Sensör kaynağı:");
            sb.AppendLine("  " + (string.IsNullOrEmpty(_d._source) ? "(henüz veri yok)" : _d._source));
            sb.AppendLine();
            sb.AppendLine("LibreHardwareMonitor: " + (string.IsNullOrEmpty(TelemetryCollector.LhmError) ? "çalışıyor" : "HATA — " + TelemetryCollector.LhmError));
            sb.AppendLine("Yönetici: " + (Program.IsAdministrator() ? "evet" : "hayır (sıcaklık/fan okunamaz)"));
            sb.AppendLine("Ses çıkışı: " + AudioSwitch.DefaultName);
            sb.AppendLine("Mikrofon: " + AudioSwitch.MicName + (AudioSwitch.MicMuted ? " (sessiz)" : ""));
            sb.AppendLine("Sürüm: v" + Updater.AppVersion);
            sb.AppendLine("Veri toplama süresi (ort.): " + Program.CollectMs.ToString("0") + " ms");
            sb.AppendLine("Çizim süresi (ort.): " + _d._paintMs.ToString("0.0") + " ms");
            sb.AppendLine("Klasör: " + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine();
            sb.AppendLine("sensorler.txt  → tüm sensörlerin listesi (otomatik yazılır)");
            sb.AppendLine("hata.log       → hata kayıtları");
            _diag.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }

        private void Tick()
        {
            if (_pages.Count > 7 && _pages[7].Visible) RefreshDevices();
            if (_pages.Count > 4 && _pages[4].Visible) RefreshFans();
            if (_pages.Count > 1 && _pages[1].Visible)
            {
                AudioSwitch.PollMic();
                _micBarCtl.Value = (int)Math.Max(0, Math.Min(100, Math.Sqrt(AudioSwitch.MicLevel) * 100));
                _loading = true;
                _micMute.Checked = AudioSwitch.MicMuted;
                _loading = false;
            }
        }
    }
}
