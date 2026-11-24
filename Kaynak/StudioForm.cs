using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    internal sealed class StudioForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(18, 20, 26), PanelBg = Color.FromArgb(24, 27, 35),
            CardBg = Color.FromArgb(34, 38, 48), LineC = Color.FromArgb(56, 62, 76),
            TxC = Color.FromArgb(234, 236, 242), DimC = Color.FromArgb(148, 154, 170),
            AccC = Color.FromArgb(0, 200, 255);

        private sealed class StudioSnapshot
        {
            public List<WInst> Widgets;
            public int Cw, Ch, BgCol, Margin;
            public string ThemeId, BgImg;
            public float Gfs, Gzoom;
            public bool TopBar, HideStrip;
        }

        private sealed class PalItem
        {
            public bool IsHeader;
            public string Text;
            public WDef Def;
        }

        private readonly DashboardForm _d;
        private List<WInst> _wd = new List<WInst>();
        private int _cw = 1920, _ch = 1080, _bgCol, _margin;
        private Theme _th = Themes.All[0];
        private string _bgImg = "";
        private float _gfs = 1f, _gzoom = 1f, _grid = 10f;
        private bool _topBar = true, _hideStrip = false, _showGrid = true, _snapOn = true;

        private readonly List<string> _selIds = new List<string>();
        private readonly List<StudioSnapshot> _undo = new List<StudioSnapshot>();
        private readonly List<StudioSnapshot> _redo = new List<StudioSnapshot>();
        private WInst _clipWidget, _clipStyle;

        // UI controls
        private ComboBox _cbRes, _cbTheme, _cbGrid;
        private CheckBox _ckGrid, _ckSnap, _ckLive;
        private Label _lblZoom, _lblInfo;
        private TextBox _txtSearch;
        private ListBox _palList, _layerList;
        private StudioCanvas _canvas;
        private Panel _inspHost;
        private System.Windows.Forms.Timer _liveTimer;

        // Inspector controls (widget vs canvas)
        private Panel _pCanvasInsp, _pWidgetInsp;
        private bool _bind;
        private NumericUpDown _cW, _cH, _cGfs, _cGz, _cMargin;
        private ColorField _cBg;
        private TextBox _cBgImg;
        private CheckBox _cBar;

        private Label _wTitle;
        private NumericUpDown _wX, _wY, _wW, _wH, _wFs, _wZoom, _wFw, _wRad, _wGw;
        private CheckBox _wLock, _wNoTitle;
        private ColorField _wA1, _wA2, _wTx, _wPc, _wFc;
        private TrackBar _wPa;
        private Label _wPaL, _wLText, _wLPath;
        private ComboBox _wFrame, _wBars, _wGlow, _wCap, _wAlign;
        private TextBox _wText, _wPath;
        private Button _wBrowse;
        private Panel _tokenPanel;

        private static string[] GetResNames()
        {
            return new[]
            {
                L.T("Özel", "Custom"), "1920 × 1080  Full HD", "2560 × 1440  QHD", "3840 × 2160  4K", "1600 × 900", "1280 × 720  HD",
                "2560 × 1080  Ultrawide", "3440 × 1440  UWQHD", L.T("1080 × 1920  Dikey FHD", "1080 × 1920  Portrait FHD"), L.T("1440 × 2560  Dikey QHD", "1440 × 2560  Portrait QHD"),
                L.T("1024 × 600  Kasa 7\"", "1024 × 600  7\" Sensor"), L.T("800 × 480  Kasa 5\"", "800 × 480  5\" Sensor"), L.T("1280 × 400  Şerit", "1280 × 400  Bar"), "480 × 320  Mini"
            };
        }
        private static readonly int[][] ResSizes = {
            null, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 }, new[] { 1600, 900 }, new[] { 1280, 720 },
            new[] { 2560, 1080 }, new[] { 3440, 1440 }, new[] { 1080, 1920 }, new[] { 1440, 2560 },
            new[] { 1024, 600 }, new[] { 800, 480 }, new[] { 1280, 400 }, new[] { 480, 320 }
        };
        private static readonly string[] GridNm = { "1", "2", "4", "5", "8", "10", "20", "40" };
        private static readonly string[] FrameIds = { "", "flat", "glass", "neon", "blueprint", "crt", "hud", "brutal", "clip", "mono", "none" };
        private static string[] GetFrameNm()
        {
            return L.IsEn
                ? new[] { "Theme", "Flat", "Glass", "Neon", "Blueprint", "CRT", "HUD Corners", "Hard Shadow", "Cut Corner", "Sharp", "Borderless" }
                : new[] { "Tema", "Düz", "Cam", "Neon", "Teknik çizim", "CRT", "HUD köşeleri", "Sert gölge", "Kesik köşe", "Köşeli", "Çerçevesiz" };
        }
        private static readonly string[] BarIds = { "", "round", "thin", "chunk", "seg" };
        private static string[] GetBarNm()
        {
            return L.IsEn
                ? new[] { "Theme", "Rounded", "Thin", "Chunky", "Segmented" }
                : new[] { "Tema", "Yuvarlak", "İnce", "Kalın blok", "Parçalı" };
        }

        private static readonly Color[] QuickSwatches = {
            Color.Empty, Color.FromArgb(255, 74, 0), Color.FromArgb(0, 200, 255), Color.FromArgb(16, 185, 129),
            Color.FromArgb(139, 92, 246), Color.FromArgb(244, 63, 94), Color.FromArgb(250, 204, 21), Color.FromArgb(248, 250, 252)
        };

        public StudioForm(DashboardForm d)
        {
            _d = d;
            _d.WReg();
            ModernUi.ApplyDarkTitleBar(this);
            Text = L.T("OnMon — Düzen Stüdyosu", "OnMon — Layout Studio");
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1380, 820);
            MinimumSize = new Size(1100, 640);
            BackColor = Bg; ForeColor = TxC;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            try { Icon = Program.TrayIcon(); } catch { }

            BuildLayout();
            ReloadFromDashboard();

            _liveTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _liveTimer.Tick += delegate { if (_canvas != null && Visible) _canvas.Invalidate(); };
            _liveTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_liveTimer != null) { _liveTimer.Stop(); _liveTimer.Dispose(); }
            base.OnFormClosed(e);
        }

        internal void ReloadFromDashboard()
        {
            _wd = DashboardForm.CloneLayout(_d._wd);
            _cw = _d._cw; _ch = _d._ch; _bgCol = _d._bgCol; _bgImg = _d._bgImg ?? "";
            _th = _d._th ?? Themes.All[0];
            _gfs = _d._gfs; _gzoom = _d._gzoom; _grid = _d._grid;
            _topBar = _d._topBar; _hideStrip = _d._hideStrip; _margin = _d._margin;
            _showGrid = _d._showGrid; _snapOn = _d._snapOn;
            _selIds.Clear();
            _undo.Clear(); _redo.Clear();
            SyncToolbar();
            RefreshPalette();
            RefreshLayers();
            BindInspector();
            if (_canvas != null) { _canvas.FitToView(); _canvas.Invalidate(); }
        }

        private void BuildLayout()
        {
            // ---- TOP TOOLBAR ----
            var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = PanelBg, Padding = new Padding(8, 4, 8, 4) };
            top.Paint += delegate (object s, PaintEventArgs e) { using (var p = new Pen(LineC)) e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1); };
            Controls.Add(top);

            int x = 10, y1 = 6, y2 = 42;
            x = TBtn(top, L.T("↩ Geri", "↩ Undo"), x, y1, 72, delegate { Undo(); });
            x = TBtn(top, L.T("↪ İleri", "↪ Redo"), x, y1, 72, delegate { Redo(); });
            x += 8;
            x = TBtn(top, L.T("◧ Sol", "◧ Left"), x, y1, 58, delegate { AlignSelected("L"); });
            x = TBtn(top, L.T("◫ Y.Ort", "◫ H.Ctr"), x, y1, 64, delegate { AlignSelected("HC"); });
            x = TBtn(top, L.T("◨ Sağ", "◨ Right"), x, y1, 60, delegate { AlignSelected("R"); });
            x = TBtn(top, L.T("⬒ Üst", "⬒ Top"), x, y1, 58, delegate { AlignSelected("T"); });
            x = TBtn(top, L.T("⬓ D.Ort", "⬓ V.Ctr"), x, y1, 64, delegate { AlignSelected("VC"); });
            x = TBtn(top, L.T("⬔ Alt", "⬔ Bot"), x, y1, 58, delegate { AlignSelected("B"); });
            x = TBtn(top, L.T("⫼ Dağıt", "⫼ Dist"), x, y1, 66, delegate { DistributeSelected(); });
            x = TBtn(top, L.T("⊞ Boyut", "⊞ Size"), x, y1, 68, delegate { MatchSizeSelected(); });
            x += 8;
            _ckLive = new CheckBox { Text = L.T("Canlı yansıt", "Live Sync"), Left = x, Top = y1 + 3, Width = 100, Height = 24, Checked = true, ForeColor = AccC, BackColor = Color.Transparent };
            top.Controls.Add(_ckLive); x += 104;
            x = TBtn(top, L.T("📁 Galeri…", "📁 Gallery…"), x, y1, 92, delegate { _d.OpenGallery(); });
            x = TBtn(top, L.T("💾 Galeriye Kaydet", "💾 Save to Gallery"), x, y1, 134, delegate { SaveToGalleryDialog(); });
            x = TBtn(top, L.T("📤 Paylaş (.onmon)", "📤 Export (.onmon)"), x, y1, 134, delegate { _d.ExportTemplateFrom(_wd, _cw, _ch, _th, _bgCol, _bgImg, _gfs, _gzoom, _topBar, _hideStrip, _margin); });

            // Row 2
            x = 10;
            top.Controls.Add(new Label { Text = L.T("Ekran:", "Screen:"), Left = x, Top = y2 + 4, Width = 48, ForeColor = DimC }); x += 50;
            _cbRes = new ComboBox { Left = x, Top = y2 + 1, Width = 172, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardBg, ForeColor = TxC, FlatStyle = FlatStyle.Flat };
            _cbRes.Items.AddRange(GetResNames());
            _cbRes.SelectedIndexChanged += delegate
            {
                if (_bind || _cbRes.SelectedIndex <= 0) return;
                int[] sz = ResSizes[_cbRes.SelectedIndex];
                ResizeCanvas(sz[0], sz[1], true);
            };
            top.Controls.Add(_cbRes); x += 176;
            x = TBtn(top, "⇄", x, y2, 32, delegate { ResizeCanvas(_ch, _cw, true); });
            x += 8;
            top.Controls.Add(new Label { Text = L.T("Tema:", "Theme:"), Left = x, Top = y2 + 4, Width = 46, ForeColor = DimC }); x += 48;
            _cbTheme = new ComboBox { Left = x, Top = y2 + 1, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardBg, ForeColor = TxC, FlatStyle = FlatStyle.Flat };
            foreach (Theme t in Themes.All) _cbTheme.Items.Add(t.Title);
            _cbTheme.SelectedIndexChanged += delegate
            {
                if (_bind || _cbTheme.SelectedIndex < 0) return;
                PushUndo();
                _th = Themes.All[_cbTheme.SelectedIndex];
                ApplyAndRedraw(true);
            };
            top.Controls.Add(_cbTheme); x += 166;
            top.Controls.Add(new Label { Text = L.T("Izgara:", "Grid:"), Left = x, Top = y2 + 4, Width = 46, ForeColor = DimC }); x += 48;
            _cbGrid = new ComboBox { Left = x, Top = y2 + 1, Width = 54, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardBg, ForeColor = TxC, FlatStyle = FlatStyle.Flat };
            _cbGrid.Items.AddRange(GridNm);
            _cbGrid.SelectedIndexChanged += delegate { if (_bind || _cbGrid.SelectedIndex < 0) return; _grid = float.Parse(GridNm[_cbGrid.SelectedIndex], CultureInfo.InvariantCulture); _d._grid = _grid; _d.SaveSettings(); _canvas.Invalidate(); };
            top.Controls.Add(_cbGrid); x += 60;
            _ckGrid = new CheckBox { Text = L.T("Göster", "Show"), Left = x, Top = y2 + 3, Width = 68, Height = 24, ForeColor = TxC };
            _ckGrid.CheckedChanged += delegate { if (_bind) return; _showGrid = _ckGrid.Checked; _d._showGrid = _showGrid; _d.SaveSettings(); _canvas.Invalidate(); };
            top.Controls.Add(_ckGrid); x += 70;
            _ckSnap = new CheckBox { Text = L.T("Mıknatıs", "Snap"), Left = x, Top = y2 + 3, Width = 78, Height = 24, ForeColor = TxC };
            _ckSnap.CheckedChanged += delegate { if (_bind) return; _snapOn = _ckSnap.Checked; _d._snapOn = _snapOn; _d.SaveSettings(); };
            top.Controls.Add(_ckSnap); x += 82;

            x = TBtn(top, L.T("Sığdır", "Fit"), x, y2, 56, delegate { _canvas.FitToView(); UpdateZoomLabel(); });
            x = TBtn(top, "−", x, y2, 30, delegate { _canvas.ZoomStep(0.85f); UpdateZoomLabel(); });
            _lblZoom = new Label { Text = "100%", Left = x, Top = y2 + 4, Width = 48, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TxC };
            top.Controls.Add(_lblZoom); x += 50;
            x = TBtn(top, "+", x, y2, 30, delegate { _canvas.ZoomStep(1.18f); UpdateZoomLabel(); });
            _lblInfo = new Label { Text = "", Left = x + 12, Top = y2 + 4, Width = 280, ForeColor = DimC };
            top.Controls.Add(_lblInfo);

            // ---- LEFT PANEL (PALETTE + LAYERS) ----
            var left = new Panel { Dock = DockStyle.Left, Width = 272, BackColor = PanelBg, Padding = new Padding(8) };
            left.Paint += delegate (object s, PaintEventArgs e) { using (var p = new Pen(LineC)) e.Graphics.DrawLine(p, left.Width - 1, 0, left.Width - 1, left.Height); };
            Controls.Add(left);

            var lblPal = new Label { Text = L.T("WİDGET EKLE (Çift tıkla)", "ADD WIDGET (Double-click)"), Dock = DockStyle.Top, Height = 22, ForeColor = AccC, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _txtSearch = new TextBox { Dock = DockStyle.Top, BackColor = CardBg, ForeColor = TxC, BorderStyle = BorderStyle.FixedSingle };
            _txtSearch.TextChanged += delegate { RefreshPalette(); };

            _palList = new ListBox
            {
                Dock = DockStyle.Top, Height = 320, BackColor = Bg, ForeColor = TxC, BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, IntegralHeight = false
            };
            _palList.DrawItem += DrawPalItem;
            _palList.DoubleClick += delegate
            {
                PalItem it = _palList.SelectedItem as PalItem;
                if (it != null && !it.IsHeader && it.Def != null) AddOrShowWidget(it.Def.Id);
            };

            var layerHdr = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = PanelBg, Padding = new Padding(0, 6, 0, 2) };
            layerHdr.Controls.Add(new Label { Text = L.T("KATMANLAR (Sıra / Seçim)", "LAYERS (Order / Select)"), Left = 0, Top = 6, Width = 250, Height = 20, ForeColor = AccC, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            int lx = 0;
            lx = TBtn(layerHdr, "▲", lx, 28, 34, delegate { MoveLayer(true); });
            lx = TBtn(layerHdr, "▼", lx, 28, 34, delegate { MoveLayer(false); });
            lx = TBtn(layerHdr, "🔒", lx, 28, 34, delegate { ToggleLockSelected(); });
            lx = TBtn(layerHdr, L.T("Çoğalt", "Dup"), lx, 28, 62, delegate { DuplicateSelected(); });
            lx = TBtn(layerHdr, L.T("Sil/Gizle", "Remove"), lx, 28, 72, delegate { DeleteSelected(); });

            _layerList = new ListBox
            {
                Dock = DockStyle.Fill, BackColor = Bg, ForeColor = TxC, BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 25, IntegralHeight = false
            };
            _layerList.DrawItem += DrawLayerItem;
            _layerList.SelectedIndexChanged += delegate
            {
                if (_bind) return;
                WInst w = _layerList.SelectedItem as WInst;
                if (w != null) { _selIds.Clear(); _selIds.Add(w.Id); BindInspector(); _canvas.Invalidate(); }
            };

            // Add in reverse Dock.Top order, then Fill last
            left.Controls.Add(_layerList);
            left.Controls.Add(layerHdr);
            left.Controls.Add(_palList);
            left.Controls.Add(_txtSearch);
            left.Controls.Add(lblPal);

            // ---- RIGHT PANEL (INSPECTOR) ----
            _inspHost = new Panel { Dock = DockStyle.Right, Width = 376, BackColor = PanelBg, AutoScroll = true };
            _inspHost.Paint += delegate (object s, PaintEventArgs e) { using (var p = new Pen(LineC)) e.Graphics.DrawLine(p, 0, 0, 0, _inspHost.Height); };
            Controls.Add(_inspHost);
            BuildInspectors();

            // ---- CENTER CANVAS ----
            _canvas = new StudioCanvas(this) { Dock = DockStyle.Fill };
            Controls.Add(_canvas);
            _canvas.BringToFront();
        }

        private int TBtn(Control p, string text, int x, int y, int w, EventHandler click)
        {
            var b = new Button { Text = text, Left = x, Top = y, Width = w, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = CardBg, ForeColor = TxC, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8.5f) };
            b.FlatAppearance.BorderColor = LineC;
            b.Click += click;
            p.Controls.Add(b);
            return x + w + 4;
        }

        private void BuildInspectors()
        {
            // Canvas inspector
            _pCanvasInsp = new Panel { Dock = DockStyle.Top, Height = 640, BackColor = PanelBg };
            int y = 10;
            EdUi.Lbl(_pCanvasInsp, L.T("TUVAL VE GENEL AYARLAR", "CANVAS & GLOBAL SETTINGS"), 12, y, 310, true); y += 28;
            EdUi.Lbl(_pCanvasInsp, L.T("Genişlik", "Width"), 12, y + 2, 60, false); _cW = EdUi.Num(_pCanvasInsp, 74, y, 76, 200, 8000, 10, 0);
            EdUi.Lbl(_pCanvasInsp, L.T("Yükseklik", "Height"), 162, y + 2, 66, false); _cH = EdUi.Num(_pCanvasInsp, 230, y, 76, 120, 8000, 10, 0); y += 32;
            EdUi.Btn(_pCanvasInsp, L.T("Ölçekleyerek Uygula", "Scale & Apply"), 12, y, 152, delegate { ResizeCanvas((int)_cW.Value, (int)_cH.Value, true); });
            EdUi.Btn(_pCanvasInsp, L.T("Sadece Tuvali Değiştir", "Resize Canvas Only"), 170, y, 152, delegate { ResizeCanvas((int)_cW.Value, (int)_cH.Value, false); }); y += 36;
            _cBar = EdUi.Chk(_pCanvasInsp, L.T("Alt bilgi çubuğu ve ses şeridi göster", "Show bottom info bar & audio strip"), 12, y, 310); y += 30;
            _cBar.CheckedChanged += delegate { if (_bind) return; PushUndo(); _topBar = _cBar.Checked; _hideStrip = !_cBar.Checked; ApplyAndRedraw(true); };

            EdUi.Lbl(_pCanvasInsp, L.T("ARKA PLAN", "BACKGROUND"), 12, y, 310, true); y += 26;
            _cBg = new ColorField(_pCanvasInsp, L.T("Arka plan rengi", "Background color"), y, delegate { return _th.Bg; });
            _cBg.Changed = delegate { if (_bind) return; PushUndo(); _bgCol = _cBg.Value; ApplyAndRedraw(true); }; y += 32;
            EdUi.Lbl(_pCanvasInsp, L.T("Arka plan resmi", "Wallpaper image"), 12, y + 2, 140, false); y += 22;
            _cBgImg = new TextBox { Left = 12, Top = y, Width = 200, BackColor = CardBg, ForeColor = TxC, BorderStyle = BorderStyle.FixedSingle };
            _cBgImg.TextChanged += delegate { if (_bind) return; _bgImg = _cBgImg.Text; ApplyAndRedraw(true); };
            _pCanvasInsp.Controls.Add(_cBgImg);
            EdUi.Btn(_pCanvasInsp, L.T("Gözat…", "Browse…"), 218, y - 1, 60, delegate { using (var od = new OpenFileDialog { Filter = L.T("Resimler|*.png;*.jpg;*.jpeg;*.bmp", "Images|*.png;*.jpg;*.jpeg;*.bmp") }) if (od.ShowDialog() == DialogResult.OK) _cBgImg.Text = od.FileName; });
            EdUi.Btn(_pCanvasInsp, L.T("Sil", "Clear"), 282, y - 1, 44, delegate { _cBgImg.Text = ""; }); y += 36;

            EdUi.Lbl(_pCanvasInsp, L.T("GENEL ÖLÇEK", "GLOBAL SCALE"), 12, y, 310, true); y += 26;
            EdUi.Lbl(_pCanvasInsp, L.T("Tüm yazılar (%)", "All fonts (%)"), 12, y + 2, 130, false); _cGfs = EdUi.Num(_pCanvasInsp, 150, y, 76, 30, 300, 5, 0); y += 30;
            EdUi.Lbl(_pCanvasInsp, L.T("Tüm içerik (%)", "All content (%)"), 12, y + 2, 130, false); _cGz = EdUi.Num(_pCanvasInsp, 150, y, 76, 20, 400, 5, 0); y += 30;
            EdUi.Lbl(_pCanvasInsp, L.T("Kenar boşluğu (px)", "Outer margin (px)"), 12, y + 2, 130, false); _cMargin = EdUi.Num(_pCanvasInsp, 150, y, 76, 0, 48, 2, 0); y += 36;
            _cGfs.ValueChanged += delegate { if (_bind) return; _gfs = (float)_cGfs.Value / 100f; ApplyAndRedraw(true); };
            _cGz.ValueChanged += delegate { if (_bind) return; _gzoom = (float)_cGz.Value / 100f; ApplyAndRedraw(true); };
            _cMargin.ValueChanged += delegate { if (_bind) return; _margin = (int)_cMargin.Value; ApplyAndRedraw(true); };
            _inspHost.Controls.Add(_pCanvasInsp);

            // Widget inspector
            _pWidgetInsp = new Panel { Dock = DockStyle.Top, Height = 820, BackColor = PanelBg, Visible = false };
            y = 8;
            _wTitle = EdUi.Lbl(_pWidgetInsp, "WIDGET", 12, y, 316, true); y += 26;

            // Quick style presets
            int qx = 12;
            qx = TBtn(_pWidgetInsp, L.T("Tema", "Theme"), qx, y, 52, delegate { ApplyQuickPreset(""); });
            qx = TBtn(_pWidgetInsp, L.T("Cam", "Glass"), qx, y, 50, delegate { ApplyQuickPreset("glass"); });
            qx = TBtn(_pWidgetInsp, "Neon", qx, y, 52, delegate { ApplyQuickPreset("neon"); });
            qx = TBtn(_pWidgetInsp, "HUD", qx, y, 48, delegate { ApplyQuickPreset("hud"); });
            TBtn(_pWidgetInsp, L.T("Çerçevesiz", "Borderless"), qx, y, 80, delegate { ApplyQuickPreset("none"); }); y += 34;

            EdUi.Lbl(_pWidgetInsp, L.T("KONUM VE BOYUT", "POSITION & SIZE"), 12, y, 300, true); y += 24;
            EdUi.Lbl(_pWidgetInsp, "X", 12, y + 2, 18, false); _wX = EdUi.Num(_pWidgetInsp, 34, y, 68, 0, 8000, 1, 0);
            EdUi.Lbl(_pWidgetInsp, "Y", 112, y + 2, 18, false); _wY = EdUi.Num(_pWidgetInsp, 134, y, 68, 0, 8000, 1, 0); y += 28;
            EdUi.Lbl(_pWidgetInsp, L.T("Gen", "W"), 12, y + 2, 32, false); _wW = EdUi.Num(_pWidgetInsp, 46, y, 68, 20, 8000, 1, 0);
            EdUi.Lbl(_pWidgetInsp, L.T("Yük", "H"), 122, y + 2, 32, false); _wH = EdUi.Num(_pWidgetInsp, 156, y, 68, 20, 8000, 1, 0); y += 28;
            _wLock = EdUi.Chk(_pWidgetInsp, L.T("Kilitli", "Locked"), 12, y, 86);
            _wNoTitle = EdUi.Chk(_pWidgetInsp, L.T("Başlığı gizle", "Hide header"), 104, y, 160); y += 28;

            EdUi.Lbl(_pWidgetInsp, L.T("ÖLÇEK", "SCALE"), 12, y, 300, true); y += 24;
            EdUi.Lbl(_pWidgetInsp, L.T("Yazı (%)", "Font (%)"), 12, y + 2, 68, false); _wFs = EdUi.Num(_pWidgetInsp, 82, y, 66, 30, 300, 5, 0);
            EdUi.Lbl(_pWidgetInsp, L.T("İçerik (%)", "Zoom (%)"), 160, y + 2, 74, false); _wZoom = EdUi.Num(_pWidgetInsp, 238, y, 66, 20, 500, 5, 0); y += 32;

            EdUi.Lbl(_pWidgetInsp, L.T("RENK PALETİ (Hızlı Vurgu)", "COLOR SWATCHES (Accent)"), 12, y, 300, true); y += 24;
            for (int i = 0; i < QuickSwatches.Length; i++)
            {
                Color swc = QuickSwatches[i];
                var b = new Button { Left = 12 + i * 36, Top = y, Width = 32, Height = 22, FlatStyle = FlatStyle.Flat, BackColor = swc == Color.Empty ? CardBg : swc, Text = swc == Color.Empty ? "T" : "", ForeColor = TxC, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };
                b.FlatAppearance.BorderColor = LineC;
                b.Click += delegate { WInst w = PrimarySel(); if (w == null) return; PushUndo(); w.A1 = swc == Color.Empty ? 0 : swc.ToArgb(); BindInspector(); ApplyAndRedraw(true); };
                _pWidgetInsp.Controls.Add(b);
            }
            y += 30;

            _wA1 = new ColorField(_pWidgetInsp, L.T("Vurgu 1", "Accent 1"), y, delegate { return _th.A1; }); y += 28;
            _wA2 = new ColorField(_pWidgetInsp, L.T("Vurgu 2", "Accent 2"), y, delegate { return _th.A2; }); y += 28;
            _wTx = new ColorField(_pWidgetInsp, L.T("Yazı rengi", "Text color"), y, delegate { return _th.Tx; }); y += 28;
            _wPc = new ColorField(_pWidgetInsp, L.T("Panel dolgu", "Panel fill"), y, delegate { return _th.Panel; }); y += 28;
            _wFc = new ColorField(_pWidgetInsp, L.T("Çerçeve rengi", "Border color"), y, delegate { return _th.Line; }); y += 30;

            EdUi.Lbl(_pWidgetInsp, L.T("Opaklık", "Opacity"), 12, y + 4, 60, false);
            _wPa = new TrackBar { Left = 76, Top = y - 2, Width = 190, Minimum = 0, Maximum = 100, TickFrequency = 25, SmallChange = 5 };
            _pWidgetInsp.Controls.Add(_wPa);
            _wPaL = EdUi.Lbl(_pWidgetInsp, "100%", 270, y + 4, 50, false); y += 34;

            EdUi.Lbl(_pWidgetInsp, L.T("Çerçeve stili", "Frame style"), 12, y + 2, 100, false); _wFrame = EdUi.Combo(_pWidgetInsp, 120, y, 194, GetFrameNm()); y += 28;
            EdUi.Lbl(_pWidgetInsp, L.T("Kalınlık", "Border W"), 12, y + 2, 64, false); _wFw = EdUi.Num(_pWidgetInsp, 80, y, 64, 0, 12, 0.5m, 1);
            EdUi.Lbl(_pWidgetInsp, L.T("Köşe", "Radius"), 156, y + 2, 46, false); _wRad = EdUi.Num(_pWidgetInsp, 206, y, 64, -1, 100, 1, 0); y += 28;
            EdUi.Lbl(_pWidgetInsp, L.T("Çubuk stili", "Bar style"), 12, y + 2, 100, false); _wBars = EdUi.Combo(_pWidgetInsp, 120, y, 194, GetBarNm()); y += 28;
            EdUi.Lbl(_pWidgetInsp, L.T("Daire uç", "Dial cap"), 12, y + 2, 100, false); _wCap = EdUi.Combo(_pWidgetInsp, 120, y, 194, L.T("Tema", "Theme"), L.T("Yuvarlak uç", "Rounded"), L.T("Düz uç", "Flat")); y += 28;
            EdUi.Lbl(_pWidgetInsp, L.T("Daire kalınlık", "Dial width"), 12, y + 2, 100, false); _wGw = EdUi.Num(_pWidgetInsp, 120, y, 66, 0, 300, 10, 0);
            _wGlow = EdUi.Combo(_pWidgetInsp, 196, y, 118, L.T("Glow:Tema", "Glow:Theme"), L.T("Glow:Açık", "Glow:On"), L.T("Glow:Kapalı", "Glow:Off")); y += 32;

            _wLText = EdUi.Lbl(_pWidgetInsp, L.T("METİN / BAŞLIK", "TEXT / CAPTION"), 12, y, 300, true); y += 22;
            _wText = new TextBox { Left = 12, Top = y, Width = 306, Height = 48, Multiline = true, BackColor = CardBg, ForeColor = TxC, BorderStyle = BorderStyle.FixedSingle };
            _pWidgetInsp.Controls.Add(_wText); y += 52;
            _tokenPanel = new Panel { Left = 12, Top = y, Width = 308, Height = 52, BackColor = PanelBg };
            string[] toks = L.IsEn
                ? new[] { "{time}", "{date}", "{cpu}", "{cputemp}", "{gpu}", "{gputemp}", "{ram}", "{fps}", "{bat}" }
                : new[] { "{saat}", "{tarih}", "{cpu}", "{cpusic}", "{gpu}", "{gpusic}", "{ram}", "{fps}", "{ping}" };
            int tx = 0, ty = 0;
            foreach (string tk in toks)
            {
                string ttk = tk;
                var tb = new Button { Text = tk, Left = tx, Top = ty, Width = 58, Height = 22, FlatStyle = FlatStyle.Flat, BackColor = CardBg, ForeColor = AccC, Font = new Font("Segoe UI", 7.5f) };
                tb.FlatAppearance.BorderColor = LineC;
                tb.Click += delegate { _wText.Text = (_wText.Text ?? "") + ttk; };
                _tokenPanel.Controls.Add(tb);
                tx += 61; if (tx + 58 > 308) { tx = 0; ty += 26; }
            }
            _pWidgetInsp.Controls.Add(_tokenPanel); y += 56;
            _wAlign = EdUi.Combo(_pWidgetInsp, 12, y, 120, L.T("Hiza: Sol", "Align: Left"), L.T("Hiza: Orta", "Align: Center"), L.T("Hiza: Sağ", "Align: Right"));

            _wLPath = EdUi.Lbl(_pWidgetInsp, L.T("RESİM / LOGO DOSYASI", "IMAGE / LOGO FILE"), 12, y, 220, true);
            _wPath = new TextBox { Left = 12, Top = y + 24, Width = 232, BackColor = CardBg, ForeColor = TxC, BorderStyle = BorderStyle.FixedSingle };
            _wBrowse = EdUi.Btn(_pWidgetInsp, L.T("Gözat…", "Browse…"), 250, y + 23, 68, delegate
            {
                using (var od = new OpenFileDialog { Filter = L.T("Resimler|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tümü|*.*", "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All|*.*") })
                    if (od.ShowDialog() == DialogResult.OK) _wPath.Text = od.FileName;
            });
            _pWidgetInsp.Controls.Add(_wPath);
            y += 62;

            EdUi.Btn(_pWidgetInsp, L.T("Stili Kopyala", "Copy Style"), 12, y, 96, delegate { WInst w = PrimarySel(); if (w != null) _clipStyle = w.Copy(); });
            EdUi.Btn(_pWidgetInsp, L.T("Stili Yapıştır", "Paste Style"), 114, y, 98, delegate { if (_clipStyle == null || _selIds.Count == 0) return; PushUndo(); foreach (string id in _selIds) DashboardForm.CopyStyleFields(_clipStyle, FindW(id)); BindInspector(); ApplyAndRedraw(true); });
            EdUi.Btn(_pWidgetInsp, L.T("Sıfırla", "Reset"), 218, y, 100, delegate { ResetSelectedStyle(); });
            _pWidgetInsp.Height = y + 44;

            _wX.ValueChanged += delegate { ApplyWidgetProps(); }; _wY.ValueChanged += delegate { ApplyWidgetProps(); };
            _wW.ValueChanged += delegate { ApplyWidgetProps(); }; _wH.ValueChanged += delegate { ApplyWidgetProps(); };
            _wFs.ValueChanged += delegate { ApplyWidgetProps(); }; _wZoom.ValueChanged += delegate { ApplyWidgetProps(); };
            _wFw.ValueChanged += delegate { ApplyWidgetProps(); }; _wRad.ValueChanged += delegate { ApplyWidgetProps(); };
            _wGw.ValueChanged += delegate { ApplyWidgetProps(); };
            _wPa.Scroll += delegate { ApplyWidgetProps(); };
            _wFrame.SelectedIndexChanged += delegate { ApplyWidgetProps(); }; _wBars.SelectedIndexChanged += delegate { ApplyWidgetProps(); };
            _wGlow.SelectedIndexChanged += delegate { ApplyWidgetProps(); }; _wCap.SelectedIndexChanged += delegate { ApplyWidgetProps(); };
            _wAlign.SelectedIndexChanged += delegate { ApplyWidgetProps(); };
            _wLock.CheckedChanged += delegate { ApplyWidgetProps(); }; _wNoTitle.CheckedChanged += delegate { ApplyWidgetProps(); };
            _wText.TextChanged += delegate { ApplyWidgetProps(); }; _wPath.TextChanged += delegate { ApplyWidgetProps(); };
            foreach (ColorField c in new[] { _wA1, _wA2, _wTx, _wPc, _wFc }) c.Changed = ApplyWidgetProps;

            _inspHost.Controls.Add(_pWidgetInsp);
        }

        private WInst FindW(string id)
        {
            foreach (WInst w in _wd) if (w.Id == id) return w;
            return null;
        }

        private WInst PrimarySel()
        {
            return _selIds.Count > 0 ? FindW(_selIds[_selIds.Count - 1]) : null;
        }

        private void SyncToolbar()
        {
            _bind = true;
            int ri = 0;
            for (int i = 1; i < ResSizes.Length; i++) if (ResSizes[i][0] == _cw && ResSizes[i][1] == _ch) { ri = i; break; }
            _cbRes.SelectedIndex = ri;
            int ti = Array.IndexOf(Themes.All, _th);
            _cbTheme.SelectedIndex = Math.Max(0, ti);
            _cbGrid.SelectedIndex = Math.Max(0, Array.IndexOf(GridNm, ((int)_grid).ToString()));
            _ckGrid.Checked = _showGrid;
            _ckSnap.Checked = _snapOn;
            _bind = false;
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel()
        {
            if (_canvas == null || _lblZoom == null) return;
            _lblZoom.Text = Math.Round(_canvas.EffectiveScale * 100f) + "%";
            _lblInfo.Text = _cw + "×" + _ch + "  ·  " + _wd.Count(x => x.Vis) + L.T(" aktif widget", " active widgets");
        }

        private void RefreshPalette()
        {
            string q = (_txtSearch.Text ?? "").Trim().ToLowerInvariant();
            _palList.BeginUpdate();
            _palList.Items.Clear();
            string lastCat = null;
            foreach (WDef d in _d._wlist)
            {
                if (q.Length > 0 && d.Title.ToLowerInvariant().IndexOf(q) < 0 && d.Name.ToLowerInvariant().IndexOf(q) < 0 && d.Id.ToLowerInvariant().IndexOf(q) < 0 && d.CatTitle.ToLowerInvariant().IndexOf(q) < 0)
                    continue;
                string ct = d.CatTitle;
                if (ct != lastCat)
                {
                    lastCat = ct;
                    _palList.Items.Add(new PalItem { IsHeader = true, Text = (lastCat ?? L.T("Diğer", "Other")).ToUpper(L.Culture) });
                }
                _palList.Items.Add(new PalItem { IsHeader = false, Text = d.Title, Def = d });
            }
            _palList.EndUpdate();
        }

        private void RefreshLayers()
        {
            _bind = true;
            _layerList.BeginUpdate();
            _layerList.Items.Clear();
            for (int i = _wd.Count - 1; i >= 0; i--)
                if (_wd[i].Vis) _layerList.Items.Add(_wd[i]);
            WInst p = PrimarySel();
            if (p != null) _layerList.SelectedItem = p;
            _layerList.EndUpdate();
            _bind = false;
            UpdateZoomLabel();
        }

        private void DrawPalItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            PalItem it = _palList.Items[e.Index] as PalItem;
            if (it == null) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && !it.IsHeader;
            using (var br = new SolidBrush(it.IsHeader ? PanelBg : (sel ? Color.FromArgb(46, 54, 70) : Bg)))
                e.Graphics.FillRectangle(br, e.Bounds);
            if (it.IsHeader)
            {
                TextRenderer.DrawText(e.Graphics, it.Text, new Font("Segoe UI", 8f, FontStyle.Bold), e.Bounds, AccC, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                return;
            }
            WInst existing = !it.Def.Multi ? FindW(it.Def.Id) : null;
            bool on = existing != null && existing.Vis;
            string prefix = it.Def.Multi ? "＋ " : (on ? "✔ " : "＋ ");
            TextRenderer.DrawText(e.Graphics, prefix + it.Text, Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height), on ? DimC : TxC, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        private void DrawLayerItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            WInst w = _layerList.Items[e.Index] as WInst;
            if (w == null) return;
            bool sel = _selIds.Contains(w.Id);
            using (var br = new SolidBrush(sel ? Color.FromArgb(46, 54, 70) : Bg)) e.Graphics.FillRectangle(br, e.Bounds);
            if (sel) using (var br = new SolidBrush(AccC)) e.Graphics.FillRectangle(br, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            WDef d = _d.DefOf(w.Id);
            string txt = (w.Locked ? "🔒 " : "") + (d != null ? d.Title : w.Id);
            string sz = (int)Math.Round(w.W) + "×" + (int)Math.Round(w.H);
            TextRenderer.DrawText(e.Graphics, txt, Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 78, e.Bounds.Height), TxC, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, sz, new Font("Segoe UI", 8f), new Rectangle(e.Bounds.Right - 72, e.Bounds.Y, 66, e.Bounds.Height), DimC, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        private void BindInspector()
        {
            _bind = true;
            WInst w = PrimarySel();
            if (w == null)
            {
                _pWidgetInsp.Visible = false;
                _pCanvasInsp.Visible = true;
                _cW.Value = Math.Max(_cW.Minimum, Math.Min(_cW.Maximum, _cw));
                _cH.Value = Math.Max(_cH.Minimum, Math.Min(_cH.Maximum, _ch));
                _cBar.Checked = _topBar && !_hideStrip;
                _cBg.Set(_bgCol);
                _cBgImg.Text = _bgImg;
                _cGfs.Value = Math.Max(_cGfs.Minimum, Math.Min(_cGfs.Maximum, (decimal)Math.Round(_gfs * 100)));
                _cGz.Value = Math.Max(_cGz.Minimum, Math.Min(_cGz.Maximum, (decimal)Math.Round(_gzoom * 100)));
                _cMargin.Value = Math.Max(_cMargin.Minimum, Math.Min(_cMargin.Maximum, _margin));
            }
            else
            {
                _pCanvasInsp.Visible = false;
                _pWidgetInsp.Visible = true;
                WDef d = _d.DefOf(w.Id);
                _wTitle.Text = (_selIds.Count > 1 ? _selIds.Count + L.T(" widget seçili — ", " widgets selected — ") : "") + (d != null ? d.Title : w.Id);
                _wX.Value = Clamp(_wX, (decimal)Math.Round(w.X));
                _wY.Value = Clamp(_wY, (decimal)Math.Round(w.Y));
                _wW.Value = Clamp(_wW, (decimal)Math.Round(w.W));
                _wH.Value = Clamp(_wH, (decimal)Math.Round(w.H));
                _wLock.Checked = w.Locked;
                _wNoTitle.Checked = w.NoTitle;
                _wFs.Value = Clamp(_wFs, (decimal)Math.Round(w.Fs * 100));
                _wZoom.Value = Clamp(_wZoom, (decimal)Math.Round(w.Zoom * 100));
                _wA1.Set(w.A1); _wA2.Set(w.A2); _wTx.Set(w.Tx); _wPc.Set(w.PanelC); _wFc.Set(w.FrameC);
                _wPa.Value = Math.Max(0, Math.Min(100, w.PAlpha)); _wPaL.Text = _wPa.Value + "%";
                _wFrame.SelectedIndex = Math.Max(0, Array.IndexOf(FrameIds, w.Frame));
                _wFw.Value = Clamp(_wFw, (decimal)w.FrameW);
                _wRad.Value = Clamp(_wRad, (decimal)w.Radius);
                _wBars.SelectedIndex = Math.Max(0, Array.IndexOf(BarIds, w.Bars));
                _wCap.SelectedIndex = w.Cap == "round" ? 1 : (w.Cap == "flat" ? 2 : 0);
                _wGw.Value = Clamp(_wGw, (decimal)Math.Round(w.GaugeW * 100));
                _wGlow.SelectedIndex = w.Glow == "on" ? 1 : (w.Glow == "off" ? 2 : 0);

                string b = DashboardForm.BaseId(w.Id);
                bool isText = b == "text", isBox = b == "box", isImg = b == "image";
                _wLText.Visible = _wText.Visible = isText || isBox;
                _tokenPanel.Visible = isText;
                _wAlign.Visible = isText;
                _wLPath.Visible = _wPath.Visible = _wBrowse.Visible = isImg;
                _wText.Text = w.Text; _wPath.Text = w.Path;
                _wAlign.SelectedIndex = Math.Max(0, Math.Min(2, w.Align));
            }
            _bind = false;
        }

        private static decimal Clamp(NumericUpDown n, decimal v) { return Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        private void ApplyWidgetProps()
        {
            if (_bind) return;
            WInst w = PrimarySel(); if (w == null) return;
            w.X = (float)_wX.Value; w.Y = (float)_wY.Value;
            w.W = (float)_wW.Value; w.H = (float)_wH.Value;
            w.Locked = _wLock.Checked; w.NoTitle = _wNoTitle.Checked;
            w.Fs = (float)_wFs.Value / 100f; w.Zoom = (float)_wZoom.Value / 100f;
            w.A1 = _wA1.Value; w.A2 = _wA2.Value; w.Tx = _wTx.Value; w.PanelC = _wPc.Value; w.FrameC = _wFc.Value;
            w.PAlpha = _wPa.Value; _wPaL.Text = _wPa.Value + "%";
            w.Frame = FrameIds[Math.Max(0, _wFrame.SelectedIndex)];
            w.FrameW = (float)_wFw.Value; w.Radius = (float)_wRad.Value;
            w.Bars = BarIds[Math.Max(0, _wBars.SelectedIndex)];
            w.Cap = _wCap.SelectedIndex == 1 ? "round" : (_wCap.SelectedIndex == 2 ? "flat" : "");
            w.GaugeW = (float)_wGw.Value / 100f;
            w.Glow = _wGlow.SelectedIndex == 1 ? "on" : (_wGlow.SelectedIndex == 2 ? "off" : "");
            w.Text = _wText.Text; w.Path = _wPath.Text; w.Align = Math.Max(0, _wAlign.SelectedIndex);
            ApplyAndRedraw(true);
        }

        private void ApplyQuickPreset(string style)
        {
            if (_selIds.Count == 0) return;
            PushUndo();
            foreach (string id in _selIds)
            {
                WInst w = FindW(id); if (w == null) continue;
                w.Frame = style;
                w.PAlpha = style == "glass" ? 80 : 100;
                w.Glow = style == "neon" ? "on" : "";
            }
            BindInspector();
            ApplyAndRedraw(true);
        }

        private void ResetSelectedStyle()
        {
            if (_selIds.Count == 0) return;
            PushUndo();
            var def = new WInst();
            foreach (string id in _selIds)
            {
                WInst w = FindW(id); if (w == null) continue;
                w.A1 = def.A1; w.A2 = def.A2; w.Tx = def.Tx; w.PanelC = def.PanelC; w.FrameC = def.FrameC;
                w.PAlpha = 100; w.Frame = ""; w.Bars = ""; w.Glow = ""; w.Cap = "";
                w.Zoom = 1f; w.FrameW = 0; w.Radius = -1; w.GaugeW = 0; w.NoTitle = false; w.Fs = 1f;
            }
            BindInspector();
            ApplyAndRedraw(true);
        }

        private void PushUndo()
        {
            _undo.Add(new StudioSnapshot
            {
                Widgets = DashboardForm.CloneLayout(_wd),
                Cw = _cw, Ch = _ch, BgCol = _bgCol, Margin = _margin,
                ThemeId = _th.Id, BgImg = _bgImg, Gfs = _gfs, Gzoom = _gzoom,
                TopBar = _topBar, HideStrip = _hideStrip
            });
            if (_undo.Count > 50) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void RestoreSnapshot(StudioSnapshot s)
        {
            _wd = DashboardForm.CloneLayout(s.Widgets);
            _cw = s.Cw; _ch = s.Ch; _bgCol = s.BgCol; _margin = s.Margin;
            _th = Themes.Get(s.ThemeId); _bgImg = s.BgImg; _gfs = s.Gfs; _gzoom = s.Gzoom;
            _topBar = s.TopBar; _hideStrip = s.HideStrip;
            _selIds.RemoveAll(id => FindW(id) == null || !FindW(id).Vis);
            SyncToolbar();
            RefreshLayers();
            BindInspector();
            ApplyAndRedraw(true);
        }

        internal void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(new StudioSnapshot
            {
                Widgets = DashboardForm.CloneLayout(_wd),
                Cw = _cw, Ch = _ch, BgCol = _bgCol, Margin = _margin,
                ThemeId = _th.Id, BgImg = _bgImg, Gfs = _gfs, Gzoom = _gzoom,
                TopBar = _topBar, HideStrip = _hideStrip
            });
            StudioSnapshot s = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            RestoreSnapshot(s);
        }

        internal void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(new StudioSnapshot
            {
                Widgets = DashboardForm.CloneLayout(_wd),
                Cw = _cw, Ch = _ch, BgCol = _bgCol, Margin = _margin,
                ThemeId = _th.Id, BgImg = _bgImg, Gfs = _gfs, Gzoom = _gzoom,
                TopBar = _topBar, HideStrip = _hideStrip
            });
            StudioSnapshot s = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            RestoreSnapshot(s);
        }

        private void ApplyAndRedraw(bool syncLive)
        {
            _canvas.Invalidate();
            _palList.Invalidate();
            _layerList.Invalidate();
            UpdateZoomLabel();
            if (syncLive && (_ckLive == null || _ckLive.Checked))
            {
                _d._cw = _cw; _d._ch = _ch; _d._th = _th; _d._bgCol = _bgCol; _d._bgImg = _bgImg;
                _d._gfs = _gfs; _d._gzoom = _gzoom; _d._topBar = _topBar; _d._hideStrip = _hideStrip; _d._margin = _margin;
                _d.ReplaceLayout(DashboardForm.CloneLayout(_wd), true);
            }
        }

        private void ResizeCanvas(int w, int h, bool scaleWidgets)
        {
            w = Math.Max(200, Math.Min(8000, w)); h = Math.Max(120, Math.Min(8000, h));
            if (w == _cw && h == _ch) return;
            PushUndo();
            if (scaleWidgets)
            {
                float kx = w / (float)_cw, ky = h / (float)_ch;
                foreach (WInst x in _wd) { x.X = (float)Math.Round(x.X * kx); x.Y = (float)Math.Round(x.Y * ky); x.W = (float)Math.Round(x.W * kx); x.H = (float)Math.Round(x.H * ky); }
                _gzoom = Math.Max(0.2f, Math.Min(4f, _gzoom * Math.Min(kx, ky)));
            }
            _cw = w; _ch = h;
            foreach (WInst x in _wd) { x.W = Math.Min(x.W, _cw); x.H = Math.Min(x.H, _ch); x.X = Math.Max(0, Math.Min(_cw - x.W, x.X)); x.Y = Math.Max(0, Math.Min(_ch - x.H, x.Y)); }
            SyncToolbar();
            BindInspector();
            _canvas.FitToView();
            ApplyAndRedraw(true);
        }

        private string NextMultiId(string baseId)
        {
            for (int n = 1; ; n++) { string id = baseId + "#" + n; if (FindW(id) == null) return id; }
        }

        internal void AddOrShowWidget(string id)
        {
            WDef bd = _d.DefOf(id); if (bd == null) return;
            PushUndo();
            WInst w;
            if (bd.Multi)
            {
                w = new WInst { Id = NextMultiId(id), Vis = true };
                if (id == "text") w.Text = L.IsEn ? "{time}" : "{saat}";
                _wd.Add(w);
            }
            else
            {
                w = FindW(id);
                if (w == null) { w = new WInst { Id = id }; _wd.Add(w); }
                w.Vis = true;
            }
            float sx = _cw / 1920f, sy = _ch / 1080f;
            w.W = (float)Math.Round(Math.Min(_cw, bd.W * Math.Min(sx, sy)));
            w.H = (float)Math.Round(Math.Min(_ch, bd.H * Math.Min(sx, sy)));
            w.X = SnapVal(Math.Max(0, (_cw - w.W) / 2f));
            w.Y = SnapVal(Math.Max(0, (_ch - w.H) / 2f));
            _wd.Remove(w); _wd.Add(w);
            _selIds.Clear(); _selIds.Add(w.Id);
            RefreshLayers(); BindInspector(); ApplyAndRedraw(true);
        }

        internal void AddImageAt(string file, float cx, float cy)
        {
            PushUndo();
            var w = new WInst { Id = NextMultiId("image"), Vis = true, Path = file, W = 300, H = 300 };
            w.X = Math.Max(0, Math.Min(_cw - w.W, SnapVal(cx - w.W / 2f)));
            w.Y = Math.Max(0, Math.Min(_ch - w.H, SnapVal(cy - w.H / 2f)));
            _wd.Add(w);
            _selIds.Clear(); _selIds.Add(w.Id);
            RefreshLayers(); BindInspector(); ApplyAndRedraw(true);
        }

        internal void DuplicateSelected()
        {
            WInst cur = PrimarySel(); if (cur == null) return;
            WDef d = _d.DefOf(cur.Id);
            if (d == null || !d.Multi) { MessageBox.Show(L.T("Tekil sensör widget'ları (CPU, GPU vb.) yalnızca bir kez eklenebilir; Metin, Resim ve Kutu öğeleri çoğaltılabilir.", "Single-instance sensor widgets (CPU, GPU, etc.) can only be placed once; Text, Image, and Box widgets can be duplicated."), "OnMon"); return; }
            PushUndo();
            WInst c = cur.Copy(); c.Id = NextMultiId(DashboardForm.BaseId(cur.Id));
            c.X = Math.Min(_cw - c.W, cur.X + 24); c.Y = Math.Min(_ch - c.H, cur.Y + 24);
            _wd.Add(c);
            _selIds.Clear(); _selIds.Add(c.Id);
            RefreshLayers(); BindInspector(); ApplyAndRedraw(true);
        }

        internal void DeleteSelected()
        {
            if (_selIds.Count == 0) return;
            PushUndo();
            foreach (string id in new List<string>(_selIds))
            {
                WInst w = FindW(id); if (w == null) continue;
                WDef d = _d.DefOf(w.Id);
                if (d != null && d.Multi) _wd.Remove(w); else w.Vis = false;
            }
            _selIds.Clear();
            RefreshLayers(); BindInspector(); ApplyAndRedraw(true);
        }

        internal void MoveLayer(bool forward)
        {
            WInst cur = PrimarySel(); if (cur == null) return;
            PushUndo();
            int idx = _wd.IndexOf(cur);
            if (idx < 0) return;
            _wd.RemoveAt(idx);
            if (forward) _wd.Add(cur); else _wd.Insert(0, cur);
            RefreshLayers(); ApplyAndRedraw(true);
        }

        internal void ToggleLockSelected()
        {
            if (_selIds.Count == 0) return;
            PushUndo();
            bool target = !PrimarySel().Locked;
            foreach (string id in _selIds) { WInst w = FindW(id); if (w != null) w.Locked = target; }
            RefreshLayers(); BindInspector(); ApplyAndRedraw(true);
        }

        private void AlignSelected(string mode)
        {
            if (_selIds.Count == 0) return;
            PushUndo();
            var list = _selIds.Select(FindW).Where(w => w != null && !w.Locked).ToList();
            if (list.Count == 0) return;
            if (list.Count == 1)
            {
                WInst w = list[0];
                if (mode == "L") w.X = 0;
                else if (mode == "R") w.X = _cw - w.W;
                else if (mode == "HC") w.X = (float)Math.Round((_cw - w.W) / 2f);
                else if (mode == "T") w.Y = 0;
                else if (mode == "B") w.Y = _ch - w.H;
                else if (mode == "VC") w.Y = (float)Math.Round((_ch - w.H) / 2f);
            }
            else
            {
                float minX = list.Min(w => w.X), maxR = list.Max(w => w.X + w.W);
                float minY = list.Min(w => w.Y), maxB = list.Max(w => w.Y + w.H);
                float midX = (minX + maxR) / 2f, midY = (minY + maxB) / 2f;
                foreach (WInst w in list)
                {
                    if (mode == "L") w.X = minX;
                    else if (mode == "R") w.X = maxR - w.W;
                    else if (mode == "HC") w.X = (float)Math.Round(midX - w.W / 2f);
                    else if (mode == "T") w.Y = minY;
                    else if (mode == "B") w.Y = maxB - w.H;
                    else if (mode == "VC") w.Y = (float)Math.Round(midY - w.H / 2f);
                }
            }
            BindInspector(); ApplyAndRedraw(true);
        }

        private void DistributeSelected()
        {
            var list = _selIds.Select(FindW).Where(w => w != null && !w.Locked).OrderBy(w => w.X).ToList();
            if (list.Count < 3) return;
            PushUndo();
            float left = list[0].X, right = list[list.Count - 1].X + list[list.Count - 1].W;
            float sumW = list.Sum(w => w.W);
            float gap = (right - left - sumW) / (list.Count - 1);
            float x = left;
            foreach (WInst w in list) { w.X = (float)Math.Round(x); x += w.W + gap; }
            BindInspector(); ApplyAndRedraw(true);
        }

        private void MatchSizeSelected()
        {
            WInst p = PrimarySel(); if (p == null || _selIds.Count < 2) return;
            PushUndo();
            foreach (string id in _selIds)
            {
                WInst w = FindW(id);
                if (w != null && !w.Locked && w != p) { w.W = p.W; w.H = p.H; }
            }
            BindInspector(); ApplyAndRedraw(true);
        }

        private void SaveToGalleryDialog()
        {
            using (var dlg = new SaveTemplateModal(_d))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _d.SaveTemplateToGallery(dlg.TplName, dlg.TplAuthor, dlg.TplDesc, _wd, _cw, _ch, _th, _bgCol, _bgImg, _gfs, _gzoom, _topBar, _hideStrip, _margin);
                if (_d._galleryForm != null && !_d._galleryForm.IsDisposed) _d._galleryForm.RefreshCards();
                MessageBox.Show(L.T("Düzen galeriye kaydedildi: ", "Layout saved to gallery: ") + dlg.TplName, "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        internal float SnapVal(float v) { return (!_snapOn || _grid <= 1f) ? (float)Math.Round(v) : (float)Math.Round(v / _grid) * _grid; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Control act = ActiveControl;
            if (act is TextBox || act is NumericUpDown) return base.ProcessCmdKey(ref msg, keyData);

            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
            if (keyData == (Keys.Control | Keys.D)) { DuplicateSelected(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { WInst p = PrimarySel(); if (p != null) { _clipWidget = p.Copy(); _clipStyle = p.Copy(); } return true; }
            if (keyData == (Keys.Control | Keys.V))
            {
                if (_clipWidget != null && _d.DefOf(_clipWidget.Id).Multi)
                {
                    PushUndo();
                    WInst c = _clipWidget.Copy(); c.Id = NextMultiId(DashboardForm.BaseId(c.Id));
                    c.X = Math.Min(_cw - c.W, c.X + 24); c.Y = Math.Min(_ch - c.H, c.Y + 24);
                    _wd.Add(c); _selIds.Clear(); _selIds.Add(c.Id);
                    RefreshLayers(); BindInspector(); ApplyAndRedraw(true);
                }
                else if (_clipStyle != null && _selIds.Count > 0)
                {
                    PushUndo();
                    foreach (string id in _selIds) DashboardForm.CopyStyleFields(_clipStyle, FindW(id));
                    BindInspector(); ApplyAndRedraw(true);
                }
                return true;
            }
            if (keyData == Keys.Delete) { DeleteSelected(); return true; }

            Keys bare = keyData & Keys.KeyCode;
            if (_selIds.Count > 0 && (bare == Keys.Left || bare == Keys.Right || bare == Keys.Up || bare == Keys.Down))
            {
                bool shift = (keyData & Keys.Shift) != 0, alt = (keyData & Keys.Alt) != 0;
                float step = alt ? 1f : (shift ? Math.Max(10f, _grid * 4f) : Math.Max(1f, _grid));
                PushUndo();
                foreach (string id in _selIds)
                {
                    WInst w = FindW(id); if (w == null || w.Locked) continue;
                    if (bare == Keys.Left) w.X = Math.Max(0, w.X - step);
                    else if (bare == Keys.Right) w.X = Math.Min(_cw - w.W, w.X + step);
                    else if (bare == Keys.Up) w.Y = Math.Max(0, w.Y - step);
                    else if (bare == Keys.Down) w.Y = Math.Min(_ch - w.H, w.Y + step);
                }
                BindInspector(); ApplyAndRedraw(true);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------- CENTER INTERACTIVE CANVAS ----------------
        private sealed class StudioCanvas : Control
        {
            private readonly StudioForm _f;
            private bool _fit = true;
            private float _zoom = 0.5f, _panX = 24f, _panY = 24f;
            public float EffectiveScale { get { return ComputeScale(); } }

            private int _mode; // 0 none, 1 move, 2..9 resize, 10 pan, 11 marquee
            private float _mx0, _my0, _panX0, _panY0, _cx0, _cy0;
            private RectangleF _box0;
            private readonly Dictionary<string, PointF> _multi0 = new Dictionary<string, PointF>();
            private RectangleF _marquee;
            private float _guideX = -1, _guideY = -1;

            public StudioCanvas(StudioForm f)
            {
                _f = f;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(14, 16, 20);
                AllowDrop = true;
            }

            public void FitToView() { _fit = true; Invalidate(); }

            public void ZoomStep(float factor)
            {
                if (_fit) { _zoom = ComputeScale(); _fit = false; }
                _zoom = Math.Max(0.15f, Math.Min(2.5f, _zoom * factor));
                CenterCanvas();
                Invalidate();
            }

            private void CenterCanvas()
            {
                _panX = (Width - _f._cw * _zoom) / 2f;
                _panY = (Height - _f._ch * _zoom) / 2f;
            }

            private float ComputeScale()
            {
                if (_fit)
                {
                    float aw = Math.Max(80, Width - 48), ah = Math.Max(80, Height - 48);
                    return Math.Max(0.1f, Math.Min(aw / _f._cw, ah / _f._ch));
                }
                return _zoom;
            }

            private void GetViewport(out float ox, out float oy, out float sc)
            {
                sc = ComputeScale();
                if (_fit) { ox = (Width - _f._cw * sc) / 2f; oy = (Height - _f._ch * sc) / 2f; }
                else { ox = _panX; oy = _panY; }
            }

            private static RectangleF[] Handles(RectangleF r)
            {
                float s = 10f, hs = s / 2f, mx = r.X + r.Width / 2f, my = r.Y + r.Height / 2f;
                return new[]
                {
                    new RectangleF(r.Right - hs, r.Bottom - hs, s, s), // 2: BR
                    new RectangleF(r.X - hs, r.Y - hs, s, s),         // 3: TL
                    new RectangleF(r.Right - hs, r.Y - hs, s, s),     // 4: TR
                    new RectangleF(r.X - hs, r.Bottom - hs, s, s),    // 5: BL
                    new RectangleF(r.X - hs, my - hs, s, s),          // 6: L
                    new RectangleF(r.Right - hs, my - hs, s, s),      // 7: R
                    new RectangleF(mx - hs, r.Y - hs, s, s),          // 8: T
                    new RectangleF(mx - hs, r.Bottom - hs, s, s)      // 9: B
                };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                float ox, oy, sc;
                GetViewport(out ox, out oy, out sc);
                int cwPx = Math.Max(16, (int)Math.Round(_f._cw * sc));
                int chPx = Math.Max(16, (int)Math.Round(_f._ch * sc));

                // Drop shadow + live rendered scene
                g.FillRectangle(Brushes.Black, ox + 6, oy + 6, cwPx, chPx);
                var st = g.Save();
                g.SetClip(new RectangleF(ox, oy, cwPx, chPx));
                g.TranslateTransform(ox, oy);
                _f._d.RenderSceneTo(g, cwPx, chPx, _f._wd, _f._cw, _f._ch, _f._th, _f._bgCol, _f._bgImg, _f._gfs, _f._gzoom, _f._topBar, _f._hideStrip, _f._margin, Program.LastSnap);
                g.Restore(st);

                g.TranslateTransform(ox, oy);
                g.ScaleTransform(sc, sc);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (_f._showGrid)
                {
                    float gr = Math.Max(1f, _f._grid), step = gr;
                    while (step * sc < 8f) step += gr;
                    using (var gp = new Pen(Color.FromArgb(26, _f._th.A1), 1f / sc))
                    {
                        for (float x = 0; x <= _f._cw; x += step) g.DrawLine(gp, x, 0, x, _f._ch);
                        for (float y = 0; y <= _f._ch; y += step) g.DrawLine(gp, 0, y, _f._cw, y);
                    }
                }
                using (var bp = new Pen(Color.FromArgb(190, _f._th.A1), 2f / sc))
                    g.DrawRectangle(bp, 0, 0, _f._cw, _f._ch);

                if (_guideX >= 0) using (var gp = new Pen(AccC, 1.5f / sc) { DashStyle = DashStyle.Dash }) g.DrawLine(gp, _guideX, 0, _guideX, _f._ch);
                if (_guideY >= 0) using (var gp = new Pen(AccC, 1.5f / sc) { DashStyle = DashStyle.Dash }) g.DrawLine(gp, 0, _guideY, _f._cw, _guideY);

                foreach (WInst w in _f._wd)
                {
                    if (!w.Vis) continue;
                    bool sel = _f._selIds.Contains(w.Id);
                    using (var wp = new Pen(sel ? AccC : Color.FromArgb(110, _f._th.A1), (sel ? 2.5f : 1.2f) / sc))
                    {
                        if (!sel) wp.DashStyle = DashStyle.Dash;
                        g.DrawRectangle(wp, w.X, w.Y, w.W, w.H);
                    }
                }

                WInst pri = _f.PrimarySel();
                if (pri != null && pri.Vis && !pri.Locked)
                {
                    float hs = 8f / sc, s = 16f / sc;
                    float mx = pri.X + pri.W / 2f, my = pri.Y + pri.H / 2f;
                    RectangleF[] hrs = {
                        new RectangleF(pri.X + pri.W - hs, pri.Y + pri.H - hs, s, s),
                        new RectangleF(pri.X - hs, pri.Y - hs, s, s),
                        new RectangleF(pri.X + pri.W - hs, pri.Y - hs, s, s),
                        new RectangleF(pri.X - hs, pri.Y + pri.H - hs, s, s),
                        new RectangleF(pri.X - hs, my - hs, s, s),
                        new RectangleF(pri.X + pri.W - hs, my - hs, s, s),
                        new RectangleF(mx - hs, pri.Y - hs, s, s),
                        new RectangleF(mx - hs, pri.Y + pri.H - hs, s, s)
                    };
                    using (var br = new SolidBrush(AccC))
                    using (var hp = new Pen(Color.Black, 1.5f / sc))
                    {
                        foreach (RectangleF hr in hrs) { g.FillRectangle(br, hr); g.DrawRectangle(hp, hr.X, hr.Y, hr.Width, hr.Height); }
                    }
                    // Info badge
                    string info = (int)Math.Round(pri.X) + ", " + (int)Math.Round(pri.Y) + "  ·  " + (int)Math.Round(pri.W) + "×" + (int)Math.Round(pri.H);
                    g.ResetTransform();
                    float bx = ox + pri.X * sc, by = oy + pri.Y * sc - 22;
                    if (by < 4) by = oy + (pri.Y + pri.H) * sc + 4;
                    var ir = new RectangleF(bx, by, 148, 20);
                    using (var ib = new SolidBrush(Color.FromArgb(220, 18, 20, 26))) g.FillRectangle(ib, ir);
                    TextRenderer.DrawText(g, info, new Font("Segoe UI", 8f, FontStyle.Bold), Rectangle.Round(ir), AccC, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                    g.TranslateTransform(ox, oy); g.ScaleTransform(sc, sc);
                }

                if (_mode == 11)
                {
                    using (var mb = new SolidBrush(Color.FromArgb(45, AccC))) g.FillRectangle(mb, _marquee);
                    using (var mp = new Pen(AccC, 1.5f / sc) { DashStyle = DashStyle.Dash }) g.DrawRectangle(mp, _marquee.X, _marquee.Y, _marquee.Width, _marquee.Height);
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                float ox, oy, sc; GetViewport(out ox, out oy, out sc);
                float cx = (e.X - ox) / sc, cy = (e.Y - oy) / sc;
                _zoom = Math.Max(0.15f, Math.Min(2.5f, sc * (e.Delta > 0 ? 1.12f : 0.89f)));
                _fit = false;
                _panX = e.X - cx * _zoom;
                _panY = e.Y - cy * _zoom;
                _f.UpdateZoomLabel();
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                float ox, oy, sc; GetViewport(out ox, out oy, out sc);
                float cx = (e.X - ox) / sc, cy = (e.Y - oy) / sc;

                if (e.Button == MouseButtons.Middle)
                {
                    if (_fit) { _zoom = sc; _panX = ox; _panY = oy; _fit = false; }
                    _mode = 10; _mx0 = e.X; _my0 = e.Y; _panX0 = _panX; _panY0 = _panY;
                    return;
                }

                if (e.Button == MouseButtons.Right)
                {
                    ShowContextMenu(cx, cy);
                    return;
                }
                if (e.Button != MouseButtons.Left) return;

                WInst pri = _f.PrimarySel();
                if (pri != null && pri.Vis && !pri.Locked && _f._selIds.Count == 1)
                {
                    RectangleF[] hrs = Handles(new RectangleF(pri.X, pri.Y, pri.W, pri.H));
                    float pad = 10f / sc;
                    for (int i = 0; i < hrs.Length; i++)
                    {
                        if (RectangleF.Inflate(hrs[i], pad, pad).Contains(cx, cy))
                        {
                            _f.PushUndo();
                            _mode = i + 2; _cx0 = cx; _cy0 = cy; _box0 = new RectangleF(pri.X, pri.Y, pri.W, pri.H);
                            return;
                        }
                    }
                }

                WInst hit = null;
                for (int i = _f._wd.Count - 1; i >= 0; i--)
                {
                    WInst w = _f._wd[i];
                    if (w.Vis && new RectangleF(w.X, w.Y, w.W, w.H).Contains(cx, cy)) { hit = w; break; }
                }

                if (hit != null)
                {
                    bool shift = (ModifierKeys & Keys.Shift) != 0;
                    if (shift)
                    {
                        if (_f._selIds.Contains(hit.Id)) _f._selIds.Remove(hit.Id);
                        else _f._selIds.Add(hit.Id);
                    }
                    else if (!_f._selIds.Contains(hit.Id))
                    {
                        _f._selIds.Clear(); _f._selIds.Add(hit.Id);
                    }
                    _f.RefreshLayers(); _f.BindInspector();

                    if (!hit.Locked)
                    {
                        _f.PushUndo();
                        _mode = 1; _cx0 = cx; _cy0 = cy;
                        _multi0.Clear();
                        foreach (string id in _f._selIds)
                        {
                            WInst sw = _f.FindW(id);
                            if (sw != null && !sw.Locked) _multi0[id] = new PointF(sw.X, sw.Y);
                        }
                    }
                    Invalidate();
                    return;
                }

                // Marquee select
                if ((ModifierKeys & Keys.Shift) == 0) _f._selIds.Clear();
                _mode = 11; _cx0 = cx; _cy0 = cy; _marquee = new RectangleF(cx, cy, 0, 0);
                _f.RefreshLayers(); _f.BindInspector();
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                float ox, oy, sc; GetViewport(out ox, out oy, out sc);
                float cx = (e.X - ox) / sc, cy = (e.Y - oy) / sc;

                if (_mode == 10)
                {
                    _panX = _panX0 + (e.X - _mx0); _panY = _panY0 + (e.Y - _my0);
                    Invalidate(); return;
                }
                if (_mode == 11)
                {
                    _marquee = new RectangleF(Math.Min(_cx0, cx), Math.Min(_cy0, cy), Math.Abs(cx - _cx0), Math.Abs(cy - _cy0));
                    Invalidate(); return;
                }
                if (_mode == 1)
                {
                    float dx = cx - _cx0, dy = cy - _cy0;
                    _guideX = _guideY = -1;
                    WInst pri = _f.PrimarySel();
                    PointF p0;
                    if (pri != null && _multi0.TryGetValue(pri.Id, out p0))
                    {
                        float tx = p0.X + dx, ty = p0.Y + dy;
                        float sx = SnapGuideX(pri, tx, pri.W, out _guideX);
                        float sy = SnapGuideY(pri, ty, pri.H, out _guideY);
                        dx = sx - p0.X; dy = sy - p0.Y;
                    }
                    foreach (var kv in _multi0)
                    {
                        WInst w = _f.FindW(kv.Key); if (w == null) continue;
                        w.X = Math.Max(0, Math.Min(_f._cw - w.W, kv.Value.X + dx));
                        w.Y = Math.Max(0, Math.Min(_f._ch - w.H, kv.Value.Y + dy));
                    }
                    Invalidate(); return;
                }
                if (_mode >= 2 && _mode <= 9)
                {
                    WInst w = _f.PrimarySel(); if (w == null) return;
                    WDef d = _f._d.DefOf(w.Id);
                    float mw = DashboardForm.MinWof(d), mh = DashboardForm.MinHof(d);
                    float ddx = cx - _cx0, ddy = cy - _cy0;
                    bool left = _mode == 3 || _mode == 5 || _mode == 6;
                    bool right = _mode == 2 || _mode == 4 || _mode == 7;
                    bool top = _mode == 3 || _mode == 4 || _mode == 8;
                    bool bottom = _mode == 2 || _mode == 5 || _mode == 9;
                    if (right) w.W = Math.Max(mw, Math.Min(_f._cw - _box0.X, _f.SnapVal(_box0.Width + ddx)));
                    if (bottom) w.H = Math.Max(mh, Math.Min(_f._ch - _box0.Y, _f.SnapVal(_box0.Height + ddy)));
                    if (left)
                    {
                        float nx = Math.Max(0, Math.Min(_box0.Right - mw, _f.SnapVal(_box0.X + ddx)));
                        w.W = _box0.Right - nx; w.X = nx;
                    }
                    if (top)
                    {
                        float ny = Math.Max(0, Math.Min(_box0.Bottom - mh, _f.SnapVal(_box0.Y + ddy)));
                        w.H = _box0.Bottom - ny; w.Y = ny;
                    }
                    Invalidate(); return;
                }

                // Hover cursor
                WInst hover = _f.PrimarySel();
                if (hover != null && hover.Vis && !hover.Locked && _f._selIds.Count == 1)
                {
                    RectangleF[] hrs = Handles(new RectangleF(hover.X, hover.Y, hover.W, hover.H));
                    float pad = 10f / sc;
                    Cursor[] curs = { Cursors.SizeNWSE, Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeWE, Cursors.SizeNS, Cursors.SizeNS };
                    for (int i = 0; i < hrs.Length; i++)
                        if (RectangleF.Inflate(hrs[i], pad, pad).Contains(cx, cy)) { Cursor = curs[i]; return; }
                }
                Cursor = Cursors.Default;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (_mode == 11)
                {
                    foreach (WInst w in _f._wd)
                    {
                        if (!w.Vis) continue;
                        if (_marquee.IntersectsWith(new RectangleF(w.X, w.Y, w.W, w.H)) && !_f._selIds.Contains(w.Id))
                            _f._selIds.Add(w.Id);
                    }
                }
                if (_mode != 0 && _mode != 10)
                {
                    _guideX = _guideY = -1;
                    _f.RefreshLayers();
                    _f.BindInspector();
                    _f.ApplyAndRedraw(true);
                }
                _mode = 0;
            }

            private float SnapGuideX(WInst self, float x, float w, out float gx)
            {
                gx = -1;
                if (!_f._snapOn) return (float)Math.Round(x);
                float best = x, dist = 7f, cc = _f._cw / 2f;
                if (Math.Abs((x + w / 2f) - cc) < dist) { dist = Math.Abs((x + w / 2f) - cc); best = cc - w / 2f; gx = cc; }
                foreach (WInst o in _f._wd)
                {
                    if (!o.Vis || _f._selIds.Contains(o.Id)) continue;
                    float[] tp = { o.X, o.X + o.W, o.X + o.W / 2f };
                    float[] sp = { x, x + w, x + w / 2f };
                    foreach (float t in tp)
                        for (int k = 0; k < 3; k++)
                        {
                            float d = Math.Abs(sp[k] - t);
                            if (d < dist) { dist = d; best = x + (t - sp[k]); gx = t; }
                        }
                }
                return gx >= 0 ? best : _f.SnapVal(x);
            }

            private float SnapGuideY(WInst self, float y, float h, out float gy)
            {
                gy = -1;
                if (!_f._snapOn) return (float)Math.Round(y);
                float best = y, dist = 7f, cc = _f._ch / 2f;
                if (Math.Abs((y + h / 2f) - cc) < dist) { dist = Math.Abs((y + h / 2f) - cc); best = cc - h / 2f; gy = cc; }
                foreach (WInst o in _f._wd)
                {
                    if (!o.Vis || _f._selIds.Contains(o.Id)) continue;
                    float[] tp = { o.Y, o.Y + o.H, o.Y + o.H / 2f };
                    float[] sp = { y, y + h, y + h / 2f };
                    foreach (float t in tp)
                        for (int k = 0; k < 3; k++)
                        {
                            float d = Math.Abs(sp[k] - t);
                            if (d < dist) { dist = d; best = y + (t - sp[k]); gy = t; }
                        }
                }
                return gy >= 0 ? best : _f.SnapVal(y);
            }

            private void ShowContextMenu(float cx, float cy)
            {
                WInst hit = null;
                for (int i = _f._wd.Count - 1; i >= 0; i--)
                    if (_f._wd[i].Vis && new RectangleF(_f._wd[i].X, _f._wd[i].Y, _f._wd[i].W, _f._wd[i].H).Contains(cx, cy)) { hit = _f._wd[i]; break; }
                if (hit != null && !_f._selIds.Contains(hit.Id))
                {
                    _f._selIds.Clear(); _f._selIds.Add(hit.Id);
                    _f.RefreshLayers(); _f.BindInspector(); Invalidate();
                }

                var m = new ContextMenuStrip();
                if (_f._selIds.Count > 0)
                {
                    m.Items.Add(L.T("Öne getir", "Bring to Front"), null, delegate { _f.MoveLayer(true); });
                    m.Items.Add(L.T("Arkaya gönder", "Send to Back"), null, delegate { _f.MoveLayer(false); });
                    m.Items.Add(L.T("Çoğalt (Ctrl+D)", "Duplicate (Ctrl+D)"), null, delegate { _f.DuplicateSelected(); });
                    m.Items.Add(L.T("Stili kopyala", "Copy Style"), null, delegate { WInst p = _f.PrimarySel(); if (p != null) _f._clipStyle = p.Copy(); });
                    if (_f._clipStyle != null)
                        m.Items.Add(L.T("Stili yapıştır", "Paste Style"), null, delegate { _f.PushUndo(); foreach (string id in _f._selIds) DashboardForm.CopyStyleFields(_f._clipStyle, _f.FindW(id)); _f.BindInspector(); _f.ApplyAndRedraw(true); });
                    m.Items.Add(L.T("Yatay ortala", "Center Horizontally"), null, delegate { _f.AlignSelected("HC"); });
                    m.Items.Add(L.T("Dikey ortala", "Center Vertically"), null, delegate { _f.AlignSelected("VC"); });
                    m.Items.Add(_f.PrimarySel().Locked ? L.T("Kilidi aç", "Unlock") : L.T("Kilitle", "Lock"), null, delegate { _f.ToggleLockSelected(); });
                    m.Items.Add(L.T("Sil / Gizle (Del)", "Delete / Hide (Del)"), null, delegate { _f.DeleteSelected(); });
                    m.Items.Add(new ToolStripSeparator());
                }
                m.Items.Add(L.T("Tümünü sığdır", "Fit to View"), null, delegate { FitToView(); _f.UpdateZoomLabel(); });
                m.Show(Cursor.Position);
            }

            protected override void OnDragEnter(DragEventArgs drgevent)
            {
                if (drgevent.Data != null && drgevent.Data.GetDataPresent(DataFormats.FileDrop))
                    drgevent.Effect = DragDropEffects.Copy;
            }

            protected override void OnDragDrop(DragEventArgs drgevent)
            {
                try
                {
                    string[] files = drgevent.Data != null ? (drgevent.Data.GetData(DataFormats.FileDrop) as string[]) : null;
                    if (files == null || files.Length == 0) return;
                    Point cp = PointToClient(new Point(drgevent.X, drgevent.Y));
                    float ox, oy, sc; GetViewport(out ox, out oy, out sc);
                    float cx = (cp.X - ox) / sc, cy = (cp.Y - oy) / sc;
                    foreach (string file in files)
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".onmon") { _f._d.ImportAndApplyExternalTemplate(file, 0); _f.ReloadFromDashboard(); break; }
                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
                            _f.AddImageAt(file, cx, cy);
                    }
                }
                catch { }
            }
        }
    }

    internal sealed class SaveTemplateModal : Form
    {
        private readonly TextBox _name, _author, _desc;
        public string TplName { get { return (_name.Text ?? "").Trim(); } }
        public string TplAuthor { get { return (_author.Text ?? "").Trim(); } }
        public string TplDesc { get { return (_desc.Text ?? "").Trim(); } }

        public SaveTemplateModal(DashboardForm d)
        {
            ModernUi.ApplyDarkTitleBar(this);
            Text = L.T("Düzeni Galeriye Kaydet", "Save Layout to Gallery"); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 260);
            BackColor = Color.FromArgb(22, 24, 30); ForeColor = Color.FromArgb(232, 234, 240);
            Font = new Font("Segoe UI", 9.5f);

            EdUi.Lbl(this, L.T("Düzen adı", "Layout Name"), 16, 14, 180, false);
            _name = new TextBox { Left = 16, Top = 38, Width = 384, Text = L.T("Düzenim ", "My Layout ") + DateTime.Now.ToString("dd.MM"), BackColor = EdUi.Card, ForeColor = EdUi.Tx, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_name);

            EdUi.Lbl(this, L.T("Hazırlayan", "Author"), 16, 72, 180, false);
            _author = new TextBox { Left = 16, Top = 96, Width = 384, Text = Environment.UserName, BackColor = EdUi.Card, ForeColor = EdUi.Tx, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_author);

            EdUi.Lbl(this, L.T("Açıklama (isteğe bağlı)", "Description (optional)"), 16, 130, 240, false);
            _desc = new TextBox { Left = 16, Top = 154, Width = 384, Height = 44, Multiline = true, BackColor = EdUi.Card, ForeColor = EdUi.Tx, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_desc);

            var ok = new Button { Text = L.T("Kaydet", "Save"), Left = 220, Top = 214, Width = 86, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 190), ForeColor = Color.White };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += delegate { if (TplName.Length == 0) return; DialogResult = DialogResult.OK; Close(); };
            var cancel = new Button { Text = L.T("İptal", "Cancel"), Left = 314, Top = 214, Width = 86, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = EdUi.Card, ForeColor = EdUi.Tx };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
