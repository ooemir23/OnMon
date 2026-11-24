using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ProSystemMonitor
{
    internal sealed class LayoutMeta
    {
        public string FilePath = "";
        public string Id = "";
        public string Name = "";
        public string Author = "";
        public string Description = "";
        public int CanvasW = 1920, CanvasH = 1080;
        public string ThemeId = "rog";
        public string Category = "custom";
        public int WidgetCount;
        public bool BuiltIn;
        public Bitmap Thumb;
    }

    public sealed partial class DashboardForm
    {
        internal bool _rcEdit;   // sağ tık doğrudan düzenleme modunu açsın

        internal static string TemplateDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "Sablonlar"); }
        }

        private string PngBase64(string path, int max)
        {
            Image im = GetImg(path, max);
            if (im == null) return null;
            using (var ms = new MemoryStream()) { im.Save(ms, ImageFormat.Png); return Convert.ToBase64String(ms.ToArray()); }
        }

        private static string BitmapBase64(Bitmap bmp)
        {
            if (bmp == null) return "";
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return Convert.ToBase64String(ms.ToArray()); }
        }

        private static string SanitizeFileName(string name)
        {
            string s = (name ?? "sablon").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length == 0 ? "sablon" : s;
        }

        /// <summary>Düzeni (widget'lar, tuval, tema, arka plan, resimler, küçük resim) tek bir .onmon v2 dosyasına yazar.</summary>
        internal void ExportTemplate()
        {
            ExportTemplateFrom(_wd, _cw, _ch, _th, _bgCol, _bgImg, _gfs, _gzoom, _topBar, _hideStrip, _margin);
        }

        internal void ExportTemplateFrom(List<WInst> widgets, int cw, int ch, Theme th, int bgCol, string bgImg, float gfs, float gzoom, bool topBar, bool hideStrip, int margin)
        {
            string def = L.T("sablonum", "my_layout");
            Directory.CreateDirectory(TemplateDir);
            using (var sd = new SaveFileDialog { Title = L.T("Şablonu kaydet / paylaş", "Save / Share Layout"), Filter = L.T("OnMon şablonu (*.onmon)|*.onmon", "OnMon Layout (*.onmon)|*.onmon"), FileName = def, InitialDirectory = TemplateDir, DefaultExt = "onmon", OverwritePrompt = true })
            {
                if (sd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    string name = Path.GetFileNameWithoutExtension(sd.FileName);
                    string txt = BuildTemplateText(name, Environment.UserName, "", widgets, cw, ch, th, bgCol, bgImg, gfs, gzoom, topBar, hideStrip, margin);
                    File.WriteAllText(sd.FileName, txt, new UTF8Encoding(false));
                }
                catch (Exception ex) { MessageBox.Show(L.T("Kaydedilemedi: ", "Could not save: ") + ex.Message, "OnMon"); }
            }
        }

        internal string SaveTemplateToGallery(string name, string author, string desc, List<WInst> widgets, int cw, int ch, Theme th, int bgCol, string bgImg, float gfs, float gzoom, bool topBar, bool hideStrip, int margin)
        {
            Directory.CreateDirectory(TemplateDir);
            string fn = Path.Combine(TemplateDir, SanitizeFileName(name) + ".onmon");
            string txt = BuildTemplateText(name, author, desc, widgets, cw, ch, th, bgCol, bgImg, gfs, gzoom, topBar, hideStrip, margin);
            File.WriteAllText(fn, txt, new UTF8Encoding(false));
            return fn;
        }

        internal string BuildTemplateText(string name)
        {
            return BuildTemplateText(name, Environment.UserName, "", _wd, _cw, _ch, _th, _bgCol, _bgImg, _gfs, _gzoom, _topBar, _hideStrip, _margin);
        }

        internal string BuildTemplateText(string name, string author, string desc, List<WInst> widgets, int cw, int ch, Theme th, int bgCol, string bgImg, float gfs, float gzoom, bool topBar, bool hideStrip, int margin)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("ONMON-SABLON 2");
            sb.AppendLine("name=" + (name ?? "").Replace("\r", " ").Replace("\n", " "));
            if (!string.IsNullOrEmpty(author)) sb.AppendLine("author=" + author.Replace("\r", " ").Replace("\n", " "));
            if (!string.IsNullOrEmpty(desc)) sb.AppendLine("desc=" + desc.Replace("\r", " ").Replace("\n", " "));
            sb.AppendLine("canvas=" + cw + "," + ch);
            sb.AppendLine("gzoom=" + gzoom.ToString(ic));
            sb.AppendLine("gfs=" + gfs.ToString(ic));
            sb.AppendLine("bgcol=" + bgCol);
            sb.AppendLine("theme=" + (th != null ? th.Id : "rog"));
            sb.AppendLine("topbar=" + (topBar ? "1" : "0"));
            sb.AppendLine("hidestrip=" + (hideStrip ? "1" : "0"));
            sb.AppendLine("margin=" + margin);

            using (Bitmap thumb = RenderLayoutThumbnail(widgets, cw, ch, th, bgCol, bgImg, gfs, gzoom, 320, 180))
            {
                string tb64 = BitmapBase64(thumb);
                if (!string.IsNullOrEmpty(tb64)) sb.AppendLine("thumb=" + tb64);
            }

            var imgs = new List<string>();
            var orig = new Dictionary<WInst, string>();
            foreach (WInst w in widgets)
                if (DashboardForm.BaseId(w.Id) == "image" && !string.IsNullOrEmpty(w.Path))
                {
                    int k = imgs.IndexOf(w.Path); if (k < 0) { imgs.Add(w.Path); k = imgs.Count - 1; }
                    orig[w] = w.Path; w.Path = "@img" + k;
                }
            string wl;
            try { wl = WLinesText(widgets); } finally { foreach (var kv in orig) kv.Key.Path = kv.Value; }
            int bgk = -1;
            if (!string.IsNullOrEmpty(bgImg)) { bgk = imgs.IndexOf(bgImg); if (bgk < 0) { imgs.Add(bgImg); bgk = imgs.Count - 1; } }
            sb.AppendLine("bgimg=" + (bgk >= 0 ? "@img" + bgk : ""));
            foreach (string line in wl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) sb.AppendLine("W:" + line);
            for (int i = 0; i < imgs.Count; i++)
            {
                string b64 = PngBase64(imgs[i], 1600);
                if (b64 != null) sb.AppendLine("I:img" + i + ":" + b64);
            }
            return sb.ToString();
        }

        internal void ImportTemplate()
        {
            Directory.CreateDirectory(TemplateDir);
            using (var od = new OpenFileDialog { Title = L.T("Şablon dosyası seç", "Select Layout File"), Filter = L.T("OnMon şablonu (*.onmon)|*.onmon|Tümü|*.*", "OnMon Layout (*.onmon)|*.onmon|All|*.*"), InitialDirectory = TemplateDir })
                if (od.ShowDialog() == DialogResult.OK) ImportAndApplyExternalTemplate(od.FileName, 0);
        }

        internal void ImportAndApplyExternalTemplate(string file) { ImportAndApplyExternalTemplate(file, 0); }

        internal void ImportAndApplyExternalTemplate(string file, int scaleMode)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
                Directory.CreateDirectory(TemplateDir);
                string fullSrc = Path.GetFullPath(file);
                string fullDir = Path.GetFullPath(TemplateDir);
                string target = fullSrc;
                if (!fullSrc.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
                {
                    target = Path.Combine(TemplateDir, Path.GetFileName(file));
                    if (!string.Equals(fullSrc, target, StringComparison.OrdinalIgnoreCase))
                        File.Copy(fullSrc, target, true);
                }
                ApplyTemplateFile(target, scaleMode);
            }
            catch (Exception ex) { MessageBox.Show(L.T("Şablon eklenemedi: ", "Could not import layout: ") + ex.Message, "OnMon"); }
        }

        internal void ApplyTemplateFile(string file) { ApplyTemplateFile(file, 0); }

        internal bool LoadTemplateData(string file, out List<WInst> list, out int cw, out int ch, out float gz, out float gf, out int bgc, out string themeId, out string bgimg, out bool topBar, out bool hideStrip, out int margin, out string name, out string author, out string desc)
        {
            list = new List<WInst>();
            cw = _cw; ch = _ch; gz = _gzoom; gf = _gfs; bgc = _bgCol;
            themeId = _th.Id; bgimg = ""; topBar = _topBar; hideStrip = _hideStrip; margin = _margin;
            name = Path.GetFileNameWithoutExtension(file); author = ""; desc = "";

            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            if (lines.Length == 0 || !lines[0].StartsWith("ONMON-SABLON")) return false;
            var ic = CultureInfo.InvariantCulture;
            var wl = new List<string>();
            var imgPaths = new Dictionary<string, string>();
            string imgDir = Path.Combine(TemplateDir, "resimler");
            foreach (string ln in lines)
            {
                if (ln.StartsWith("W:")) wl.Add(ln.Substring(2));
                else if (ln.StartsWith("I:"))
                {
                    int c2 = ln.IndexOf(':', 2); if (c2 < 0) continue;
                    string key = ln.Substring(2, c2 - 2);
                    byte[] data = Convert.FromBase64String(ln.Substring(c2 + 1));
                    Directory.CreateDirectory(imgDir);
                    string fn = Path.Combine(imgDir, SanitizeFileName(Path.GetFileNameWithoutExtension(file)) + "_" + key + ".png");
                    File.WriteAllBytes(fn, data); imgPaths["@" + key] = fn;
                }
                else
                {
                    int e = ln.IndexOf('='); if (e < 0) continue;
                    string k = ln.Substring(0, e), v = ln.Substring(e + 1);
                    if (k == "name" && v.Length > 0) name = v;
                    else if (k == "author") author = v;
                    else if (k == "desc") desc = v;
                    else if (k == "canvas") { string[] q = v.Split(','); cw = int.Parse(q[0], ic); ch = int.Parse(q[1], ic); }
                    else if (k == "gzoom") gz = float.Parse(v, ic);
                    else if (k == "gfs") gf = float.Parse(v, ic);
                    else if (k == "bgcol") bgc = int.Parse(v, ic);
                    else if (k == "theme") themeId = v;
                    else if (k == "bgimg") bgimg = v;
                    else if (k == "topbar") topBar = v != "0";
                    else if (k == "hidestrip") hideStrip = v == "1";
                    else if (k == "margin") int.TryParse(v, out margin);
                }
            }
            WReg();
            list = ParseWLines(wl);
            foreach (WInst w in list) { string rp; if (w.Path != null && imgPaths.TryGetValue(w.Path, out rp)) w.Path = rp; }
            string rb;
            bgimg = (bgimg != null && imgPaths.TryGetValue(bgimg, out rb)) ? rb : "";
            return list.Count > 0;
        }

        /// <summary>scaleMode: 0 = farklı çözünürlükte sor, 1 = mevcut tuvale ölçekle, 2 = şablon çözünürlüğünü uygula.</summary>
        internal void ApplyTemplateFile(string file, int scaleMode)
        {
            try
            {
                List<WInst> list; int cw, ch, bgc, margin; float gz, gf; string themeId, bgimg, name, author, desc; bool topBar, hideStrip;
                if (!LoadTemplateData(file, out list, out cw, out ch, out gz, out gf, out bgc, out themeId, out bgimg, out topBar, out hideStrip, out margin, out name, out author, out desc))
                {
                    MessageBox.Show(L.T("Bu dosya geçerli bir OnMon şablonu değil veya içinde widget yok.", "Invalid OnMon layout file or empty widget list."), "OnMon");
                    return;
                }

                cw = Math.Max(200, Math.Min(8000, cw)); ch = Math.Max(120, Math.Min(8000, ch));
                int mode = scaleMode;
                if (mode == 0 && (cw != _cw || ch != _ch))
                {
                    DialogResult dr = MessageBox.Show(
                        L.T(
                            "Bu şablon " + cw + "×" + ch + " için hazırlanmış (senin tuvalin: " + _cw + "×" + _ch + ").\n\nEvet: Mevcut tuval boyutuma uyarla (önerilen)\nHayır: Şablonun kendi çözünürlüğünü kullan",
                            "This layout was designed for " + cw + "×" + ch + " (your canvas: " + _cw + "×" + _ch + ").\n\nYes: Scale to fit my current canvas (recommended)\nNo: Switch to the layout's original resolution"),
                        L.T("Çözünürlük uyarlama", "Resolution Scaling"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (dr == DialogResult.Cancel) return;
                    mode = dr == DialogResult.Yes ? 1 : 2;
                }

                if (mode == 1 && (cw != _cw || ch != _ch))
                {
                    float kx = _cw / (float)cw, ky = _ch / (float)ch;
                    foreach (WInst x in list)
                    {
                        x.X = (float)Math.Round(x.X * kx); x.Y = (float)Math.Round(x.Y * ky);
                        x.W = (float)Math.Round(x.W * kx); x.H = (float)Math.Round(x.H * ky);
                    }
                    gz = Math.Max(0.2f, Math.Min(4f, gz * Math.Min(kx, ky)));
                }
                else
                {
                    _cw = cw; _ch = ch;
                }

                WFill(list);
                foreach (WInst x in list)
                {
                    x.W = Math.Min(x.W, _cw); x.H = Math.Min(x.H, _ch);
                    x.X = Math.Max(0, Math.Min(_cw - x.W, x.X)); x.Y = Math.Max(0, Math.Min(_ch - x.H, x.Y));
                }

                _gzoom = Math.Max(0.2f, Math.Min(4f, gz));
                _gfs = Math.Max(0.3f, Math.Min(3f, gf));
                _bgCol = bgc;
                _bgImg = bgimg;
                _topBar = topBar;
                _hideStrip = hideStrip;
                _margin = margin;
                if (!string.IsNullOrEmpty(themeId)) _th = Themes.Get(themeId);
                if (_wSaved != null) _wSaved = null;
                _wd = list; _sel = null;
                WSave(); SaveSettings(); _dirty = true; Invalidate();
            }
            catch (Exception ex) { MessageBox.Show(L.T("Şablon yüklenemedi: ", "Could not load layout: ") + ex.Message, "OnMon"); }
        }

        internal LayoutMeta ReadTemplateMeta(string file, bool loadThumb)
        {
            var meta = new LayoutMeta { FilePath = file, Id = file, Name = Path.GetFileNameWithoutExtension(file) };
            try
            {
                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                if (lines.Length == 0 || !lines[0].StartsWith("ONMON-SABLON")) return null;
                var ic = CultureInfo.InvariantCulture;
                string tb64 = null;
                var wl = new List<string>();
                int bgc = 0; float gf = 1f, gz = 1f;
                foreach (string ln in lines)
                {
                    if (ln.StartsWith("W:")) { wl.Add(ln.Substring(2)); continue; }
                    if (ln.StartsWith("I:")) continue;
                    int e = ln.IndexOf('='); if (e < 0) continue;
                    string k = ln.Substring(0, e), v = ln.Substring(e + 1);
                    if (k == "name" && v.Length > 0) meta.Name = v;
                    else if (k == "author") meta.Author = v;
                    else if (k == "desc") meta.Description = v;
                    else if (k == "canvas") { string[] q = v.Split(','); if (q.Length >= 2) { int.TryParse(q[0], out meta.CanvasW); int.TryParse(q[1], out meta.CanvasH); } }
                    else if (k == "theme" && v.Length > 0) meta.ThemeId = v;
                    else if (k == "bgcol") int.TryParse(v, out bgc);
                    else if (k == "gfs") float.TryParse(v, NumberStyles.Float, ic, out gf);
                    else if (k == "gzoom") float.TryParse(v, NumberStyles.Float, ic, out gz);
                    else if (k == "thumb") tb64 = v;
                }
                WReg();
                var list = ParseWLines(wl);
                int visCount = 0;
                foreach (WInst w in list) if (w.Vis) visCount++;
                meta.WidgetCount = visCount;

                if (loadThumb)
                {
                    if (!string.IsNullOrEmpty(tb64))
                    {
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(tb64);
                            using (var ms = new MemoryStream(bytes))
                            using (Image tmp = Image.FromStream(ms))
                                meta.Thumb = new Bitmap(tmp);
                        }
                        catch { meta.Thumb = null; }
                    }
                    if (meta.Thumb == null && list.Count > 0)
                        meta.Thumb = RenderLayoutThumbnail(list, meta.CanvasW, meta.CanvasH, Themes.Get(meta.ThemeId), bgc, "", gf, gz, 240, 135);
                }
                return meta;
            }
            catch { return null; }
        }

        internal static bool RegisterOnmonFileAssociation()
        {
            try
            {
                string exe = Application.ExecutablePath;
                using (RegistryKey ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.onmon"))
                    if (ext != null) ext.SetValue("", "OnMon.Template");
                using (RegistryKey prog = Registry.CurrentUser.CreateSubKey(@"Software\Classes\OnMon.Template"))
                    if (prog != null) prog.SetValue("", "OnMon Düzen Şablonu");
                using (RegistryKey cmd = Registry.CurrentUser.CreateSubKey(@"Software\Classes\OnMon.Template\shell\open\command"))
                    if (cmd != null) cmd.SetValue("", "\"" + exe + "\" \"%1\"");
                return true;
            }
            catch { return false; }
        }
    }
}
