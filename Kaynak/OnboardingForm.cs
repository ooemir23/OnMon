using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>İlk kurulum veya yeniden yapılandırma için 3 adımlı modern karşılama sihirbazı.</summary>
    internal sealed class OnboardingForm : Form
    {
        private readonly DashboardForm _d;
        private int _step;
        private Panel _body;
        private ModernButton _btnBack, _btnNext;
        private Label _lblStep, _lblTitle, _lblSub;

        private string _selLayout = "bento";
        private string _selTheme = "rog";
        private int _selScreenIdx;

        public OnboardingForm(DashboardForm d)
        {
            _d = d;
            if (_d != null)
            {
                if (!string.IsNullOrEmpty(_d._layout)) _selLayout = _d._layout;
                if (_d._th != null) _selTheme = _d._th.Id;
                Screen cur = Screen.FromControl(_d);
                Screen[] all = Screen.AllScreens;
                for (int i = 0; i < all.Length; i++) if (all[i].DeviceName == cur.DeviceName) { _selScreenIdx = i; break; }
            }

            ModernUi.ApplyDarkTitleBar(this);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(680, 470);
            BackColor = ModernColors.CanvasBg;
            ForeColor = ModernColors.Text;
            Font = new Font("Segoe UI", 9.5f);
            TopMost = true;
            try { Icon = Program.TrayIcon(); } catch { }

            var hdr = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = ModernColors.SidebarBg };
            hdr.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pn = new Pen(ModernColors.Border)) e.Graphics.DrawLine(pn, 0, hdr.Height - 1, hdr.Width, hdr.Height - 1);
            };
            _lblStep = new Label { Left = 24, Top = 14, Width = 200, Height = 18, ForeColor = Themes.Get(_selTheme).A1, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), BackColor = ModernColors.SidebarBg };
            _lblTitle = new Label { Left = 24, Top = 34, Width = 620, Height = 28, ForeColor = ModernColors.Text, Font = new Font("Segoe UI", 14f, FontStyle.Bold), BackColor = ModernColors.SidebarBg };
            _lblSub = new Label { Left = 24, Top = 60, Width = 620, Height = 18, ForeColor = ModernColors.Dim, Font = new Font("Segoe UI", 8.8f), BackColor = ModernColors.SidebarBg };
            hdr.Controls.Add(_lblStep);
            hdr.Controls.Add(_lblTitle);
            hdr.Controls.Add(_lblSub);
            Controls.Add(hdr);

            var ftr = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = ModernColors.SidebarBg };
            ftr.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pn = new Pen(ModernColors.Border)) e.Graphics.DrawLine(pn, 0, 0, ftr.Width, 0);
            };
            _btnBack = new ModernButton { Left = 24, Top = 12, Width = 120, Height = 34, Variant = ModernButtonVariant.Secondary };
            _btnBack.Click += delegate { if (_step > 0) { _step--; RenderStep(); } };
            _btnNext = new ModernButton { Left = ClientSize.Width - 174, Top = 12, Width = 150, Height = 34, Variant = ModernButtonVariant.Primary, AccentColor = Themes.Get(_selTheme).A1 };
            _btnNext.Click += delegate
            {
                if (_step < 2) { _step++; RenderStep(); }
                else FinishWizard();
            };
            ftr.Controls.Add(_btnBack);
            ftr.Controls.Add(_btnNext);
            Controls.Add(ftr);

            _body = new Panel { Dock = DockStyle.Fill, BackColor = ModernColors.CanvasBg, Padding = new Padding(24) };
            Controls.Add(_body);
            _body.BringToFront();

            RenderStep();
        }

        private void RenderStep()
        {
            Text = L.T("OnMon — Hızlı Kurulum Sihirbazı", "OnMon — Quick Setup Wizard");
            _btnBack.Text = L.T("← Geri", "← Back");
            _btnBack.Visible = _step > 0;
            _btnNext.Text = _step < 2 ? L.T("İleri →", "Next →") : L.T("Tamamla & Başlat", "Finish & Launch");
            _btnNext.AccentColor = Themes.Get(_selTheme).A1;
            _lblStep.ForeColor = Themes.Get(_selTheme).A1;

            _body.Controls.Clear();

            if (_step == 0)
            {
                _lblStep.Text = L.T("ADIM 1 / 3", "STEP 1 / 3");
                _lblTitle.Text = L.T("Hoş Geldiniz — Dil Seçimi", "Welcome — Choose Language");
                _lblSub.Text = L.T("OnMon arayüz dilini seçin (Ayarlardan her zaman değiştirebilirsiniz).", "Choose your interface language (can be changed anytime in Settings).");

                AddLangCard(24, 24, "🇹🇷 Türkçe", "Tam Türkçe arayüz, şehir bazlı namaz vakti ve hava durumu.", !L.IsEn, delegate { L.Set("tr", _d); RenderStep(); });
                AddLangCard(348, 24, "🇬🇧 English", "Full English UI, global cities, °C / °F & 12h/24h support.", L.IsEn, delegate { L.Set("en", _d); RenderStep(); });

                var note = new Label
                {
                    Left = 24, Top = 215, Width = 630, Height = 64, ForeColor = ModernColors.Dim,
                    Text = L.T(
                        "OnMon v" + Updater.AppVersion + ": Tüm PC markaları, çoklu GPU, dizüstü pil, tam ekran oyun içi mini HUD ve LibreHardwareMonitor sürücü desteği ile gelir.",
                        "OnMon v" + Updater.AppVersion + ": Includes universal PC brand & multi-GPU support, laptop battery tracking, in-game mini HUD, and LibreHardwareMonitor driver support.")
                };
                _body.Controls.Add(note);
            }
            else if (_step == 1)
            {
                _lblStep.Text = L.T("ADIM 2 / 3", "STEP 2 / 3");
                _lblTitle.Text = L.T("Düzen ve Tema Seçimi", "Layout & Color Theme");
                _lblSub.Text = L.T("Başlangıç için favori pano düzeninizi ve renginizi seçin.", "Pick a starter dashboard layout and accent theme.");

                var lblL = new Label { Left = 24, Top = 14, Width = 300, Height = 20, Text = L.T("PANO DÜZENİ", "DASHBOARD LAYOUT"), ForeColor = ModernColors.Dim, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
                _body.Controls.Add(lblL);

                string[,] layouts = {
                    { "bento", "Bento Modern", "Bento Modern" },
                    { "zen", "Zen Minimal", "Zen Minimal" },
                    { "esports", "eSports FPS", "eSports FPS" },
                    { "classic", "Klasik Pano", "Classic Dashboard" }
                };
                for (int i = 0; i < 4; i++)
                {
                    string lid = layouts[i, 0];
                    var b = new ModernButton
                    {
                        Left = 24 + (i % 2) * 316, Top = 40 + (i / 2) * 44, Width = 304, Height = 36,
                        Text = L.T(layouts[i, 1], layouts[i, 2]),
                        Checked = _selLayout == lid, AccentColor = Themes.Get(_selTheme).A1
                    };
                    b.Click += delegate { _selLayout = lid; RenderStep(); };
                    _body.Controls.Add(b);
                }

                var lblT = new Label { Left = 24, Top = 144, Width = 300, Height = 20, Text = L.T("RENK TEMASI", "COLOR THEME"), ForeColor = ModernColors.Dim, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
                _body.Controls.Add(lblT);

                string[] tids = { "rog", "frost", "nzxt", "oled" };
                for (int i = 0; i < tids.Length; i++)
                {
                    Theme th = Themes.Get(tids[i]);
                    var tb = new ModernButton
                    {
                        Left = 24 + (i % 2) * 316, Top = 170 + (i / 2) * 44, Width = 304, Height = 36,
                        Text = th.Title, Checked = _selTheme == th.Id, AccentColor = th.A1
                    };
                    tb.Click += delegate { _selTheme = th.Id; RenderStep(); };
                    _body.Controls.Add(tb);
                }
            }
            else
            {
                _lblStep.Text = L.T("ADIM 3 / 3", "STEP 3 / 3");
                _lblTitle.Text = L.T("Monitör & Sensör Sürücüsü", "Monitor & Sensor Driver");
                _lblSub.Text = L.T("Panonun açılacağı ekranı ve CPU/anakart sensör sürücüsünü doğrulayın.", "Confirm target screen and CPU/motherboard sensor driver.");

                var cMon = new ModernCard { Left = 24, Top = 16, Width = 630, Height = 104 };
                cMon.Controls.Add(new Label { Left = 16, Top = 12, Width = 400, Height = 22, Text = L.T("Hedef Monitör", "Target Display"), Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = ModernColors.Text });
                var cb = new ComboBox { Left = 16, Top = 42, Width = 590, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = ModernColors.InputBg, ForeColor = ModernColors.Text };
                Screen[] scrs = Screen.AllScreens;
                for (int i = 0; i < scrs.Length; i++)
                {
                    Screen sc = scrs[i];
                    cb.Items.Add((i + 1) + ". " + sc.DeviceName + " (" + sc.Bounds.Width + "×" + sc.Bounds.Height + (sc.Primary ? L.T(" · Ana", " · Primary") : "") + ")");
                }
                if (cb.Items.Count > 0) cb.SelectedIndex = Math.Max(0, Math.Min(cb.Items.Count - 1, _selScreenIdx));
                cb.SelectedIndexChanged += delegate { _selScreenIdx = Math.Max(0, cb.SelectedIndex); };
                cMon.Controls.Add(cb);
                _body.Controls.Add(cMon);

                bool admin = Program.IsAdministrator();
                var cDrv = new ModernCard { Left = 24, Top = 132, Width = 630, Height = 148 };
                cDrv.Controls.Add(new Label { Left = 16, Top = 12, Width = 400, Height = 22, Text = L.T("Sensör Erişimi & PawnIO", "Sensor Access & PawnIO"), Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = ModernColors.Text });
                cDrv.Controls.Add(new Label
                {
                    Left = 16, Top = 38, Width = 590, Height = 42, ForeColor = ModernColors.Dim,
                    Text = admin
                        ? L.T("Yönetici yetkisi aktif. CPU sıcaklık, watt ve anakart sensörleri için PawnIO sürücüsü önerilir (Bellek Bütünlüğü ile uyumlu).",
                              "Administrator mode active. PawnIO driver is recommended for CPU temp/power and SuperIO sensors (Core Isolation compatible).")
                        : L.T("Tam donanım sensörü için Yönetici olarak çalıştırmanız ve PawnIO sürücüsünü kurmanız önerilir.",
                              "Running as Administrator and installing the signed PawnIO driver is recommended for full hardware sensors.")
                });
                var bPawn = new ModernButton
                {
                    Left = 16, Top = 92, Width = 250, Height = 34, Variant = ModernButtonVariant.Secondary,
                    Text = L.T("PawnIO Sürücüsünü Kur (winget)", "Install PawnIO Driver (winget)"),
                    AccentColor = Themes.Get(_selTheme).A1
                };
                bPawn.Click += delegate
                {
                    try { Process.Start(new ProcessStartInfo("cmd.exe", "/c winget install --id namazso.PawnIO -e --accept-source-agreements --accept-package-agreements & echo. & echo Done. & pause") { UseShellExecute = true }); }
                    catch { }
                };
                cDrv.Controls.Add(bPawn);
                _body.Controls.Add(cDrv);
            }
        }

        private void AddLangCard(int left, int top, string title, string desc, bool selected, EventHandler onClick)
        {
            Color acc = Themes.Get(_selTheme).A1;
            var c = new ModernCard
            {
                Left = left, Top = top, Width = 306, Height = 166,
                BorderColor = selected ? acc : ModernColors.Border,
                Cursor = Cursors.Hand
            };
            var lblT = new Label { Left = 18, Top = 22, Width = 270, Height = 30, Text = title, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = selected ? acc : ModernColors.Text, BackColor = ModernColors.CardBg, Cursor = Cursors.Hand };
            var lblD = new Label { Left = 18, Top = 60, Width = 270, Height = 64, Text = desc, ForeColor = ModernColors.Dim, BackColor = ModernColors.CardBg, Cursor = Cursors.Hand };
            var btn = new ModernButton { Left = 18, Top = 122, Width = 140, Height = 30, Text = selected ? L.T("✓ Seçili", "✓ Selected") : L.T("Seç", "Select"), Checked = selected, AccentColor = acc };
            c.Click += onClick; lblT.Click += onClick; lblD.Click += onClick; btn.Click += onClick;
            c.Controls.Add(lblT); c.Controls.Add(lblD); c.Controls.Add(btn);
            _body.Controls.Add(c);
        }

        private void FinishWizard()
        {
            AppPrefs.Onboarded = true;
            if (_d != null && !_d.IsDisposed)
            {
                Screen[] all = Screen.AllScreens;
                if (_selScreenIdx >= 0 && _selScreenIdx < all.Length) _d.MoveToScreen(all[_selScreenIdx]);
                _d.SetTheme(Themes.Get(_selTheme));
                _d.SetLayout(_selLayout);
                _d.SaveSettings();
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
