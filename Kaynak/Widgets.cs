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
        public int A1, A2, Tx, PanelC, FrameC;      // ARGB, 0 = tema
        public int PAlpha = 100;                    // panel opaklığı %
        public string Frame = "", Bars = "", Glow = "", Cap = "";
        public float FrameW, Radius = -1f, GaugeW;  // 0 / -1 = tema
        public bool NoTitle, Locked;
        public int Align = 1;
        public float Zoom = 1f;
        public string Text = "", Path = "";
        public WInst Copy() { return (WInst)MemberwiseClone(); }
    }

    internal sealed class WDef
    {
        public string Id, Name, NameEn, Category, CategoryEn;
        public float W, H, MinW, MinH;
        public bool Multi;
        public Action<RectangleF, Snap> Draw;
        public string Title { get { return L.T(Name, string.IsNullOrEmpty(NameEn) ? Name : NameEn); } }
        public string CatTitle { get { return L.T(Category, string.IsNullOrEmpty(CategoryEn) ? Category : CategoryEn); } }
    }

    public sealed partial class DashboardForm
    {
        // ---------------- widget kaydı ----------------
        internal List<WDef> _wlist;
        private Dictionary<string, WDef> _wdefs;
        internal List<WInst> _wd = new List<WInst>();
        private List<WInst> _wSaved;           // oyun modu geçici düzeni
        private float _fs = 1f;
        private float _chh = 1080f;
        internal int _cw = 1920, _ch = 1080, _bgCol;
        internal float _grid = 10f, _gfs = 1f, _gzoom = 1f;
        internal bool _showGrid = true, _snapOn = true;
        internal string _bgImg = "";
        private WInst _cur;
        private static readonly Color[] AccPal = {
            Color.Empty, Color.FromArgb(0, 229, 255), Color.FromArgb(61, 220, 132), Color.FromArgb(255, 193, 7),
            Color.FromArgb(255, 122, 24), Color.FromArgb(255, 77, 77), Color.FromArgb(255, 79, 216), Color.FromArgb(166, 107, 255), Color.FromArgb(232, 232, 232) };
        internal static readonly string[] TplIds = {
            "empty", "zen", "minimalring", "bento", "esports", "extreme", "creator", "panoramic", "vertical",
            "classic", "hud", "cockpit", "aida", "studio"
        };

        internal static string GetTplName(string id)
        {
            switch (id)
            {
                case "empty": return L.T("Boş Tuval (Sıfırdan)", "Blank Canvas (Start Fresh)");
                case "zen": return L.T("Zen Minimal (Sade)", "Zen Minimal");
                case "minimalring": return L.T("Minimal Halkalar", "Minimal Rings");
                case "bento": return L.T("Modern Bento Izgara", "Modern Bento Grid");
                case "esports": return L.T("E-Spor Rekabetçi", "Esports Competitive");
                case "extreme": return L.T("Ekstrem Mühendislik", "Extreme Telemetry");
                case "creator": return L.T("Yayıncı & Stüdyo", "Creator & Streamer");
                case "panoramic": return L.T("Panoramik Şerit", "Panoramic Strip");
                case "vertical": return L.T("Dikey / Kompakt Kule", "Vertical / Compact Tower");
                case "classic": return L.T("Klasik Pano", "Classic Dashboard");
                case "hud": return L.T("Oyun HUD", "Gaming HUD");
                case "cockpit": return L.T("Pro Kokpit", "Pro Cockpit");
                case "aida": return L.T("Sensör Paneli (AIDA)", "Sensor Panel (AIDA)");
                case "studio": return L.T("Komuta Merkezi", "Command Center");
                default: return id ?? "";
            }
        }

        internal static string GetTplCat(string id)
        {
            switch (id)
            {
                case "zen":
                case "minimalring":
                case "vertical":
                case "empty":
                    return "minimal";
                case "bento":
                case "creator":
                case "panoramic":
                case "classic":
                    return "modern";
                case "esports":
                case "hud":
                    return "gaming";
                case "extreme":
                case "cockpit":
                case "aida":
                case "studio":
                    return "detailed";
                default:
                    return "custom";
            }
        }

        internal static string GetTplDesc(string id)
        {
            switch (id)
            {
                case "empty": return L.T("Kendi tasarımın için boş başlangıç", "Clean slate for custom design");
                case "zen": return L.T("Ferah, göz yormayan sade kartlar", "Spacious, distraction-free cards");
                case "minimalring": return L.T("Çift halka + tek bakışta özet", "Dual ring + clean summary");
                case "bento": return L.T("Modern asimetrik Bento modülleri", "Modern modular Bento grid");
                case "esports": return L.T("FPS, %1 Low, kare süresi ve termal", "FPS, 1% Low, frametime & thermals");
                case "extreme": return L.T("Tüm sensörler, voltaj ve fan matrisi", "Full sensors, voltages & fan matrix");
                case "creator": return L.T("Yayın, ses mikseri, RAM ve ağ odaklı", "Audio mixer, top apps, VRAM & net");
                case "panoramic": return L.T("Yatay performans şeritleri ve mini kartlar", "Horizontal performance strip & cards");
                case "vertical": return L.T("İkili kule dizilimi ve yan ekran uyumu", "Stacked 2-column tower layout");
                case "classic": return L.T("Dengeli günlük sistem izleme panosu", "Balanced everyday system monitor");
                case "hud": return L.T("Oyun sırasında otomatik açılan HUD", "Full gaming HUD with session stats");
                case "cockpit": return L.T("Çoklu kadran ve grafik kokpiti", "Multi-dial performance cockpit");
                case "aida": return L.T("Teknik donanım ve anakart raporu", "Deep hardware & motherboard panel");
                case "studio": return L.T("16 panelli tam kapsamlı merkez", "16-panel full command station");
                default: return "";
            }
        }

        private static string WidgetsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "widgets.txt"); }
        }

        internal void WReg()
        {
            if (_wlist != null) return;
            _wlist = new List<WDef>();
            string cModTr = "Modern ve Sade", cModEn = "Modern & Minimal";
            string cGamTr = "Oyun ve Performans", cGamEn = "Gaming & Performance";
            string cDetTr = "Detaylı Sensör Panelleri", cDetEn = "Detailed Sensor Panels";
            string cThmTr = "Sıcaklık, Güç ve Fan", cThmEn = "Thermals, Power & Fans";
            string cSysTr = "Sistem, Disk ve Ağ", cSysEn = "System, Storage & Network";
            string cAudTr = "Ses, Saat ve Özel Öğeler", cAudEn = "Audio, Clock & Custom";

            Wd("hero", "Üst Sistem Başlığı", "System Hero Banner", cModTr, cModEn, 1900, 105, 420, 76, (r, s) => PHero(r, s));
            Wd("perfstrip", "Performans Şeridi", "Performance Strip", cModTr, cModEn, 1900, 130, 520, 90, (r, s) => PPerfStrip(r, s));
            Wd("dualring", "CPU + GPU İkili Halka", "CPU + GPU Dual Ring", cModTr, cModEn, 1240, 390, 480, 220, (r, s) => PDualRing(r, s));
            Wd("minicpu", "CPU (Minimal Kart)", "CPU (Minimal Card)", cModTr, cModEn, 520, 300, 220, 140, (r, s) => PMiniCpu(r, s));
            Wd("minigpu", "GPU (Minimal Kart)", "GPU (Minimal Card)", cModTr, cModEn, 520, 300, 220, 140, (r, s) => PMiniGpu(r, s));
            Wd("miniram", "RAM & I/O (Minimal)", "RAM & I/O (Minimal)", cModTr, cModEn, 520, 260, 220, 130, (r, s) => PMiniRam(r, s));
            Wd("thermals", "Termal & Fan Matrisi", "Thermal & Fan Matrix", cModTr, cModEn, 620, 380, 340, 200, (r, s) => PThermals(r, s));

            Wd("hudbar", "Oyun Şeridi", "Game Bar", cGamTr, cGamEn, 1900, 86, 600, 70, (r, s) => PHudBar(r, s));
            Wd("hudfps", "FPS (HUD)", "FPS (HUD)", cGamTr, cGamEn, 800, 620, 380, 300, (r, s) => PHudFps(r, s));
            Wd("fpsl", "FPS (Büyük)", "FPS (Large)", cGamTr, cGamEn, 770, 300, 300, 160, (r, s) => PFps(r, s, true));
            Wd("fps", "FPS", "FPS", cGamTr, cGamEn, 950, 244, 300, 160, (r, s) => PFps(r, s, false));
            Wd("cpu", "CPU", "CPU", cGamTr, cGamEn, 560, 300, 260, 150, (r, s) => PCpu(r, s, true));
            Wd("gpu", "GPU", "GPU", cGamTr, cGamEn, 560, 300, 260, 150, (r, s) => PGpu(r, s));
            Wd("cores", "Çekirdekler", "CPU Cores", cGamTr, cGamEn, 650, 250, 260, 120, (r, s) => PCores(r, s));
            Wd("gpugraph", "GPU Grafiği", "GPU Load Graph", cGamTr, cGamEn, 420, 250, 200, 100, (r, s) => PGpuGraph(r, s));

            Wd("acpu", "CPU (Sensör)", "CPU (Detailed)", cDetTr, cDetEn, 940, 550, 300, 200, (r, s) => PACpu(r, s));
            Wd("agpu", "GPU (Sensör)", "GPU (Detailed)", cDetTr, cDetEn, 940, 550, 300, 200, (r, s) => PAGpu(r, s));
            Wd("amobo", "Anakart", "Motherboard", cDetTr, cDetEn, 960, 245, 300, 100, (r, s) => PAMobo(r, s));
            Wd("amem", "Bellek (Sensör)", "Memory (Detailed)", cDetTr, cDetEn, 470, 245, 220, 100, (r, s) => PAMem(r, s));
            Wd("astorage", "Depolama", "Storage (Detailed)", cDetTr, cDetEn, 940, 245, 300, 100, (r, s) => PAStorage(r, s));
            Wd("apower", "Güç (Sensör)", "Power (Detailed)", cDetTr, cDetEn, 310, 245, 200, 100, (r, s) => PAPower(r, s));
            Wd("anet", "Ağ (Sensör)", "Network (Detailed)", cDetTr, cDetEn, 630, 245, 260, 100, (r, s) => PANet(r, s));
            Wd("aclock", "Saat (Sensör)", "Clock (Detailed)", cDetTr, cDetEn, 450, 245, 220, 100, (r, s) => PAClock(r, s));

            Wd("temps", "Sıcaklık Grafiği", "Temp History", cThmTr, cThmEn, 420, 250, 200, 100, (r, s) => PTemps(r, s));
            Wd("power", "Güç Grafiği", "Power History", cThmTr, cThmEn, 380, 250, 200, 100, (r, s) => PPower(r, s));
            Wd("fans", "Fanlar", "Fans", cThmTr, cThmEn, 250, 250, 160, 100, (r, s) => PFans(r, s));
            Wd("volts", "Voltajlar", "Voltages", cThmTr, cThmEn, 200, 250, 160, 100, (r, s) => PVolts(r, s));

            Wd("mem", "Bellek", "Memory", cSysTr, cSysEn, 450, 250, 220, 120, (r, s) => PMem(r, s));
            Wd("disks", "Diskler", "Disks", cSysTr, cSysEn, 500, 250, 220, 120, (r, s) => PDisks(r, s));
            Wd("net", "Ağ", "Network", cSysTr, cSysEn, 450, 250, 220, 120, (r, s) => PNet(r, s));
            Wd("procs", "İşlemler", "Processes", cSysTr, cSysEn, 380, 234, 220, 120, (r, s) => PProcs(r, s));
            Wd("apps", "Uygulamalar", "Top Apps", cSysTr, cSysEn, 360, 338, 240, 200, (r, s) => PApps(r, s));
            Wd("sys", "Sistem Özeti", "System Summary", cSysTr, cSysEn, 530, 234, 220, 100, (r, s) => PSys(r, s));

            Wd("sound", "Ses Mikseri", "Audio Mixer", cAudTr, cAudEn, 440, 338, 300, 160, (r, s) => PSound(r, s));
            Wd("clock", "Saat & Hava", "Clock & Weather", cAudTr, cAudEn, 420, 234, 200, 100, (r, s) => PClock(r, s));
            Wd("text", "Özel Metin", "Custom Text", cAudTr, cAudEn, 500, 120, 80, 40, (r, s) => WText(r, s)); _wlist[_wlist.Count - 1].Multi = true;
            Wd("image", "Logo / Resim", "Logo / Image", cAudTr, cAudEn, 300, 300, 60, 60, (r, s) => WImage(r, s)); _wlist[_wlist.Count - 1].Multi = true;
            Wd("box", "Kutu / Çerçeve", "Box / Frame", cAudTr, cAudEn, 500, 300, 60, 60, (r, s) => WBox(r, s)); _wlist[_wlist.Count - 1].Multi = true;

            _wdefs = new Dictionary<string, WDef>();
            foreach (WDef d in _wlist) _wdefs[d.Id] = d;
        }

        internal static string BaseId(string id) { int i = id.IndexOf('#'); return i < 0 ? id : id.Substring(0, i); }
        internal WDef DefOf(string id) { WReg(); WDef d; return _wdefs.TryGetValue(BaseId(id), out d) ? d : null; }
        internal static float MinWof(WDef d) { return Math.Max(40f, d.MinW * 0.5f); }
        internal static float MinHof(WDef d) { return Math.Max(30f, d.MinH * 0.5f); }

        private void Wd(string id, string name, string nameEn, string category, string categoryEn, float w, float h, float mw, float mh, Action<RectangleF, Snap> draw)
        {
            _wlist.Add(new WDef { Id = id, Name = name, NameEn = nameEn, Category = category, CategoryEn = categoryEn, W = w, H = h, MinW = mw, MinH = mh, Draw = draw });
        }

        // ---------------- yerleşik şablonlar ----------------
        internal List<WInst> WTemplate(string id) { return WTemplate(id, _cw, _ch); }
        internal List<WInst> WTemplate(string id, int cw, int ch)
        {
            WReg();
            var l = new List<WInst>();
            Action<string, RectangleF> A = delegate (string wid, RectangleF r) { l.Add(new WInst { Id = wid, X = r.X, Y = r.Y, W = r.Width, H = r.Height, Vis = true }); };
            RectangleF[] a, b, c, d;
            if (id == "zen")
            {
                A("hero", new RectangleF(10, 10, 1900, 110));
                A("minicpu", new RectangleF(10, 135, 620, 480));
                A("minigpu", new RectangleF(650, 135, 620, 480));
                A("miniram", new RectangleF(1290, 135, 620, 480));
                A("net", new RectangleF(10, 635, 940, 435));
                A("clock", new RectangleF(970, 635, 940, 435));
            }
            else if (id == "minimalring")
            {
                A("hero", new RectangleF(10, 10, 1900, 105));
                A("dualring", new RectangleF(10, 130, 1280, 550));
                A("clock", new RectangleF(1305, 130, 605, 550));
                A("perfstrip", new RectangleF(10, 695, 1280, 135));
                A("net", new RectangleF(10, 845, 632, 225));
                A("disks", new RectangleF(658, 845, 632, 225));
                A("sound", new RectangleF(1305, 695, 605, 375));
            }
            else if (id == "bento")
            {
                A("hero", new RectangleF(10, 10, 1900, 100));
                A("dualring", new RectangleF(10, 122, 1240, 390));
                A("cores", new RectangleF(10, 524, 614, 266));
                A("fps", new RectangleF(636, 524, 614, 266));
                A("net", new RectangleF(10, 802, 614, 268));
                A("disks", new RectangleF(636, 802, 614, 268));
                A("miniram", new RectangleF(1262, 122, 648, 230));
                A("thermals", new RectangleF(1262, 364, 648, 366));
                A("sound", new RectangleF(1262, 742, 648, 328));
            }
            else if (id == "esports")
            {
                A("hudbar", new RectangleF(10, 10, 1900, 86));
                A("hudfps", new RectangleF(10, 108, 860, 590));
                A("minicpu", new RectangleF(882, 108, 508, 289));
                A("minigpu", new RectangleF(882, 409, 508, 289));
                A("thermals", new RectangleF(1402, 108, 508, 590));
                A("cores", new RectangleF(10, 710, 625, 360));
                A("net", new RectangleF(647, 710, 625, 360));
                A("sound", new RectangleF(1284, 710, 626, 360));
            }
            else if (id == "extreme")
            {
                A("hero", new RectangleF(10, 10, 1900, 96));
                A("acpu", new RectangleF(10, 116, 680, 510));
                A("agpu", new RectangleF(700, 116, 680, 510));
                A("thermals", new RectangleF(1390, 116, 520, 510));
                A("amobo", new RectangleF(10, 636, 680, 212));
                A("amem", new RectangleF(700, 636, 420, 212));
                A("volts", new RectangleF(1130, 636, 350, 212));
                A("power", new RectangleF(1490, 636, 420, 212));
                A("astorage", new RectangleF(10, 858, 680, 212));
                A("anet", new RectangleF(700, 858, 680, 212));
                A("apps", new RectangleF(1390, 858, 520, 212));
            }
            else if (id == "creator")
            {
                A("hero", new RectangleF(10, 10, 1900, 100));
                A("dualring", new RectangleF(10, 120, 1040, 460));
                A("sound", new RectangleF(1060, 120, 850, 460));
                A("miniram", new RectangleF(10, 590, 460, 235));
                A("disks", new RectangleF(10, 835, 460, 235));
                A("apps", new RectangleF(480, 590, 570, 480));
                A("net", new RectangleF(1060, 590, 420, 480));
                A("fpsl", new RectangleF(1490, 590, 420, 480));
            }
            else if (id == "panoramic")
            {
                A("hero", new RectangleF(10, 10, 1900, 110));
                A("perfstrip", new RectangleF(10, 132, 1900, 130));
                A("minicpu", new RectangleF(10, 274, 467, 430));
                A("minigpu", new RectangleF(487, 274, 468, 430));
                A("miniram", new RectangleF(965, 274, 467, 430));
                A("thermals", new RectangleF(1442, 274, 468, 430));
                A("cores", new RectangleF(10, 716, 626, 354));
                A("fps", new RectangleF(646, 716, 628, 354));
                A("net", new RectangleF(1284, 716, 626, 354));
            }
            else if (id == "vertical")
            {
                A("hero", new RectangleF(10, 10, 1900, 104));
                A("perfstrip", new RectangleF(10, 124, 1900, 126));
                A("minicpu", new RectangleF(10, 260, 945, 260));
                A("minigpu", new RectangleF(965, 260, 945, 260));
                A("thermals", new RectangleF(10, 530, 945, 265));
                A("miniram", new RectangleF(965, 530, 945, 265));
                A("fps", new RectangleF(10, 805, 945, 265));
                A("clock", new RectangleF(965, 805, 945, 265));
            }
            else if (id == "studio")
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
            else if (id == "empty") { }
            else
            {
                A("hudbar", new RectangleF(10, 10, 1900, 86));
                a = Row(106, 620, 800, 560, 520);
                A("hudfps", a[0]);
                A("cpu", new RectangleF(a[1].X, a[1].Y, a[1].Width, 305)); A("gpu", new RectangleF(a[1].X, a[1].Y + 315, a[1].Width, 305));
                A("temps", new RectangleF(a[2].X, a[2].Y, a[2].Width, 200)); A("gpugraph", new RectangleF(a[2].X, a[2].Y + 210, a[2].Width, 200)); A("power", new RectangleF(a[2].X, a[2].Y + 420, a[2].Width, 200));
                b = Row(736, 338, 440, 360, 360, 340, 360); A("sound", b[0]); A("net", b[1]); A("disks", b[2]); A("mem", b[3]); A("apps", b[4]);
            }
            if (cw != 1920 || ch != 1080)
            {
                float kx = cw / 1920f, ky = ch / 1080f;
                foreach (WInst x in l) { x.X = (float)Math.Round(x.X * kx); x.Y = (float)Math.Round(x.Y * ky); x.W = (float)Math.Round(x.W * kx); x.H = (float)Math.Round(x.H * ky); }
            }
            WFill(l);
            return l;
        }

        // listede olmayan widgetları gizli olarak ekle
        internal void WFill(List<WInst> l)
        {
            foreach (WDef d in _wlist)
            {
                if (d.Multi) continue;
                bool has = false;
                foreach (WInst w in l) if (w.Id == d.Id) { has = true; break; }
                if (!has)
                {
                    float ww = Math.Min(d.W, _cw), hh = Math.Min(d.H, _ch);
                    float xx = Math.Max(0, Math.Min(100, _cw - ww)), yy = Math.Max(0, Math.Min(100, _ch - hh));
                    l.Add(new WInst { Id = d.Id, X = xx, Y = yy, W = ww, H = hh, Vis = false });
                }
            }
        }

        internal bool WVis(string id)
        {
            for (int i = 0; i < _wd.Count; i++) if (BaseId(_wd[i].Id) == id && _wd[i].Vis) return true;
            return false;
        }

        internal WInst WGet(string id)
        {
            for (int i = 0; i < _wd.Count; i++) if (_wd[i].Id == id) return _wd[i];
            return null;
        }

        internal WInst WSelected { get { return _sel != null ? WGet(_sel) : null; } }

        internal void WChanged() { WSave(); _dirty = true; Invalidate(); }

        // ---------------- kayıt / yükleme ----------------
        private static string Esc(string s) { return Uri.EscapeDataString(s ?? ""); }
        private static string Unesc(string s) { try { return Uri.UnescapeDataString(s ?? ""); } catch { return ""; } }

        private WInst ParseWLine(string line)
        {
            var ic = CultureInfo.InvariantCulture;
            string[] p = line.Split('|');
            if (p.Length < 8 || DefOf(p[0]) == null) return null;
            var w = new WInst { Id = p[0] };
            w.X = float.Parse(p[1], ic); w.Y = float.Parse(p[2], ic); w.W = float.Parse(p[3], ic); w.H = float.Parse(p[4], ic);
            w.Vis = p[5] == "1"; w.Fs = float.Parse(p[6], ic);
            if (p.Length >= 25)
            {
                w.A1 = int.Parse(p[7]); w.A2 = int.Parse(p[8]); w.Tx = int.Parse(p[9]); w.PanelC = int.Parse(p[10]); w.PAlpha = int.Parse(p[11]);
                w.Frame = p[12]; w.FrameC = int.Parse(p[13]); w.FrameW = float.Parse(p[14], ic); w.Radius = float.Parse(p[15], ic);
                w.NoTitle = p[16] == "1"; w.Bars = p[17]; w.Glow = p[18]; w.Cap = p[19]; w.GaugeW = float.Parse(p[20], ic);
                w.Locked = p[21] == "1"; w.Align = int.Parse(p[22]); w.Text = Unesc(p[23]); w.Path = Unesc(p[24]);
                if (p.Length >= 26) w.Zoom = Math.Max(0.2f, Math.Min(5f, float.Parse(p[25], ic)));
            }
            else { int c; int.TryParse(p[7], out c); if (c > 0 && c < AccPal.Length) w.A1 = AccPal[c].ToArgb(); }
            WDef d = DefOf(w.Id);
            w.W = Math.Max(MinWof(d), w.W); w.H = Math.Max(MinHof(d), w.H);
            w.Fs = Math.Max(0.3f, Math.Min(3f, w.Fs));
            return w;
        }

        internal List<WInst> ParseWLines(IEnumerable<string> lines)
        {
            var l = new List<WInst>();
            foreach (string line in lines)
            {
                WInst w = null;
                try { w = ParseWLine(line); } catch { }
                if (w == null) continue;
                bool dup = false; foreach (WInst o in l) if (o.Id == w.Id) dup = true;
                if (!dup) l.Add(w);
            }
            return l;
        }

        private void WLoad()
        {
            WReg();
            var l = new List<WInst>();
            try { if (File.Exists(WidgetsPath)) l = ParseWLines(File.ReadAllLines(WidgetsPath)); } catch { l.Clear(); }
            if (l.Count == 0) { _wd = WTemplate(_layout); return; }
            WFill(l);
            _wd = l;
        }

        internal string WLinesText() { return WLinesText(_wd); }
        internal string WLinesText(List<WInst> list)
        {
            var sb = new StringBuilder();
            var ic = CultureInfo.InvariantCulture;
            foreach (WInst w in list)
                sb.Append(w.Id).Append('|').Append(w.X.ToString("0.##", ic)).Append('|').Append(w.Y.ToString("0.##", ic)).Append('|').Append(w.W.ToString("0.##", ic)).Append('|').Append(w.H.ToString("0.##", ic))
                  .Append('|').Append(w.Vis ? "1" : "0").Append('|').Append(w.Fs.ToString("0.00", ic))
                  .Append('|').Append(w.A1).Append('|').Append(w.A2).Append('|').Append(w.Tx).Append('|').Append(w.PanelC).Append('|').Append(w.PAlpha)
                  .Append('|').Append(w.Frame).Append('|').Append(w.FrameC).Append('|').Append(w.FrameW.ToString("0.##", ic)).Append('|').Append(w.Radius.ToString("0.##", ic))
                  .Append('|').Append(w.NoTitle ? "1" : "0").Append('|').Append(w.Bars).Append('|').Append(w.Glow).Append('|').Append(w.Cap).Append('|').Append(w.GaugeW.ToString("0.##", ic))
                  .Append('|').Append(w.Locked ? "1" : "0").Append('|').Append(w.Align).Append('|').Append(Esc(w.Text)).Append('|').Append(Esc(w.Path)).Append('|').Append(w.Zoom.ToString("0.##", ic)).AppendLine();
            return sb.ToString();
        }

        internal static List<WInst> CloneLayout(List<WInst> src)
        {
            var l = new List<WInst>(src.Count);
            foreach (WInst w in src) l.Add(w.Copy());
            return l;
        }

        internal void ReplaceLayout(List<WInst> list, bool save)
        {
            if (_wSaved != null) _wSaved = null;
            _wd = list; _sel = null;
            if (save) { WSave(); SaveSettings(); }
            _dirty = true; Invalidate();
        }

        internal void WSave()
        {
            if (_wSaved != null) return;   // oyun modunda geçici düzeni yazma
            try
            {
                string dir = Path.GetDirectoryName(WidgetsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(WidgetsPath, WLinesText());
            }
            catch { }
        }

        internal void ApplyTemplate(string id)
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

        // ---------------- tuval (çözünürlük) ----------------
        internal void SetCanvas(int w, int h, bool scaleWidgets)
        {
            w = Math.Max(200, Math.Min(8000, w)); h = Math.Max(120, Math.Min(8000, h));
            if (scaleWidgets && (w != _cw || h != _ch))
            {
                float kx = w / (float)_cw, ky = h / (float)_ch;
                foreach (WInst x in _wd) { x.X = (float)Math.Round(x.X * kx); x.Y = (float)Math.Round(x.Y * ky); x.W = (float)Math.Round(x.W * kx); x.H = (float)Math.Round(x.H * ky); }
                if (_wSaved != null) foreach (WInst x in _wSaved) { x.X = (float)Math.Round(x.X * kx); x.Y = (float)Math.Round(x.Y * ky); x.W = (float)Math.Round(x.W * kx); x.H = (float)Math.Round(x.H * ky); }
            }
            if (scaleWidgets && (w != _cw || h != _ch)) _gzoom = Math.Max(0.2f, Math.Min(4f, _gzoom * Math.Min(w / (float)_cw, h / (float)_ch)));
            _cw = w; _ch = h;
            foreach (WInst x in _wd) { x.W = Math.Min(x.W, _cw); x.H = Math.Min(x.H, _ch); x.X = Math.Max(0, Math.Min(_cw - x.W, x.X)); x.Y = Math.Max(0, Math.Min(_ch - x.H, x.Y)); }
            WSave(); SaveSettings(); _dirty = true; Invalidate();
        }

        // ---------------- görüntü önbelleği ----------------
        private readonly Dictionary<string, Image> _imgs = new Dictionary<string, Image>();
        private Image GetImg(string path, int max)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = path + "|" + max;
            Image im;
            if (_imgs.TryGetValue(key, out im)) return im;
            try
            {
                using (var fs = File.OpenRead(path))
                using (Image src = Image.FromStream(fs))
                {
                    float k = Math.Min(1f, max / (float)Math.Max(src.Width, src.Height));
                    int nw = Math.Max(1, (int)(src.Width * k)), nh = Math.Max(1, (int)(src.Height * k));
                    var bmp = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(bmp)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, nw, nh); }
                    im = bmp;
                }
            }
            catch { im = null; }
            if (_imgs.Count > 12) { foreach (Image x in _imgs.Values) if (x != null) x.Dispose(); _imgs.Clear(); }
            _imgs[key] = im;
            return im;
        }

        // ---------------- özel widget'lar ----------------
        private string Tokens(string t, Snap s)
        {
            if (string.IsNullOrEmpty(t) || t.IndexOf('{') < 0) return t ?? "";
            CultureInfo ci = L.Culture;
            DateTime n = DateTime.Now;
            string tm = n.ToString("HH:mm", ci), sc = n.ToString("HH:mm:ss", ci), dt = n.ToString("d MMMM yyyy", ci), dy = n.ToString("dddd", ci);
            t = t.Replace("{saat}", tm).Replace("{time}", tm)
                 .Replace("{saniye}", sc).Replace("{sec}", sc)
                 .Replace("{tarih}", dt).Replace("{date}", dt)
                 .Replace("{gun}", dy).Replace("{day}", dy);
            if (s != null)
            {
                string bat = double.IsNaN(s.SysBat) ? "--" : (I0(s.SysBat) + "%");
                t = t.Replace("{cpu}", I0(s.CpuLoad) + "%").Replace("{gpu}", I0(s.GpuLoad) + "%")
                     .Replace("{cpusic}", Tc(s.CpuTemp)).Replace("{cputemp}", Tc(s.CpuTemp))
                     .Replace("{gpusic}", Tc(s.GpuTemp)).Replace("{gputemp}", Tc(s.GpuTemp))
                     .Replace("{ram}", I0(s.MemLoad) + "%").Replace("{fps}", I0(s.Fps))
                     .Replace("{ping}", I0(s.Ping) + " ms").Replace("{bat}", bat);
            }
            return t;
        }

        private void WText(RectangleF r, Snap s)
        {
            WInst w = _cur; if (w == null) return;
            Panel(r);
            string txt = Tokens(w.Text, s);
            if (string.IsNullOrEmpty(txt)) { if (_edit) Txt(L.T("Metin yaz (Özellikler)", "Enter text (Properties)"), F(20, false, true), _th.Dim, r.X, r.Y, r.Width, r.Height, 1); return; }
            string[] lines = txt.Replace("\r", "").Split('\n');
            float sz = Math.Max(10f, r.Height * 0.55f / Math.Max(1, lines.Length) / Math.Max(0.3f, _fs));
            float avail = r.Width - 24;
            for (; sz > 8f; sz -= 2f)
            {
                Font f = F(sz, true, false);
                float mw = 0; foreach (string ln in lines) mw = Math.Max(mw, TW(ln, f));
                if (mw <= avail) break;
            }
            Font ff = F(sz, true, false);
            float lh = ff.Size * 1.3f, total = lh * lines.Length, y = r.Y + (r.Height - total) / 2f;
            for (int i = 0; i < lines.Length; i++) Txt(lines[i], ff, _th.Tx, r.X + 12, y + i * lh, avail, lh, w.Align);
        }

        private void WImage(RectangleF r, Snap s)
        {
            WInst w = _cur; if (w == null) return;
            Panel(r);
            Image im = GetImg(w.Path, 1024);
            if (im == null) { Txt(string.IsNullOrEmpty(w.Path) ? L.T("Resim seç (Özellikler)", "Select image (Properties)") : L.T("Resim bulunamadı", "Image not found"), F(20, false, true), _th.Dim, r.X, r.Y, r.Width, r.Height, 1); return; }
            float pad = Math.Min(12f, Math.Min(r.Width, r.Height) * 0.05f);
            float aw = r.Width - pad * 2, ah = r.Height - pad * 2;
            float k = Math.Min(aw / im.Width, ah / im.Height);
            float dw = im.Width * k, dh = im.Height * k;
            G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            G.DrawImage(im, r.X + (r.Width - dw) / 2f, r.Y + (r.Height - dh) / 2f, dw, dh);
        }

        private void WBox(RectangleF r, Snap s)
        {
            WInst w = _cur; if (w == null) return;
            if (!string.IsNullOrEmpty(w.Text)) Card(r, Tokens(w.Text, s), null, null); else Panel(r);
        }

        // ---------------- widget başına stil ----------------
        private readonly Dictionary<string, Theme> _accThemes = new Dictionary<string, Theme>();
        private float _wsFw, _wsGw; private bool _wsNoTitle; private string _wsCap = "";

        private Theme WTheme(Theme t, WInst w)
        {
            if (w.A1 == 0 && w.A2 == 0 && w.Tx == 0 && w.PanelC == 0 && w.PAlpha >= 100 && w.Frame == "" && w.FrameC == 0 && w.Radius < 0 && w.Bars == "" && w.Glow == "") return t;
            string k = t.Id + "|" + w.A1 + "|" + w.A2 + "|" + w.Tx + "|" + w.PanelC + "|" + w.PAlpha + "|" + w.Frame + "|" + w.FrameC + "|" + w.Radius + "|" + w.Bars + "|" + w.Glow;
            Theme o;
            if (_accThemes.TryGetValue(k, out o)) return o;
            o = new Theme { Id = t.Id, Name = t.Name, Style = t.Style, Bars = t.Bars, Fx = t.Fx, Bg = t.Bg, Panel = t.Panel, Tile = t.Tile, Line = t.Line, A1 = t.A1, A2 = t.A2, Track = t.Track, Tx = t.Tx, Dim = t.Dim, Crit = t.Crit, R = t.R, Glow = t.Glow, Mono = t.Mono };
            if (w.A1 != 0) { o.A1 = Color.FromArgb(255, Color.FromArgb(w.A1)); o.A2 = Mix(o.A1, t.A2, 0.45f); }
            if (w.A2 != 0) o.A2 = Color.FromArgb(255, Color.FromArgb(w.A2));
            if (w.Tx != 0) { o.Tx = Color.FromArgb(255, Color.FromArgb(w.Tx)); o.Dim = Mix(o.Tx, t.Bg, 0.45f); }
            if (w.PanelC != 0) o.Panel = Color.FromArgb(255, Color.FromArgb(w.PanelC));
            if (w.PAlpha < 100) { o.Panel = Color.FromArgb(Math.Max(0, w.PAlpha) * 255 / 100, o.Panel); o.Tile = Color.FromArgb(Math.Max(0, w.PAlpha) * 255 / 100, o.Tile); }
            if (w.FrameC != 0) o.Line = Color.FromArgb(255, Color.FromArgb(w.FrameC));
            if (w.Frame != "") o.Style = w.Frame == "flat" ? "flat" : w.Frame;
            if (w.Radius >= 0) o.R = w.Radius;
            if (w.Bars != "") o.Bars = w.Bars;
            if (w.Glow != "") o.Glow = w.Glow == "on";
            if (_accThemes.Count > 300) _accThemes.Clear();
            _accThemes[k] = o;
            return o;
        }

        private float FW(float d) { return _wsFw > 0 ? _wsFw : d; }

        private bool _wErr;
        private void DrawWidgets(Snap s)
        {
            WReg();
            for (int i = 0; i < _wd.Count; i++)
            {
                WInst w = _wd[i];
                if (!w.Vis) continue;
                WDef d = DefOf(w.Id);
                if (d == null) continue;
                Theme saved = _th;
                _th = WTheme(saved, w);
                _fs = w.Fs * _gfs; _wsFw = w.FrameW; _wsGw = w.GaugeW; _wsNoTitle = w.NoTitle; _wsCap = w.Cap; _cur = w;
                var r = new RectangleF(w.X, w.Y, w.W, w.H);
                var st = G.Save();
                G.SetClip(RectangleF.Inflate(r, 10, 10), CombineMode.Intersect);
                float z = w.Zoom * _gzoom;
                int h0 = _hits.Count;
                RectangleF rd = r;
                if (Math.Abs(z - 1f) > 0.001f) { G.TranslateTransform(r.X, r.Y); G.ScaleTransform(z, z); rd = new RectangleF(0, 0, r.Width / z, r.Height / z); }
                try { d.Draw(rd, s); }
                catch (Exception ex) { if (!_wErr) { _wErr = true; Program.Log("widget " + w.Id, ex); } }
                if (Math.Abs(z - 1f) > 0.001f)
                    for (int hi = h0; hi < _hits.Count; hi++) { RectangleF hr = _hits[hi].Key; _hits[hi] = new KeyValuePair<RectangleF, string>(new RectangleF(r.X + hr.X * z, r.Y + hr.Y * z, hr.Width * z, hr.Height * z), _hits[hi].Value); }
                G.Restore(st);
                _th = saved; _fs = 1f; _wsFw = 0; _wsGw = 0; _wsNoTitle = false; _wsCap = ""; _cur = null;
            }
        }

        // ---------------- düzenleme modu ----------------
        internal bool _edit;
        private bool _palOpen;
        internal string _sel;
        private int _drag;                 // 0 yok, 1 taşı, 2..9 boyut (2=BR, 3=TL, 4=TR, 5=BL, 6=L, 7=R, 8=T, 9=B)
        private float _dx, _dy, _dragX0, _dragY0, _dragW0, _dragH0;
        private float _snapGuideX = -1, _snapGuideY = -1;
        private int _lastEdPaint;
        private readonly List<KeyValuePair<RectangleF, string>> _edHits = new List<KeyValuePair<RectangleF, string>>();
        private readonly List<List<WInst>> _undo = new List<List<WInst>>();
        private readonly List<List<WInst>> _redo = new List<List<WInst>>();
        internal WInst _copiedStyle;

        private void PushUndo()
        {
            _undo.Add(CloneLayout(_wd));
            if (_undo.Count > 40) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void DoUndo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(CloneLayout(_wd));
            _wd = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            WSave(); SyncProps(); _dirty = true; Invalidate();
        }

        private void DoRedo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(CloneLayout(_wd));
            _wd = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            WSave(); SyncProps(); _dirty = true; Invalidate();
        }

        internal static void CopyStyleFields(WInst src, WInst dst)
        {
            if (src == null || dst == null) return;
            dst.Fs = src.Fs; dst.Zoom = src.Zoom; dst.A1 = src.A1; dst.A2 = src.A2; dst.Tx = src.Tx;
            dst.PanelC = src.PanelC; dst.FrameC = src.FrameC; dst.PAlpha = src.PAlpha;
            dst.Frame = src.Frame; dst.Bars = src.Bars; dst.Glow = src.Glow; dst.Cap = src.Cap;
            dst.FrameW = src.FrameW; dst.Radius = src.Radius; dst.GaugeW = src.GaugeW;
            dst.NoTitle = src.NoTitle; dst.Align = src.Align;
        }

        internal void ToggleEdit()
        {
            _edit = !_edit; _sel = null; _palOpen = false; _drag = 0; _snapGuideX = _snapGuideY = -1;
            if (_edit && _wSaved != null) { _wd = _wSaved; _wSaved = null; }
            if (!_edit) { WSave(); CloseEditorDialogs(); }
            _dirty = true; Invalidate();
        }

        internal float SnapV(float v)
        {
            if (!_snapOn || _grid <= 1f) return (float)Math.Round(v);
            return (float)Math.Round(v / _grid) * _grid;
        }

        private RectangleF WinRect(WInst w) { return new RectangleF(_ox + w.X * _sc, _oy + w.Y * _sc, w.W * _sc, w.H * _sc); }

        private static RectangleF[] HandleRects(RectangleF r)
        {
            float s = 14f, hs = s / 2f;
            float mx = r.X + r.Width / 2f, my = r.Y + r.Height / 2f;
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

        private void DrawEditor()
        {
            Theme t = _th;
            WReg();
            _edHits.Clear();
            G.SmoothingMode = SmoothingMode.AntiAlias;
            // tuval sınırı + ızgara
            G.DrawRectangle(Pn(Al(t.A1, 200), 2), 0, 0, _cw, _ch);
            if (_showGrid)
            {
                float g = Math.Max(1f, _grid), step = g;
                while (step * _sc < 7f) step += g;
                Pen gp = Pn(Al(t.A1, 24), 1);
                for (float x = 0; x <= _cw; x += step) G.DrawLine(gp, x, 0, x, _ch);
                for (float y = 0; y <= _ch; y += step) G.DrawLine(gp, 0, y, _cw, y);
            }
            if (_snapGuideX >= 0) using (var sp = new Pen(Color.FromArgb(220, 0, 229, 255), 1.5f)) { sp.DashStyle = DashStyle.Dash; G.DrawLine(sp, _snapGuideX, 0, _snapGuideX, _ch); }
            if (_snapGuideY >= 0) using (var sp = new Pen(Color.FromArgb(220, 0, 229, 255), 1.5f)) { sp.DashStyle = DashStyle.Dash; G.DrawLine(sp, 0, _snapGuideY, _cw, _snapGuideY); }
            Font fn = F(16, true, false);
            foreach (WInst w in _wd)
            {
                if (!w.Vis) continue;
                WDef d = DefOf(w.Id); if (d == null) continue;
                bool sel = w.Id == _sel;
                var r = new RectangleF(w.X, w.Y, w.W, w.H);
                Color oc = sel ? t.A1 : Al(t.A1, 150);
                using (var dp = new Pen(oc, sel ? 3f : 1.6f)) { if (!sel) dp.DashStyle = DashStyle.Dash; G.DrawRectangle(dp, r.X, r.Y, r.Width, r.Height); }
                string nm = (w.Locked ? "🔒 " : "") + d.Title + (sel ? "  [" + (int)Math.Round(w.X) + "," + (int)Math.Round(w.Y) + " " + (int)Math.Round(w.W) + "×" + (int)Math.Round(w.H) + "]" : (w.Fs != 1f ? "  " + ((int)Math.Round(w.Fs * 100)) + "%" : ""));
                float tw = TW(nm, fn) + 14;
                var tr = new RectangleF(r.X, r.Y, tw, 24);
                G.FillRectangle(Br(sel ? t.A1 : Al(t.Panel, 235)), tr);
                Txt(nm, fn, sel ? t.Bg : t.Tx, tr.X + 7, tr.Y, tw, 24, 0);
                if (sel && !w.Locked)
                {
                    foreach (RectangleF hr in HandleRects(r))
                    {
                        G.FillRectangle(Br(t.A1), hr);
                        G.DrawRectangle(Pn(t.Bg, 1.5f), hr.X, hr.Y, hr.Width, hr.Height);
                    }
                }
                else if (!w.Locked)
                {
                    var hnd = new RectangleF(r.Right - 22, r.Bottom - 22, 22, 22);
                    G.FillRectangle(Br(Al(t.A1, 150)), hnd);
                    G.DrawLine(Pn(t.Bg, 2), hnd.X + 6, hnd.Bottom - 4, hnd.Right - 4, hnd.Y + 6);
                }
            }

            // ----- araç çubuğu: pencere pikselleriyle çizilir (tuval ölçeğinden bağımsız) -----
            G.ResetTransform();
            float W = ClientSize.Width, Hh = ClientSize.Height;
            WInst cur = WSelected;
            float bh = 52, by = Hh - bh - 6;
            if (cur != null) { RectangleF wr = WinRect(cur); if (wr.Bottom > by - 4) by = 6; }
            var bar = new RectangleF(6, by, W - 12, bh);
            G.FillRectangle(Br(Al(t.Bg, 250)), bar);
            G.DrawRectangle(Pn(t.A1, 2), bar.X, bar.Y, bar.Width, bar.Height);
            float x0 = bar.X + 8, yb = bar.Y + 7, hb = 38;
            Font fb = F(15, true, false);
            x0 = EdBtn(L.T("✔ Bitti", "✔ Done"), "ed:done", x0, yb, hb, fb, true);
            x0 = EdBtn(L.T("🎨 Stüdyo", "🎨 Studio"), "ed:studio", x0, yb, hb, fb, false);
            x0 = EdBtn(L.T("📁 Galeri", "📁 Gallery"), "ed:gallery", x0, yb, hb, fb, false);
            x0 = EdBtn(L.T("＋ Ekle", "＋ Add"), "ed:add", x0, yb, hb, fb, _palOpen);
            x0 = EdBtn(L.T("Düzen ▾", "Layout ▾"), "ed:tpl", x0, yb, hb, fb, false);
            x0 = EdBtn(L.T("⚙ Pano", "⚙ Canvas"), "ed:canvas", x0, yb, hb, fb, false);
            x0 += 8;
            if (cur != null)
            {
                WDef cd = DefOf(cur.Id);
                string nm = cd.Title; float nw = Math.Min(150, TW(nm, fb) + 12);
                Txt(nm, fb, t.A1, x0, yb, nw, hb, 0); x0 += nw + 4;
                x0 = EdBtn("A−", "ed:fs-", x0, yb, hb, fb, false);
                Txt(((int)Math.Round(cur.Fs * 100)) + "%", fb, t.Tx, x0, yb, 48, hb, 1); x0 += 48;
                x0 = EdBtn("A+", "ed:fs+", x0, yb, hb, fb, false);
                var sw = new RectangleF(x0, yb + 3, 36, hb - 6);
                G.FillRectangle(Br(cur.A1 == 0 ? t.A1 : Color.FromArgb(255, Color.FromArgb(cur.A1))), sw);
                G.DrawRectangle(Pn(t.Tx, 1.5f), sw.X, sw.Y, sw.Width, sw.Height);
                _edHits.Add(new KeyValuePair<RectangleF, string>(sw, "ed:color"));
                x0 += 42;
                x0 = EdBtn(L.T("Özellikler", "Properties"), "ed:props", x0, yb, hb, fb, false);
                x0 = EdBtn(cd.Multi ? L.T("Sil", "Delete") : L.T("Gizle", "Hide"), "ed:hide", x0, yb, hb, fb, false);
            }
            else
            {
                Txt(L.T("Ctrl+Z: geri al  ·  sürükle: taşı  ·  kenar/köşe: boyut  ·  sağ tık: menü  ·  Esc: bitir", "Ctrl+Z: undo  ·  drag: move  ·  edges/corners: resize  ·  right-click: menu  ·  Esc: finish"), F(13, false, true), t.Dim, x0, yb, Math.Max(100, bar.Right - x0 - 10), hb, 0);
            }
            // ekleme paneli
            if (_palOpen)
            {
                var hid = new List<WInst>();
                foreach (WInst w in _wd) if (!w.Vis && !DefOf(w.Id).Multi) hid.Add(w);
                int cols = Math.Max(1, Math.Min(5, (int)((W - 40) / 190)));
                float cw = 180, ch = 36;
                int items = hid.Count + 3;
                int rows = Math.Max(1, (items + cols - 1) / cols);
                float ph = rows * (ch + 6) + 16;
                float py = by > 100 ? by - ph - 6 : by + bh + 6;
                var pr = new RectangleF(6, py, cols * (cw + 6) + 10, ph);
                G.FillRectangle(Br(Al(t.Bg, 250)), pr);
                G.DrawRectangle(Pn(t.A1, 2), pr.X, pr.Y, pr.Width, pr.Height);
                string[] nid = { "text", "image", "box" };
                string[] nnm = { L.T("＋ Yeni metin", "＋ New Text"), L.T("＋ Logo / resim", "＋ Logo / Image"), L.T("＋ Kutu / çerçeve", "＋ Box / Frame") };
                for (int i = 0; i < 3; i++) EdBtnAt(nnm[i], "ed:new:" + nid[i], pr.X + 8 + (i % cols) * (cw + 6), pr.Y + 8 + (i / cols) * (ch + 6), cw, ch, fb, true);
                for (int i = 0; i < hid.Count; i++)
                {
                    int k = i + 3;
                    EdBtnAt(DefOf(hid[i].Id).Title, "ed:addw:" + hid[i].Id, pr.X + 8 + (k % cols) * (cw + 6), pr.Y + 8 + (k / cols) * (ch + 6), cw, ch, fb, false);
                }
            }
            G.TranslateTransform(_ox, _oy); G.ScaleTransform(_sc, _sc);
        }

        private float EdBtn(string text, string id, float x, float y, float h, Font f, bool on)
        {
            float w = TW(text, f) + 22;
            EdBtnAt(text, id, x, y, w, h, f, on);
            return x + w + 6;
        }

        private void EdBtnAt(string text, string id, float x, float y, float w, float h, Font f, bool on)
        {
            Theme t = _th;
            var r = new RectangleF(x, y, w, h);
            using (GraphicsPath p = RR(r, 7)) { G.FillPath(Br(on ? t.A1 : t.Tile), p); G.DrawPath(Pn(on ? t.A1 : t.Line, 1.4f), p); }
            Txt(text, f, on ? t.Bg : t.Tx, x, y, w, h, 1);
            _edHits.Add(new KeyValuePair<RectangleF, string>(r, id));
        }

        private bool EditMouseDown(float wx, float wy, float cx, float cy)
        {
            foreach (var h in _edHits)
                if (h.Key.Contains(wx, wy)) { EditAction(h.Value); return true; }
            _palOpen = false;
            _snapGuideX = _snapGuideY = -1;
            // Seçili widget'ın 8 tutamacını kontrol et
            WInst cur = WSelected;
            if (cur != null && cur.Vis && !cur.Locked)
            {
                RectangleF[] hrs = HandleRects(new RectangleF(cur.X, cur.Y, cur.W, cur.H));
                for (int hi = 0; hi < hrs.Length; hi++)
                {
                    if (RectangleF.Inflate(hrs[hi], 6, 6).Contains(cx, cy))
                    {
                        PushUndo();
                        _drag = hi + 2;
                        _dx = cx; _dy = cy; _dragX0 = cur.X; _dragY0 = cur.Y; _dragW0 = cur.W; _dragH0 = cur.H;
                        return true;
                    }
                }
            }
            // en üstteki widget (listenin sonu üstte)
            for (int i = _wd.Count - 1; i >= 0; i--)
            {
                WInst w = _wd[i];
                if (!w.Vis) continue;
                if (new RectangleF(w.X, w.Y, w.W, w.H).Contains(cx, cy))
                {
                    _sel = w.Id;
                    _wd.RemoveAt(i); _wd.Add(w);   // öne al
                    if (w.Locked) _drag = 0;
                    else if (cx > w.X + w.W - 26 && cy > w.Y + w.H - 26)
                    {
                        PushUndo(); _drag = 2; _dx = cx; _dy = cy; _dragX0 = w.X; _dragY0 = w.Y; _dragW0 = w.W; _dragH0 = w.H;
                    }
                    else
                    {
                        PushUndo(); _drag = 1; _dx = cx - w.X; _dy = cy - w.Y;
                    }
                    _dirty = true; Invalidate(); SyncProps();
                    return true;
                }
            }
            _sel = null; _dirty = true; Invalidate(); SyncProps();
            return true;
        }

        private float SmartSnapX(WInst self, float x, float w, out float guideX)
        {
            guideX = -1;
            if (!_snapOn) return x;
            float best = x, bestDist = 7f;
            float centerC = _cw / 2f;
            if (Math.Abs((x + w / 2f) - centerC) < bestDist) { bestDist = Math.Abs((x + w / 2f) - centerC); best = centerC - w / 2f; guideX = centerC; }
            foreach (WInst o in _wd)
            {
                if (!o.Vis || o == self) continue;
                float[] targets = { o.X, o.X + o.W, o.X + o.W / 2f };
                float[] selfPts = { x, x + w, x + w / 2f };
                foreach (float tp in targets)
                    for (int k = 0; k < 3; k++)
                    {
                        float d = Math.Abs(selfPts[k] - tp);
                        if (d < bestDist) { bestDist = d; best = x + (tp - selfPts[k]); guideX = tp; }
                    }
            }
            return guideX >= 0 ? best : SnapV(x);
        }

        private float SmartSnapY(WInst self, float y, float h, out float guideY)
        {
            guideY = -1;
            if (!_snapOn) return y;
            float best = y, bestDist = 7f;
            float centerC = _ch / 2f;
            if (Math.Abs((y + h / 2f) - centerC) < bestDist) { bestDist = Math.Abs((y + h / 2f) - centerC); best = centerC - h / 2f; guideY = centerC; }
            foreach (WInst o in _wd)
            {
                if (!o.Vis || o == self) continue;
                float[] targets = { o.Y, o.Y + o.H, o.Y + o.H / 2f };
                float[] selfPts = { y, y + h, y + h / 2f };
                foreach (float tp in targets)
                    for (int k = 0; k < 3; k++)
                    {
                        float d = Math.Abs(selfPts[k] - tp);
                        if (d < bestDist) { bestDist = d; best = y + (tp - selfPts[k]); guideY = tp; }
                    }
            }
            return guideY >= 0 ? best : SnapV(y);
        }

        private void EditMouseMove(float cx, float cy)
        {
            if (_drag == 0 || _sel == null) return;
            int now = Environment.TickCount;
            if (now - _lastEdPaint < 16) return;
            _lastEdPaint = now;
            WInst w = WGet(_sel); if (w == null) return;
            WDef d = DefOf(w.Id);
            float mw = MinWof(d), mh = MinHof(d);
            _snapGuideX = _snapGuideY = -1;
            if (_drag == 1)
            {
                float nx = SmartSnapX(w, cx - _dx, w.W, out _snapGuideX);
                float ny = SmartSnapY(w, cy - _dy, w.H, out _snapGuideY);
                w.X = Math.Max(0, Math.Min(_cw - w.W, nx));
                w.Y = Math.Max(0, Math.Min(_ch - w.H, ny));
            }
            else
            {
                float ddx = cx - _dx, ddy = cy - _dy;
                bool left = _drag == 3 || _drag == 5 || _drag == 6;
                bool right = _drag == 2 || _drag == 4 || _drag == 7;
                bool top = _drag == 3 || _drag == 4 || _drag == 8;
                bool bottom = _drag == 2 || _drag == 5 || _drag == 9;
                if (right) w.W = Math.Max(mw, Math.Min(_cw - _dragX0, SnapV(_dragW0 + ddx)));
                if (bottom) w.H = Math.Max(mh, Math.Min(_ch - _dragY0, SnapV(_dragH0 + ddy)));
                if (left)
                {
                    float nx = Math.Max(0, Math.Min(_dragX0 + _dragW0 - mw, SnapV(_dragX0 + ddx)));
                    w.W = _dragX0 + _dragW0 - nx; w.X = nx;
                }
                if (top)
                {
                    float ny = Math.Max(0, Math.Min(_dragY0 + _dragH0 - mh, SnapV(_dragY0 + ddy)));
                    w.H = _dragY0 + _dragH0 - ny; w.Y = ny;
                }
            }
            _dirty = true; Invalidate();
        }

        private void EditMouseUp()
        {
            _snapGuideX = _snapGuideY = -1;
            if (_drag != 0) { _drag = 0; WSave(); SyncProps(); _dirty = true; Invalidate(); }
        }

        internal string NewId(string b)
        {
            for (int n = 1; ; n++) { string id = b + "#" + n; if (WGet(id) == null) return id; }
        }

        internal void AddWidget(string id)
        {
            WReg();
            WDef bd = DefOf(id);
            WInst w;
            if (bd.Multi)
            {
                w = new WInst { Id = NewId(id), Vis = true };
                if (id == "text") w.Text = L.IsEn ? "{time}" : "{saat}";
                _wd.Add(w);
            }
            else { w = WGet(id); if (w == null) return; }
            w.Vis = true; w.Fs = 1f;
            float sx = _cw / 1920f, sy = _ch / 1080f;
            w.W = (float)Math.Round(Math.Min(_cw, bd.W * Math.Min(sx, sy))); w.H = (float)Math.Round(Math.Min(_ch, bd.H * Math.Min(sx, sy)));
            w.X = SnapV(Math.Max(0, (_cw - w.W) / 2f)); w.Y = SnapV(Math.Max(0, (_ch - w.H) / 2f));
            _wd.Remove(w); _wd.Add(w); _sel = w.Id; _palOpen = false; WChanged(); SyncProps();
        }

        internal void RemoveSelected()
        {
            WInst cur = WSelected; if (cur == null) return;
            if (DefOf(cur.Id).Multi) _wd.Remove(cur); else cur.Vis = false;
            _sel = null; WChanged(); SyncProps();
        }

        internal void DuplicateSelected()
        {
            WInst cur = WSelected; if (cur == null) return;
            WDef d = DefOf(cur.Id);
            if (!d.Multi) return;
            WInst c = cur.Copy(); c.Id = NewId(BaseId(cur.Id));
            c.X = Math.Min(_cw - c.W, cur.X + 20); c.Y = Math.Min(_ch - c.H, cur.Y + 20);
            _wd.Add(c); _sel = c.Id; WChanged(); SyncProps();
        }

        internal void ReorderSelected(bool front)
        {
            WInst cur = WSelected; if (cur == null) return;
            _wd.Remove(cur); if (front) _wd.Add(cur); else _wd.Insert(0, cur);
            WChanged();
        }

        private void EditAction(string a)
        {
            WInst cur = WSelected;
            if (a == "ed:done") ToggleEdit();
            else if (a == "ed:studio") { ToggleEdit(); OpenStudio(); return; }
            else if (a == "ed:gallery") { ToggleEdit(); OpenGallery(); return; }
            else if (a == "ed:reset") { PushUndo(); _wd = WTemplate(_layout); _sel = null; WSave(); SyncProps(); }
            else if (a == "ed:tpl") ShowTemplateMenu();
            else if (a == "ed:add") _palOpen = !_palOpen;
            else if (a == "ed:canvas") OpenCanvasDialog();
            else if (a == "ed:props") OpenPropsDialog();
            else if (a == "ed:color") { if (cur != null) QuickColor(cur); }
            else if (a.StartsWith("ed:new:")) { PushUndo(); AddWidget(a.Substring(7)); }
            else if (a.StartsWith("ed:addw:")) { PushUndo(); AddWidget(a.Substring(8)); }
            else if (cur != null)
            {
                if (a == "ed:fs-") { PushUndo(); cur.Fs = Math.Max(0.3f, (float)Math.Round(cur.Fs - 0.05f, 2)); }
                else if (a == "ed:fs+") { PushUndo(); cur.Fs = Math.Min(3f, (float)Math.Round(cur.Fs + 0.05f, 2)); }
                else if (a == "ed:hide") { PushUndo(); RemoveSelected(); return; }
                WSave(); SyncProps();
            }
            _dirty = true; Invalidate();
        }

        private void QuickColor(WInst cur)
        {
            using (var cd = new ColorDialog { FullOpen = true, Color = cur.A1 != 0 ? Color.FromArgb(cur.A1) : _th.A1 })
                if (cd.ShowDialog(this) == DialogResult.OK) { cur.A1 = Color.FromArgb(255, cd.Color).ToArgb(); WSave(); SyncProps(); }
        }

        private void ShowTemplateMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = new Font("Segoe UI", 10f);
            for (int i = 0; i < TplIds.Length; i++)
            {
                string id = TplIds[i];
                var it = new ToolStripMenuItem(GetTplName(id)) { Checked = id == _layout };
                it.Click += delegate { ApplyTemplate(id); };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            var sv = new ToolStripMenuItem(L.T("Şablonu kaydet / paylaşmak için dışa aktar…", "Save / Export Layout (.onmon)…")); sv.Click += delegate { ExportTemplate(); }; m.Items.Add(sv);
            var ld = new ToolStripMenuItem(L.T("Şablon dosyası aç (içe aktar)…", "Open / Import Layout File…")); ld.Click += delegate { ImportTemplate(); }; m.Items.Add(ld);
            try
            {
                string dir = TemplateDir;
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.onmon");
                    if (files.Length > 0) m.Items.Add(new ToolStripSeparator());
                    foreach (string f in files)
                    {
                        string ff = f;
                        var it = new ToolStripMenuItem(L.T("Kayıtlı: ", "Saved: ") + Path.GetFileNameWithoutExtension(f));
                        it.Click += delegate { ApplyTemplateFile(ff); };
                        m.Items.Add(it);
                    }
                }
            }
            catch { }
            var od = new ToolStripMenuItem(L.T("Şablon klasörünü aç", "Open Layouts Folder")); od.Click += delegate { try { Directory.CreateDirectory(TemplateDir); System.Diagnostics.Process.Start(TemplateDir); } catch { } }; m.Items.Add(od);
            m.Show(Cursor.Position);
        }

        // sağ tık menüsü (düzenleme modunda)
        private void ShowEditMenu()
        {
            Point cp = PointToClient(Cursor.Position);
            float cx = (cp.X - _ox) / _sc, cy = (cp.Y - _oy) / _sc;
            WInst hit = null;
            for (int i = _wd.Count - 1; i >= 0; i--) { WInst w = _wd[i]; if (w.Vis && new RectangleF(w.X, w.Y, w.W, w.H).Contains(cx, cy)) { hit = w; break; } }
            _sel = hit != null ? hit.Id : null; _dirty = true; Invalidate(); SyncProps();
            var m = new ContextMenuStrip();
            m.Font = new Font("Segoe UI", 10f);
            Action<string, Action> add = delegate (string txt, Action act) { var it = new ToolStripMenuItem(txt); it.Click += delegate { act(); }; m.Items.Add(it); };
            if (hit != null)
            {
                WDef d = DefOf(hit.Id);
                var hdr = new ToolStripMenuItem(d.Title) { Enabled = false, Font = new Font("Segoe UI", 10f, FontStyle.Bold) }; m.Items.Add(hdr);
                add(L.T("Özellikler…", "Properties…"), delegate { OpenPropsDialog(); });
                add(L.T("Stili kopyala", "Copy Style"), delegate { _copiedStyle = hit.Copy(); });
                if (_copiedStyle != null) add(L.T("Stili yapıştır", "Paste Style"), delegate { PushUndo(); CopyStyleFields(_copiedStyle, hit); WChanged(); SyncProps(); });
                if (d.Multi) add(L.T("Çoğalt", "Duplicate"), delegate { DuplicateSelected(); });
                add(L.T("Öne getir", "Bring to Front"), delegate { ReorderSelected(true); });
                add(L.T("Arkaya gönder", "Send to Back"), delegate { ReorderSelected(false); });
                add(hit.Locked ? L.T("Kilidi aç", "Unlock") : L.T("Kilitle", "Lock"), delegate { hit.Locked = !hit.Locked; WChanged(); SyncProps(); });
                add(L.T("Ortala (yatay)", "Center Horizontally"), delegate { PushUndo(); hit.X = (float)Math.Round((_cw - hit.W) / 2f); WChanged(); SyncProps(); });
                add(L.T("Ortala (dikey)", "Center Vertically"), delegate { PushUndo(); hit.Y = (float)Math.Round((_ch - hit.H) / 2f); WChanged(); SyncProps(); });
                add(d.Multi ? L.T("Sil", "Delete") : L.T("Gizle", "Hide"), delegate { PushUndo(); RemoveSelected(); });
                m.Items.Add(new ToolStripSeparator());
            }
            add(L.T("🎨 Düzen Stüdyosunu aç…", "🎨 Open Layout Studio…"), delegate { ToggleEdit(); OpenStudio(); });
            add(L.T("📁 Düzen Galerisi…", "📁 Layout Gallery…"), delegate { ToggleEdit(); OpenGallery(); });
            add(L.T("Widget ekle…", "Add Widget…"), delegate { _palOpen = true; _dirty = true; Invalidate(); });
            add(L.T("Pano ayarları (çözünürlük, ızgara, arka plan)…", "Canvas Settings (resolution, grid, background)…"), delegate { OpenCanvasDialog(); });
            add(L.T("Düzen seç…", "Select Layout…"), delegate { ShowTemplateMenu(); });
            m.Items.Add(new ToolStripSeparator());
            add(L.T("Uygulama ayarları…", "Application Settings…"), delegate { OpenSettings(); });
            add(L.T("✔ Düzenlemeyi bitir", "✔ Finish Editing"), delegate { ToggleEdit(); });
            m.Show(Cursor.Position);
        }

        private void EditKey(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) { DoUndo(); return; }
            if (e.Control && e.KeyCode == Keys.Y) { DoRedo(); return; }
            WInst cur = WSelected;
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.E) { ToggleEdit(); return; }
            if (cur == null) return;
            if (cur.Locked && e.KeyCode != Keys.Delete) return;
            WDef d = DefOf(cur.Id);
            float step = e.Alt ? 1f : (e.Shift ? Math.Max(10f, _grid * 4f) : Math.Max(1f, _grid));
            if (e.KeyCode == Keys.Delete) { PushUndo(); RemoveSelected(); return; }
            else if (e.Control)
            {
                if (e.KeyCode == Keys.Left) cur.W = Math.Max(MinWof(d), cur.W - step);
                else if (e.KeyCode == Keys.Right) cur.W = Math.Min(_cw - cur.X, cur.W + step);
                else if (e.KeyCode == Keys.Up) cur.H = Math.Max(MinHof(d), cur.H - step);
                else if (e.KeyCode == Keys.Down) cur.H = Math.Min(_ch - cur.Y, cur.H + step);
                else return;
            }
            else if (e.KeyCode == Keys.Left) cur.X = Math.Max(0, cur.X - step);
            else if (e.KeyCode == Keys.Right) cur.X = Math.Min(_cw - cur.W, cur.X + step);
            else if (e.KeyCode == Keys.Up) cur.Y = Math.Max(0, cur.Y - step);
            else if (e.KeyCode == Keys.Down) cur.Y = Math.Min(_ch - cur.H, cur.Y + step);
            else return;
            WSave(); SyncProps(); _dirty = true; Invalidate();
        }

        // ---------------- düzenleme pencereleri ----------------
        private WidgetPropsForm _propsForm;
        private CanvasForm _canvasForm;

        internal void OpenPropsDialog()
        {
            if (WSelected == null) return;
            if (_propsForm == null || _propsForm.IsDisposed) _propsForm = new WidgetPropsForm(this);
            _propsForm.Bind(WSelected);
            _propsForm.PlaceNear(this);
            _propsForm.Show(); _propsForm.Activate();
        }

        internal void OpenCanvasDialog()
        {
            if (_canvasForm == null || _canvasForm.IsDisposed) _canvasForm = new CanvasForm(this);
            _canvasForm.Reload();
            _canvasForm.PlaceNear(this);
            _canvasForm.Show(); _canvasForm.Activate();
        }

        private void SyncProps() { if (_propsForm != null && !_propsForm.IsDisposed && _propsForm.Visible) _propsForm.Bind(WSelected); }

        private void CloseEditorDialogs()
        {
            if (_propsForm != null && !_propsForm.IsDisposed) _propsForm.Close();
            if (_canvasForm != null && !_canvasForm.IsDisposed) _canvasForm.Close();
        }
    }
}
