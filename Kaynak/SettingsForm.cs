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
            ShowInTaskbar = true;
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
            if (i == 4) RefreshDiag();
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
            _layoutList = new ListBox { Left = 0, Top = 68, Width = 260, Height = 150, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34 };
            string[] names = { "Klasik", "Oyun HUD", "Pro Kokpit", "Sensör Paneli" };
            string[] ids = { "classic", "hud", "cockpit", "aida" };
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

            p.Controls.Add(Sub("KENAR BOŞLUĞU", 0, 232, 200));
            var tb = new TrackBar { Left = 0, Top = 254, Width = 260, Minimum = 0, Maximum = 48, TickFrequency = 8, SmallChange = 4, LargeChange = 8, Value = Math.Max(0, Math.Min(48, _d._margin)) };
            var tl = new Label { Left = 270, Top = 262, Width = 80, ForeColor = Tx, Text = _d._margin + " px" };
            tb.Scroll += delegate { tl.Text = tb.Value + " px"; if (!_loading) { _d._margin = tb.Value; _d.SaveSettings(); _d.Invalidate(); } };
            p.Controls.Add(tb); p.Controls.Add(tl);

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
            var p = new Panel();
            p.Controls.Add(H("Ses ve mikrofon", 0));

            p.Controls.Add(Sub("ÇIKIŞ CİHAZLARI — kutuyu işaretlediklerin alt çubukta tek tıkla geçiş için görünür", 0, 44, 740));
            _outList = LV(0, 68, 740, 150, true);
            _outList.Columns.Add("", 40); _outList.Columns.Add("Cihaz", 560); _outList.Columns.Add("Durum", 130);
            _outList.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
            {
                if (_loading) return;
                string id = e.Item.Tag as string;
                if (e.Item.Checked) _d._favs.Add(id); else _d._favs.Remove(id);
                _d.SaveSettings(); _d.Invalidate();
            };
            _outList.DoubleClick += delegate { SetDefaultFrom(_outList, true); };
            p.Controls.Add(_outList);
            p.Controls.Add(Bt("Varsayılan yap", 0, 226, 150, delegate { SetDefaultFrom(_outList, true); }));
            p.Controls.Add(Bt("Test sesi çal", 160, 226, 150, delegate { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }));
            p.Controls.Add(Ck("Alt çubukta ses cihazlarını göster", 330, 230, 330, _d._audioBar, v => { _d._audioBar = v; _d.SaveSettings(); _d.Invalidate(); }));

            p.Controls.Add(Sub("MİKROFON", 0, 286, 300));
            _micList = LV(0, 310, 740, 110, false);
            _micList.Columns.Add("Cihaz", 600); _micList.Columns.Add("Durum", 130);
            _micList.DoubleClick += delegate { SetDefaultFrom(_micList, false); };
            p.Controls.Add(_micList);
            p.Controls.Add(Bt("Varsayılan yap", 0, 428, 150, delegate { SetDefaultFrom(_micList, false); }));
            _micMute = Ck("Mikrofonu sustur", 170, 432, 200, false, v => { if (v != AudioSwitch.MicMuted) _d.ToggleMic(); });
            p.Controls.Add(_micMute);
            p.Controls.Add(Ck("Alt çubukta mikrofon göstergesi", 380, 432, 300, _d._micBar, v => { _d._micBar = v; _d.SaveSettings(); _d.Invalidate(); }));
            p.Controls.Add(Sub("Anlık seviye", 0, 478, 100));
            _micBarCtl = new ProgressBar { Left = 100, Top = 480, Width = 400, Height = 14, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
            p.Controls.Add(_micBarCtl);
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
                    var it = new ListViewItem(a.Name) { Tag = a.Id };
                    it.SubItems.Add(a.IsDefault ? "● Varsayılan" : "");
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
            p.Controls.Add(Ck("Her zaman üstte", 0, 152, 300, _d.TopMost, v => { _d.TopMost = v; _d.SaveSettings(); }));
            p.Controls.Add(Bt("Panoyu küçült", 0, 190, 160, delegate { _d.WindowState = FormWindowState.Minimized; }));
            p.Controls.Add(Bt("Panoyu kapat", 170, 190, 160, delegate { _d.Close(); }));
            p.Controls.Add(Sub("Win+D (masaüstünü göster) panoyu küçültür; görev çubuğundan geri açılır.", 0, 232, 700));

            p.Controls.Add(Sub("SİSTEM", 0, 276, 300));
            bool auto = false; try { auto = Program.IsAutostartEnabled(); } catch { }
            p.Controls.Add(Ck("Windows ile birlikte başlat", 0, 302, 400, auto, v => System.Threading.ThreadPool.QueueUserWorkItem(delegate { Program.SetAutostart(v); })));

            p.Controls.Add(Sub("ESKİ ARAYÜZLER (daha çok RAM kullanır)", 0, 352, 500));
            p.Controls.Add(Bt("Tarayıcıda aç", 0, 378, 160, delegate { Program.LaunchBrowserDashboard(); }));
            p.Controls.Add(Bt("Edge uygulaması olarak aç", 170, 378, 220, delegate { Program.LaunchEdgeDashboard(); }));

            p.Controls.Add(Bt("Uygulamadan çık", 0, 450, 180, delegate { Program.ExitApp(); }));
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
            sb.AppendLine("Klasör: " + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine();
            sb.AppendLine("sensorler.txt  → tüm sensörlerin listesi (otomatik yazılır)");
            sb.AppendLine("hata.log       → hata kayıtları");
            _diag.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }

        private void Tick()
        {
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
