using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    internal sealed class GalleryControl : Panel
    {
        private static readonly Color Bg = Color.FromArgb(22, 24, 29), BarBg = Color.FromArgb(26, 29, 37),
            CardBg = Color.FromArgb(30, 34, 43), LineC = Color.FromArgb(56, 62, 76),
            TxC = Color.FromArgb(234, 236, 242), DimC = Color.FromArgb(148, 154, 170),
            AccC = Color.FromArgb(0, 200, 255);

        private readonly DashboardForm _d;
        private readonly FlowLayoutPanel _flow;
        private readonly CheckBox _ckAutoScale;
        private readonly List<Bitmap> _thumbs = new List<Bitmap>();

        private sealed class CardFlow : FlowLayoutPanel
        {
            public CardFlow() { DoubleBuffered = true; }
        }

        public GalleryControl(DashboardForm d)
        {
            _d = d;
            BackColor = Bg;
            AllowDrop = true;

            var top = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = BarBg };
            top.Paint += delegate (object s, PaintEventArgs e) { using (var p = new Pen(LineC)) e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1); };
            Controls.Add(top);

            int x = 10, y1 = 10, y2 = 46;
            x = Btn(top, "➕ Dosyadan Ekle (.onmon)…", x, y1, 186, true, delegate { ImportDialog(); });
            x = Btn(top, "💾 Mevcut Düzeni Kaydet…", x, y1, 176, false, delegate { SaveCurrentDialog(); });
            x = Btn(top, "📤 Paylaş (.onmon)…", x, y1, 146, false, delegate { _d.ExportTemplate(); });
            x = Btn(top, "🎨 Stüdyoyu Aç", x, y1, 122, false, delegate { _d.OpenStudio(); });
            Btn(top, "🔗 .onmon İlişkilendir", x, y1, 152, false, delegate
            {
                bool ok = DashboardForm.RegisterOnmonFileAssociation();
                MessageBox.Show(ok ? ".onmon dosyaları artık çift tıklandığında doğrudan OnMon ile açılacak." : "Dosya ilişkilendirmesi yazılamadı.", "OnMon", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            });

            _ckAutoScale = new CheckBox
            {
                Text = "Eklenen düzenleri mevcut ekran çözünürlüğüme otomatik ölçekle (" + _d._cw + "×" + _d._ch + ")",
                Left = 12, Top = y2 + 2, Width = 470, Height = 24, Checked = true, ForeColor = TxC, BackColor = Color.Transparent
            };
            top.Controls.Add(_ckAutoScale);
            var tip = new Label
            {
                Text = "İpucu: .onmon dosyalarını buraya sürükleyip bırakabilirsin.",
                Left = 490, Top = y2 + 5, Width = 380, Height = 20, ForeColor = DimC
            };
            top.Controls.Add(tip);

            _flow = new CardFlow
            {
                Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
                BackColor = Bg, Padding = new Padding(12), AllowDrop = true
            };
            Controls.Add(_flow);
            _flow.BringToFront();

            DragEnter += OnDragEnterHandler;
            DragDrop += OnDragDropHandler;
            _flow.DragEnter += OnDragEnterHandler;
            _flow.DragDrop += OnDragDropHandler;

            RefreshCards();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearThumbs();
            base.Dispose(disposing);
        }

        private void ClearThumbs()
        {
            foreach (Bitmap b in _thumbs) { try { if (b != null) b.Dispose(); } catch { } }
            _thumbs.Clear();
        }

        private int Btn(Control p, string text, int x, int y, int w, bool primary, EventHandler click)
        {
            var b = new Button
            {
                Text = text, Left = x, Top = y, Width = w, Height = 28, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(0, 128, 175) : CardBg, ForeColor = Color.White, Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, primary ? FontStyle.Bold : FontStyle.Regular)
            };
            b.FlatAppearance.BorderColor = primary ? AccC : LineC;
            b.Click += click;
            p.Controls.Add(b);
            return x + w + 6;
        }

        private void OnDragEnterHandler(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] fs = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (fs != null && Array.Exists(fs, x => x.EndsWith(".onmon", StringComparison.OrdinalIgnoreCase)))
                    e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnDragDropHandler(object sender, DragEventArgs e)
        {
            try
            {
                string[] fs = e.Data != null ? (e.Data.GetData(DataFormats.FileDrop) as string[]) : null;
                if (fs == null) return;
                int mode = _ckAutoScale.Checked ? 1 : 2;
                foreach (string f in fs)
                {
                    if (f.EndsWith(".onmon", StringComparison.OrdinalIgnoreCase))
                        _d.ImportAndApplyExternalTemplate(f, mode);
                }
                RefreshCards();
            }
            catch { }
        }

        private void ImportDialog()
        {
            using (var od = new OpenFileDialog { Title = "Düzen dosyası (.onmon) ekle", Filter = "OnMon şablonu (*.onmon)|*.onmon|Tümü|*.*", Multiselect = true })
            {
                if (od.ShowDialog(FindForm()) != DialogResult.OK) return;
                int mode = _ckAutoScale.Checked ? 1 : 2;
                foreach (string f in od.FileNames)
                    _d.ImportAndApplyExternalTemplate(f, mode);
                RefreshCards();
            }
        }

        private void SaveCurrentDialog()
        {
            using (var dlg = new SaveTemplateModal(_d))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                _d.SaveTemplateToGallery(dlg.TplName, dlg.TplAuthor, dlg.TplDesc, _d._wd, _d._cw, _d._ch, _d._th, _d._bgCol, _d._bgImg, _d._gfs, _d._gzoom, _d._topBar, _d._hideStrip, _d._margin);
                RefreshCards();
            }
        }

        public void RefreshCards()
        {
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls) c.Dispose();
            _flow.Controls.Clear();
            ClearThumbs();

            _d.WReg();
            var list = new List<LayoutMeta>();
            string[] builtIds = { "classic", "hud", "cockpit", "aida", "studio", "empty" };
            string[] builtNames = { "Klasik", "Oyun HUD", "Pro Kokpit", "Sensör Paneli", "Komuta Merkezi", "Boş Tuval" };
            for (int i = 0; i < builtIds.Length; i++)
            {
                List<WInst> ws = _d.WTemplate(builtIds[i], 1920, 1080);
                int vis = 0; foreach (WInst w in ws) if (w.Vis) vis++;
                Bitmap thm = _d.RenderLayoutThumbnail(ws, 1920, 1080, _d._th, 0, "", 1f, 1f, 240, 135);
                _thumbs.Add(thm);
                list.Add(new LayoutMeta
                {
                    Id = builtIds[i], Name = builtNames[i], Author = "OnMon Yerleşik",
                    CanvasW = 1920, CanvasH = 1080, ThemeId = _d._th.Id, WidgetCount = vis, BuiltIn = true, Thumb = thm
                });
            }

            try
            {
                string dir = DashboardForm.TemplateDir;
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.onmon"))
                    {
                        LayoutMeta m = _d.ReadTemplateMeta(f, true);
                        if (m != null)
                        {
                            if (m.Thumb != null) _thumbs.Add(m.Thumb);
                            list.Add(m);
                        }
                    }
                }
            }
            catch { }

            foreach (LayoutMeta m in list) _flow.Controls.Add(BuildCard(m));
            _flow.ResumeLayout();
        }

        private Panel BuildCard(LayoutMeta meta)
        {
            bool active = meta.BuiltIn && meta.Id == _d._layout;
            var card = new Panel
            {
                Width = 236, Height = 216, Margin = new Padding(8), BackColor = CardBg, Cursor = Cursors.Hand
            };
            card.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var p = new Pen(active ? AccC : LineC, active ? 2f : 1f))
                    g.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            var pic = new PictureBox
            {
                Left = 8, Top = 8, Width = 220, Height = 124, SizeMode = PictureBoxSizeMode.Zoom,
                Image = meta.Thumb, BackColor = Color.Black
            };
            pic.Paint += delegate (object s, PaintEventArgs e)
            {
                string badge = meta.CanvasW + "×" + meta.CanvasH;
                Font bf = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                Size sz = TextRenderer.MeasureText(badge, bf);
                var br = new Rectangle(pic.Width - sz.Width - 6, pic.Height - 18, sz.Width + 4, 15);
                using (var b = new SolidBrush(Color.FromArgb(190, 12, 14, 18))) e.Graphics.FillRectangle(b, br);
                TextRenderer.DrawText(e.Graphics, badge, bf, br, AccC, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (active)
                {
                    var ar = new Rectangle(4, 4, 56, 16);
                    using (var ab = new SolidBrush(Color.FromArgb(220, AccC))) e.Graphics.FillRectangle(ab, ar);
                    TextRenderer.DrawText(e.Graphics, "AKTİF", bf, ar, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };
            pic.DoubleClick += delegate { ApplyCard(meta); };
            card.Controls.Add(pic);

            var title = new Label
            {
                Text = meta.Name, Left = 8, Top = 136, Width = 220, Height = 20,
                ForeColor = active ? AccC : TxC, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoEllipsis = true
            };
            string subTxt = (string.IsNullOrEmpty(meta.Author) ? "Özel Şablon" : meta.Author) + "  ·  " + meta.WidgetCount + " widget";
            var sub = new Label
            {
                Text = subTxt, Left = 8, Top = 156, Width = 220, Height = 16,
                ForeColor = DimC, Font = new Font("Segoe UI", 8f), AutoEllipsis = true
            };
            card.Controls.Add(title);
            card.Controls.Add(sub);

            int bx = 8, by = 180;
            var bApply = MakeCardBtn("Uygula", bx, by, meta.BuiltIn ? 106 : 74, true, delegate { ApplyCard(meta); });
            card.Controls.Add(bApply);
            bx += bApply.Width + 4;
            var bEdit = MakeCardBtn("Düzenle", bx, by, meta.BuiltIn ? 106 : 68, false, delegate { ApplyCard(meta); _d.OpenStudio(); });
            card.Controls.Add(bEdit);
            bx += bEdit.Width + 4;

            if (!meta.BuiltIn)
            {
                var bShare = MakeCardBtn("📤", bx, by, 34, false, delegate { ShareCard(meta); });
                card.Controls.Add(bShare);
                bx += 38;
                var bDel = MakeCardBtn("🗑", bx, by, 32, false, delegate { DeleteCard(meta); });
                card.Controls.Add(bDel);
            }

            var cm = new ContextMenuStrip();
            cm.Items.Add("Düzeni uygula", null, delegate { ApplyCard(meta); });
            cm.Items.Add("Stüdyoda düzenle…", null, delegate { ApplyCard(meta); _d.OpenStudio(); });
            if (!meta.BuiltIn)
            {
                cm.Items.Add("Paylaş / Farklı kaydet…", null, delegate { ShareCard(meta); });
                cm.Items.Add("Sil", null, delegate { DeleteCard(meta); });
            }
            card.ContextMenuStrip = cm;
            pic.ContextMenuStrip = cm;
            return card;
        }

        private Button MakeCardBtn(string text, int x, int y, int w, bool accent, EventHandler click)
        {
            var b = new Button
            {
                Text = text, Left = x, Top = y, Width = w, Height = 26, FlatStyle = FlatStyle.Flat,
                BackColor = accent ? Color.FromArgb(0, 128, 175) : Bg, ForeColor = TxC,
                Font = new Font("Segoe UI", 8.2f, accent ? FontStyle.Bold : FontStyle.Regular), Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = accent ? AccC : LineC;
            b.Click += click;
            return b;
        }

        private void ApplyCard(LayoutMeta meta)
        {
            if (meta.BuiltIn)
                _d.SetLayout(meta.Id);
            else
                _d.ApplyTemplateFile(meta.FilePath, _ckAutoScale.Checked ? 1 : 2);
            RefreshCards();
        }

        private void ShareCard(LayoutMeta meta)
        {
            if (string.IsNullOrEmpty(meta.FilePath) || !File.Exists(meta.FilePath)) return;
            using (var sd = new SaveFileDialog { Title = "Şablonu paylaş", Filter = "OnMon şablonu (*.onmon)|*.onmon", FileName = Path.GetFileName(meta.FilePath), DefaultExt = "onmon" })
            {
                if (sd.ShowDialog(FindForm()) == DialogResult.OK)
                    File.Copy(meta.FilePath, sd.FileName, true);
            }
        }

        private void DeleteCard(LayoutMeta meta)
        {
            if (string.IsNullOrEmpty(meta.FilePath) || !File.Exists(meta.FilePath)) return;
            if (MessageBox.Show(FindForm(), "\"" + meta.Name + "\" düzeni silinsin mi?", "OnMon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { File.Delete(meta.FilePath); } catch { }
            RefreshCards();
        }
    }

    internal sealed class GalleryForm : Form
    {
        private readonly GalleryControl _ctrl;

        public GalleryForm(DashboardForm d)
        {
            Text = "OnMon — Düzen Galerisi ve Paylaşım";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1060, 680);
            MinimumSize = new Size(800, 520);
            BackColor = Color.FromArgb(22, 24, 29);
            ForeColor = Color.FromArgb(234, 236, 242);
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Program.TrayIcon(); } catch { }

            _ctrl = new GalleryControl(d) { Dock = DockStyle.Fill };
            Controls.Add(_ctrl);
        }

        public void RefreshCards() { _ctrl.RefreshCards(); }
    }
}
