using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>Koyu temalı küçük yardımcılar (düzenleme pencereleri için).</summary>
    internal static class EdUi
    {
        public static readonly Color Bg = Color.FromArgb(24, 27, 34), Card = Color.FromArgb(34, 38, 48), Tx = Color.FromArgb(232, 234, 240), Dim = Color.FromArgb(150, 156, 170), Acc = Color.FromArgb(0, 200, 255);

        public static Label Lbl(Control p, string text, int x, int y, int w, bool head)
        {
            var l = new Label { Text = text, Left = x, Top = y, Width = w, Height = head ? 24 : 22, ForeColor = head ? Acc : Tx, BackColor = Color.Transparent, Font = new Font("Segoe UI", head ? 10f : 9f, head ? FontStyle.Bold : FontStyle.Regular) };
            p.Controls.Add(l); return l;
        }

        public static NumericUpDown Num(Control p, int x, int y, int w, decimal min, decimal max, decimal step, int dec)
        {
            var n = new NumericUpDown { Left = x, Top = y, Width = w, Minimum = min, Maximum = max, Increment = step, DecimalPlaces = dec, BackColor = Card, ForeColor = Tx, BorderStyle = BorderStyle.FixedSingle };
            p.Controls.Add(n); return n;
        }

        public static ComboBox Combo(Control p, int x, int y, int w, params string[] items)
        {
            var c = new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Card, ForeColor = Tx, FlatStyle = FlatStyle.Flat };
            c.Items.AddRange(items); p.Controls.Add(c); return c;
        }

        public static Button Btn(Control p, string text, int x, int y, int w, EventHandler click)
        {
            var b = new Button { Text = text, Left = x, Top = y, Width = w, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Tx };
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 92);
            b.Click += click; p.Controls.Add(b); return b;
        }

        public static CheckBox Chk(Control p, string text, int x, int y, int w)
        {
            var c = new CheckBox { Text = text, Left = x, Top = y, Width = w, ForeColor = Tx, BackColor = Color.Transparent };
            p.Controls.Add(c); return c;
        }
    }

    /// <summary>Renk seçici: önizleme + Seç + Tema (sıfırla).</summary>
    internal sealed class ColorField
    {
        public int Value;                 // ARGB, 0 = tema
        public Action Changed;
        private readonly Panel _sw;
        private readonly Func<Color> _themeColor;

        public ColorField(Control p, string label, int y, Func<Color> themeColor)
        {
            _themeColor = themeColor;
            EdUi.Lbl(p, label, 12, y + 3, 150, false);
            _sw = new Panel { Left = 170, Top = y, Width = 44, Height = 24, BorderStyle = BorderStyle.FixedSingle };
            p.Controls.Add(_sw);
            EdUi.Btn(p, "Seç…", 222, y - 1, 60, delegate
            {
                using (var cd = new ColorDialog { FullOpen = true, Color = Value != 0 ? Color.FromArgb(Value) : _themeColor() })
                    if (cd.ShowDialog() == DialogResult.OK) { Value = Color.FromArgb(255, cd.Color).ToArgb(); Refresh(); if (Changed != null) Changed(); }
            });
            EdUi.Btn(p, "Tema", 288, y - 1, 60, delegate { Value = 0; Refresh(); if (Changed != null) Changed(); });
        }

        public void Set(int v) { Value = v; Refresh(); }
        private void Refresh() { _sw.BackColor = Value != 0 ? Color.FromArgb(255, Color.FromArgb(Value)) : _themeColor(); }
    }

    internal sealed class WidgetPropsForm : Form
    {
        private readonly DashboardForm _d;
        private WInst _w;
        private bool _bind;
        private NumericUpDown _x, _y, _wd, _h, _fs, _fw, _rad, _gw, _zoom;
        private ColorField _a1, _a2, _tx, _pc, _fc;
        private TrackBar _pa; private Label _paL;
        private ComboBox _frame, _bars, _glow, _cap, _align;
        private CheckBox _notitle, _lock;
        private TextBox _text, _path;
        private Label _title, _lText, _lPath;
        private Button _browse;

        private static readonly string[] FrameIds = { "", "flat", "glass", "neon", "blueprint", "crt", "hud", "brutal", "clip", "mono", "none" };
        private static readonly string[] FrameNm = { "Tema", "Düz", "Cam", "Neon", "Teknik çizim", "CRT", "HUD köşeleri", "Sert gölge", "Kesik köşe", "Köşeli", "Çerçevesiz" };
        private static readonly string[] BarIds = { "", "round", "thin", "chunk", "seg" };
        private static readonly string[] BarNm = { "Tema", "Yuvarlak", "İnce", "Kalın blok", "Parçalı" };

        public WidgetPropsForm(DashboardForm d)
        {
            _d = d;
            Text = "Widget özellikleri"; FormBorderStyle = FormBorderStyle.FixedToolWindow; TopMost = true; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; ClientSize = new Size(372, 680); BackColor = EdUi.Bg; ForeColor = EdUi.Tx; AutoScroll = true;
            Font = new Font("Segoe UI", 9f);
            int y = 8;
            _title = EdUi.Lbl(this, "", 12, y, 340, true); y += 30;

            EdUi.Lbl(this, "KONUM VE BOYUT", 12, y, 300, true); y += 26;
            EdUi.Lbl(this, "X", 12, y + 2, 20, false); _x = EdUi.Num(this, 34, y, 70, 0, 10000, 1, 0);
            EdUi.Lbl(this, "Y", 120, y + 2, 20, false); _y = EdUi.Num(this, 142, y, 70, 0, 10000, 1, 0); y += 30;
            EdUi.Lbl(this, "Genişlik", 12, y + 2, 60, false); _wd = EdUi.Num(this, 74, y, 70, 20, 10000, 1, 0);
            EdUi.Lbl(this, "Yükseklik", 160, y + 2, 70, false); _h = EdUi.Num(this, 232, y, 70, 20, 10000, 1, 0); y += 30;
            _lock = EdUi.Chk(this, "Kilitli (taşınamaz / boyutlanamaz)", 12, y, 340); y += 30;

            EdUi.Lbl(this, "YAZI", 12, y, 300, true); y += 26;
            EdUi.Lbl(this, "Yazı ölçeği (%)", 12, y + 2, 120, false); _fs = EdUi.Num(this, 140, y, 70, 30, 300, 5, 0); y += 30;
            EdUi.Lbl(this, "İçerik ölçeği (%)", 12, y + 2, 125, false); _zoom = EdUi.Num(this, 140, y, 70, 20, 500, 5, 0);
            EdUi.Lbl(this, "(yazı + boşluklar)", 218, y + 2, 130, false); y += 30;
            _a1 = new ColorField(this, "Yazı rengi", y, delegate { return _d._th.Tx; }); _tx = _a1; y += 30;

            EdUi.Lbl(this, "RENKLER", 12, y, 300, true); y += 26;
            _a1 = new ColorField(this, "Vurgu rengi (çizgi, grafik)", y, delegate { return _d._th.A1; }); y += 30;
            _a2 = new ColorField(this, "İkinci vurgu (geçiş)", y, delegate { return _d._th.A2; }); y += 30;

            EdUi.Lbl(this, "ÇERÇEVE VE PANEL", 12, y, 300, true); y += 26;
            EdUi.Lbl(this, "Çerçeve stili", 12, y + 2, 120, false); _frame = EdUi.Combo(this, 140, y, 200, FrameNm); y += 30;
            _pc = new ColorField(this, "Panel dolgu rengi", y, delegate { return _d._th.Panel; }); y += 30;
            _fc = new ColorField(this, "Çerçeve rengi", y, delegate { return _d._th.Line; }); y += 30;
            EdUi.Lbl(this, "Panel opaklığı", 12, y + 4, 110, false);
            _pa = new TrackBar { Left = 120, Top = y - 2, Width = 190, Minimum = 0, Maximum = 100, TickFrequency = 25, SmallChange = 5, LargeChange = 10 }; Controls.Add(_pa);
            _paL = EdUi.Lbl(this, "100%", 314, y + 4, 50, false); y += 36;
            EdUi.Lbl(this, "Çerçeve kalınlığı", 12, y + 2, 120, false); _fw = EdUi.Num(this, 140, y, 70, 0, 12, 0.5m, 1);
            EdUi.Lbl(this, "(0 = tema)", 218, y + 2, 80, false); y += 30;
            EdUi.Lbl(this, "Köşe yuvarlaklığı", 12, y + 2, 120, false); _rad = EdUi.Num(this, 140, y, 70, -1, 100, 1, 0);
            EdUi.Lbl(this, "(-1 = tema)", 218, y + 2, 80, false); y += 30;
            _notitle = EdUi.Chk(this, "Başlığı gizle (CPU, GPU… yazısı ve vurgu çubuğu)", 12, y, 350); y += 30;

            EdUi.Lbl(this, "GÖSTERGE VE ÇUBUKLAR", 12, y, 300, true); y += 26;
            EdUi.Lbl(this, "Daire uç stili", 12, y + 2, 120, false); _cap = EdUi.Combo(this, 140, y, 200, "Tema", "Yuvarlak uç", "Düz uç"); y += 30;
            EdUi.Lbl(this, "Daire kalınlığı (%)", 12, y + 2, 125, false); _gw = EdUi.Num(this, 140, y, 70, 0, 300, 10, 0);
            EdUi.Lbl(this, "(0 = tema)", 218, y + 2, 80, false); y += 30;
            EdUi.Lbl(this, "Parlama (glow)", 12, y + 2, 120, false); _glow = EdUi.Combo(this, 140, y, 200, "Tema", "Açık", "Kapalı"); y += 30;
            EdUi.Lbl(this, "Çubuk stili", 12, y + 2, 120, false); _bars = EdUi.Combo(this, 140, y, 200, BarNm); y += 34;

            _lText = EdUi.Lbl(this, "METİN  ({saat} {tarih} {cpu} {gpu} {cpusic} {gpusic} {ram} {fps} {ping})", 12, y, 350, true); y += 26;
            _text = new TextBox { Left = 12, Top = y, Width = 340, Height = 54, Multiline = true, BackColor = EdUi.Card, ForeColor = EdUi.Tx, BorderStyle = BorderStyle.FixedSingle }; Controls.Add(_text); y += 60;
            EdUi.Lbl(this, "Hizalama", 12, y + 2, 120, false); _align = EdUi.Combo(this, 140, y, 120, "Sol", "Orta", "Sağ"); y += 34;
            _lPath = EdUi.Lbl(this, "RESİM / LOGO DOSYASI", 12, y, 300, true); y += 26;
            _path = new TextBox { Left = 12, Top = y, Width = 270, BackColor = EdUi.Card, ForeColor = EdUi.Tx, BorderStyle = BorderStyle.FixedSingle }; Controls.Add(_path);
            _browse = EdUi.Btn(this, "Gözat…", 288, y - 1, 64, delegate
            {
                using (var od = new OpenFileDialog { Filter = "Resimler|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tümü|*.*" })
                    if (od.ShowDialog() == DialogResult.OK) _path.Text = od.FileName;
            });
            y += 40;
            EdUi.Btn(this, "Kapat", 12, y, 100, delegate { Hide(); });
            EdUi.Btn(this, "Çoğalt", 120, y, 90, delegate { _d.DuplicateSelected(); });
            EdUi.Btn(this, "Varsayılana dön", 218, y, 134, delegate { if (_w != null) { Reset(); } });
            AutoScrollMinSize = new Size(0, y + 50);

            _x.ValueChanged += delegate { Apply(); }; _y.ValueChanged += delegate { Apply(); }; _wd.ValueChanged += delegate { Apply(); }; _h.ValueChanged += delegate { Apply(); };
            _fs.ValueChanged += delegate { Apply(); }; _zoom.ValueChanged += delegate { Apply(); }; _fw.ValueChanged += delegate { Apply(); }; _rad.ValueChanged += delegate { Apply(); }; _gw.ValueChanged += delegate { Apply(); };
            _pa.Scroll += delegate { Apply(); };
            _frame.SelectedIndexChanged += delegate { Apply(); }; _bars.SelectedIndexChanged += delegate { Apply(); }; _glow.SelectedIndexChanged += delegate { Apply(); };
            _cap.SelectedIndexChanged += delegate { Apply(); }; _align.SelectedIndexChanged += delegate { Apply(); };
            _notitle.CheckedChanged += delegate { Apply(); }; _lock.CheckedChanged += delegate { Apply(); };
            _text.TextChanged += delegate { Apply(); }; _path.TextChanged += delegate { Apply(); };
            foreach (ColorField c in new[] { _a1, _a2, _tx, _pc, _fc }) c.Changed = Apply;
            FormClosing += delegate (object s, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }

        public void PlaceNear(DashboardForm d)
        {
            if (Visible) return;
            Rectangle wa = Screen.FromControl(d).WorkingArea;
            Location = new Point(Math.Max(wa.X, wa.Right - Width - 20), wa.Y + 40);
        }

        private void Reset()
        {
            var n = new WInst();
            _w.A1 = n.A1; _w.A2 = n.A2; _w.Tx = n.Tx; _w.PanelC = n.PanelC; _w.FrameC = n.FrameC; _w.PAlpha = 100; _w.Frame = ""; _w.Bars = ""; _w.Glow = ""; _w.Cap = "";
            _w.Zoom = 1f; _w.FrameW = 0; _w.Radius = -1; _w.GaugeW = 0; _w.NoTitle = false; _w.Fs = 1f;
            _d.WChanged(); Bind(_w);
        }

        public void Bind(WInst w)
        {
            _w = w;
            if (w == null) { Hide(); return; }
            _bind = true;
            WDef def = _d.DefOf(w.Id);
            _title.Text = def.Name + "  (" + w.Id + ")";
            _x.Value = Clamp(_x, (decimal)Math.Round(w.X)); _y.Value = Clamp(_y, (decimal)Math.Round(w.Y)); _wd.Value = Clamp(_wd, (decimal)Math.Round(w.W)); _h.Value = Clamp(_h, (decimal)Math.Round(w.H));
            _lock.Checked = w.Locked;
            _fs.Value = Clamp(_fs, (decimal)Math.Round(w.Fs * 100)); _zoom.Value = Clamp(_zoom, (decimal)Math.Round(w.Zoom * 100));
            _tx.Set(w.Tx); _a1.Set(w.A1); _a2.Set(w.A2); _pc.Set(w.PanelC); _fc.Set(w.FrameC);
            _frame.SelectedIndex = Math.Max(0, Array.IndexOf(FrameIds, w.Frame));
            _pa.Value = Math.Max(0, Math.Min(100, w.PAlpha)); _paL.Text = _pa.Value + "%";
            _fw.Value = Clamp(_fw, (decimal)w.FrameW); _rad.Value = Clamp(_rad, (decimal)w.Radius);
            _notitle.Checked = w.NoTitle;
            _cap.SelectedIndex = w.Cap == "round" ? 1 : (w.Cap == "flat" ? 2 : 0);
            _gw.Value = Clamp(_gw, (decimal)Math.Round(w.GaugeW * 100));
            _glow.SelectedIndex = w.Glow == "on" ? 1 : (w.Glow == "off" ? 2 : 0);
            _bars.SelectedIndex = Math.Max(0, Array.IndexOf(BarIds, w.Bars));
            bool isText = BaseOf(w) == "text", isBox = BaseOf(w) == "box", isImg = BaseOf(w) == "image";
            _lText.Visible = _text.Visible = isText || isBox; _lText.Text = isBox ? "BAŞLIK (boşsa yok)" : "METİN  ({saat} {tarih} {cpu} {gpu} {cpusic} {gpusic} {ram} {fps} {ping})";
            _align.Visible = isText;
            _lPath.Visible = _path.Visible = _browse.Visible = isImg;
            _text.Text = w.Text; _path.Text = w.Path; _align.SelectedIndex = Math.Max(0, Math.Min(2, w.Align));
            _bind = false;
        }

        private static string BaseOf(WInst w) { return DashboardForm.BaseId(w.Id); }
        private static decimal Clamp(NumericUpDown n, decimal v) { return Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        private void Apply()
        {
            if (_bind || _w == null) return;
            _w.X = (float)_x.Value; _w.Y = (float)_y.Value; _w.W = (float)_wd.Value; _w.H = (float)_h.Value;
            _w.Locked = _lock.Checked; _w.Fs = (float)_fs.Value / 100f; _w.Zoom = (float)_zoom.Value / 100f;
            _w.Tx = _tx.Value; _w.A1 = _a1.Value; _w.A2 = _a2.Value; _w.PanelC = _pc.Value; _w.FrameC = _fc.Value;
            _w.Frame = FrameIds[Math.Max(0, _frame.SelectedIndex)];
            _w.PAlpha = _pa.Value; _paL.Text = _pa.Value + "%";
            _w.FrameW = (float)_fw.Value; _w.Radius = (float)_rad.Value; _w.NoTitle = _notitle.Checked;
            _w.Cap = _cap.SelectedIndex == 1 ? "round" : (_cap.SelectedIndex == 2 ? "flat" : "");
            _w.GaugeW = (float)_gw.Value / 100f;
            _w.Glow = _glow.SelectedIndex == 1 ? "on" : (_glow.SelectedIndex == 2 ? "off" : "");
            _w.Bars = BarIds[Math.Max(0, _bars.SelectedIndex)];
            _w.Text = _text.Text; _w.Path = _path.Text; _w.Align = Math.Max(0, _align.SelectedIndex);
            _d.WChanged();
        }
    }

    internal sealed class CanvasForm : Form
    {
        private readonly DashboardForm _d;
        private bool _load;
        private ComboBox _preset, _grid;
        private NumericUpDown _w, _h, _gfs, _gz;
        private CheckBox _scale, _showGrid, _snap, _bar;
        private TextBox _img;
        private ColorField _bg;

        private static readonly string[] PNames = {
            "Özel", "1920 × 1080  Full HD (16:9)", "2560 × 1440  QHD", "3840 × 2160  4K", "1600 × 900", "1366 × 768", "1280 × 720  HD",
            "2560 × 1080  Ultrawide (21:9)", "3440 × 1440  Ultrawide QHD", "5120 × 1440  Super Ultrawide", "1280 × 1024  (5:4)",
            "1080 × 1920  Dikey Full HD", "1440 × 2560  Dikey QHD", "1024 × 600  Kasa ekranı 7\"", "800 × 480  Kasa ekranı 5\"", "1280 × 400  Şerit ekran", "480 × 320  Mini ekran" };
        private static readonly int[][] PSizes = {
            null, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 }, new[] { 1600, 900 }, new[] { 1366, 768 }, new[] { 1280, 720 },
            new[] { 2560, 1080 }, new[] { 3440, 1440 }, new[] { 5120, 1440 }, new[] { 1280, 1024 },
            new[] { 1080, 1920 }, new[] { 1440, 2560 }, new[] { 1024, 600 }, new[] { 800, 480 }, new[] { 1280, 400 }, new[] { 480, 320 } };
        private static readonly string[] GridNm = { "1", "2", "4", "5", "8", "10", "20", "40" };

        public CanvasForm(DashboardForm d)
        {
            _d = d;
            Text = "Pano ayarları"; FormBorderStyle = FormBorderStyle.FixedToolWindow; TopMost = true; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; ClientSize = new Size(372, 600); BackColor = EdUi.Bg; ForeColor = EdUi.Tx; Font = new Font("Segoe UI", 9f);
            int y = 10;
            EdUi.Lbl(this, "ÇÖZÜNÜRLÜK / EKRAN", 12, y, 340, true); y += 28;
            EdUi.Lbl(this, "Hazır ayar", 12, y + 2, 90, false); _preset = EdUi.Combo(this, 110, y, 240, PNames); y += 32;
            EdUi.Lbl(this, "Genişlik", 12, y + 2, 60, false); _w = EdUi.Num(this, 74, y, 80, 200, 8000, 10, 0);
            EdUi.Lbl(this, "Yükseklik", 170, y + 2, 70, false); _h = EdUi.Num(this, 242, y, 80, 120, 8000, 10, 0); y += 32;
            _scale = EdUi.Chk(this, "Değiştirince widget'ları yeni boyuta ölçekle", 12, y, 340); _scale.Checked = true; y += 26;
            EdUi.Btn(this, "Uygula", 12, y, 100, delegate { Apply(false); });
            EdUi.Btn(this, "Yatay ⇄ Dikey çevir", 120, y, 160, delegate { Apply(true); });
            y += 34;
            _bar = EdUi.Chk(this, "Alt bilgi çubuğu ve ses şeridi göster", 12, y, 340); y += 34;

            EdUi.Lbl(this, "IZGARA", 12, y, 340, true); y += 28;
            EdUi.Lbl(this, "Izgara boyutu (birim)", 12, y + 2, 140, false); _grid = EdUi.Combo(this, 160, y, 80, GridNm); y += 30;
            _showGrid = EdUi.Chk(this, "Izgarayı göster", 12, y, 160); _snap = EdUi.Chk(this, "Izgaraya yapış", 180, y, 160); y += 34;

            EdUi.Lbl(this, "ARKA PLAN VE GENEL", 12, y, 340, true); y += 28;
            _bg = new ColorField(this, "Arka plan rengi", y, delegate { return _d._th.Bg; }); y += 32;
            EdUi.Lbl(this, "Arka plan resmi", 12, y + 2, 120, false); y += 24;
            _img = new TextBox { Left = 12, Top = y, Width = 236, BackColor = EdUi.Card, ForeColor = EdUi.Tx, BorderStyle = BorderStyle.FixedSingle }; Controls.Add(_img);
            EdUi.Btn(this, "Gözat…", 254, y - 1, 60, delegate { using (var od = new OpenFileDialog { Filter = "Resimler|*.png;*.jpg;*.jpeg;*.bmp" }) if (od.ShowDialog() == DialogResult.OK) _img.Text = od.FileName; });
            EdUi.Btn(this, "Sil", 318, y - 1, 40, delegate { _img.Text = ""; });
            y += 36;
            EdUi.Lbl(this, "Tüm yazılar (%)", 12, y + 2, 120, false); _gfs = EdUi.Num(this, 140, y, 70, 30, 300, 5, 0);
            EdUi.Lbl(this, "(sadece yazı boyu)", 216, y + 2, 140, false); y += 30;
            EdUi.Lbl(this, "Tüm içerik (%)", 12, y + 2, 120, false); _gz = EdUi.Num(this, 140, y, 70, 20, 400, 5, 0);
            EdUi.Lbl(this, "(yazı + boşluklar)", 216, y + 2, 140, false); y += 40;
            EdUi.Btn(this, "Kapat", 12, y, 100, delegate { Hide(); });

            _preset.SelectedIndexChanged += delegate
            {
                if (_load || _preset.SelectedIndex <= 0) return;
                int[] sz = PSizes[_preset.SelectedIndex]; _w.Value = sz[0]; _h.Value = sz[1];
                bool small = sz[0] < 1280;
                if (small) { _d._topBar = false; _d._hideStrip = true; _bar.Checked = false; }
                Apply(false);
            };
            _bar.CheckedChanged += delegate { if (_load) return; _d._topBar = _bar.Checked; _d._hideStrip = !_bar.Checked; _d.SaveSettings(); _d.Invalidate(); };
            _grid.SelectedIndexChanged += delegate { if (_load) return; _d._grid = float.Parse(GridNm[_grid.SelectedIndex], CultureInfo.InvariantCulture); _d.SaveSettings(); _d.Invalidate(); };
            _showGrid.CheckedChanged += delegate { if (_load) return; _d._showGrid = _showGrid.Checked; _d.SaveSettings(); _d.Invalidate(); };
            _snap.CheckedChanged += delegate { if (_load) return; _d._snapOn = _snap.Checked; _d.SaveSettings(); _d.Invalidate(); };
            _bg.Changed = delegate { if (_load) return; _d._bgCol = _bg.Value; _d.SaveSettings(); _d.Invalidate(); };
            _img.TextChanged += delegate { if (_load) return; _d._bgImg = _img.Text; _d.SaveSettings(); _d.Invalidate(); };
            _gz.ValueChanged += delegate { if (_load) return; _d._gzoom = (float)_gz.Value / 100f; _d.SaveSettings(); _d.Invalidate(); };
            _gfs.ValueChanged += delegate { if (_load) return; _d._gfs = (float)_gfs.Value / 100f; _d.SaveSettings(); _d.Invalidate(); };
            FormClosing += delegate (object s, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }

        public void PlaceNear(DashboardForm d)
        {
            if (Visible) return;
            Rectangle wa = Screen.FromControl(d).WorkingArea;
            Location = new Point(Math.Max(wa.X, wa.Right - Width - 20), wa.Y + 40);
        }

        public void Reload()
        {
            _load = true;
            _w.Value = _d._cw; _h.Value = _d._ch;
            int pi = 0;
            for (int i = 1; i < PSizes.Length; i++) if (PSizes[i][0] == _d._cw && PSizes[i][1] == _d._ch) pi = i;
            _preset.SelectedIndex = pi;
            _bar.Checked = _d._topBar && !_d._hideStrip;
            _grid.SelectedIndex = Math.Max(0, Array.IndexOf(GridNm, ((int)_d._grid).ToString()));
            _showGrid.Checked = _d._showGrid; _snap.Checked = _d._snapOn;
            _bg.Set(_d._bgCol); _img.Text = _d._bgImg;
            _gz.Value = Math.Max(_gz.Minimum, Math.Min(_gz.Maximum, (decimal)Math.Round(_d._gzoom * 100)));
            _gfs.Value = Math.Max(_gfs.Minimum, Math.Min(_gfs.Maximum, (decimal)Math.Round(_d._gfs * 100)));
            _load = false;
        }

        private void Apply(bool flip)
        {
            int w = (int)_w.Value, h = (int)_h.Value;
            if (flip) { int t = w; w = h; h = t; }
            _d.SetCanvas(w, h, _scale.Checked);
            Reload();
        }
    }
}
