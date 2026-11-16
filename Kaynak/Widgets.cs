using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    internal sealed class WInst
    {
        public string Id;
        public float X, Y, W, H;
        public bool Vis;
        public float Fs = 1f;
        public int Col;   // 0 = tema rengi
        public WInst Copy() { return (WInst)MemberwiseClone(); }
    }

    internal sealed class WDef
    {
        public string Id, Name;
        public float W, H, MinW, MinH;
        public Action<RectangleF, Snap> Draw;
    }

    public sealed partial class DashboardForm
    {
        // ---------------- widget kaydı ----------------
        private List<WDef> _wlist;
        private Dictionary<string, WDef> _wdefs;
        private List<WInst> _wd = new List<WInst>();
        private List<WInst> _wSaved;           // oyun modu geçici düzeni
        private float _fs = 1f;
        private float _chh = 1080f;
        private static readonly Color[] AccPal = {
            Color.Empty, Color.FromArgb(0, 229, 255), Color.FromArgb(61, 220, 132), Color.FromArgb(255, 193, 7),
            Color.FromArgb(255, 122, 24), Color.FromArgb(255, 77, 77), Color.FromArgb(255, 79, 216), Color.FromArgb(166, 107, 255), Color.FromArgb(232, 232, 232) };
        private static readonly string[] TplIds = { "classic", "hud", "cockpit", "aida", "studio" };
        private static readonly string[] TplNames = { "Klasik", "Oyun HUD", "Pro Kokpit", "Sensör Paneli", "Komuta Merkezi" };

        private static string WidgetsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "widgets.txt"); }
        }

        private void WReg()
        {
            if (_wlist != null) return;
            _wlist = new List<WDef>();
            Wd("hudbar", "Oyun şeridi", 1900, 86, 600, 70, (r, s) => PHudBar(r, s));
            Wd("hudfps", "FPS (HUD)", 800, 620, 380, 300, (r, s) => PHudFps(r, s));
            Wd("fpsl", "FPS (büyük)", 770, 300, 300, 160, (r, s) => PFps(r, s, true));
            Wd("fps", "FPS", 950, 244, 300, 160, (r, s) => PFps(r, s, false));
            Wd("cpu", "CPU", 560, 300, 260, 150, (r, s) => PCpu(r, s, true));
            Wd("gpu", "GPU", 560, 300, 260, 150, (r, s) => PGpu(r, s));
            Wd("mem", "Bellek", 450, 250, 220, 120, (r, s) => PMem(r, s));
            Wd("net", "Ağ", 450, 250, 220, 120, (r, s) => PNet(r, s));
            Wd("disks", "Diskler", 500, 250, 220, 120, (r, s) => PDisks(r, s));
            Wd("cores", "Çekirdekler", 650, 250, 260, 120, (r, s) => PCores(r, s));
            Wd("procs", "İşlemler", 380, 234, 220, 120, (r, s) => PProcs(r, s));
            Wd("apps", "Uygulamalar", 360, 338, 240, 200, (r, s) => PApps(r, s));
            Wd("clock", "Saat", 420, 234, 200, 100, (r, s) => PClock(r, s));
            Wd("sys", "Sistem", 530, 234, 220, 100, (r, s) => PSys(r, s));
            Wd("power", "Güç", 380, 250, 200, 100, (r, s) => PPower(r, s));
            Wd("temps", "Sıcaklıklar", 420, 250, 200, 100, (r, s) => PTemps(r, s));
            Wd("gpugraph", "GPU grafiği", 420, 250, 200, 100, (r, s) => PGpuGraph(r, s));
            Wd("fans", "Fanlar", 250, 250, 160, 100, (r, s) => PFans(r, s));
            Wd("volts", "Voltajlar", 200, 250, 160, 100, (r, s) => PVolts(r, s));
            Wd("sound", "Ses", 440, 338, 300, 160, (r, s) => PSound(r, s));
            Wd("aclock", "Saat (sensör)", 450, 245, 220, 100, (r, s) => PAClock(r, s));
            Wd("amem", "Bellek (sensör)", 470, 245, 220, 100, (r, s) => PAMem(r, s));
            Wd("amobo", "Anakart", 960, 245, 300, 100, (r, s) => PAMobo(r, s));
            Wd("acpu", "CPU (sensör)", 940, 550, 300, 200, (r, s) => PACpu(r, s));
            Wd("agpu", "GPU (sensör)", 940, 550, 300, 200, (r, s) => PAGpu(r, s));
            Wd("astorage", "Depolama", 940, 245, 300, 100, (r, s) => PAStorage(r, s));
            Wd("apower", "Güç (sensör)", 310, 245, 200, 100, (r, s) => PAPower(r, s));
            Wd("anet", "Ağ (sensör)", 630, 245, 260, 100, (r, s) => PANet(r, s));
            _wdefs = new Dictionary<string, WDef>();
            foreach (WDef d in _wlist) _wdefs[d.Id] = d;
        }

        private void Wd(string id, string name, float w, float h, float mw, float mh, Action<RectangleF, Snap> draw)
        {
            _wlist.Add(new WDef { Id = id, Name = name, W = w, H = h, MinW = mw, MinH = mh, Draw = draw });
        }

        // ---------------- şablonlar (eski düzenler) ----------------
        private List<WInst> WTemplate(string id)
        {
            WReg();
            var l = new List<WInst>();
            Action<string, RectangleF> A = delegate (string wid, RectangleF r) { l.Add(new WInst { Id = wid, X = r.X, Y = r.Y, W = r.Width, H = r.Height, Vis = true }); };
            RectangleF[] a, b, c, d;
            if (id == "studio")
            {
                a = Row(10, 300, 560, 770, 560); A("cpu", a[0]); A("fpsl", a[1]); A("gpu", a[2]);
                b = Row(320, 250, 650, 420, 420, 380); A("cores", b[0]); A("temps", b[1]); A("gpugraph", b[2]); A("power", b[3]);
                c = Row(580, 250, 450, 450, 500, 250, 200); A("mem", c[0]); A("net", c[1]); A("disks", c[2]); A("fans", c[3]); A("volts", c[4]);
                d = Row(840, 234, 540, 380, 530, 420); A("sound", d[0]); A("procs", d[1]); A("sys", d[2]); A("clock", d[3]);
            }
            else if (id == "aida")
            {
                a = Row(10, 245, 450, 470, 960); A("aclock", a[0]); A("amem", a[1]); A("amobo", a[2]);
                b = Row(265, 550, 940, 940); A("acpu", b[0]); A("agpu", b[1]);
                c = Row(825, 245, 940, 310, 630); A("astorage", c[0]); A("apower", c[1]); A("anet", c[2]);
            }
            else if (id == "cockpit")
            {
                a = Row(10, 300, 570, 798, 512); A("cpu", a[0]); A("cores", a[1]); A("gpu", a[2]);
                b = Row(320, 460, 596.8f, 686.3f, 596.8f);
                A("fps", b[0]);
                A("temps", new RectangleF(b[1].X, b[1].Y, b[1].Width, 225)); A("power", new RectangleF(b[1].X, b[1].Y + 235, b[1].Width, 225));
                A("mem", new RectangleF(b[2].X, b[2].Y, b[2].Width, 225)); A("net", new RectangleF(b[2].X, b[2].Y + 235, b[2].Width, 225));
                c = Row(790, 280, 561, 467.5f, 374, 467.5f); A("disks", c[0]); A("fans", c[1]); A("volts", c[2]); A("clock", c[3]);
            }
            else if (id == "classic")
            {
                a = Row(10, 236, 470, 470, 940); A("cpu", a[0]); A("gpu", a[1]); A("clock", a[2]);
                b = Row(256, 560, 945, 945);
                A("cores", new RectangleF(b[0].X, b[0].Y, b[0].Width, 275)); A("mem", new RectangleF(b[0].X, b[0].Y + 285, b[0].Width, 275));
                A("net", new RectangleF(b[1].X, b[1].Y, b[1].Width, 275)); A("disks", new RectangleF(b[1].X, b[1].Y + 285, b[1].Width, 275));
                c = Row(826, 244, 950, 323, 607); A("fps", c[0]); A("procs", c[1]); A("sys", c[2]);
            }
            else
            {
                A("hudbar", new RectangleF(10, 10, 1900, 86));
                a = Row(106, 620, 800, 560, 520);
                A("hudfps", a[0]);
                A("cpu", new RectangleF(a[1].X, a[1].Y, a[1].Width, 305)); A("gpu", new RectangleF(a[1].X, a[1].Y + 315, a[1].Width, 305));
                A("temps", new RectangleF(a[2].X, a[2].Y, a[2].Width, 200)); A("gpugraph", new RectangleF(a[2].X, a[2].Y + 210, a[2].Width, 200)); A("power", new RectangleF(a[2].X, a[2].Y + 420, a[2].Width, 200));
                b = Row(736, 338, 440, 360, 360, 340, 360); A("sound", b[0]); A("net", b[1]); A("disks", b[2]); A("mem", b[3]); A("apps", b[4]);
            }
            WFill(l);
            return l;
        }

        // listede olmayan widgetları gizli olarak ekle
        private void WFill(List<WInst> l)
        {
            foreach (WDef d in _wlist)
            {
                bool has = false;
                foreach (WInst w in l) if (w.Id == d.Id) { has = true; break; }
                if (!has) l.Add(new WInst { Id = d.Id, X = 100, Y = 100, W = d.W, H = d.H, Vis = false });
            }
        }

        internal bool WVis(string id)
        {
            for (int i = 0; i < _wd.Count; i++) if (_wd[i].Id == id && _wd[i].Vis) return true;
            return false;
        }

        private WInst WGet(string id)
        {
            for (int i = 0; i < _wd.Count; i++) if (_wd[i].Id == id) return _wd[i];
            return null;
        }

        // ---------------- kayıt / yükleme ----------------
        private void WLoad()
        {
            WReg();
            var l = new List<WInst>();
            try
            {
                if (File.Exists(WidgetsPath))
                {
                    foreach (string line in File.ReadAllLines(WidgetsPath))
                    {
                        string[] p = line.Split('|');
                        if (p.Length < 8 || !_wdefs.ContainsKey(p[0])) continue;
                        var w = new WInst { Id = p[0] };
                        var ic = CultureInfo.InvariantCulture;
                        w.X = float.Parse(p[1], ic); w.Y = float.Parse(p[2], ic); w.W = float.Parse(p[3], ic); w.H = float.Parse(p[4], ic);
                        w.Vis = p[5] == "1"; w.Fs = float.Parse(p[6], ic); int c; int.TryParse(p[7], out c);
                        w.Col = (c >= 0 && c < AccPal.Length) ? c : 0;
                        WDef d = _wdefs[w.Id];
                        w.W = Math.Max(d.MinW, w.W); w.H = Math.Max(d.MinH, w.H);
                        w.Fs = Math.Max(0.5f, Math.Min(2f, w.Fs));
                        bool dup = false; foreach (WInst o in l) if (o.Id == w.Id) dup = true;
                        if (!dup) l.Add(w);
                    }
                }
            }
            catch { l.Clear(); }
            if (l.Count == 0) { _wd = WTemplate(_layout); return; }
            WFill(l);
            _wd = l;
        }

        private void WSave()
        {
            if (_wSaved != null) return;   // oyun modunda geçici düzeni yazma
            try
            {
                string dir = Path.GetDirectoryName(WidgetsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                var ic = CultureInfo.InvariantCulture;
                foreach (WInst w in _wd)
                    sb.Append(w.Id).Append('|').Append(w.X.ToString("0.#", ic)).Append('|').Append(w.Y.ToString("0.#", ic)).Append('|').Append(w.W.ToString("0.#", ic)).Append('|').Append(w.H.ToString("0.#", ic))
                      .Append('|').Append(w.Vis ? "1" : "0").Append('|').Append(w.Fs.ToString("0.00", ic)).Append('|').Append(w.Col).AppendLine();
                File.WriteAllText(WidgetsPath, sb.ToString());
            }
            catch { }
        }

        private void ApplyTemplate(string id)
        {
            _layout = id; _wd = WTemplate(id); _sel = null;
            WSave(); SaveSettings(); _dirty = true; Invalidate();
        }

        // oyun başlayınca HUD'a geçici geçiş
        private void GameLayout(int ev)
        {
            if (_edit) return;
            if (ev == 1 && _wSaved == null && _layout != "hud") { _wSaved = _wd; _wd = WTemplate("hud"); }
            else if (ev == -1 && _wSaved != null) { _wd = _wSaved; _wSaved = null; }
            if (ev != 0) { _dirty = true; }
        }

        // ---------------- çizim ----------------
        private readonly Dictionary<string, Theme> _accThemes = new Dictionary<string, Theme>();
        private Theme AccTheme(Theme t, int col)
        {
            string k = t.Id + "|" + col;
            Theme o;
            if (_accThemes.TryGetValue(k, out o) && o.Bg == t.Bg) return o;
            Color c = AccPal[col];
            o = new Theme { Id = t.Id, Name = t.Name, Style = t.Style, Bars = t.Bars, Fx = t.Fx, Bg = t.Bg, Panel = t.Panel, Tile = t.Tile, Line = t.Line, A1 = c, A2 = Mix(c, t.A2, 0.45f), Track = t.Track, Tx = t.Tx, Dim = t.Dim, Crit = t.Crit, R = t.R, Glow = t.Glow, Mono = t.Mono };
            _accThemes[k] = o;
            return o;
        }

        private bool _wErr;
        private void DrawWidgets(Snap s)
        {
            WReg();
            for (int i = 0; i < _wd.Count; i++)
            {
                WInst w = _wd[i];
                if (!w.Vis) continue;
                WDef d;
                if (!_wdefs.TryGetValue(w.Id, out d)) continue;
                Theme saved = _th;
                if (w.Col > 0) _th = AccTheme(saved, w.Col);
                _fs = w.Fs;
                var r = new RectangleF(w.X, w.Y, w.W, w.H);
                var st = G.Save();
                G.SetClip(RectangleF.Inflate(r, 10, 10), CombineMode.Intersect);
                try { d.Draw(r, s); }
                catch (Exception ex) { if (!_wErr) { _wErr = true; Program.Log("widget " + w.Id, ex); } }
                G.Restore(st);
                _th = saved; _fs = 1f;
            }
        }

        // ---------------- düzenleme modu ----------------
        private bool _edit, _palOpen;
        private string _sel;
        private int _drag;                 // 0 yok, 1 taşı, 2 boyutlandır
        private float _dx, _dy;
        private int _lastEdPaint;
        private readonly List<KeyValuePair<RectangleF, string>> _edHits = new List<KeyValuePair<RectangleF, string>>();

        internal void ToggleEdit()
        {
            _edit = !_edit; _sel = null; _palOpen = false; _drag = 0;
            if (_edit && _wSaved != null) { _wd = _wSaved; _wSaved = null; }
            if (!_edit) WSave();
            _dirty = true; Invalidate();
        }

        private void DrawEditor()
        {
            Theme t = _th;
            WReg();
            _edHits.Clear();
            G.SmoothingMode = SmoothingMode.AntiAlias;
            // ızgara
            Pen gp = Pn(Al(t.A1, 22), 1);
            for (int x = 0; x <= 1920; x += 40) G.DrawLine(gp, x, 0, x, 1080);
            for (int y = 0; y <= 1080; y += 40) G.DrawLine(gp, 0, y, 1920, y);
            Font fn = F(16, true, false);
            foreach (WInst w in _wd)
            {
                if (!w.Vis) continue;
                WDef d; if (!_wdefs.TryGetValue(w.Id, out d)) continue;
                bool sel = w.Id == _sel;
                var r = new RectangleF(w.X, w.Y, w.W, w.H);
                Color oc = sel ? t.A1 : Al(t.A1, 150);
                using (var dp = new Pen(oc, sel ? 3f : 1.6f)) { if (!sel) dp.DashStyle = DashStyle.Dash; G.DrawRectangle(dp, r.X, r.Y, r.Width, r.Height); }
                string nm = d.Name + (w.Fs != 1f ? "  " + ((int)Math.Round(w.Fs * 100)) + "%" : "");
                float tw = TW(nm, fn) + 14;
                var tr = new RectangleF(r.X, r.Y, tw, 24);
                G.FillRectangle(Br(sel ? t.A1 : Al(t.Panel, 235)), tr);
                Txt(nm, fn, sel ? t.Bg : t.Tx, tr.X + 7, tr.Y, tw, 24, 0);
                var hnd = new RectangleF(r.Right - 26, r.Bottom - 26, 26, 26);
                G.FillRectangle(Br(sel ? t.A1 : Al(t.A1, 160)), hnd);
                G.DrawLine(Pn(t.Bg, 2), hnd.X + 8, hnd.Bottom - 5, hnd.Right - 5, hnd.Y + 8);
            }
            // araç çubuğu
            WInst cur = _sel != null ? WGet(_sel) : null;
            float by = _chh - 66;
            if (cur != null && cur.Y + cur.H > by - 6) by = 8;   // seçili widget altta ise çubuk üste geçer
            var bar = new RectangleF(10, by, 1900, 58);
            G.FillRectangle(Br(Al(t.Bg, 250)), bar);
            G.DrawRectangle(Pn(t.A1, 2), bar.X, bar.Y, bar.Width, bar.Height);
            float x0 = bar.X + 10, yb = bar.Y + 9, hb = 40;
            Font fb = F(20, true, false);
            x0 = EdBtn("✔ Bitti", "ed:done", x0, yb, hb, fb, true);
            x0 = EdBtn("⟲ Sıfırla", "ed:reset", x0, yb, hb, fb, false);
            int ti = Math.Max(0, Array.IndexOf(TplIds, _layout));
            x0 = EdBtn("Şablon: " + TplNames[ti], "ed:tpl", x0, yb, hb, fb, false);
            x0 = EdBtn("＋ Widget ekle", "ed:add", x0, yb, hb, fb, _palOpen);
            x0 += 14;
            if (cur != null)
            {
                WDef cd = _wdefs[cur.Id];
                Txt(cd.Name, fb, t.A1, x0, yb, 190, hb, 0); x0 += 170;
                x0 = EdBtn("A−", "ed:fs-", x0, yb, hb, fb, false);
                Txt(((int)Math.Round(cur.Fs * 100)) + "%", fb, t.Tx, x0, yb, 66, hb, 1); x0 += 66;
                x0 = EdBtn("A+", "ed:fs+", x0, yb, hb, fb, false);
                x0 += 10;
                x0 = EdBtn("◀", "ed:col-", x0, yb, hb, fb, false);
                var sw = new RectangleF(x0, yb + 4, 46, hb - 8);
                G.FillRectangle(Br(cur.Col == 0 ? t.A1 : AccPal[cur.Col]), sw);
                G.DrawRectangle(Pn(t.Tx, 1.5f), sw.X, sw.Y, sw.Width, sw.Height);
                Txt(cur.Col == 0 ? "tema" : "", F(14, true, false), t.Bg, sw.X, sw.Y, sw.Width, sw.Height, 1);
                x0 += 52;
                x0 = EdBtn("▶", "ed:col+", x0, yb, hb, fb, false);
                x0 = EdBtn("Gizle", "ed:hide", x0 + 10, yb, hb, fb, false);
            }
            else
            {
                Txt("Bir widget seç  ·  sürükle: taşı  ·  sağ-alt köşe: boyut  ·  Del: gizle  ·  Esc: bitir", F(17, false, true), t.Dim, x0, yb, 1100, hb, 0);
            }
            // ekleme paneli
            if (_palOpen)
            {
                var hid = new List<WInst>();
                foreach (WInst w in _wd) if (!w.Vis) hid.Add(w);
                int cols = 5; float cw = 300, ch = 40;
                int rows = Math.Max(1, (hid.Count + cols - 1) / cols);
                float ph = rows * (ch + 8) + 20;
                float py = by > 100 ? by - ph - 8 : by + 66;
                var pr = new RectangleF(10, py, cols * (cw + 8) + 12, ph);
                G.FillRectangle(Br(Al(t.Bg, 250)), pr);
                G.DrawRectangle(Pn(t.A1, 2), pr.X, pr.Y, pr.Width, pr.Height);
                if (hid.Count == 0) Txt("Tüm widgetlar zaten ekranda.", fb, t.Dim, pr.X + 14, pr.Y + 8, 600, 40, 0);
                for (int i = 0; i < hid.Count; i++)
                    EdBtnAt(_wdefs[hid[i].Id].Name, "ed:addw:" + hid[i].Id, pr.X + 10 + (i % cols) * (cw + 8), pr.Y + 10 + (i / cols) * (ch + 8), cw, ch, fb, false);
            }
        }

        private float EdBtn(string text, string id, float x, float y, float h, Font f, bool on)
        {
            float w = TW(text, f) + 30;
            EdBtnAt(text, id, x, y, w, h, f, on);
            return x + w + 8;
        }

        private void EdBtnAt(string text, string id, float x, float y, float w, float h, Font f, bool on)
        {
            Theme t = _th;
            var r = new RectangleF(x, y, w, h);
            using (GraphicsPath p = RR(r, 8)) { G.FillPath(Br(on ? t.A1 : t.Tile), p); G.DrawPath(Pn(on ? t.A1 : t.Line, 1.4f), p); }
            Txt(text, f, on ? t.Bg : t.Tx, x, y, w, h, 1);
            _edHits.Add(new KeyValuePair<RectangleF, string>(r, id));
        }

        private bool EditMouseDown(float cx, float cy)
        {
            foreach (var h in _edHits)
                if (h.Key.Contains(cx, cy)) { EditAction(h.Value); return true; }
            _palOpen = false;
            // en üstteki widget (listenin sonu üstte)
            for (int i = _wd.Count - 1; i >= 0; i--)
            {
                WInst w = _wd[i];
                if (!w.Vis) continue;
                if (new RectangleF(w.X, w.Y, w.W, w.H).Contains(cx, cy))
                {
                    _sel = w.Id;
                    _wd.RemoveAt(i); _wd.Add(w);   // öne al
                    if (cx > w.X + w.W - 34 && cy > w.Y + w.H - 34) { _drag = 2; _dx = w.X + w.W - cx; _dy = w.Y + w.H - cy; }
                    else { _drag = 1; _dx = cx - w.X; _dy = cy - w.Y; }
                    _dirty = true; Invalidate();
                    return true;
                }
            }
            _sel = null; _dirty = true; Invalidate();
            return true;
        }

        private static float Snap10(float v) { return (float)Math.Round(v / 10f) * 10f; }

        private void EditMouseMove(float cx, float cy)
        {
            if (_drag == 0 || _sel == null) return;
            int now = Environment.TickCount;
            if (now - _lastEdPaint < 20) return;
            _lastEdPaint = now;
            WInst w = WGet(_sel); if (w == null) return;
            WDef d = _wdefs[w.Id];
            if (_drag == 1)
            {
                w.X = Math.Max(0, Math.Min(1920 - w.W, Snap10(cx - _dx)));
                w.Y = Math.Max(0, Math.Min(1080 - w.H, Snap10(cy - _dy)));
            }
            else
            {
                w.W = Math.Max(d.MinW, Math.Min(1920 - w.X, Snap10(cx + _dx - w.X)));
                w.H = Math.Max(d.MinH, Math.Min(1080 - w.Y, Snap10(cy + _dy - w.Y)));
            }
            _dirty = true; Invalidate();
        }

        private void EditMouseUp()
        {
            if (_drag != 0) { _drag = 0; WSave(); }
        }

        private void EditAction(string a)
        {
            WInst cur = _sel != null ? WGet(_sel) : null;
            if (a == "ed:done") ToggleEdit();
            else if (a == "ed:reset") { _wd = WTemplate(_layout); _sel = null; WSave(); }
            else if (a == "ed:tpl")
            {
                int i = (Math.Max(0, Array.IndexOf(TplIds, _layout)) + 1) % TplIds.Length;
                ApplyTemplate(TplIds[i]);
            }
            else if (a == "ed:add") _palOpen = !_palOpen;
            else if (a.StartsWith("ed:addw:"))
            {
                WInst w = WGet(a.Substring(8));
                if (w != null)
                {
                    w.Vis = true; w.Fs = 1f;
                    WDef d = _wdefs[w.Id];
                    w.W = d.W; w.H = d.H;
                    w.X = Snap10(Math.Max(0, Math.Min(1920 - w.W, (1920 - w.W) / 2f))); w.Y = Snap10(Math.Max(0, Math.Min(1080 - w.H, (1080 - w.H) / 2f)));
                    _wd.Remove(w); _wd.Add(w); _sel = w.Id; _palOpen = false; WSave();
                }
            }
            else if (cur != null)
            {
                if (a == "ed:fs-") cur.Fs = Math.Max(0.5f, (float)Math.Round(cur.Fs - 0.1f, 2));
                else if (a == "ed:fs+") cur.Fs = Math.Min(2f, (float)Math.Round(cur.Fs + 0.1f, 2));
                else if (a == "ed:col+") cur.Col = (cur.Col + 1) % AccPal.Length;
                else if (a == "ed:col-") cur.Col = (cur.Col + AccPal.Length - 1) % AccPal.Length;
                else if (a == "ed:hide") { cur.Vis = false; _sel = null; }
                WSave();
            }
            _dirty = true; Invalidate();
        }

        private void EditKey(KeyEventArgs e)
        {
            WInst cur = _sel != null ? WGet(_sel) : null;
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.E) { ToggleEdit(); return; }
            if (cur == null) return;
            WDef d = _wdefs[cur.Id];
            float step = e.Shift ? 40f : 10f;
            if (e.KeyCode == Keys.Delete) { cur.Vis = false; _sel = null; }
            else if (e.Control)
            {
                if (e.KeyCode == Keys.Left) cur.W = Math.Max(d.MinW, cur.W - step);
                else if (e.KeyCode == Keys.Right) cur.W = Math.Min(1920 - cur.X, cur.W + step);
                else if (e.KeyCode == Keys.Up) cur.H = Math.Max(d.MinH, cur.H - step);
                else if (e.KeyCode == Keys.Down) cur.H = Math.Min(1080 - cur.Y, cur.H + step);
                else return;
            }
            else if (e.KeyCode == Keys.Left) cur.X = Math.Max(0, cur.X - step);
            else if (e.KeyCode == Keys.Right) cur.X = Math.Min(1920 - cur.W, cur.X + step);
            else if (e.KeyCode == Keys.Up) cur.Y = Math.Max(0, cur.Y - step);
            else if (e.KeyCode == Keys.Down) cur.Y = Math.Min(1080 - cur.H, cur.Y + step);
            else return;
            WSave(); _dirty = true; Invalidate();
        }
    }
}
