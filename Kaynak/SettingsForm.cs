using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>Modern ayarlar ve kontrol merkezi penceresi.</summary>
    internal sealed class SettingsForm : Form
    {
        private const int SidebarW = 248;
        private const int InitPageW = 900;

        private Color _acc;
        private readonly DashboardForm _d;
        private readonly List<Panel> _pages = new List<Panel>();
        private readonly List<SideNode> _sideNodes = new List<SideNode>();
        private readonly List<Control> _accentCtls = new List<Control>();
        private Panel _content, _navScroll, _brandLogo;
        private ModernBadge _verBadge;
        private ModernButton _langTrBtn, _langEnBtn, _viewLangTr, _viewLangEn;
        private TextBox _searchBox;
        private Label _searchHint;
        private ModernButton _searchClear;
        private bool _loading;

        // View / Appearance
        private ListBox _themeList, _layoutList;
        private ModernBadge _themeActiveBadge;
        private ModernSlider _marginSlider;
        private ModernToggle _ckTopBar, _ckRcEdit;

        // Audio & Hotkeys
        private ListView _outList, _micList;
        private ModernMeter _micBarCtl;
        private ModernToggle _micMute;
        private readonly Dictionary<string, TextBox> _hkBoxes = new Dictionary<string, TextBox>();
        private Label _hkStatus;

        // Profiles, Sessions, Fans, Devices, Data, System, Timer, Update, Diag
        private ListView _fanList, _sessList, _profList, _perList;
        private TextBox _profName;
        private ComboBox[] _fanCombo = new ComboBox[4];
        private ComboBox _screenBox, _cityBox;
        private GalleryControl _galleryCtrl;

        private System.Windows.Forms.Timer _tmrTick;
        private Label _tmrStatus;
        private ModernBadge _tmrBadge;
        private RadioButton _rbAt, _rbAfter;
        private ComboBox _atH, _atM, _atS, _afH, _afM, _afS, _tmrAct;
        private ModernToggle _tmrForce;

        private Label _updVer, _updStatus;
        private ModernBadge _updBadge, _updInstalledBadge, _updLatestBadge;
        private TextBox _updNotes;
        private ModernButton _updGo, _updCheck;

        private TextBox _diag;
        private readonly System.Windows.Forms.Timer _t = new System.Windows.Forms.Timer();

        private sealed class SideNode
        {
            public bool IsHeader;
            public int PageIdx;
            public string Keywords;
            public Control Control;
        }

        internal int PageCount { get { return _pages.Count; } }

        public SettingsForm(DashboardForm d)
        {
            _d = d;
            _acc = d._th.A1;
            ModernUi.ApplyDarkTitleBar(this);

            Text = L.T("OnMon — Ayarlar ve Kontrol Merkezi", "OnMon — Settings & Control Center");
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1160, 730);
            MinimumSize = new Size(960, 620);
            BackColor = ModernColors.CanvasBg;
            ForeColor = ModernColors.Text;
            Font = new Font("Segoe UI", 9.5f);
            TopMost = true;
            ShowInTaskbar = false;
            try { Icon = Program.TrayIcon(); } catch { }

            var side = BuildSidebar();
            Controls.Add(side);

            _content = new Panel { Dock = DockStyle.Fill, BackColor = ModernColors.CanvasBg };
            Controls.Add(_content);
            _content.BringToFront();

            // 12 Pages grouped in sidebar
            AddSectionHeader(L.T("KİŞİSELLEŞTİRME", "PERSONALIZATION"));
            AddPage("\u25C8", L.T("Görünüm ve Dil", "Appearance & Language"), "tema theme layout duzen sablon preset dil language turkce english studio fahrenheit celsius 12h 24h night gece", BuildView());
            AddPage("\u25A6", L.T("Düzen Galerisi", "Layout Gallery"), "gallery galeri onmon import export paylas sablon community topluluk", BuildLayoutsPage());

            AddSectionHeader(L.T("SES VE OYUN", "AUDIO & GAMING"));
            AddPage("\u266A", L.T("Ses ve Mikrofon", "Audio & Microphone"), "ses audio mic mikrofon speaker hoparlor kulaklik hotkey kisayol media", BuildAudio());
            AddPage("\u2630", L.T("Ses Profilleri", "Audio Profiles"), "profil profile ses audio game music", BuildProfiles());
            AddPage("\u25B6", L.T("Oyun Oturumları", "Game Sessions"), "oyun game fps csv hud auto session oturum", BuildSessions());

            AddSectionHeader(L.T("DONANIM VE SİSTEM", "HARDWARE & SYSTEM"));
            AddPage("\u273A", L.T("Fan Eşleme", "Fan Mapping"), "fan rpm pump pompa cooling sogutma anakart", BuildFans());
            AddPage("\u26A1", L.T("Pil ve Cihazlar", "Batteries & Devices"), "pil battery laptop bluetooth cetra hid wireless smart disk", BuildDevices());
            AddPage("\u25CE", L.T("Konum ve Uyarılar", "Location & Alerts"), "sehir city hava weather namaz prayer alarm alert sicaklik beep ping ram esik threshold", BuildData());
            AddPage("\u2756", L.T("Pencere ve Sistem", "Window & System"), "monitor ekran display topmost desktop masaustu win+d startup cpu web browser overlay hud tray backup yedek wizard sihirbaz", BuildSystem());

            AddSectionHeader(L.T("ARAÇLAR", "TOOLS"));
            AddPage("\u23F0", L.T("Zamanlayıcı", "Power Timer"), "shutdown restart kapat yeniden baslat zamanlayici timer", BuildTimer());
            AddPage("\u21BB", L.T("Güncelleme (OTA)", "Updates (OTA)"), "update guncelleme ota github version surum changelog", BuildUpdate());
            AddPage("\u2699", L.T("Tanılama", "Diagnostics"), "diag tanilama sensor pawnio log fps hata peak min max wifi", BuildDiag());

            ReflowSidebar("");
            ShowPage(0);

            _t.Interval = 400;
            _t.Tick += delegate { Tick(); };
            _t.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _t.Stop();
            if (_tmrTick != null) _tmrTick.Stop();
            base.OnFormClosed(e);
        }

        // ---------------- Sidebar ----------------
        private Panel BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = SidebarW, BackColor = ModernColors.SidebarBg };
            side.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pn = new Pen(ModernColors.Border, 1f))
                    e.Graphics.DrawLine(pn, side.Width - 1, 0, side.Width - 1, side.Height);
            };

            // Brand header
            var hdr = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = ModernColors.SidebarBg };
            _brandLogo = new Panel { Left = 16, Top = 14, Width = 36, Height = 36, BackColor = ModernColors.SidebarBg };
            _brandLogo.Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(1, 1, 33, 33);
                using (var bg = new SolidBrush(ModernUi.Alpha(_acc, 36))) e.Graphics.FillEllipse(bg, r);
                using (var pn = new Pen(_acc, 2f)) e.Graphics.DrawEllipse(pn, r);
                TextRenderer.DrawText(e.Graphics, "O", new Font("Segoe UI", 12f, FontStyle.Bold), new Rectangle(0, 0, 36, 36), _acc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            hdr.Controls.Add(_brandLogo);
            hdr.Controls.Add(new Label { Text = "OnMon", Left = 60, Top = 11, Width = 78, Height = 24, ForeColor = ModernColors.Text, Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), BackColor = ModernColors.SidebarBg });
            _verBadge = new ModernBadge { Text = "v" + Updater.AppVersion, Left = 140, Top = 13, Width = 46, Height = 21, BadgeColor = _acc };
            _accentCtls.Add(_verBadge);
            hdr.Controls.Add(_verBadge);
            hdr.Controls.Add(new Label { Text = L.T("Kontrol Merkezi", "Control Center"), Left = 60, Top = 36, Width = 170, Height = 18, ForeColor = ModernColors.Dim, Font = new Font("Segoe UI", 8.3f), BackColor = ModernColors.SidebarBg });

            // Search bar
            var searchHost = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = ModernColors.SidebarBg };
            var searchBoxCard = new ModernCard { Left = 12, Top = 4, Width = SidebarW - 25, Height = 34, Radius = 8, CardColor = ModernColors.InputBg, BackColor = ModernColors.InputBg, BorderColor = ModernColors.Border };
            var sIco = new Label { Text = "\u2315", Left = 8, Top = 4, Width = 22, Height = 24, ForeColor = ModernColors.Dim, Font = new Font("Segoe UI Symbol", 11f), BackColor = ModernColors.InputBg, TextAlign = ContentAlignment.MiddleCenter };
            _searchBox = new TextBox
            {
                Left = 32, Top = 8, Width = SidebarW - 86, BorderStyle = BorderStyle.None,
                BackColor = ModernColors.InputBg, ForeColor = ModernColors.Text, Font = new Font("Segoe UI", 9.2f)
            };
            _searchHint = new Label
            {
                Text = L.T("Ayarlarda ara…", "Search settings…"), Left = 32, Top = 7, Width = SidebarW - 86, Height = 20,
                ForeColor = ModernColors.Muted, BackColor = ModernColors.InputBg, Font = new Font("Segoe UI", 9f), Cursor = Cursors.IBeam
            };
            _searchHint.Click += delegate { _searchBox.Focus(); };
            _searchClear = new ModernButton
            {
                Text = "✕", Left = SidebarW - 52, Top = 5, Width = 22, Height = 22, Radius = 5,
                Variant = ModernButtonVariant.Ghost, Font = new Font("Segoe UI", 8f), Visible = false
            };
            _searchClear.Click += delegate { _searchBox.Text = ""; _searchBox.Focus(); };
            _searchBox.TextChanged += delegate
            {
                string q = (_searchBox.Text ?? "").Trim();
                _searchHint.Visible = _searchBox.Text.Length == 0;
                _searchClear.Visible = _searchBox.Text.Length > 0;
                ReflowSidebar(q);
            };
            _searchBox.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    foreach (SideNode sn in _sideNodes)
                    {
                        if (!sn.IsHeader && sn.Control.Visible) { ShowPage(sn.PageIdx); break; }
                    }
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _searchBox.Text = "";
                }
            };
            searchBoxCard.Controls.Add(sIco);
            searchBoxCard.Controls.Add(_searchHint);
            searchBoxCard.Controls.Add(_searchBox);
            searchBoxCard.Controls.Add(_searchClear);
            _searchHint.BringToFront();
            searchHost.Controls.Add(searchBoxCard);

            // Footer with persistent Language switch
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = ModernColors.SidebarBg };
            footer.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pn = new Pen(ModernColors.Divider, 1f))
                    e.Graphics.DrawLine(pn, 12, 0, footer.Width - 13, 0);
            };
            _langTrBtn = new ModernButton
            {
                Text = "Türkçe", Left = 12, Top = 11, Width = 106, Height = 30, Radius = 7,
                Variant = ModernButtonVariant.Secondary, AccentColor = _acc, Checked = !L.IsEn,
                Font = new Font("Segoe UI", 8.8f, !L.IsEn ? FontStyle.Bold : FontStyle.Regular)
            };
            _langTrBtn.Click += delegate { if (L.IsEn) L.Set("tr", _d); };
            _langEnBtn = new ModernButton
            {
                Text = "English", Left = 126, Top = 11, Width = 108, Height = 30, Radius = 7,
                Variant = ModernButtonVariant.Secondary, AccentColor = _acc, Checked = L.IsEn,
                Font = new Font("Segoe UI", 8.8f, L.IsEn ? FontStyle.Bold : FontStyle.Regular)
            };
            _langEnBtn.Click += delegate { if (!L.IsEn) L.Set("en", _d); };
            _accentCtls.Add(_langTrBtn);
            _accentCtls.Add(_langEnBtn);
            footer.Controls.Add(_langTrBtn);
            footer.Controls.Add(_langEnBtn);

            // Scrollable navigation host
            _navScroll = new Panel { Dock = DockStyle.Fill, BackColor = ModernColors.SidebarBg, AutoScroll = true };

            side.Controls.Add(_navScroll);
            side.Controls.Add(footer);
            side.Controls.Add(searchHost);
            side.Controls.Add(hdr);
            _navScroll.BringToFront();
            return side;
        }

        private void AddSectionHeader(string text)
        {
            var lbl = new Label
            {
                Text = text, Left = 18, Width = SidebarW - 36, Height = 22,
                ForeColor = ModernColors.Muted, BackColor = ModernColors.SidebarBg,
                Font = new Font("Segoe UI", 7.8f, FontStyle.Bold)
            };
            _navScroll.Controls.Add(lbl);
            _sideNodes.Add(new SideNode { IsHeader = true, PageIdx = -1, Control = lbl });
        }

        private void AddPage(string glyph, string name, string keywords, Panel page)
        {
            int idx = _pages.Count;
            var nb = new ModernNavButton
            {
                Text = name, IconGlyph = glyph, Keywords = (name + " " + keywords).ToLowerInvariant(),
                AccentColor = _acc, Left = 0, Width = SidebarW - 2, Height = 36
            };
            nb.Click += delegate { ShowPage(idx); };
            _navScroll.Controls.Add(nb);
            _sideNodes.Add(new SideNode { IsHeader = false, PageIdx = idx, Keywords = nb.Keywords, Control = nb });
            _accentCtls.Add(nb);

            page.Visible = false;
            page.BackColor = ModernColors.CanvasBg;
            page.Dock = DockStyle.Fill;
            _content.Controls.Add(page);
            _pages.Add(page);
        }

        private void ReflowSidebar(string query)
        {
            string q = (query ?? "").Trim().ToLowerInvariant();
            bool filtering = q.Length > 0;
            _navScroll.SuspendLayout();
            int y = 4;
            for (int i = 0; i < _sideNodes.Count; i++)
            {
                SideNode sn = _sideNodes[i];
                if (sn.IsHeader)
                {
                    if (filtering) { sn.Control.Visible = false; continue; }
                    if (y > 8) y += 8;
                    sn.Control.Visible = true;
                    sn.Control.Top = y;
                    y += 22;
                }
                else
                {
                    bool vis = !filtering || sn.Keywords.IndexOf(q) >= 0;
                    sn.Control.Visible = vis;
                    if (vis)
                    {
                        sn.Control.Top = y;
                        y += 38;
                    }
                }
            }
            _navScroll.ResumeLayout();
        }

        internal void ShowPage(int i)
        {
            if (i < 0 || i >= _pages.Count) return;
            for (int k = 0; k < _pages.Count; k++)
                _pages[k].Visible = (k == i);

            foreach (SideNode sn in _sideNodes)
            {
                var nb = sn.Control as ModernNavButton;
                if (nb != null) nb.Active = (sn.PageIdx == i);
            }

            if (i == 1 && _galleryCtrl != null) _galleryCtrl.RefreshCards();
            if (i == 2) RefreshAudio();
            if (i == 3) RefreshProfiles();
            if (i == 4) RefreshSessions();
            if (i == 5) RefreshFans();
            if (i == 6) RefreshDevices();
            if (i == 8) RefreshScreens();
            if (i == 9) RefreshTimer();
            if (i == 10) RefreshUpdate();
            if (i == 11) RefreshDiag();
            if (_tmrTick != null) _tmrTick.Enabled = (i == 9);
        }

        private void UpdateAccentColors()
        {
            foreach (Control c in _accentCtls)
            {
                if (c is ModernNavButton) { ((ModernNavButton)c).AccentColor = _acc; }
                else if (c is ModernButton) { ((ModernButton)c).AccentColor = _acc; }
                else if (c is ModernToggle) { ((ModernToggle)c).AccentColor = _acc; }
                else if (c is ModernSlider) { ((ModernSlider)c).AccentColor = _acc; }
                else if (c is ModernMeter) { ((ModernMeter)c).AccentColor = _acc; }
                else if (c is ModernBadge) { ((ModernBadge)c).BadgeColor = _acc; }
            }
            if (_brandLogo != null) _brandLogo.Invalidate();
            if (_tmrBadge != null && ShutdownTimer.Active) _tmrBadge.BadgeColor = _acc;
        }

        // ---------------- Page Building Helpers ----------------
        private Panel NewPage(string title, string subtitle)
        {
            var p = new Panel { Size = new Size(InitPageW, 700), AutoScroll = true, AutoScrollMargin = new Size(0, 22), BackColor = ModernColors.CanvasBg };
            p.Controls.Add(new Label
            {
                Text = title, Left = 26, Top = 18, Width = InitPageW - 68, Height = 30,
                ForeColor = ModernColors.Text, Font = new Font("Segoe UI", 15.5f, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });
            p.Controls.Add(new Label
            {
                Text = subtitle, Left = 26, Top = 49, Width = InitPageW - 68, Height = 20,
                ForeColor = ModernColors.Dim, Font = new Font("Segoe UI", 9.2f),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });
            return p;
        }

        private ModernCard AddCard(Panel page, int top, int height, bool anchorBottom)
        {
            var card = new ModernCard
            {
                Left = 26, Top = top, Width = InitPageW - 68, Height = height,
                Anchor = anchorBottom ? (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right) : (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right)
            };
            page.Controls.Add(card);
            return card;
        }

        private Label CardTitle(Control card, string text, int x, int y, int w)
        {
            var l = new Label
            {
                Text = text, Left = x, Top = y, Width = w, Height = 20,
                ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg,
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold)
            };
            card.Controls.Add(l);
            return l;
        }

        private ModernButton AddBtn(Control parent, string text, int left, int top, int width, ModernButtonVariant variant, EventHandler onClick)
        {
            var b = new ModernButton
            {
                Text = text, Left = left, Top = top, Width = width, Height = 34,
                Variant = variant, AccentColor = _acc
            };
            if (variant == ModernButtonVariant.Primary) _accentCtls.Add(b);
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        private ModernToggle AddToggle(Control card, string title, string subtitle, int top, bool checkedVal, Action<bool> onChanged)
        {
            var t = new ModernToggle
            {
                Title = title, Subtitle = subtitle, Left = 14, Top = top,
                Width = card.Width - 28, Height = string.IsNullOrEmpty(subtitle) ? 42 : 52,
                AccentColor = _acc,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            t.SetCheckedSilent(checkedVal);
            t.CheckedChanged += delegate { if (!_loading) onChanged(t.Checked); };
            _accentCtls.Add(t);
            card.Controls.Add(t);
            return t;
        }

        private static ListView StyledLV(Control parent, int left, int top, int w, int h, bool checks, bool showHeader)
        {
            var lv = new ListView
            {
                Left = left, Top = top, Width = w, Height = h,
                View = View.Details, FullRowSelect = true, CheckBoxes = checks,
                BackColor = ModernColors.CardBg, ForeColor = ModernColors.Text,
                BorderStyle = BorderStyle.None, HideSelection = false, MultiSelect = false,
                HeaderStyle = showHeader ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            if (!checks && showHeader)
            {
                lv.OwnerDraw = true;
                lv.DrawColumnHeader += delegate (object s, DrawListViewColumnHeaderEventArgs e)
                {
                    using (var bg = new SolidBrush(ModernColors.InputBg)) e.Graphics.FillRectangle(bg, e.Bounds);
                    using (var pn = new Pen(ModernColors.Border)) e.Graphics.DrawLine(pn, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    using (var hf = new Font("Segoe UI", 8f, FontStyle.Bold))
                        TextRenderer.DrawText(e.Graphics, (e.Header.Text ?? "").ToUpper(L.Culture), hf,
                            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(4, e.Bounds.Width - 10), e.Bounds.Height),
                            ModernColors.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                };
                lv.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { };
                lv.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e)
                {
                    bool sel = e.Item.Selected;
                    Color bg = sel ? Color.FromArgb(38, 46, 62) : ModernColors.CardBg;
                    using (var br = new SolidBrush(bg)) e.Graphics.FillRectangle(br, e.Bounds);
                    using (var pn = new Pen(ModernColors.Divider)) e.Graphics.DrawLine(pn, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    Color fg = sel ? Color.White : e.Item.ForeColor;
                    if (fg == Color.Empty) fg = ModernColors.Text;
                    TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", lv.Font,
                        new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(4, e.Bounds.Width - 10), e.Bounds.Height),
                        fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                };
            }
            parent.Controls.Add(lv);
            return lv;
        }

        private static ComboBox StyledCombo(Control parent, int left, int top, int width, ComboBoxStyle style)
        {
            var cb = new ComboBox
            {
                Left = left, Top = top, Width = width, DropDownStyle = style,
                BackColor = ModernColors.InputBg, ForeColor = ModernColors.Text, FlatStyle = FlatStyle.Flat
            };
            parent.Controls.Add(cb);
            return cb;
        }

        // ---------------- Page 0: Görünüm ve Dil ----------------
        private Panel BuildView()
        {
            Panel p = NewPage(
                L.T("Görünüm ve Dil", "Appearance & Language"),
                L.T("Tema paletini, uygulama dilini ve yerleşik dashboard düzenlerini özelleştir.", "Customize color themes, application language, and built-in dashboard layouts."));

            // Top quick bar card
            ModernCard topCard = AddCard(p, 80, 62, false);
            var lLang = new Label { Text = L.T("DİL:", "LANG:"), Left = 16, Top = 21, Width = 44, Height = 20, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 8.2f, FontStyle.Bold) };
            topCard.Controls.Add(lLang);
            _viewLangTr = new ModernButton { Text = "Türkçe", Left = 64, Top = 14, Width = 86, Height = 34, Variant = ModernButtonVariant.Secondary, AccentColor = _acc, Checked = !L.IsEn };
            _viewLangTr.Click += delegate { if (L.IsEn) L.Set("tr", _d); };
            _viewLangEn = new ModernButton { Text = "English", Left = 156, Top = 14, Width = 86, Height = 34, Variant = ModernButtonVariant.Secondary, AccentColor = _acc, Checked = L.IsEn };
            _viewLangEn.Click += delegate { if (!L.IsEn) L.Set("en", _d); };
            _accentCtls.Add(_viewLangTr);
            _accentCtls.Add(_viewLangEn);
            topCard.Controls.Add(_viewLangTr);
            topCard.Controls.Add(_viewLangEn);

            var vsep = new Panel { Left = 256, Top = 14, Width = 1, Height = 34, BackColor = ModernColors.Divider };
            topCard.Controls.Add(vsep);

            var bStu = AddBtn(topCard, L.T("🎨 Düzen Stüdyosu", "🎨 Layout Studio"), 272, 14, 174, ModernButtonVariant.Primary, delegate { _d.OpenStudio(); Close(); });
            var bGal = AddBtn(topCard, L.T("⊞ Görsel Galeri", "⊞ Layout Gallery"), 454, 14, 158, ModernButtonVariant.Secondary, delegate { ShowPage(1); });
            var bEd = AddBtn(topCard, L.T("✎ Hızlı Düzenle (E)", "✎ Quick Edit (E)"), 620, 14, 174, ModernButtonVariant.Secondary, delegate { _d.ToggleEdit(); Close(); });
            topCard.SizeChanged += delegate
            {
                int avail = Math.Max(200, topCard.Width - 284);
                int gap = 8;
                int bw = (avail - gap * 2) / 3;
                if (bw > 176) bw = 176;
                bStu.Left = 270; bStu.Width = bw;
                bGal.Left = bStu.Right + gap; bGal.Width = bw;
                bEd.Left = bGal.Right + gap; bEd.Width = bw;
            };

            // Split container for Layouts (left) + Themes (right)
            var split = new Panel
            {
                Left = 26, Top = 154, Width = InitPageW - 68, Height = 410,
                BackColor = ModernColors.CanvasBg,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            p.Controls.Add(split);

            var leftCard = new ModernCard { Left = 0, Top = 0, Width = 366, Height = 410 };
            var rightCard = new ModernCard { Left = 380, Top = 0, Width = 452, Height = 410 };
            split.Controls.Add(leftCard);
            split.Controls.Add(rightCard);

            // Units, Clock & Night Mode card below split
            ModernCard cPref = AddCard(p, 576, 206, false);
            CardTitle(cPref, L.T("BİRİMLER, SAAT VE OTOMATİK GECE MODU", "UNITS, CLOCK & AUTO NIGHT MODE"), 16, 12, 520);

            int colW = (cPref.Width - 42) / 2;
            var ckTempF = new ModernToggle
            {
                Title = L.T("Fahrenheit (°F) kullan", "Use Fahrenheit (°F)"),
                Subtitle = L.T("Açıkken sıcaklıklar °F olarak gösterilir", "Displays all temperatures in °F"),
                Left = 14, Top = 36, Width = colW, Height = 46, AccentColor = _acc
            };
            ckTempF.SetCheckedSilent(AppPrefs.TempFahrenheit);
            ckTempF.CheckedChanged += delegate { if (!_loading) { AppPrefs.TempFahrenheit = ckTempF.Checked; _d.SaveSettings(); _d.Invalidate(); Program.RebuildTrayMenu(); } };
            _accentCtls.Add(ckTempF);
            cPref.Controls.Add(ckTempF);

            var ckClock12 = new ModernToggle
            {
                Title = L.T("12 saatlik (AM/PM) biçimi", "12-hour (AM/PM) clock"),
                Subtitle = L.T("Saat widget ve bilgi çubuğu biçimi", "Applies to clock widget & info bar"),
                Left = 28 + colW, Top = 36, Width = colW, Height = 46, AccentColor = _acc
            };
            ckClock12.SetCheckedSilent(AppPrefs.Clock12h);
            ckClock12.CheckedChanged += delegate { if (!_loading) { AppPrefs.Clock12h = ckClock12.Checked; _d.SaveSettings(); _d.Invalidate(); } };
            _accentCtls.Add(ckClock12);
            cPref.Controls.Add(ckClock12);

            var ckPrayer = new ModernToggle
            {
                Title = L.T("Namaz vakitlerini göster", "Show Prayer Times"),
                Subtitle = L.T("Bilgi çubuğunda ve Saat kartında vakitler", "Prayer schedule on clock & info bar"),
                Left = 14, Top = 86, Width = colW, Height = 46, AccentColor = _acc
            };
            ckPrayer.SetCheckedSilent(AppPrefs.ShowPrayer);
            ckPrayer.CheckedChanged += delegate { if (!_loading) { AppPrefs.ShowPrayer = ckPrayer.Checked; _d.SaveSettings(); _d.Invalidate(); } };
            _accentCtls.Add(ckPrayer);
            cPref.Controls.Add(ckPrayer);

            var ckNight = new ModernToggle
            {
                Title = L.T("Otomatik Gece Modu", "Auto Night Mode"),
                Subtitle = L.T("Belirlenen saatlerde parlaklığı otomatik kısar", "Automatically dims dashboard at night hours"),
                Left = 28 + colW, Top = 86, Width = colW, Height = 46, AccentColor = _acc
            };
            ckNight.SetCheckedSilent(AppPrefs.NightMode);
            ckNight.CheckedChanged += delegate { if (!_loading) { AppPrefs.NightMode = ckNight.Checked; _d.SaveSettings(); _d.Invalidate(); } };
            _accentCtls.Add(ckNight);
            cPref.Controls.Add(ckNight);

            var lNs = new Label { Text = L.T("Başlangıç:", "Start:"), Left = 16, Top = 152, Width = 72, Height = 22, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg };
            cPref.Controls.Add(lNs);
            ComboBox cbNs = StyledCombo(cPref, 90, 148, 72, ComboBoxStyle.DropDownList);
            cbNs.MaxDropDownItems = 10; cbNs.IntegralHeight = false; cbNs.DropDownHeight = 200;
            var lNe = new Label { Text = L.T("Bitiş:", "End:"), Left = 176, Top = 152, Width = 50, Height = 22, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg };
            cPref.Controls.Add(lNe);
            ComboBox cbNe = StyledCombo(cPref, 228, 148, 72, ComboBoxStyle.DropDownList);
            cbNe.MaxDropDownItems = 10; cbNe.IntegralHeight = false; cbNe.DropDownHeight = 200;
            for (int h = 0; h < 24; h++) { string hh = h.ToString("00") + ":00"; cbNs.Items.Add(hh); cbNe.Items.Add(hh); }
            cbNs.SelectedIndex = Math.Max(0, Math.Min(23, AppPrefs.NightStartHour));
            cbNe.SelectedIndex = Math.Max(0, Math.Min(23, AppPrefs.NightEndHour));
            cbNs.SelectedIndexChanged += delegate { if (!_loading) { AppPrefs.NightStartHour = cbNs.SelectedIndex; _d.SaveSettings(); _d.Invalidate(); } };
            cbNe.SelectedIndexChanged += delegate { if (!_loading) { AppPrefs.NightEndHour = cbNe.SelectedIndex; _d.SaveSettings(); _d.Invalidate(); } };

            var slNightBr = new ModernSlider
            {
                Label = L.T("GECE PARLAKLIĞI", "NIGHT BRIGHTNESS"), Suffix = "%",
                Minimum = 15, Maximum = 95, Left = 324, Top = 142, Width = cPref.Width - 340, Height = 44,
                AccentColor = _acc
            };
            slNightBr.SetValueSilent(Math.Max(15, Math.Min(95, AppPrefs.NightBright)));
            slNightBr.ValueChanged += delegate { if (!_loading) { AppPrefs.NightBright = slNightBr.Value; _d.SaveSettings(); if (AppPrefs.IsNightNow()) _d.Invalidate(); } };
            _accentCtls.Add(slNightBr);
            cPref.Controls.Add(slNightBr);

            cPref.SizeChanged += delegate
            {
                int cw2 = Math.Max(200, (cPref.Width - 42) / 2);
                ckTempF.Width = cw2; ckPrayer.Width = cw2;
                ckClock12.Left = 28 + cw2; ckClock12.Width = cw2;
                ckNight.Left = 28 + cw2; ckNight.Width = cw2;
                slNightBr.Width = Math.Max(160, cPref.Width - 340);
            };

            split.SizeChanged += delegate
            {
                int gap = 14;
                int lw = Math.Max(310, (int)(split.Width * 0.45));
                int rw = Math.Max(300, split.Width - lw - gap);
                leftCard.Bounds = new Rectangle(0, 0, lw, split.Height);
                rightCard.Bounds = new Rectangle(lw + gap, 0, rw, split.Height);
            };

            // Left card contents: Layouts + Margin + Toggles
            CardTitle(leftCard, L.T("YERLEŞİK DÜZENLER (13 ŞABLON)", "BUILT-IN LAYOUTS (13 PRESETS)"), 16, 14, 330);
            _layoutList = new ListBox
            {
                Left = 14, Top = 38, Width = leftCard.Width - 28, Height = leftCard.Height - 232,
                BackColor = ModernColors.CardBg, ForeColor = ModernColors.Text, BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 44, IntegralHeight = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            string[] ids = {
                "zen", "minimalring", "bento", "esports", "extreme", "creator", "panoramic", "vertical",
                "classic", "hud", "cockpit", "aida", "studio"
            };
            foreach (string id in ids) _layoutList.Items.Add(id);
            _layoutList.DrawItem += delegate (object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var cb = new SolidBrush(ModernColors.CardBg)) g.FillRectangle(cb, e.Bounds);
                if (sel)
                {
                    using (GraphicsPath gp = ModernUi.RoundRect(new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4), 8))
                    using (var sb = new SolidBrush(ModernUi.Alpha(_acc, 28))) g.FillPath(sb, gp);
                    using (GraphicsPath bar = ModernUi.RoundRect(new RectangleF(e.Bounds.X + 4, e.Bounds.Y + 9, 3.5f, e.Bounds.Height - 18), 2))
                    using (var ab = new SolidBrush(_acc)) g.FillPath(ab, bar);
                }
                string id = (string)_layoutList.Items[e.Index];
                string nm = DashboardForm.GetTplName(id);
                string desc = DashboardForm.GetTplDesc(id);
                using (var fn = new Font("Segoe UI", 9.6f, sel ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(g, nm, fn, new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 4, e.Bounds.Width - 20, 20), sel ? _acc : ModernColors.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                using (var fd = new Font("Segoe UI", 8.2f))
                    TextRenderer.DrawText(g, desc, fd, new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 23, e.Bounds.Width - 20, 17), ModernColors.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            _layoutList.SelectedIndexChanged += delegate
            {
                if (!_loading && _layoutList.SelectedIndex >= 0) _d.SetLayout(ids[_layoutList.SelectedIndex]);
                _layoutList.Invalidate();
            };
            leftCard.Controls.Add(_layoutList);

            int botY = leftCard.Height - 186;
            var divL = new Panel { Left = 14, Top = botY, Width = leftCard.Width - 28, Height = 1, BackColor = ModernColors.Divider, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            leftCard.Controls.Add(divL);

            _marginSlider = new ModernSlider
            {
                Label = L.T("KENAR BOŞLUĞU", "OUTER MARGIN"), Suffix = " px",
                Minimum = 0, Maximum = 48, Left = 14, Top = botY + 8, Width = leftCard.Width - 28, Height = 44,
                AccentColor = _acc, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _marginSlider.SetValueSilent(Math.Max(0, Math.Min(48, _d._margin)));
            _marginSlider.ValueChanged += delegate { if (!_loading) { _d._margin = _marginSlider.Value; _d.SaveSettings(); _d.Invalidate(); } };
            _accentCtls.Add(_marginSlider);
            leftCard.Controls.Add(_marginSlider);

            _ckTopBar = new ModernToggle
            {
                Title = L.T("Alt bilgi çubuğunu göster", "Show bottom info bar"),
                Subtitle = L.T("Marka, pil, hava durumu, vakit ve saat", "Brand, battery, weather, and clock"),
                Left = 8, Top = botY + 56, Width = leftCard.Width - 16, Height = 48,
                AccentColor = _acc, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _ckTopBar.SetCheckedSilent(_d._topBar);
            _ckTopBar.CheckedChanged += delegate { if (!_loading) { _d._topBar = _ckTopBar.Checked; _d.SaveSettings(); _d.Invalidate(); } };
            _accentCtls.Add(_ckTopBar);
            leftCard.Controls.Add(_ckTopBar);

            _ckRcEdit = new ModernToggle
            {
                Title = L.T("Sağ tıkla hızlı düzenleme", "Right-click opens quick edit"),
                Subtitle = L.T("Kapalıyken sağ tık menüsü açılır", "Opens context menu when off"),
                Left = 8, Top = botY + 106, Width = leftCard.Width - 16, Height = 48,
                AccentColor = _acc, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _ckRcEdit.SetCheckedSilent(_d._rcEdit);
            _ckRcEdit.CheckedChanged += delegate { if (!_loading) { _d._rcEdit = _ckRcEdit.Checked; _d.SaveSettings(); } };
            _accentCtls.Add(_ckRcEdit);
            leftCard.Controls.Add(_ckRcEdit);

            // Right card contents: Themes
            CardTitle(rightCard, L.T("TEMA PALETLERİ (21 SEÇENEK)", "COLOR THEMES (21 PRESETS)"), 16, 14, 240);
            _themeActiveBadge = new ModernBadge
            {
                Text = _d._th.Title, Left = rightCard.Width - 170, Top = 10, Width = 154, Height = 22,
                BadgeColor = _acc, Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _accentCtls.Add(_themeActiveBadge);
            rightCard.Controls.Add(_themeActiveBadge);

            _themeList = new ListBox
            {
                Left = 14, Top = 38, Width = rightCard.Width - 28, Height = rightCard.Height - 52,
                BackColor = ModernColors.CardBg, ForeColor = ModernColors.Text, BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 42, IntegralHeight = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            foreach (Theme t in Themes.All) _themeList.Items.Add(t.Title);
            _themeList.DrawItem += delegate (object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Theme t = Themes.All[e.Index];
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var cb = new SolidBrush(ModernColors.CardBg)) g.FillRectangle(cb, e.Bounds);
                if (sel)
                {
                    using (GraphicsPath gp = ModernUi.RoundRect(new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4), 8))
                    using (var sb = new SolidBrush(ModernUi.Alpha(_acc, 26))) g.FillPath(sb, gp);
                    using (GraphicsPath bar = ModernUi.RoundRect(new RectangleF(e.Bounds.X + 4, e.Bounds.Y + 9, 3.5f, e.Bounds.Height - 18), 2))
                    using (var ab = new SolidBrush(_acc)) g.FillPath(ab, bar);
                }

                int sx = e.Bounds.X + 14, sy = e.Bounds.Y + 9;
                Color[] sw = { t.Bg, t.Panel, t.A1, t.A2 };
                for (int i = 0; i < 4; i++)
                {
                    using (var br = new SolidBrush(sw[i])) g.FillRectangle(br, sx + i * 21, sy, 21, 22);
                }
                using (var pn = new Pen(ModernColors.Border, 1f)) g.DrawRectangle(pn, sx, sy, 84, 22);

                using (var ft = new Font("Segoe UI", 9.6f, sel ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(g, t.Title, ft, new Rectangle(sx + 98, e.Bounds.Y, Math.Max(40, e.Bounds.Width - sx - 180), e.Bounds.Height), sel ? _acc : ModernColors.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                using (var fs = new Font("Segoe UI", 7.8f, FontStyle.Bold))
                    TextRenderer.DrawText(g, (t.Style ?? "flat").ToUpperInvariant(), fs, new Rectangle(e.Bounds.Right - 84, e.Bounds.Y, 72, e.Bounds.Height), ModernColors.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
            _themeList.SelectedIndexChanged += delegate
            {
                if (!_loading && _themeList.SelectedIndex >= 0)
                {
                    Theme chosen = Themes.All[_themeList.SelectedIndex];
                    _d.SetTheme(chosen);
                    _acc = _d._th.A1;
                    if (_themeActiveBadge != null) _themeActiveBadge.Text = chosen.Title;
                    UpdateAccentColors();
                    _themeList.Invalidate();
                    _layoutList.Invalidate();
                }
            };
            rightCard.Controls.Add(_themeList);

            _loading = true;
            int li = Array.IndexOf(ids, _d._layout);
            if (li >= 0) _layoutList.SelectedIndex = li;
            _themeList.SelectedIndex = Math.Max(0, Array.IndexOf(Themes.All, _d._th));
            _loading = false;
            return p;
        }

        // ---------------- Page 1: Düzen Galerisi ----------------
        private Panel BuildLayoutsPage()
        {
            var p = new Panel { Size = new Size(InitPageW, 700), BackColor = ModernColors.CanvasBg };
            var hdr = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = ModernColors.CanvasBg };
            hdr.Controls.Add(new Label { Text = L.T("Düzen Galerisi ve Paylaşım", "Layout Gallery & Sharing"), Left = 26, Top = 14, Width = 600, Height = 28, ForeColor = ModernColors.Text, Font = new Font("Segoe UI", 15.5f, FontStyle.Bold) });
            hdr.Controls.Add(new Label { Text = L.T("Kategoriye göre filtrele, .onmon düzenlerini sürükle-bırak ile ekle veya dışa aktar.", "Filter by category, drag-and-drop .onmon files to import, or share your own layouts."), Left = 26, Top = 42, Width = 760, Height = 18, ForeColor = ModernColors.Dim, Font = new Font("Segoe UI", 9f) });
            p.Controls.Add(hdr);
            _galleryCtrl = new GalleryControl(_d) { Dock = DockStyle.Fill };
            p.Controls.Add(_galleryCtrl);
            _galleryCtrl.BringToFront();
            return p;
        }

        // ---------------- Page 2: Ses ve Mikrofon ----------------
        private Panel BuildAudio()
        {
            Panel p = NewPage(
                L.T("Ses ve Mikrofon", "Audio & Microphone"),
                L.T("Favori çıkış/giriş cihazlarını seç, alt ses şeridini ve global kısayolları yapılandır.", "Choose favorite playback/recording devices, configure bottom strip, and set global hotkeys."));

            // Card 1: Output Devices
            ModernCard cOut = AddCard(p, 80, 186, false);
            CardTitle(cOut, L.T("ÇIKIŞ CİHAZLARI — işaretlediklerin alt çubukta ve hızlı geçişte görünür", "OUTPUT DEVICES — checked items appear in the bottom strip & hotkey cycle"), 16, 12, cOut.Width - 32);
            _outList = StyledLV(cOut, 16, 36, cOut.Width - 32, 98, true, false);
            _outList.Columns.Add("", 36); _outList.Columns.Add(L.T("Cihaz", "Device"), 560); _outList.Columns.Add(L.T("Durum", "Status"), 140);
            _outList.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
            {
                if (_loading) return;
                string id = e.Item.Tag as string;
                if (e.Item.Checked) _d._favs.Add(id); else _d._favs.Remove(id);
                _d.SaveSettings(); _d.Invalidate();
            };
            _outList.DoubleClick += delegate { SetDefaultFrom(_outList, true); };
            AddBtn(cOut, L.T("Varsayılan Yap", "Set Default"), 16, 142, 150, ModernButtonVariant.Primary, delegate { SetDefaultFrom(_outList, true); });
            AddBtn(cOut, L.T("Test Sesi Çal", "Play Test Sound"), 176, 142, 156, ModernButtonVariant.Secondary, delegate { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } });

            // Card 2: Mic Devices
            ModernCard cMic = AddCard(p, 278, 194, false);
            CardTitle(cMic, L.T("MİKROFON / GİRİŞ CİHAZLARI", "MICROPHONE / INPUT DEVICES"), 16, 12, cMic.Width - 32);
            _micList = StyledLV(cMic, 16, 36, cMic.Width - 32, 86, true, false);
            _micList.Columns.Add("", 36); _micList.Columns.Add(L.T("Cihaz", "Device"), 560); _micList.Columns.Add(L.T("Durum", "Status"), 140);
            _micList.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
            {
                if (_loading) return;
                string id = e.Item.Tag as string;
                if (e.Item.Checked) _d._micFavs.Add(id); else _d._micFavs.Remove(id);
                _d.SaveSettings(); _d.Invalidate();
            };
            _micList.DoubleClick += delegate { SetDefaultFrom(_micList, false); };
            AddBtn(cMic, L.T("Varsayılan Yap", "Set Default"), 16, 136, 150, ModernButtonVariant.Secondary, delegate { SetDefaultFrom(_micList, false); });
            _micMute = new ModernToggle
            {
                Title = L.T("Mikrofonu sustur", "Mute microphone"), Left = 180, Top = 132, Width = 210, Height = 40, AccentColor = _acc
            };
            _micMute.CheckedChanged += delegate { if (!_loading && _micMute.Checked != AudioSwitch.MicMuted) _d.ToggleMic(); };
            _accentCtls.Add(_micMute);
            cMic.Controls.Add(_micMute);
            cMic.Controls.Add(new Label { Text = L.T("Seviye", "Level"), Left = 406, Top = 143, Width = 54, Height = 20, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 8.8f) });
            _micBarCtl = new ModernMeter { Left = 464, Top = 147, Width = cMic.Width - 482, Height = 12, AccentColor = _acc, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _accentCtls.Add(_micBarCtl);
            cMic.Controls.Add(_micBarCtl);

            // Card 3: Bottom Strip
            ModernCard cStrip = AddCard(p, 484, 154, false);
            CardTitle(cStrip, L.T("ALT SES ŞERİDİ", "BOTTOM AUDIO STRIP"), 16, 12, 300);
            var t1 = AddToggle(cStrip, L.T("Ses çıkış cihazlarını göster", "Show audio output device pills"), null, 38, _d._audioBar, v => { _d._audioBar = v; _d.SaveSettings(); _d.Invalidate(); });
            t1.Left = 14; t1.Width = (cStrip.Width - 36) / 2; t1.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            var t2 = AddToggle(cStrip, L.T("Mikrofon göstergesini göster", "Show microphone indicator"), null, 38, _d._micBar, v => { _d._micBar = v; _d.SaveSettings(); _d.Invalidate(); });
            t2.Left = 18 + t1.Width; t2.Width = t1.Width; t2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var t3 = AddToggle(cStrip, L.T("Ses seviyesi kaydırıcısı", "Show master volume slider"), null, 90, _d._volBar, v => { _d._volBar = v; _d.SaveSettings(); _d.Invalidate(); });
            t3.Left = 14; t3.Width = t1.Width; t3.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            var t4 = AddToggle(cStrip, L.T("Alt şeridi tamamen gizle (B tuşu)", "Hide bottom strip (B key)"), null, 90, _d._hideStrip, v => { _d._hideStrip = v; _d.SaveSettings(); _d.Invalidate(); });
            t4.Left = 18 + t1.Width; t4.Width = t1.Width; t4.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // Card 4: Global Hotkeys
            ModernCard cHk = AddCard(p, 650, 290, false);
            CardTitle(cHk, L.T("GLOBAL KISAYOL TUŞLARI — kutuya tıkla ve tuş kombinasyonuna bas", "GLOBAL HOTKEYS — click a box and press your key combination"), 16, 12, cHk.Width - 32);
            for (int i = 0; i < Hotkeys.Ids.Length; i++)
            {
                string id = Hotkeys.Ids[i];
                int ry = 42 + i * 42;
                cHk.Controls.Add(new Label { Text = Hotkeys.GetName(i), Left = 16, Top = ry + 6, Width = cHk.Width - 340, Height = 24, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 9.3f), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right });
                var tbHost = new ModernCard { Left = cHk.Width - 314, Top = ry + 1, Width = 200, Height = 32, Radius = 7, CardColor = ModernColors.InputBg, BackColor = ModernColors.InputBg, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                var tb = new TextBox
                {
                    Left = 8, Top = 7, Width = 184, ReadOnly = true, BackColor = ModernColors.InputBg, ForeColor = _acc,
                    BorderStyle = BorderStyle.None, ShortcutsEnabled = false, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), TextAlign = HorizontalAlignment.Center
                };
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
                tbHost.Controls.Add(tb);
                cHk.Controls.Add(tbHost);
                var bClr = AddBtn(cHk, L.T("Temizle", "Clear"), cHk.Width - 104, ry, 88, ModernButtonVariant.Ghost, delegate { Hotkeys.Set(cid, 0, 0); tb.Text = ""; _hkStatus.Text = Hotkeys.Status; });
                bClr.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }
            _hkStatus = new Label { Text = Hotkeys.Status, Left = 16, Top = 256, Width = cHk.Width - 32, Height = 22, ForeColor = ModernColors.Warn, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 9f), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            cHk.Controls.Add(_hkStatus);

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
                string defLbl = L.T("● Varsayılan", "● Default");
                _outList.Items.Clear();
                foreach (AudioDevice a in AudioSwitch.Devices)
                {
                    var it = new ListViewItem("") { Tag = a.Id, Checked = _d._favs.Contains(a.Id) };
                    it.SubItems.Add(a.Name); it.SubItems.Add(a.IsDefault ? defLbl : "");
                    if (a.IsDefault) it.ForeColor = _acc;
                    _outList.Items.Add(it);
                }
                _micList.Items.Clear();
                foreach (AudioDevice a in AudioSwitch.MicDevices)
                {
                    var it = new ListViewItem("") { Tag = a.Id, Checked = _d._micFavs.Contains(a.Id) };
                    it.SubItems.Add(a.Name); it.SubItems.Add(a.IsDefault ? defLbl : "");
                    if (a.IsDefault) it.ForeColor = _acc;
                    _micList.Items.Add(it);
                }
                AudioSwitch.PollMic();
                if (_micMute != null) _micMute.SetCheckedSilent(AudioSwitch.MicMuted);
            }
            finally { _loading = false; }
        }

        // ---------------- Page 3: Ses Profilleri ----------------
        private Panel BuildProfiles()
        {
            Panel p = NewPage(
                L.T("Ses Profilleri", "Audio Profiles"),
                L.T("Çıkış cihazı + ses seviyesi + mikrofon durumunu tek profillere kaydet; tek tıkla veya P tuşuyla geçiş yap.", "Save output device + volume + mic state as presets and switch with one click or the P key."));

            ModernCard cList = AddCard(p, 80, 326, false);
            CardTitle(cList, L.T("KAYITLI SES PROFİLLERİ", "SAVED AUDIO PROFILES"), 16, 12, 300);
            _profList = StyledLV(cList, 16, 38, cList.Width - 32, 224, false, true);
            _profList.Columns.Add(L.T("Profil", "Profile"), 160); _profList.Columns.Add(L.T("Çıkış cihazı", "Output Device"), 380); _profList.Columns.Add(L.T("Ses", "Vol"), 80); _profList.Columns.Add(L.T("Mikrofon", "Mic"), 110);
            AddBtn(cList, L.T("Seçileni Uygula", "Apply Selected"), 16, 276, 166, ModernButtonVariant.Primary, delegate
            {
                if (_profList.SelectedIndices.Count == 0) return;
                int i = _profList.SelectedIndices[0];
                if (i < AudioProfiles.All.Count) _d.ApplyProfile(AudioProfiles.All[i]);
            });
            AddBtn(cList, L.T("Seçileni Sil", "Delete Selected"), 192, 276, 150, ModernButtonVariant.Danger, delegate
            {
                if (_profList.SelectedIndices.Count == 0) return;
                int i = _profList.SelectedIndices[0];
                if (i < AudioProfiles.All.Count) { AudioProfiles.Remove(AudioProfiles.All[i].Name); RefreshProfiles(); }
            });

            ModernCard cNew = AddCard(p, 418, 128, false);
            CardTitle(cNew, L.T("MEVCUT SES AYARINDAN YENİ PROFİL OLUŞTUR", "CREATE PROFILE FROM CURRENT AUDIO STATE"), 16, 12, cNew.Width - 32);
            cNew.Controls.Add(new Label { Text = L.T("Profil adı:", "Profile name:"), Left = 16, Top = 46, Width = 96, Height = 22, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg });
            var pnHost = new ModernCard { Left = 118, Top = 40, Width = 280, Height = 34, Radius = 7, CardColor = ModernColors.InputBg, BackColor = ModernColors.InputBg };
            _profName = new TextBox { Left = 10, Top = 8, Width = 260, BorderStyle = BorderStyle.None, BackColor = ModernColors.InputBg, ForeColor = ModernColors.Text, Font = new Font("Segoe UI", 9.5f) };
            pnHost.Controls.Add(_profName);
            cNew.Controls.Add(pnHost);
            AddBtn(cNew, L.T("Şu Anki Ayarı Kaydet", "Save Current State"), 410, 40, 200, ModernButtonVariant.Primary, delegate
            {
                string n = (_profName.Text ?? "").Trim();
                if (n.Length == 0) return;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    AudioProfiles.CaptureCurrent(n);
                    try { BeginInvoke((MethodInvoker)delegate { RefreshProfiles(); _profName.Text = ""; }); } catch { }
                });
            });
            cNew.Controls.Add(new Label { Text = L.T("İpucu: Kulaklığı seç, sesi %40 yap → \"Oyun\" kaydet. Hoparlöre geç → \"Müzik\" kaydet.", "Tip: Select headset & 40% volume → save as \"Gaming\". Switch to speakers → save as \"Music\"."), Left = 16, Top = 90, Width = cNew.Width - 32, Height = 24, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 8.8f) });
            return p;
        }

        private void RefreshProfiles()
        {
            AudioProfiles.Load();
            _profList.BeginUpdate(); _profList.Items.Clear();
            foreach (AudioProfile ap in AudioProfiles.All)
                _profList.Items.Add(new ListViewItem(new[] { ap.Name, ap.OutName, ap.Volume >= 0 ? ap.Volume + "%" : "-", ap.MicMute < 0 ? "-" : (ap.MicMute == 1 ? L.T("Kapalı", "Muted") : L.T("Açık", "Live")) }));
            _profList.EndUpdate();
        }

        // ---------------- Page 4: Oyun Oturumları ----------------
        private Panel BuildSessions()
        {
            Panel p = NewPage(
                L.T("Oyun Oturumları", "Game Sessions"),
                L.T("1 dakikadan uzun oyun oturumları ortalama FPS, %1 düşük, zirve sıcaklık ve güçle otomatik kaydedilir.", "Game sessions longer than 1 minute are logged with Avg FPS, 1% Low, peak temperatures, and power."));

            ModernCard cOpt = AddCard(p, 80, 68, false);
            AddToggle(cOpt,
                L.T("Oyun algılanınca otomatik Oyun HUD düzenine geç", "Auto-switch to Gaming HUD when a game launches"),
                L.T("Oyun kapandığında önceki düzenine geri döner", "Restores your previous layout after exiting the game"),
                10, _d._autoGame, v => { _d._autoGame = v; _d.SaveSettings(); });

            ModernCard cTbl = AddCard(p, 160, 520, true);
            CardTitle(cTbl, L.T("OTURUM GEÇMİŞİ", "SESSION HISTORY"), 16, 12, 300);
            _sessList = StyledLV(cTbl, 16, 38, cTbl.Width - 32, cTbl.Height - 104, false, true);
            _sessList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            string[] cols = L.IsEn
                ? new[] { "Start", "Game", "Min", "Avg FPS", "1% Low", "Min FPS", "CPU °C", "GPU °C", "Power W" }
                : new[] { "Başlangıç", "Oyun", "Süre dk", "Ort FPS", "%1 Düşük", "Min FPS", "CPU °C", "GPU °C", "Güç W" };
            int[] ws = { 130, 170, 64, 72, 82, 72, 64, 64, 66 };
            for (int i = 0; i < cols.Length; i++) _sessList.Columns.Add(cols[i], ws[i]);

            var bRef = AddBtn(cTbl, L.T("Yenile", "Refresh"), 16, cTbl.Height - 50, 120, ModernButtonVariant.Secondary, delegate { RefreshSessions(); });
            bRef.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            var bCsv = AddBtn(cTbl, L.T("Kayıt Dosyasını Aç (CSV)", "Open CSV Log"), 146, cTbl.Height - 50, 200, ModernButtonVariant.Secondary, delegate { try { if (File.Exists(GameSession.CsvPath)) Process.Start(GameSession.CsvPath); } catch { } });
            bCsv.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            var bDir = AddBtn(cTbl, L.T("Klasörü Aç", "Open Folder"), 356, cTbl.Height - 50, 140, ModernButtonVariant.Secondary, delegate { try { Process.Start(AppData.Dir); } catch { } });
            bDir.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            return p;
        }

        private void RefreshSessions()
        {
            GameSession.LoadHistory();
            _sessList.BeginUpdate(); _sessList.Items.Clear();
            for (int i = GameSession.History.Count - 1; i >= 0; i--) _sessList.Items.Add(new ListViewItem(GameSession.History[i].Take(9).ToArray()));
            _sessList.EndUpdate();
        }

        // ---------------- Page 5: Fan Eşleme ----------------
        private Panel BuildFans()
        {
            Panel p = NewPage(
                L.T("Fan Eşleme", "Fan Mapping"),
                L.T("Anakart fan girişlerinin canlı devir değerlerini okuyup doğru sistem rolüne eşle.", "Inspect live motherboard fan headers and assign each one to its real cooling role."));

            ModernCard cLive = AddCard(p, 80, 232, false);
            CardTitle(cLive, L.T("CANLI ANAKART FAN GİRİŞLERİ", "LIVE MOTHERBOARD FAN HEADERS"), 16, 12, 400);
            _fanList = StyledLV(cLive, 16, 38, cLive.Width - 32, 178, false, true);
            _fanList.Columns.Add(L.T("Fan Girişi", "Fan Header"), 320); _fanList.Columns.Add(L.T("Devir (RPM)", "Speed (RPM)"), 260);

            ModernCard cMap = AddCard(p, 324, 240, false);
            CardTitle(cMap, L.T("ROL ATAMALARI", "ROLE ASSIGNMENTS"), 16, 12, 400);
            string[] roles = { L.T("CPU fanı", "CPU Fan"), L.T("Kasa fanı 1", "Chassis Fan 1"), L.T("Kasa fanı 2", "Chassis Fan 2"), L.T("AIO pompa", "AIO Pump") };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                int ry = 42 + i * 42;
                cMap.Controls.Add(new Label { Text = roles[i], Left = 16, Top = ry + 4, Width = 170, Height = 24, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg });
                ComboBox cb = StyledCombo(cMap, 196, ry, 280, ComboBoxStyle.DropDownList);
                cb.Items.Add(L.T("Otomatik", "Auto"));
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
                _fanCombo[i] = cb;
            }
            cMap.Controls.Add(new Label { Text = L.T("0 RPM görünen girişler boştur veya fan durma (0dB) modundadır.", "0 RPM headers are disconnected or paused in zero-RPM mode."), Left = 16, Top = 212, Width = cMap.Width - 32, Height = 20, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 8.5f) });
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
                it.SubItems.Add(raw[i] > 0 ? raw[i].ToString("0", CultureInfo.InvariantCulture) + " RPM" : L.T("0 (boş/durgun)", "0 (idle/empty)"));
                if (raw[i] > 0) it.ForeColor = _acc;
                _fanList.Items.Add(it);
            }
            _fanList.EndUpdate();
        }

        // ---------------- Page 6: Pil ve Cihazlar ----------------
        private Panel BuildDevices()
        {
            Panel p = NewPage(
                L.T("Pil ve Kablosuz Cihazlar", "Batteries & Wireless Devices"),
                L.T("Dizüstü bilgisayar pili ve Windows'a pil durumu bildiren Bluetooth / HID cihazları.", "System laptop battery and Bluetooth / HID peripherals reporting charge level to Windows."));

            ModernCard cDev = AddCard(p, 80, 410, true);
            CardTitle(cDev, L.T("BAĞLI CİHAZLAR VE PİL SEVİYELERİ", "CONNECTED DEVICES & BATTERY LEVELS"), 16, 12, 460);
            _perList = StyledLV(cDev, 16, 38, cDev.Width - 32, cDev.Height - 134, false, true);
            _perList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _perList.Columns.Add(L.T("Cihaz", "Device"), 500); _perList.Columns.Add(L.T("Pil / Durum", "Battery / Status"), 220);

            var bRead = AddBtn(cDev, L.T("Şimdi Oku", "Refresh Now"), 16, cDev.Height - 82, 140, ModernButtonVariant.Primary, delegate { _lastPer = ""; Peripherals.RefreshAsync(); RefreshDevices(); });
            bRead.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            var bDump = AddBtn(cDev, L.T("Pil Dökümü (piller.txt)", "Export Batteries (piller.txt)"), 166, cDev.Height - 82, 230, ModernButtonVariant.Secondary, delegate { Peripherals.DumpAsync(); });
            bDump.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            var bHid = AddBtn(cDev, L.T("HID Dökümü (hid.txt)", "Export HID (hid.txt)"), 406, cDev.Height - 82, 200, ModernButtonVariant.Secondary, delegate { Peripherals.HidDumpAsync(); });
            bHid.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            var note = new Label { Text = L.T("Yalnızca Windows'a pil yüzdesi bildiren cihazlar listelenir.", "Only devices reporting battery status to Windows PnP or native HID are listed."), Left = 16, Top = cDev.Height - 36, Width = cDev.Width - 32, Height = 20, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 8.6f), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            cDev.Controls.Add(note);
            return p;
        }

        private string _lastPer = "";
        private void RefreshDevices()
        {
            string sysBat = "";
            try
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown)
                {
                    int pct = (int)Math.Round(ps.BatteryLifePercent * 100.0);
                    if (pct >= 0 && pct <= 100)
                        sysBat = pct + "%" + (ps.PowerLineStatus == PowerLineStatus.Online ? L.T(" (Şarjda)", " (Charging)") : "");
                }
            }
            catch { }
            string key = sysBat + "|" + string.Join(";", Peripherals.Items.Select(x => x.Key + x.Value).ToArray()) + Peripherals.Status;
            if (key == _lastPer) return;
            _lastPer = key;
            _perList.BeginUpdate(); _perList.Items.Clear();
            if (sysBat.Length > 0)
            {
                var bit = new ListViewItem(new[] { L.T("Dizüstü Bilgisayar Pili", "System / Laptop Battery"), sysBat });
                bit.ForeColor = _acc;
                _perList.Items.Add(bit);
            }
            foreach (var kv in Peripherals.Items) _perList.Items.Add(new ListViewItem(new[] { kv.Key, kv.Value + "%" }));
            if (sysBat.Length == 0 && Peripherals.Items.Length == 0) _perList.Items.Add(new ListViewItem(new[] { Peripherals.Status, "--" }));
            _perList.EndUpdate();
        }

        // ---------------- Page 7: Konum ve Uyarılar ----------------
        private Panel BuildData()
        {
            Panel p = NewPage(
                L.T("Konum ve Uyarılar", "Location & Alerts"),
                L.T("Hava durumu ve namaz vakitleri şehri ile yüksek sıcaklık tepsi bildirimlerini ayarla.", "Configure city for weather & prayer times and set thermal tray alert notifications."));

            ModernCard cLoc = AddCard(p, 80, 134, false);
            CardTitle(cLoc, L.T("ŞEHİR (HAVA DURUMU VE VAKİTLER)", "CITY (WEATHER & PRAYER TIMES)"), 16, 12, 460);
            _cityBox = StyledCombo(cLoc, 16, 42, 300, ComboBoxStyle.DropDown);
            foreach (string c in new[] { "istanbul", "ankara", "izmir", "bursa", "antalya", "adana", "konya", "trabzon", "kocaeli", "gaziantep", "london,uk", "berlin,germany", "amsterdam,netherlands", "new york,usa", "dubai,uae" }) _cityBox.Items.Add(c);
            _cityBox.Text = _d._city;
            AddBtn(cLoc, L.T("Uygula", "Apply"), 328, 39, 120, ModernButtonVariant.Primary, delegate
            {
                string c = (_cityBox.Text ?? "").Trim();
                if (c.Length == 0) return;
                _d._city = c; _d.SaveSettings(); Extras.Start(c);
            });
            cLoc.Controls.Add(new Label { Text = L.T("Şehir adını veya 'şehir,ülke' biçimini yazabilirsin (ör. izmir, london,uk, berlin,germany).", "Enter any city or 'city,country' format (e.g. istanbul, london,uk, new york,usa)."), Left = 16, Top = 92, Width = cLoc.Width - 32, Height = 22, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 8.8f) });

            ModernCard cAl = AddCard(p, 226, 336, false);
            CardTitle(cAl, L.T("ÖZELLEŞTİRİLEBİLİR UYARI EŞİKLERİ", "CUSTOM ALERT THRESHOLDS"), 16, 12, 460);
            AddToggle(cAl,
                L.T("Yüksek sıcaklık & sistem uyarıları", "Threshold tray alerts"),
                L.T("Belirlediğiniz eşikler aşılınca Windows bildirimi gösterir", "Shows Windows alerts when configured limits are exceeded"),
                36, Program.AlertsEnabled, v => Program.SetAlertsEnabled(v));
            AddToggle(cAl,
                L.T("Uyarıda sesli ikaz (Bip)", "Play alert sound (Beep)"),
                L.T("Bildirim gösterildiğinde sistem uyarı sesi çalar", "Plays Windows exclamation sound on alert"),
                90, AppPrefs.AlertBeep, v => { AppPrefs.AlertBeep = v; _d.SaveSettings(); });

            int alColW = (cAl.Width - 44) / 2;
            Func<string, string, int, int, int, int, int, Action<int>, ModernSlider> addTh = delegate (string lbl, string suf, int mn, int mx, int cur, int x, int y, Action<int> onVal)
            {
                var sl = new ModernSlider { Label = lbl, Suffix = suf, Minimum = mn, Maximum = mx, Left = x, Top = y, Width = alColW, Height = 46, AccentColor = _acc };
                sl.SetValueSilent(Math.Max(mn, Math.Min(mx, cur)));
                sl.ValueChanged += delegate { if (!_loading) { onVal(sl.Value); _d.SaveSettings(); Program.RebuildTrayMenu(); } };
                _accentCtls.Add(sl);
                cAl.Controls.Add(sl);
                return sl;
            };
            var sCpuAl = addTh(L.T("CPU SICAKLIK EŞİĞİ", "CPU TEMP THRESHOLD"), "°C", 60, 105, AppPrefs.AlertCpu, 16, 148, v => AppPrefs.AlertCpu = v);
            var sGpuAl = addTh(L.T("GPU SICAKLIK EŞİĞİ", "GPU TEMP THRESHOLD"), "°C", 60, 105, AppPrefs.AlertGpu, 28 + alColW, 148, v => AppPrefs.AlertGpu = v);
            var sDskAl = addTh(L.T("DİSK SICAKLIK EŞİĞİ", "DISK TEMP THRESHOLD"), "°C", 45, 90, AppPrefs.AlertDisk, 16, 202, v => AppPrefs.AlertDisk = v);
            var sRamAl = addTh(L.T("RAM KULLANIM EŞİĞİ", "RAM USAGE THRESHOLD"), "%", 70, 99, AppPrefs.AlertRam, 28 + alColW, 202, v => AppPrefs.AlertRam = v);
            var sBatAl = addTh(L.T("DÜŞÜK PİL EŞİĞİ", "LOW BATTERY ALERT"), "%", 5, 40, AppPrefs.AlertBat, 16, 256, v => AppPrefs.AlertBat = v);
            var sPngAl = addTh(L.T("YÜKSEK PİNG EŞİĞİ", "HIGH PING ALERT"), " ms", 50, 500, AppPrefs.AlertPing, 28 + alColW, 256, v => AppPrefs.AlertPing = v);

            cAl.SizeChanged += delegate
            {
                int cw2 = Math.Max(180, (cAl.Width - 44) / 2);
                sCpuAl.Width = cw2; sDskAl.Width = cw2; sBatAl.Width = cw2;
                sGpuAl.Left = 28 + cw2; sGpuAl.Width = cw2;
                sRamAl.Left = 28 + cw2; sRamAl.Width = cw2;
                sPngAl.Left = 28 + cw2; sPngAl.Width = cw2;
            };
            return p;
        }

        // ---------------- Page 8: Pencere ve Sistem ----------------
        private Panel BuildSystem()
        {
            Panel p = NewPage(
                L.T("Pencere ve Sistem", "Window & System"),
                L.T("Pano monitörü, pencere davranışı, Windows otomatik başlatma ve web sunucusu ayarları.", "Dashboard monitor placement, window layering, Windows autostart, and web UI launcher."));

            ModernCard cWin = AddCard(p, 80, 240, false);
            CardTitle(cWin, L.T("MONİTÖR VE PENCERE DAVRANIŞI", "DISPLAY & WINDOW BEHAVIOR"), 16, 12, 460);
            _screenBox = StyledCombo(cWin, 16, 42, 460, ComboBoxStyle.DropDownList);
            AddBtn(cWin, L.T("Bu Monitöre Taşı", "Move Here"), 488, 39, 170, ModernButtonVariant.Primary, delegate
            {
                Screen[] all = Screen.AllScreens;
                if (_screenBox.SelectedIndex >= 0 && _screenBox.SelectedIndex < all.Length) _d.MoveToScreen(all[_screenBox.SelectedIndex]);
            });

            ModernToggle ckTop = null, ckDesk = null;
            ckTop = AddToggle(cWin, L.T("Her zaman üstte", "Always on top"), L.T("Pano diğer tüm pencerelerin üstünde kalır", "Keeps dashboard pinned above other windows"), 82, _d.TopMost, v =>
            {
                _d.TopMost = v;
                if (v && _d._desk) { _d._desk = false; _d.ApplyDeskMode(); _loading = true; ckDesk.SetCheckedSilent(false); _loading = false; }
                _d.SaveSettings();
            });
            ckDesk = AddToggle(cWin, L.T("Masaüstü modu (Win+D korumalı)", "Desktop background mode (Win+D safe)"), L.T("Diğer uygulamaların altında durur, Win+D ile küçülmez ve Alt+Tab'da gizlenir", "Stays behind app windows, ignores Win+D minimize, hidden from Alt+Tab"), 136, _d._desk, v =>
            {
                _d._desk = v; _d.ApplyDeskMode();
                if (v) { _d.TopMost = false; _loading = true; ckTop.SetCheckedSilent(false); _loading = false; _d.SendToBottom(); }
                _d.SaveSettings();
            });
            AddBtn(cWin, L.T("Panoyu Küçült", "Minimize Dashboard"), 16, 194, 170, ModernButtonVariant.Secondary, delegate { _d.UserMinimize(); });
            AddBtn(cWin, L.T("Panoyu Kapat", "Close Dashboard"), 196, 194, 170, ModernButtonVariant.Secondary, delegate { _d.Close(); });

            ModernCard cSys = AddCard(p, 332, 134, false);
            CardTitle(cSys, L.T("OTOMATİK BAŞLATMA VE PERFORMANS", "STARTUP & PERFORMANCE"), 16, 12, 460);
            bool auto = false; try { auto = Program.IsAutostartEnabled(); } catch { }
            AddToggle(cSys, L.T("Windows ile otomatik başlat", "Start automatically with Windows"), L.T("Görev Zamanlayıcı ile yönetici yetkisinde UAC sormadan açılır", "Launches silently with full sensor permissions via Task Scheduler"), 36, auto, v => System.Threading.ThreadPool.QueueUserWorkItem(delegate { Program.SetAutostart(v); }));
            AddToggle(cSys, L.T("Düşük CPU tasarruf modu", "Low CPU power-saving mode"), L.T("Sensör verileri 1 saniye yerine 2 saniyede bir okunur", "Polls hardware sensors every 2 seconds instead of 1 second"), 84, _d._lowCpu, v => { _d._lowCpu = v; Program.PollIntervalMs = v ? 2000 : 1000; _d.SaveSettings(); });

            ModernCard cWeb = AddCard(p, 478, 92, false);
            CardTitle(cWeb, L.T("WEB ARAYÜZÜ VE UYGULAMA", "WEB TELEMETRY UI & APP"), 16, 12, 460);
            AddBtn(cWeb, L.T("🌐 Tarayıcıda Aç", "🌐 Open in Browser"), 16, 40, 174, ModernButtonVariant.Secondary, delegate { Program.LaunchBrowserDashboard(); });
            AddBtn(cWeb, L.T("Edge Uygulaması Olarak Aç", "Open as Edge App"), 200, 40, 210, ModernButtonVariant.Secondary, delegate { Program.LaunchEdgeDashboard(); });
            var bExit = AddBtn(cWeb, L.T("Uygulamadan Çık", "Exit Application"), cWeb.Width - 186, 40, 170, ModernButtonVariant.Danger, delegate { Program.ExitAppAsk(); });
            bExit.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Mini Overlay HUD & Tray Icon Card
            ModernCard cHud = AddCard(p, 582, 162, false);
            CardTitle(cHud, L.T("MİNİ OVERLAY HUD (TEK MONİTÖR) VE TEPSİ SİMGESİ", "MINI OVERLAY HUD & LIVE TRAY ICON"), 16, 12, 560);
            AddToggle(cHud,
                L.T("Oyun içi tıklamayı geçiren Mini Overlay HUD", "In-game click-through Mini Overlay HUD"),
                L.T("Ekran köşesinde FPS, CPU/GPU sıcaklık ve RAM çubuğu gösterir (tıklamaları oyuna geçirir)", "Renders a transparent click-through HUD bar at a screen corner"),
                36, AppPrefs.OverlayEnabled, v => { AppPrefs.OverlayEnabled = v; Program.SyncOverlay(); Program.RebuildTrayMenu(); _d.SaveSettings(); });

            cHud.Controls.Add(new Label { Text = L.T("Köşe:", "Corner:"), Left = 16, Top = 104, Width = 56, Height = 22, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg });
            ComboBox cbCorner = StyledCombo(cHud, 74, 100, 140, ComboBoxStyle.DropDownList);
            cbCorner.Items.AddRange(new object[] { L.T("Sol Üst", "Top-Left"), L.T("Sağ Üst", "Top-Right"), L.T("Sol Alt", "Bottom-Left"), L.T("Sağ Alt", "Bottom-Right") });
            cbCorner.SelectedIndex = Math.Max(0, Math.Min(3, AppPrefs.OverlayCorner));
            cbCorner.SelectedIndexChanged += delegate { if (!_loading) { AppPrefs.OverlayCorner = cbCorner.SelectedIndex; Program.SyncOverlay(); _d.SaveSettings(); } };

            cHud.Controls.Add(new Label { Text = L.T("Tepsi:", "Tray:"), Left = 226, Top = 104, Width = 52, Height = 22, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg });
            ComboBox cbTray = StyledCombo(cHud, 280, 100, 150, ComboBoxStyle.DropDownList);
            cbTray.Items.AddRange(new object[] { L.T("OnMon Logo", "OnMon Logo"), L.T("Canlı CPU °C", "Live CPU Temp"), L.T("Canlı GPU °C", "Live GPU Temp") });
            cbTray.SelectedIndex = Math.Max(0, Math.Min(2, AppPrefs.TrayIconMode));
            cbTray.SelectedIndexChanged += delegate { if (!_loading) { AppPrefs.TrayIconMode = cbTray.SelectedIndex; _d.SaveSettings(); Program.UpdateTrayLive(Program.LastSnap); } };

            var slOvOp = new ModernSlider { Label = L.T("OPAKLIK", "OPACITY"), Suffix = "%", Minimum = 30, Maximum = 100, Left = 446, Top = 94, Width = cHud.Width - 462, Height = 46, AccentColor = _acc, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            slOvOp.SetValueSilent(Math.Max(30, Math.Min(100, AppPrefs.OverlayOpacity)));
            slOvOp.ValueChanged += delegate { if (!_loading) { AppPrefs.OverlayOpacity = slOvOp.Value; Program.SyncOverlay(); _d.SaveSettings(); } };
            _accentCtls.Add(slOvOp);
            cHud.Controls.Add(slOvOp);

            // Backup, Restore & Setup Wizard Card
            ModernCard cBak = AddCard(p, 756, 98, false);
            CardTitle(cBak, L.T("YEDEKLEME VE KURULUM SİHİRBAZI", "BACKUP, RESTORE & SETUP WIZARD"), 16, 12, 520);
            AddBtn(cBak, L.T("📤 Ayarları Yedekle…", "📤 Backup Settings…"), 16, 42, 184, ModernButtonVariant.Secondary, delegate
            {
                using (var sd = new SaveFileDialog { Title = L.T("OnMon Yedeği Kaydet", "Save OnMon Backup"), Filter = "OnMon Backup (*.onmonbak)|*.onmonbak", FileName = "OnMon_Backup_" + DateTime.Now.ToString("yyyyMMdd") + ".onmonbak", DefaultExt = "onmonbak" })
                {
                    if (sd.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        _d.SaveSettings();
                        int n = BackupManager.ExportBackup(sd.FileName);
                        MessageBox.Show(this, L.T(n + " dosya başarıyla yedeklendi.", "Exported " + n + " files successfully."), "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            });
            AddBtn(cBak, L.T("📥 Yedekten Geri Yükle…", "📥 Restore Backup…"), 210, 42, 196, ModernButtonVariant.Secondary, delegate
            {
                using (var od = new OpenFileDialog { Title = L.T("OnMon Yedeği Yükle", "Restore OnMon Backup"), Filter = "OnMon Backup (*.onmonbak)|*.onmonbak|All|*.*" })
                {
                    if (od.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        int n;
                        if (!BackupManager.ImportBackup(od.FileName, out n))
                        {
                            MessageBox.Show(this, L.T("Geçersiz yedek dosyası.", "Invalid backup file."), "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        _d.ReloadFromDisk();
                        MessageBox.Show(this, L.T(n + " dosya geri yüklendi ve uygulandı.", "Restored and applied " + n + " files."), "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            });
            AddBtn(cBak, L.T("✨ Kurulum Sihirbazı", "✨ Setup Wizard"), 416, 42, 186, ModernButtonVariant.Primary, delegate
            {
                new OnboardingForm(_d).Show();
            });
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
                _screenBox.Items.Add(L.T("Monitör ", "Display ") + (i + 1) + (sc.Primary ? L.T(" (ana)", " (primary)") : "") + " — " + sc.Bounds.Width + "×" + sc.Bounds.Height);
                if (sc.DeviceName == cur.DeviceName) _screenBox.SelectedIndex = i;
            }
        }

        // ---------------- Page 9: Zamanlayıcı ----------------
        private ComboBox TimeCombo(Control parent, int left, int top, int count)
        {
            ComboBox c = StyledCombo(parent, left, top, 68, ComboBoxStyle.DropDownList);
            c.MaxDropDownItems = 10; c.IntegralHeight = false; c.DropDownHeight = 240;
            for (int i = 0; i < count; i++) c.Items.Add(i.ToString("00"));
            return c;
        }

        private Panel BuildTimer()
        {
            Panel p = NewPage(
                L.T("Kapatma ve Yeniden Başlatma Zamanlayıcısı", "Shutdown & Restart Timer"),
                L.T("Zamanlayıcıyı Windows tutar, OnMon kapansa bile belirlenen saatte uygulanır.", "Managed natively by Windows shutdown scheduler — persists even if OnMon closes."));

            ModernCard cHero = AddCard(p, 80, 74, false);
            _tmrBadge = new ModernBadge { Text = L.T("PASİF", "IDLE"), Left = 16, Top = 24, Width = 86, Height = 24, BadgeColor = ModernColors.Dim };
            cHero.Controls.Add(_tmrBadge);
            _tmrStatus = new Label { Left = 114, Top = 16, Width = cHero.Width - 130, Height = 42, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            cHero.Controls.Add(_tmrStatus);

            ModernCard cCfg = AddCard(p, 166, 268, false);
            CardTitle(cCfg, L.T("ZAMAN VE EYLEM AYARLARI", "SCHEDULE & ACTION"), 16, 12, 360);
            cCfg.Controls.Add(new Label { Text = L.T("saat        dakika     saniye", "hours      minutes    seconds"), Left = 196, Top = 36, Width = 260, Height = 18, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 8.4f) });
            _rbAt = new RadioButton { Text = L.T("Şu saatte:", "At time:"), Left = 16, Top = 60, Width = 170, Height = 26, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg, Checked = true };
            _atH = TimeCombo(cCfg, 196, 58, 24); _atM = TimeCombo(cCfg, 272, 58, 60); _atS = TimeCombo(cCfg, 348, 58, 60);
            DateTime nx = DateTime.Now.AddHours(1);
            _atH.SelectedIndex = nx.Hour; _atM.SelectedIndex = 0; _atS.SelectedIndex = 0;

            _rbAfter = new RadioButton { Text = L.T("Şu süre sonra:", "After duration:"), Left = 16, Top = 100, Width = 170, Height = 26, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg };
            _afH = TimeCombo(cCfg, 196, 98, 25); _afM = TimeCombo(cCfg, 272, 98, 60); _afS = TimeCombo(cCfg, 348, 98, 60);
            _afH.SelectedIndex = 1; _afM.SelectedIndex = 0; _afS.SelectedIndex = 0;
            cCfg.Controls.Add(_rbAt); cCfg.Controls.Add(_rbAfter);
            _atH.DropDown += delegate { _rbAt.Checked = true; }; _atM.DropDown += delegate { _rbAt.Checked = true; }; _atS.DropDown += delegate { _rbAt.Checked = true; };
            _afH.DropDown += delegate { _rbAfter.Checked = true; }; _afM.DropDown += delegate { _rbAfter.Checked = true; }; _afS.DropDown += delegate { _rbAfter.Checked = true; };

            cCfg.Controls.Add(new Label { Text = L.T("Eylem:", "Action:"), Left = 16, Top = 142, Width = 170, Height = 22, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg });
            _tmrAct = StyledCombo(cCfg, 196, 138, 220, ComboBoxStyle.DropDownList);
            _tmrAct.Items.Add(L.T("Bilgisayarı kapat", "Shut down PC"));
            _tmrAct.Items.Add(L.T("Bilgisayarı yeniden başlat", "Restart PC"));
            _tmrAct.SelectedIndex = 0;

            _tmrForce = AddToggle(cCfg, L.T("Açık uygulamaları zorla kapat", "Force close open applications"), L.T("Kaydedilmemiş çalışmalar kaybolabilir", "Unsaved work may be lost"), 172, ShutdownTimer.Force, delegate { });

            ModernCard cAct = AddCard(p, 446, 66, false);
            AddBtn(cAct, L.T("⏱ Zamanlayıcıyı Başlat", "⏱ Start Timer"), 16, 16, 210, ModernButtonVariant.Primary, delegate
            {
                DateTime target;
                if (_rbAt.Checked)
                {
                    target = DateTime.Today.AddHours(_atH.SelectedIndex).AddMinutes(_atM.SelectedIndex).AddSeconds(_atS.SelectedIndex);
                    if (target <= DateTime.Now.AddSeconds(20)) target = target.AddDays(1);
                }
                else target = DateTime.Now.AddHours(_afH.SelectedIndex).AddMinutes(_afM.SelectedIndex).AddSeconds(_afS.SelectedIndex);
                int act = _tmrAct.SelectedIndex == 1 ? 2 : 1;
                if (!ShutdownTimer.Start(act, target, _tmrForce.Checked))
                {
                    _tmrBadge.Text = L.T("HATA", "ERROR");
                    _tmrBadge.BadgeColor = ModernColors.Danger;
                    _tmrStatus.Text = ShutdownTimer.Error;
                    _tmrStatus.ForeColor = ModernColors.Danger;
                    return;
                }
                _d.Invalidate();
                RefreshTimer();
            });
            AddBtn(cAct, L.T("İptal Et", "Cancel Timer"), 236, 16, 150, ModernButtonVariant.Danger, delegate { ShutdownTimer.Cancel(); _d.Invalidate(); RefreshTimer(); });

            _tmrTick = new System.Windows.Forms.Timer { Interval = 1000 };
            _tmrTick.Tick += delegate { RefreshTimer(); };
            return p;
        }

        private void RefreshTimer()
        {
            if (_tmrStatus == null) return;
            if (ShutdownTimer.Active)
            {
                TimeSpan r = ShutdownTimer.Remaining;
                _tmrBadge.Text = L.T("AKTİF", "ACTIVE");
                _tmrBadge.BadgeColor = _acc;
                _tmrStatus.ForeColor = _acc;
                if (L.IsEn)
                    _tmrStatus.Text = (ShutdownTimer.Action == 2 ? "Restart" : "Shutdown") + " scheduled for " + ShutdownTimer.Target.ToString("dd.MM HH:mm") +
                        "   —   " + ((int)r.TotalHours) + "h " + r.Minutes + "m " + r.Seconds + "s left";
                else
                    _tmrStatus.Text = (ShutdownTimer.Action == 2 ? "Yeniden başlatma" : "Kapanış") + " kuruldu: " + ShutdownTimer.Target.ToString("dd.MM HH:mm") +
                        "   —   kalan " + ((int)r.TotalHours) + " sa " + r.Minutes + " dk " + r.Seconds + " sn";
            }
            else
            {
                _tmrBadge.Text = L.T("PASİF", "IDLE");
                _tmrBadge.BadgeColor = ModernColors.Dim;
                _tmrStatus.ForeColor = ModernColors.Dim;
                _tmrStatus.Text = L.T("Kurulu bir kapanış zamanlayıcısı yok.", "No active shutdown/restart timer.");
            }
        }

        // ---------------- Page 10: Güncelleme (OTA) ----------------
        private Panel BuildUpdate()
        {
            Panel p = NewPage(
                L.T("Güncelleme (OTA)", "Software Updates (OTA)"),
                L.T("GitHub deposundan (ooemir23/OnMon) SHA-256 doğrulamalı otomatik uygulama güncellemesi.", "Automatic SHA-256 verified updates directly from github.com/ooemir23/OnMon."));

            // Hero update status card
            ModernCard cHero = AddCard(p, 80, 152, false);
            _updBadge = new ModernBadge { Text = L.T("✓ Güncelsin", "✓ Up to date"), Left = 18, Top = 16, Width = 148, Height = 26, BadgeColor = ModernColors.Good };
            _updInstalledBadge = new ModernBadge { Text = L.T("Yüklü: v", "Installed: v") + Updater.AppVersion, Left = 176, Top = 16, Width = 114, Height = 26, BadgeColor = _acc };
            _updLatestBadge = new ModernBadge { Text = L.T("Sunucu: --", "Latest: --"), Left = 298, Top = 16, Width = 114, Height = 26, BadgeColor = ModernColors.Dim };
            _accentCtls.Add(_updInstalledBadge);
            cHero.Controls.Add(_updBadge);
            cHero.Controls.Add(_updInstalledBadge);
            cHero.Controls.Add(_updLatestBadge);

            _updVer = new Label { Left = 18, Top = 52, Width = cHero.Width - 36, Height = 24, ForeColor = ModernColors.Text, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _updStatus = new Label { Left = 18, Top = 76, Width = cHero.Width - 36, Height = 22, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Font = new Font("Segoe UI", 9.2f), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            cHero.Controls.Add(_updVer);
            cHero.Controls.Add(_updStatus);

            _updCheck = AddBtn(cHero, L.T("↻ Güncellemeleri Denetle", "↻ Check for Updates"), 18, 106, 224, ModernButtonVariant.Secondary, delegate
            {
                _updStatus.Text = L.T("GitHub kontrol ediliyor…", "Checking GitHub…");
                _updCheck.Enabled = false;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    Updater.Check();
                    try { BeginInvoke((MethodInvoker)delegate { _updCheck.Enabled = true; RefreshUpdate(); }); } catch { }
                });
            });
            _updGo = AddBtn(cHero, L.T("⬇ Şimdi Güncelle ve Yeniden Başlat", "⬇ Update & Restart Now"), 252, 106, 286, ModernButtonVariant.Primary, delegate
            {
                if (MessageBox.Show(this, L.T("Uygulama güncellenip yeniden başlatılacak. Devam edilsin mi?", "The application will download and restart automatically. Continue?"), L.T("Güncelleme", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _updGo.Enabled = false;
                _updStatus.Text = L.T("Güncelleme paketi indiriliyor…", "Downloading update package…");
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = Updater.ApplyUpdate();
                    if (err != null) try { BeginInvoke((MethodInvoker)delegate { _updGo.Enabled = true; RefreshUpdate(); }); } catch { }
                });
            });

            // Auto-check toggle card
            ModernCard cAuto = AddCard(p, 244, 62, false);
            AddToggle(cAuto,
                L.T("Otomatik güncelleme denetimi", "Automatic update checks"),
                L.T("Açılışta ve 6 saatte bir arka planda yeni sürümü kontrol edip bildirim gösterir", "Checks at startup and every 6 hours, showing a tray notification for new releases"),
                8, Updater.AutoCheck, v => Updater.AutoCheck = v);

            // Changelog card
            ModernCard cNotes = AddCard(p, 318, 360, true);
            CardTitle(cNotes, L.T("SÜRÜM NOTLARI VE DEĞİŞİKLİK GEÇMİŞİ", "RELEASE NOTES & CHANGELOG"), 18, 12, 460);
            _updNotes = new TextBox
            {
                Left = 18, Top = 38, Width = cNotes.Width - 36, Height = cNotes.Height - 52,
                Multiline = true, ReadOnly = true, BackColor = ModernColors.CardBg, ForeColor = ModernColors.Text,
                BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 9.4f),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            cNotes.Controls.Add(_updNotes);
            return p;
        }

        private void RefreshUpdate()
        {
            bool hasUpd = Updater.UpdateAvailable;
            _updInstalledBadge.Text = L.T("Yüklü: v", "Installed: v") + Updater.AppVersion;
            _updLatestBadge.Text = Updater.LatestVersion > 0 ? L.T("Sunucu: v", "Server: v") + Updater.LatestVersion : L.T("Sunucu: --", "Server: --");
            _updLatestBadge.BadgeColor = hasUpd ? _acc : ModernColors.Dim;

            if (hasUpd)
            {
                _updBadge.Text = L.T("↓ Yeni Sürüm!", "↓ Update Ready");
                _updBadge.BadgeColor = _acc;
            }
            else if (Updater.LatestVersion > 0)
            {
                _updBadge.Text = L.T("✓ Güncelsin", "✓ Up to date");
                _updBadge.BadgeColor = ModernColors.Good;
            }
            else
            {
                _updBadge.Text = L.T("• Hazır", "• Ready");
                _updBadge.BadgeColor = ModernColors.Dim;
            }

            _updVer.Text = L.T("Yüklü sürüm: v", "Installed version: v") + Updater.AppVersion + (Updater.LatestVersion > 0 ? L.T("   ·   Son sürüm: v", "   ·   Latest release: v") + Updater.LatestVersion : "");
            _updStatus.Text = string.IsNullOrEmpty(Updater.Status) ? L.T("Denetlemek için 'Güncellemeleri Denetle' düğmesine tıkla.", "Click 'Check for Updates' to fetch the latest release.") : Updater.Status;

            string log = Updater.Changelog;
            if (string.IsNullOrEmpty(log))
            {
                try
                {
                    string localLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog.txt");
                    if (File.Exists(localLog)) log = File.ReadAllText(localLog, System.Text.Encoding.UTF8).Replace("\r", "").Replace("\n", "\r\n");
                }
                catch { }
            }
            _updNotes.Text = (Updater.LatestNotes.Length > 0 && hasUpd ? L.T("YENİ SÜRÜM NOTU:\r\n", "NEW RELEASE NOTES:\r\n") + Updater.LatestNotes + "\r\n\r\n" : "") + log;
            _updGo.Enabled = hasUpd && !Updater.Busy;
            _updGo.Variant = hasUpd ? ModernButtonVariant.Primary : ModernButtonVariant.Secondary;
        }

        // ---------------- Page 11: Tanılama ----------------
        private Panel BuildDiag()
        {
            Panel p = NewPage(
                L.T("Tanılama ve Donanım Raporu", "Diagnostics & Hardware Report"),
                L.T("Sensör sürücüleri, donanım arka ucu durumu, ortalama gecikme süreleri ve günlük dosyaları.", "Sensor drivers, telemetry backend state, frame timings, and diagnostic logs."));

            ModernCard cLog = AddCard(p, 80, 480, true);
            CardTitle(cLog, L.T("CANLI SENSÖR VE PERFORMANS RAPORU", "LIVE SENSOR & PERFORMANCE REPORT"), 16, 12, 460);
            _diag = new TextBox
            {
                Left = 16, Top = 38, Width = cLog.Width - 32, Height = cLog.Height - 52,
                Multiline = true, ReadOnly = true, BackColor = ModernColors.CardBg, ForeColor = ModernColors.Text,
                BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.6f), ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            cLog.Controls.Add(_diag);

            ModernCard cAct = AddCard(p, 572, 108, false);
            cAct.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            AddBtn(cAct, L.T("↻ Yenile", "↻ Refresh"), 16, 14, 124, ModernButtonVariant.Primary, delegate { RefreshDiag(); });
            AddBtn(cAct, L.T("Sensör Listesini Kaydet", "Save Sensor Dump"), 150, 14, 210, ModernButtonVariant.Secondary, delegate { Program.RequestSensorDump(); });
            AddBtn(cAct, L.T("Uygulama Klasörünü Aç", "Open App Folder"), 370, 14, 200, ModernButtonVariant.Secondary, delegate { try { Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory); } catch { } });

            AddBtn(cAct, L.T("PawnIO Sürücüsünü Kur (winget)", "Install PawnIO Driver (winget)"), 16, 58, 250, ModernButtonVariant.Secondary, delegate
            {
                if (MessageBox.Show(
                    L.T("CPU sıcaklık/güç ve anakart fan/voltaj sensörleri için PawnIO sürücüsü gerekir (Windows Bellek Bütünlüğü ile uyumlu, imzalı sürücü).\n\nwinget ile 'namazso.PawnIO' kurulsun mu?",
                        "PawnIO driver is required for CPU package temp/power and motherboard SuperIO sensors (signed driver compatible with Windows Core Isolation).\n\nInstall 'namazso.PawnIO' via winget?"),
                    "OnMon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { Process.Start(new ProcessStartInfo("cmd.exe", "/c winget install --id namazso.PawnIO -e --accept-source-agreements --accept-package-agreements & echo. & echo Done. Please restart OnMon. & pause") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(L.T("Başlatılamadı: ", "Could not start: ") + ex.Message); }
            });
            AddBtn(cAct, L.T("pawnio.eu Sitesini Aç", "Open pawnio.eu"), 276, 58, 180, ModernButtonVariant.Ghost, delegate { try { Process.Start("https://pawnio.eu"); } catch { } });
            AddBtn(cAct, L.T("🔄 Peak Değerlerini Sıfırla", "🔄 Reset Session Peaks"), 466, 58, 210, ModernButtonVariant.Secondary, delegate { PeakStats.Reset(); RefreshDiag(); });
            return p;
        }

        private void RefreshDiag()
        {
            double pms = _d._paintMs;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T("--- Oturum Min / Maks (Peak) ---", "--- Session Min / Max (Peak) ---"));
            sb.AppendLine(PeakStats.SummaryText());
            sb.AppendLine(L.T("Sensör kaynağı:", "Sensor backend:"));
            sb.AppendLine("  " + (string.IsNullOrEmpty(_d._source) ? L.T("(henüz veri yok)", "(waiting for data)") : _d._source));
            sb.AppendLine();
            sb.AppendLine("LibreHardwareMonitor: " + (string.IsNullOrEmpty(TelemetryCollector.LhmError) ? L.T("çalışıyor", "running") : L.T("HATA — ", "ERROR — ") + TelemetryCollector.LhmError));
            sb.AppendLine(L.T("Sürücü ve sensör durumu:", "Driver & sensor report:"));
            sb.AppendLine(Program.SensorReport());
            sb.AppendLine(L.T("Ses çıkışı: ", "Audio output: ") + AudioSwitch.DefaultName);
            sb.AppendLine(L.T("Mikrofon: ", "Microphone: ") + AudioSwitch.MicName + (AudioSwitch.MicMuted ? L.T(" (sessiz)", " (muted)") : ""));
            sb.AppendLine(L.T("Sürüm: v", "Version: v") + Updater.AppVersion + "  (" + L.Lang.ToUpperInvariant() + ")");
            sb.AppendLine(L.T("Veri toplama süresi (ort.): ", "Avg poll time: ") + Program.CollectMs.ToString("0") + " ms");
            sb.AppendLine(L.T("Çizim süresi (ort.): ", "Avg render time: ") + pms.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
            sb.AppendLine(L.T("Klasör: ", "Directory: ") + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine();
            sb.AppendLine(L.T("sensorler.txt  → tüm sensörlerin listesi", "sensorler.txt  → full sensor dump"));
            sb.AppendLine(L.T("hata.log       → hata kayıtları", "hata.log       → error logs"));
            _diag.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }

        private void Tick()
        {
            if (_pages.Count > 6 && _pages[6].Visible) RefreshDevices();
            if (_pages.Count > 5 && _pages[5].Visible) RefreshFans();
            if (_pages.Count > 2 && _pages[2].Visible)
            {
                AudioSwitch.PollMic();
                if (_micBarCtl != null) _micBarCtl.Value = (int)Math.Max(0, Math.Min(100, Math.Sqrt(AudioSwitch.MicLevel) * 100));
                if (_micMute != null)
                {
                    _loading = true;
                    _micMute.SetCheckedSilent(AudioSwitch.MicMuted);
                    _loading = false;
                }
            }
        }
    }
}
