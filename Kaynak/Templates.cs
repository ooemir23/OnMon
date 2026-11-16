using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ProSystemMonitor
{
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

        /// <summary>Düzeni (widget'lar, tuval, tema, arka plan, resimler) tek bir dosyaya yazar.</summary>
        internal void ExportTemplate()
        {
            string def = "sablonum";
            Directory.CreateDirectory(TemplateDir);
            using (var sd = new SaveFileDialog { Title = "Şablonu kaydet / paylaş", Filter = "OnMon şablonu (*.onmon)|*.onmon", FileName = def, InitialDirectory = TemplateDir, DefaultExt = "onmon", OverwritePrompt = true })
            {
                if (sd.ShowDialog() != DialogResult.OK) return;
                try { File.WriteAllText(sd.FileName, BuildTemplateText(Path.GetFileNameWithoutExtension(sd.FileName)), new UTF8Encoding(false)); }
                catch (Exception ex) { MessageBox.Show("Kaydedilemedi: " + ex.Message, "OnMon"); }
            }
        }

        private string BuildTemplateText(string name)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("ONMON-SABLON 1");
            sb.AppendLine("name=" + name);
            sb.AppendLine("canvas=" + _cw + "," + _ch);
            sb.AppendLine("gzoom=" + _gzoom.ToString(ic));
            sb.AppendLine("gfs=" + _gfs.ToString(ic));
            sb.AppendLine("bgcol=" + _bgCol);
            sb.AppendLine("theme=" + _th.Id);
            var imgs = new List<string>();
            var orig = new Dictionary<WInst, string>();
            foreach (WInst w in _wd)
                if (DashboardForm.BaseId(w.Id) == "image" && !string.IsNullOrEmpty(w.Path))
                {
                    int k = imgs.IndexOf(w.Path); if (k < 0) { imgs.Add(w.Path); k = imgs.Count - 1; }
                    orig[w] = w.Path; w.Path = "@img" + k;
                }
            string wl;
            try { wl = WLinesText(); } finally { foreach (var kv in orig) kv.Key.Path = kv.Value; }
            int bgk = -1;
            if (!string.IsNullOrEmpty(_bgImg)) { bgk = imgs.IndexOf(_bgImg); if (bgk < 0) { imgs.Add(_bgImg); bgk = imgs.Count - 1; } }
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
            using (var od = new OpenFileDialog { Title = "Şablon dosyası seç", Filter = "OnMon şablonu (*.onmon)|*.onmon|Tümü|*.*", InitialDirectory = TemplateDir })
                if (od.ShowDialog() == DialogResult.OK) ApplyTemplateFile(od.FileName);
        }

        internal void ApplyTemplateFile(string file)
        {
            try
            {
                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                if (lines.Length == 0 || !lines[0].StartsWith("ONMON-SABLON")) { MessageBox.Show("Bu dosya geçerli bir OnMon şablonu değil.", "OnMon"); return; }
                var ic = CultureInfo.InvariantCulture;
                var wl = new List<string>();
                var imgPaths = new Dictionary<string, string>();
                int cw = _cw, ch = _ch; float gz = _gzoom, gf = _gfs; int bgc = _bgCol; string theme = null, bgimg = "";
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
                        string fn = Path.Combine(imgDir, Path.GetFileNameWithoutExtension(file) + "_" + key + ".png");
                        File.WriteAllBytes(fn, data); imgPaths["@" + key] = fn;
                    }
                    else
                    {
                        int e = ln.IndexOf('='); if (e < 0) continue;
                        string k = ln.Substring(0, e), v = ln.Substring(e + 1);
                        if (k == "canvas") { string[] q = v.Split(','); cw = int.Parse(q[0]); ch = int.Parse(q[1]); }
                        else if (k == "gzoom") gz = float.Parse(v, ic);
                        else if (k == "gfs") gf = float.Parse(v, ic);
                        else if (k == "bgcol") bgc = int.Parse(v);
                        else if (k == "theme") theme = v;
                        else if (k == "bgimg") bgimg = v;
                    }
                }
                WReg();
                var list = ParseWLines(wl);
                foreach (WInst w in list) { string rp; if (w.Path != null && imgPaths.TryGetValue(w.Path, out rp)) w.Path = rp; }
                if (list.Count == 0) { MessageBox.Show("Şablonda widget bulunamadı.", "OnMon"); return; }
                WFill(list);
                _cw = Math.Max(200, Math.Min(8000, cw)); _ch = Math.Max(120, Math.Min(8000, ch));
                _gzoom = Math.Max(0.2f, Math.Min(4f, gz)); _gfs = Math.Max(0.3f, Math.Min(3f, gf)); _bgCol = bgc;
                string rb; _bgImg = (bgimg != null && imgPaths.TryGetValue(bgimg, out rb)) ? rb : "";
                if (theme != null) foreach (Theme t in Themes.All) if (t.Id == theme) { _th = t; break; }
                if (_wSaved != null) _wSaved = null;
                _wd = list; _sel = null;
                WSave(); SaveSettings(); _dirty = true; Invalidate();
            }
            catch (Exception ex) { MessageBox.Show("Şablon yüklenemedi: " + ex.Message, "OnMon"); }
        }
    }
}
