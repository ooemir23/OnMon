using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    internal sealed class GalleryControl : Panel
    {
        private static readonly Color Bg = Color.FromArgb(22, 24, 29), BarBg = Color.FromArgb(26, 29, 37),
            CardBg = Color.FromArgb(30, 34, 43), LineC = Color.FromArgb(56, 62, 76),
            TxC = Color.FromArgb(234, 236, 242), DimC = Color.FromArgb(148, 154, 170),
            AccC = Color.FromArgb(0, 200, 255);

        private static readonly string[] CuratedIds = {
            "zen", "minimalring", "bento", "esports", "extreme", "creator", "panoramic", "vertical",
            "classic", "hud", "cockpit", "aida", "studio", "empty"
        };

        private readonly DashboardForm _d;
        private readonly FlowLayoutPanel _flow;
        private readonly CheckBox _ckAutoScale;
        private readonly List<Bitmap> _thumbs = new List<Bitmap>();
        private readonly List<Button> _catBtns = new List<Button>();
        private string _cat = "all";
        private List<CommunityItem> _commCache;
        private bool _commBusy;

        internal sealed class CommunityItem
        {
            public string Id = "", Name = "", Author = "", DescTr = "", DescEn = "", ThemeId = "rog", Res = "1920x1080", File = "";
            public int Widgets;
        }

        internal static List<CommunityItem> ParseCommunityJson(string json)
        {
            var list = new List<CommunityItem>();
            if (string.IsNullOrEmpty(json)) return list;
            foreach (Match om in Regex.Matches(json, @"\{[^{}]*\}"))
            {
                string obj = om.Value;
                var c = new CommunityItem
                {
                    Id = JsonStr(obj, "id"),
                    Name = JsonStr(obj, "name"),
                    Author = JsonStr(obj, "author"),
                    DescTr = JsonStr(obj, "desc_tr"),
                    DescEn = JsonStr(obj, "desc_en"),
                    ThemeId = JsonStr(obj, "theme"),
                    Res = JsonStr(obj, "res"),
                    File = JsonStr(obj, "file"),
                    Widgets = JsonInt(obj, "widgets")
                };
                if (!string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.File)) list.Add(c);
            }
            return list;
        }

        private static string JsonStr(string obj, string key)
        {
            Match m = Regex.Match(obj, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static int JsonInt(string obj, string key)
        {
            Match m = Regex.Match(obj, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\d+)");
            int n; return m.Success && int.TryParse(m.Groups[1].Value, out n) ? n : 0;
        }

        private sealed class CardFlow : FlowLayoutPanel
        {
            public CardFlow() { DoubleBuffered = true; }
        }

        public GalleryControl(DashboardForm d)
        {
            _d = d;
            BackColor = Bg;
            AllowDrop = true;

            var top = new Panel { Dock = DockStyle.Top, Height = 114, BackColor = BarBg };
            top.Paint += delegate (object s, PaintEventArgs e) { using (var p = new Pen(LineC)) e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1); };
            Controls.Add(top);

            int x = 10, y1 = 10, y2 = 46, y3 = 80;
            x = Btn(top, L.T("➕ Dosyadan Ekle (.onmon)…", "➕ Import File (.onmon)…"), x, y1, 186, true, delegate { ImportDialog(); });
            x = Btn(top, L.T("💾 Mevcut Düzeni Kaydet…", "💾 Save Current Layout…"), x, y1, 180, false, delegate { SaveCurrentDialog(); });
            x = Btn(top, L.T("📤 Paylaş (.onmon)…", "📤 Share / Export…"), x, y1, 152, false, delegate { _d.ExportTemplate(); });
            x = Btn(top, L.T("🎨 Stüdyoyu Aç", "🎨 Open Studio"), x, y1, 126, false, delegate { _d.OpenStudio(); });
            Btn(top, L.T("🔗 .onmon İlişkilendir", "🔗 Associate .onmon"), x, y1, 154, false, delegate
            {
                bool ok = DashboardForm.RegisterOnmonFileAssociation();
                MessageBox.Show(
                    ok ? L.T(".onmon dosyaları artık çift tıklandığında doğrudan OnMon ile açılacak.", ".onmon files will now open directly with OnMon when double-clicked.")
                       : L.T("Dosya ilişkilendirmesi yazılamadı.", "Could not register file association."),
                    "OnMon", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            });

            int fx = 10;
            string[] cIds = { "all", "modern", "minimal", "gaming", "detailed", "custom", "community" };
            string[] cNms = {
                L.T("Tümü (14+)", "All (14+)"),
                L.T("✨ Modern", "✨ Modern"),
                L.T("🌿 Sade", "🌿 Minimal"),
                L.T("🎮 Oyun", "🎮 Gaming"),
                L.T("🔬 Detaylı", "🔬 Detailed"),
                L.T("📁 Kayıtlı", "📁 Saved"),
                L.T("🌐 Topluluk", "🌐 Community")
            };
            for (int i = 0; i < cIds.Length; i++)
            {
                string cid = cIds[i];
                var cb = new Button
                {
                    Text = cNms[i], Left = fx, Top = y2, Width = 112, Height = 26, FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8.2f, FontStyle.Bold), Tag = cid
                };
                cb.Click += delegate { _cat = cid; UpdateCatStyles(); RefreshCards(); };
                top.Controls.Add(cb);
                _catBtns.Add(cb);
                fx += 116;
            }
            UpdateCatStyles();

            _ckAutoScale = new CheckBox
            {
                Text = L.T("Eklenen düzenleri ekranıma otomatik ölçekle (", "Auto-scale imported layouts to my canvas (") + _d._cw + "×" + _d._ch + ")",
                Left = 12, Top = y3 + 2, Width = 470, Height = 24, Checked = true, ForeColor = TxC, BackColor = Color.Transparent
            };
            top.Controls.Add(_ckAutoScale);
            var tip = new Label
            {
                Text = L.T("İpucu: .onmon dosyalarını buraya sürükleyip bırakabilirsin.", "Tip: Drag and drop .onmon files anywhere here."),
                Left = 490, Top = y3 + 5, Width = 420, Height = 20, ForeColor = DimC
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

        private void UpdateCatStyles()
        {
            foreach (Button b in _catBtns)
            {
                bool on = (b.Tag as string) == _cat;
                b.BackColor = on ? AccC : CardBg;
                b.ForeColor = on ? Color.Black : TxC;
                b.FlatAppearance.BorderColor = on ? AccC : LineC;
            }
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
            using (var od = new OpenFileDialog { Title = L.T("Düzen dosyası (.onmon) ekle", "Import Layout File (.onmon)"), Filter = L.T("OnMon şablonu (*.onmon)|*.onmon|Tümü|*.*", "OnMon Layout (*.onmon)|*.onmon|All|*.*"), Multiselect = true })
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

            if (_cat == "community")
            {
                ShowCommunityCards();
                _flow.ResumeLayout();
                return;
            }

            _d.WReg();
            var list = new List<LayoutMeta>();
            if (_cat != "custom")
            {
                for (int i = 0; i < CuratedIds.Length; i++)
                {
                    string id = CuratedIds[i];
                    string cat = DashboardForm.GetTplCat(id);
                    if (_cat != "all" && cat != _cat) continue;
                    List<WInst> ws = _d.WTemplate(id, 1920, 1080);
                    int vis = 0; foreach (WInst w in ws) if (w.Vis) vis++;
                    Bitmap thm = _d.RenderLayoutThumbnail(ws, 1920, 1080, _d._th, 0, "", 1f, 1f, 240, 135);
                    _thumbs.Add(thm);
                    list.Add(new LayoutMeta
                    {
                        Id = id, Name = DashboardForm.GetTplName(id), Author = L.T("OnMon Yerleşik", "OnMon Built-in"),
                        Description = DashboardForm.GetTplDesc(id), Category = cat,
                        CanvasW = 1920, CanvasH = 1080, ThemeId = _d._th.Id, WidgetCount = vis, BuiltIn = true, Thumb = thm
                    });
                }
            }

            if (_cat == "all" || _cat == "custom")
            {
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
                                m.Category = "custom";
                                if (m.Thumb != null) _thumbs.Add(m.Thumb);
                                list.Add(m);
                            }
                        }
                    }
                }
                catch { }
            }

            if (list.Count == 0)
            {
                var emptyLbl = new Label
                {
                    Text = L.T("Bu kategoride henüz kayıtlı bir düzen yok.", "No saved layouts in this category yet."),
                    Width = 520, Height = 44, ForeColor = DimC, Font = new Font("Segoe UI", 10.5f), Padding = new Padding(8, 12, 0, 0)
                };
                _flow.Controls.Add(emptyLbl);
            }
            else
            {
                foreach (LayoutMeta m in list) _flow.Controls.Add(BuildCard(m));
            }
            _flow.ResumeLayout();
        }

        private Panel BuildCard(LayoutMeta meta)
        {
            bool active = meta.BuiltIn && meta.Id == _d._layout;
            var card = new Panel
            {
                Width = 236, Height = 224, Margin = new Padding(8), BackColor = CardBg, Cursor = Cursors.Hand
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
                string badge = meta.WidgetCount + " widget";
                Font bf = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                Size sz = TextRenderer.MeasureText(badge, bf);
                var br = new Rectangle(pic.Width - sz.Width - 6, pic.Height - 18, sz.Width + 4, 15);
                using (var b = new SolidBrush(Color.FromArgb(195, 12, 14, 18))) e.Graphics.FillRectangle(b, br);
                TextRenderer.DrawText(e.Graphics, badge, bf, br, AccC, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (active)
                {
                    string actTxt = L.T("AKTİF", "ACTIVE");
                    var ar = new Rectangle(4, 4, 58, 16);
                    using (var ab = new SolidBrush(Color.FromArgb(220, AccC))) e.Graphics.FillRectangle(ab, ar);
                    TextRenderer.DrawText(e.Graphics, actTxt, bf, ar, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            string subTxt = !string.IsNullOrEmpty(meta.Description)
                ? meta.Description
                : ((string.IsNullOrEmpty(meta.Author) ? L.T("Özel Şablon", "Custom Layout") : meta.Author) + "  ·  " + meta.CanvasW + "×" + meta.CanvasH);
            var sub = new Label
            {
                Text = subTxt, Left = 8, Top = 156, Width = 220, Height = 20,
                ForeColor = DimC, Font = new Font("Segoe UI", 8f), AutoEllipsis = true
            };
            card.Controls.Add(title);
            card.Controls.Add(sub);

            int bx = 8, by = 186;
            var bApply = MakeCardBtn(L.T("Uygula", "Apply"), bx, by, meta.BuiltIn ? 106 : 74, true, delegate { ApplyCard(meta); });
            card.Controls.Add(bApply);
            bx += bApply.Width + 4;
            var bEdit = MakeCardBtn(L.T("Düzenle", "Customize"), bx, by, meta.BuiltIn ? 106 : 68, false, delegate { ApplyCard(meta); _d.OpenStudio(); });
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
            cm.Items.Add(L.T("Düzeni uygula", "Apply Layout"), null, delegate { ApplyCard(meta); });
            cm.Items.Add(L.T("Stüdyoda düzenle…", "Edit in Layout Studio…"), null, delegate { ApplyCard(meta); _d.OpenStudio(); });
            if (!meta.BuiltIn)
            {
                cm.Items.Add(L.T("Paylaş / Farklı kaydet…", "Share / Export As…"), null, delegate { ShareCard(meta); });
                cm.Items.Add(L.T("Sil", "Delete"), null, delegate { DeleteCard(meta); });
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
            using (var sd = new SaveFileDialog { Title = L.T("Şablonu paylaş", "Share Layout"), Filter = L.T("OnMon şablonu (*.onmon)|*.onmon", "OnMon Layout (*.onmon)|*.onmon"), FileName = Path.GetFileName(meta.FilePath), DefaultExt = "onmon" })
            {
                if (sd.ShowDialog(FindForm()) == DialogResult.OK)
                    File.Copy(meta.FilePath, sd.FileName, true);
            }
        }

        private void DeleteCard(LayoutMeta meta)
        {
            if (string.IsNullOrEmpty(meta.FilePath) || !File.Exists(meta.FilePath)) return;
            if (MessageBox.Show(FindForm(), L.T("\"" + meta.Name + "\" düzeni silinsin mi?", "Delete layout \"" + meta.Name + "\"?"), "OnMon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { File.Delete(meta.FilePath); } catch { }
            RefreshCards();
        }

        private void ShowCommunityCards()
        {
            if (_commCache != null && _commCache.Count > 0)
            {
                foreach (CommunityItem ci in _commCache) _flow.Controls.Add(BuildCommunityCard(ci));
                return;
            }
            if (_commBusy) return;
            _commBusy = true;

            _flow.Controls.Add(new Label
            {
                Text = L.T("🌐 Topluluk düzenleri indiriliyor…", "🌐 Loading community layouts…"),
                Width = 500, Height = 44, ForeColor = TxC, Font = new Font("Segoe UI", 10.5f), Padding = new Padding(8, 12, 0, 0)
            });

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<CommunityItem> items = null;
                string err = null;
                try
                {
                    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls; } catch { }
                    using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "OnMon-Gallery";
                        string json = wc.DownloadString("https://raw.githubusercontent.com/ooemir23/OnMon/main/layouts/index.json?t=" + DateTime.UtcNow.Ticks);
                        items = ParseCommunityJson(json);
                    }
                }
                catch (Exception ex) { err = ex.Message; }

                if (items == null || items.Count == 0)
                {
                    try
                    {
                        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layouts", "index.json");
                        if (File.Exists(local))
                            items = ParseCommunityJson(File.ReadAllText(local, Encoding.UTF8));
                    }
                    catch { }
                }

                _commBusy = false;
                if (items != null && items.Count > 0) _commCache = items;

                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (IsDisposed || _cat != "community") return;
                            _flow.SuspendLayout();
                            foreach (Control c in _flow.Controls) c.Dispose();
                            _flow.Controls.Clear();
                            if (_commCache != null && _commCache.Count > 0)
                            {
                                foreach (CommunityItem ci in _commCache) _flow.Controls.Add(BuildCommunityCard(ci));
                            }
                            else
                            {
                                _flow.Controls.Add(new Label
                                {
                                    Text = L.T("Topluluk galerisi alınamadı: ", "Could not load community gallery: ") + (err ?? ""),
                                    Width = 640, Height = 48, ForeColor = Color.FromArgb(245, 158, 11), Font = new Font("Segoe UI", 10f), Padding = new Padding(8, 12, 0, 0)
                                });
                            }
                            _flow.ResumeLayout();
                        });
                    }
                }
                catch { }
            });
        }

        private Panel BuildCommunityCard(CommunityItem item)
        {
            Theme th = Themes.Get(item.ThemeId);
            var card = new Panel
            {
                Width = 236, Height = 224, Margin = new Padding(8), BackColor = CardBg
            };
            card.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var p = new Pen(LineC, 1f)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            string cachedLocal = Path.Combine(DashboardForm.TemplateDir, Path.GetFileName(item.File));
            string devLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layouts", Path.GetFileName(item.File));
            string previewSrc = File.Exists(cachedLocal) ? cachedLocal : (File.Exists(devLocal) ? devLocal : null);
            Bitmap thm = null;
            if (previewSrc != null)
            {
                LayoutMeta lm = _d.ReadTemplateMeta(previewSrc, true);
                if (lm != null && lm.Thumb != null) { thm = lm.Thumb; _thumbs.Add(thm); }
            }

            if (thm != null)
            {
                var pic = new PictureBox { Left = 8, Top = 8, Width = 220, Height = 124, SizeMode = PictureBoxSizeMode.Zoom, Image = thm, BackColor = Color.Black };
                card.Controls.Add(pic);
            }
            else
            {
                var banner = new Panel { Left = 8, Top = 8, Width = 220, Height = 124, BackColor = th.Bg };
                banner.Paint += delegate (object s, PaintEventArgs e)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var ab = new SolidBrush(th.A1)) e.Graphics.FillRectangle(ab, 0, 0, 5, banner.Height);
                    using (var b2 = new SolidBrush(th.A2)) e.Graphics.FillEllipse(b2, 16, 18, 18, 18);
                    TextRenderer.DrawText(e.Graphics, th.Title, new Font("Segoe UI", 9.5f, FontStyle.Bold), new Rectangle(42, 16, 168, 24), th.Tx, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(e.Graphics, item.Res + " · " + item.Widgets + " widgets", new Font("Consolas", 8.5f), new Rectangle(16, 54, 190, 22), th.A1, TextFormatFlags.Left);
                    TextRenderer.DrawText(e.Graphics, "🌐 GitHub Community", new Font("Segoe UI", 8f), new Rectangle(16, 90, 190, 22), th.Dim, TextFormatFlags.Left);
                };
                card.Controls.Add(banner);
            }

            card.Controls.Add(new Label
            {
                Text = item.Name, Left = 8, Top = 136, Width = 220, Height = 20,
                ForeColor = TxC, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoEllipsis = true
            });
            card.Controls.Add(new Label
            {
                Text = L.IsEn ? (string.IsNullOrEmpty(item.DescEn) ? item.DescTr : item.DescEn) : (string.IsNullOrEmpty(item.DescTr) ? item.DescEn : item.DescTr),
                Left = 8, Top = 156, Width = 220, Height = 22, ForeColor = DimC, Font = new Font("Segoe UI", 7.8f), AutoEllipsis = true
            });

            var bDown = MakeCardBtn(L.T("⬇ İndir ve Uygula", "⬇ Download & Apply"), 8, 186, 220, true, delegate { DownloadAndApplyCommunity(item); });
            card.Controls.Add(bDown);
            return card;
        }

        private void DownloadAndApplyCommunity(CommunityItem item)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Directory.CreateDirectory(DashboardForm.TemplateDir);
                    string safeName = Path.GetFileName(item.File);
                    string dest = Path.Combine(DashboardForm.TemplateDir, safeName);
                    bool downloaded = false;
                    try
                    {
                        try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls; } catch { }
                        using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = "OnMon-Gallery";
                            string content = wc.DownloadString("https://raw.githubusercontent.com/ooemir23/OnMon/main/layouts/" + Uri.EscapeDataString(safeName) + "?t=" + DateTime.UtcNow.Ticks);
                            if (content.StartsWith("ONMON-SABLON"))
                            {
                                File.WriteAllText(dest, content, new UTF8Encoding(false));
                                downloaded = true;
                            }
                        }
                    }
                    catch { }

                    if (!downloaded)
                    {
                        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layouts", safeName);
                        if (File.Exists(local)) { File.Copy(local, dest, true); downloaded = true; }
                    }

                    if (!downloaded || !File.Exists(dest)) throw new Exception(L.T("Dosya indirilemedi.", "Could not download layout file."));

                    int mode = _ckAutoScale.Checked ? 1 : 2;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _d.ImportAndApplyExternalTemplate(dest, mode);
                        RefreshCards();
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        MessageBox.Show(FindForm(), L.T("Düzen indirilemedi: ", "Could not download layout: ") + ex.Message, "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
            });
        }
    }

    internal sealed class GalleryForm : Form
    {
        private readonly GalleryControl _ctrl;

        public GalleryForm(DashboardForm d)
        {
            ModernUi.ApplyDarkTitleBar(this);
            Text = L.T("OnMon — Düzen Galerisi ve Paylaşım", "OnMon — Layout Gallery & Sharing");
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1060, 710);
            MinimumSize = new Size(840, 540);
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
