using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>Panonun çizdiği anlık veri (NaN = okunamadı).</summary>
    public sealed class Snap
    {
        public string Brand = "", MoboModel = "", Chipset = "", Bios = "", CpuModel = "", GpuModel = "", GpuBus = "", MemSpeed = "";
        public string Uptime = "", Ip = "", Game = "", Throttle = "", Source = "";
        public double Sound = double.NaN, Procs = double.NaN, Ping = double.NaN;
        public double MemTotal, MemUsed, MemFree, MemLoad, MoboTemp = double.NaN;
        public double CpuLoad, CpuTemp = double.NaN, CpuClock = double.NaN, CpuFan = double.NaN, Cha1 = double.NaN, Cha2 = double.NaN, Pump = double.NaN;
        public double CpuW = double.NaN, CpuV = double.NaN, GpuW = double.NaN, GpuV = double.NaN;
        public double GpuHot = double.NaN, CpuDiode = double.NaN, DimmTemp = double.NaN, SocV = double.NaN, V12 = double.NaN;
        public double GpuLoad, GpuTemp = double.NaN, GpuClock = double.NaN, VramUsed, VramTotal, GpuFan = double.NaN;
        public double Fps, FrameMs, Low1 = double.NaN;
        public double NetUp, NetDown, NetUpTot, NetDownTot, DiskR = double.NaN, DiskW = double.NaN;
        public CoreTelemetry[] Cores = new CoreTelemetry[0];
        public DriveTelemetry[] Drives = new DriveTelemetry[0];
        public ProcItem[] TopProcs = new ProcItem[0], TopCpu = new ProcItem[0];
        public double SelfMb, SelfCpu;
        public double SysBat = double.NaN;
        public bool SysBatChg;
        public int SysBatMin = -1;
        public double SysBatW = double.NaN;
        public string WifiSsid = "";
        public int WifiSignal = -1;
    }

    /// <summary>Oturum boyunca gözlenen en düşük / en yüksek sensör değerleri.</summary>
    public static class PeakStats
    {
        private static readonly object _lk = new object();
        public static DateTime StartedAt = DateTime.Now;
        public static double CpuTempMin = double.NaN, CpuTempMax = double.NaN;
        public static double GpuTempMin = double.NaN, GpuTempMax = double.NaN;
        public static double CpuWMax = double.NaN, GpuWMax = double.NaN;
        public static double FpsMin = double.NaN, FpsMax = double.NaN;

        public static void Update(Snap s)
        {
            if (s == null) return;
            lock (_lk)
            {
                if (!double.IsNaN(s.CpuTemp) && s.CpuTemp > 0 && s.CpuTemp < 130)
                {
                    if (double.IsNaN(CpuTempMin) || s.CpuTemp < CpuTempMin) CpuTempMin = s.CpuTemp;
                    if (double.IsNaN(CpuTempMax) || s.CpuTemp > CpuTempMax) CpuTempMax = s.CpuTemp;
                }
                if (!double.IsNaN(s.GpuTemp) && s.GpuTemp > 0 && s.GpuTemp < 130)
                {
                    if (double.IsNaN(GpuTempMin) || s.GpuTemp < GpuTempMin) GpuTempMin = s.GpuTemp;
                    if (double.IsNaN(GpuTempMax) || s.GpuTemp > GpuTempMax) GpuTempMax = s.GpuTemp;
                }
                if (!double.IsNaN(s.CpuW) && s.CpuW >= 0 && s.CpuW < 1000)
                {
                    if (double.IsNaN(CpuWMax) || s.CpuW > CpuWMax) CpuWMax = s.CpuW;
                }
                if (!double.IsNaN(s.GpuW) && s.GpuW >= 0 && s.GpuW < 1500)
                {
                    if (double.IsNaN(GpuWMax) || s.GpuW > GpuWMax) GpuWMax = s.GpuW;
                }
                if (s.Fps >= 5 && s.Fps < 2000)
                {
                    if (double.IsNaN(FpsMin) || s.Fps < FpsMin) FpsMin = s.Fps;
                    if (double.IsNaN(FpsMax) || s.Fps > FpsMax) FpsMax = s.Fps;
                }
            }
        }

        public static void Reset()
        {
            lock (_lk)
            {
                StartedAt = DateTime.Now;
                CpuTempMin = double.NaN; CpuTempMax = double.NaN;
                GpuTempMin = double.NaN; GpuTempMax = double.NaN;
                CpuWMax = double.NaN; GpuWMax = double.NaN;
                FpsMin = double.NaN; FpsMax = double.NaN;
            }
        }

        public static string SummaryText()
        {
            lock (_lk)
            {
                var sb = new StringBuilder();
                sb.AppendLine(L.T("Oturum başlangıcı: ", "Session started: ") + AppPrefs.FmtClock(StartedAt));
                sb.AppendLine(L.T("CPU sıcaklık Min / Maks: ", "CPU temp Min / Max: ") + AppPrefs.FmtTemp(CpuTempMin) + " / " + AppPrefs.FmtTemp(CpuTempMax));
                sb.AppendLine(L.T("GPU sıcaklık Min / Maks: ", "GPU temp Min / Max: ") + AppPrefs.FmtTemp(GpuTempMin) + " / " + AppPrefs.FmtTemp(GpuTempMax));
                sb.AppendLine(L.T("Maks. Güç (CPU / GPU): ", "Max Power (CPU / GPU): ") +
                    (double.IsNaN(CpuWMax) ? "--" : Math.Round(CpuWMax) + "W") + " / " +
                    (double.IsNaN(GpuWMax) ? "--" : Math.Round(GpuWMax) + "W"));
                sb.AppendLine(L.T("FPS Min / Maks: ", "FPS Min / Max: ") +
                    (double.IsNaN(FpsMin) ? "--" : Math.Round(FpsMin).ToString(CultureInfo.InvariantCulture)) + " / " +
                    (double.IsNaN(FpsMax) ? "--" : Math.Round(FpsMax).ToString(CultureInfo.InvariantCulture)));
                return sb.ToString();
            }
        }
    }

    /// <summary>Tüm formlar ve arka plan servisi arasında paylaşılan kullanıcı tercihleri.</summary>
    public static class AppPrefs
    {
        public static volatile bool TempFahrenheit;
        public static volatile bool Clock12h;
        public static volatile bool ShowPrayer = true;

        public static volatile bool NightMode;
        public static volatile int NightStartHour = 23;
        public static volatile int NightEndHour = 7;
        public static volatile int NightBright = 35;

        public static volatile bool OverlayEnabled;
        public static volatile int OverlayCorner = 0;   // 0=Sol Üst, 1=Sağ Üst, 2=Sol Alt, 3=Sağ Alt
        public static volatile int OverlayOpacity = 82; // 30..100

        public static volatile int TrayIconMode = 0;    // 0=Logo, 1=CPU °C, 2=GPU °C

        public static volatile int AlertCpu = 90;
        public static volatile int AlertGpu = 85;
        public static volatile int AlertDisk = 70;
        public static volatile int AlertRam = 92;
        public static volatile int AlertBat = 15;
        public static volatile int AlertPing = 150;
        public static volatile bool AlertBeep;

        public static volatile bool Onboarded;

        public static double ConvertTemp(double c)
        {
            if (double.IsNaN(c)) return double.NaN;
            return TempFahrenheit ? (c * 9.0 / 5.0 + 32.0) : c;
        }

        public static string TempUnitStr()
        {
            return TempFahrenheit ? "°F" : "°C";
        }

        public static string FmtTemp(double c)
        {
            if (double.IsNaN(c)) return "--";
            double v = ConvertTemp(c);
            return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) + TempUnitStr();
        }

        public static string FmtTempShort(double c)
        {
            if (double.IsNaN(c)) return "--";
            double v = ConvertTemp(c);
            return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) + "°";
        }

        public static string FmtClock(DateTime dt)
        {
            return Clock12h
                ? dt.ToString("h:mm tt", CultureInfo.InvariantCulture)
                : dt.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public static string FmtClockSec(DateTime dt)
        {
            return Clock12h
                ? dt.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)
                : dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static bool IsNightNow()
        {
            if (!NightMode) return false;
            int h = DateTime.Now.Hour;
            int s = NightStartHour, e = NightEndHour;
            if (s == e) return false;
            if (s < e) return h >= s && h < e;
            return h >= s || h < e;
        }

        public static string FmtMinutes(int mins)
        {
            if (mins <= 0) return "";
            int h = mins / 60, m = mins % 60;
            if (L.IsEn) return h > 0 ? (h + "h " + m + "m") : (m + "m");
            return h > 0 ? (h + "sa " + m + "dk") : (m + "dk");
        }

        public static string FmtWx(string wx)
        {
            if (string.IsNullOrEmpty(wx) || wx == "--" || !TempFahrenheit) return wx;
            Match m = Regex.Match(wx, @"(-?\d+(?:[.,]\d+)?)");
            double c;
            if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out c))
                return ((int)Math.Round(c * 9.0 / 5.0 + 32.0)).ToString(CultureInfo.InvariantCulture) + "°F";
            return wx;
        }

        public static void LoadFromDisk()
        {
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "ui.txt");
                if (!File.Exists(p)) { Onboarded = false; return; }
                bool sawOnb = false;
                foreach (string line in File.ReadAllLines(p))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant(), v = line.Substring(i + 1).Trim();
                    int n;
                    if (k == "tempf") TempFahrenheit = v == "1";
                    else if (k == "clock12h") Clock12h = v == "1";
                    else if (k == "showprayer") ShowPrayer = v != "0";
                    else if (k == "nightmode") NightMode = v == "1";
                    else if (k == "nightstart" && int.TryParse(v, out n)) NightStartHour = Math.Max(0, Math.Min(23, n));
                    else if (k == "nightend" && int.TryParse(v, out n)) NightEndHour = Math.Max(0, Math.Min(23, n));
                    else if (k == "nightbright" && int.TryParse(v, out n)) NightBright = Math.Max(15, Math.Min(95, n));
                    else if (k == "overlay") OverlayEnabled = v == "1";
                    else if (k == "overlaycorner" && int.TryParse(v, out n)) OverlayCorner = Math.Max(0, Math.Min(3, n));
                    else if (k == "overlayop" && int.TryParse(v, out n)) OverlayOpacity = Math.Max(30, Math.Min(100, n));
                    else if (k == "traymode" && int.TryParse(v, out n)) TrayIconMode = Math.Max(0, Math.Min(2, n));
                    else if (k == "alcpu" && int.TryParse(v, out n)) AlertCpu = Math.Max(50, Math.Min(115, n));
                    else if (k == "algpu" && int.TryParse(v, out n)) AlertGpu = Math.Max(50, Math.Min(115, n));
                    else if (k == "aldisk" && int.TryParse(v, out n)) AlertDisk = Math.Max(40, Math.Min(95, n));
                    else if (k == "alram" && int.TryParse(v, out n)) AlertRam = Math.Max(50, Math.Min(99, n));
                    else if (k == "albat" && int.TryParse(v, out n)) AlertBat = Math.Max(5, Math.Min(50, n));
                    else if (k == "alping" && int.TryParse(v, out n)) AlertPing = Math.Max(30, Math.Min(999, n));
                    else if (k == "albeep") AlertBeep = v == "1";
                    else if (k == "onboarded") { sawOnb = true; Onboarded = v == "1"; }
                    else if (k == "lowcpu2") Program.PollIntervalMs = (v == "1") ? 2000 : 1000;
                }
                if (!sawOnb) Onboarded = true;
            }
            catch { }
        }
    }

    internal sealed class Theme
    {
        public string Id, Name, Style, Bars, Fx;
        public Color Bg, Panel, Tile, Line, A1, A2, Track, Tx, Dim, Crit;
        public float R;
        public bool Glow, Mono;
        public string Title
        {
            get
            {
                if (!L.IsEn) return Name;
                return Name.Replace("(Açık)", "(Light)").Replace("Synthwave Gece", "Synthwave Night")
                           .Replace("Blueprint (Teknik Çizim)", "Blueprint").Replace("Brütalist", "Brutalist")
                           .Replace("Karbon Yarış", "Carbon Racing").Replace("Derin Okyanus", "Deep Ocean")
                           .Replace("Taktik HUD", "Tactical HUD");
            }
        }
    }

    internal static class Themes
    {
        private static Color C(string h)
        {
            h = h.TrimStart(new[] { '#' });
            if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
            return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }

        private static Theme T(string id, string name, string style, string bars, string bg, string panel, string tile, string line,
            string a1, string a2, string track, string tx, string dim, float r, string fx, bool glow, bool mono, string crit)
        {
            return new Theme
            {
                Id = id, Name = name, Style = style, Bars = bars, Fx = fx,
                Bg = C(bg), Panel = C(panel), Tile = C(tile), Line = C(line), A1 = C(a1), A2 = C(a2), Track = C(track), Tx = C(tx), Dim = C(dim),
                R = r, Glow = glow, Mono = mono, Crit = C(crit ?? "#ff2d2d")
            };
        }

        public static readonly Theme[] All = new Theme[]
        {
            T("rog", "ROG Orange", "flat", "smooth", "#000", "#040404", "#0c0c0e", "#2c2c30", "#ff4a00", "#ffa21a", "#333336", "#fff", "#a9a9b0", 12, "", false, false, null),
            T("rogseg", "ROG Segment", "flat", "seg", "#000", "#050506", "#0d0d10", "#33261f", "#ff3d00", "#ff9a00", "#2a2a2e", "#fff", "#a9a9b0", 6, "", true, false, null),
            T("amber", "Amber Glass", "glass", "smooth", "#07090d", "#141923", "#1b2130", "#ffa000", "#ff8a00", "#ffc233", "#2a3042", "#f8fafc", "#97a1b4", 18, "glow:ff8c00:ffbe3c", false, false, null),
            T("crimson", "Aorus Crimson", "neon", "smooth", "#060305", "#0e070a", "#190d12", "#e60026", "#e60026", "#ff5470", "#2a1520", "#fff", "#ae98a1", 10, "", true, false, null),
            T("nzxt", "NZXT Violet", "glass", "thin", "#07070f", "#14162a", "#1c1f38", "#8b5cf6", "#8b5cf6", "#d08bff", "#272a49", "#fff", "#a2a8cf", 22, "glow:8b5cf6:d08bff", true, false, null),
            T("icue", "Corsair iCUE", "flat", "seg", "#020605", "#07110d", "#0e1d17", "#143d2d", "#10b981", "#6ee7b7", "#15302a", "#fff", "#8fb3a5", 8, "", true, false, null),
            T("noctua", "Noctua", "flat", "smooth", "#0b0806", "#16100c", "#221913", "#4a3a2c", "#b5532a", "#e6c99a", "#33261c", "#f6eedd", "#b9a797", 14, "", false, false, null),
            T("frost", "Frost Neon", "neon", "thin", "#02060a", "#06101a", "#0b1a29", "#00c8ff", "#00c8ff", "#7df9ff", "#10263a", "#fff", "#8fb2c8", 16, "", true, false, null),
            T("oled", "OLED Minimal", "mono", "thin", "#000", "#000", "#0a0a0a", "#26262b", "#38bdf8", "#a5f3fc", "#17171c", "#fff", "#8b8b95", 10, "", false, false, null),
            T("arctic", "Arctic (Açık)", "flat", "smooth", "#c9d3df", "#ffffff", "#eef2f7", "#c2ccd9", "#0369a1", "#0ea5e9", "#d5dde8", "#0f172a", "#52627a", 14, "", false, false, "#d00000"),
            T("cyber", "Cyberpunk 2077", "clip", "seg", "#03050c", "#0b1122", "#121b34", "#143a55", "#fcee0a", "#00f0ff", "#1a2644", "#fff", "#8ea4c8", 0, "glow:00f0ff:00f0ff", true, false, null),
            T("synth", "Synthwave Gece", "neon", "seg", "#12002b", "#1a0536", "#260a4a", "#ff2bd6", "#ff2bd6", "#00e5ff", "#34125f", "#fff", "#bba0e0", 14, "grid:00e5ff:ff2bd6", true, false, null),
            T("blueprint", "Blueprint (Teknik Çizim)", "blueprint", "thin", "#0a3d91", "#0d4aa8", "#0f56c2", "#8fb8ff", "#ffffff", "#9ecbff", "#1b62cc", "#eaf3ff", "#b4d0fa", 2, "grid:ffffff:ffffff", false, false, null),
            T("crt", "Retro CRT Amber", "crt", "seg", "#0a0600", "#110a02", "#1b1104", "#ff9d00", "#ffb000", "#ffd84d", "#2a1a05", "#ffcf6b", "#b8842a", 6, "", true, true, null),
            T("matrix", "Matrix Terminal", "crt", "seg", "#000", "#010a03", "#03140a", "#0b4d1f", "#00ff41", "#9dff5a", "#0a2a12", "#c9ffd6", "#5fae78", 4, "", true, true, null),
            T("brutal", "Brütalist (Açık)", "brutal", "chunk", "#f4efe3", "#fffaf0", "#ece5d3", "#111111", "#ff3b30", "#ffb400", "#e0d7bf", "#111111", "#5c5648", 0, "", false, false, "#c00000"),
            T("aurora", "Aurora", "glass", "thin", "#020b10", "#0a1a22", "#10262f", "#2dd4bf", "#2dd4bf", "#a78bfa", "#16323c", "#ecfeff", "#8fb7bf", 22, "glow:2dd4bf:a78bfa", true, false, null),
            T("carbon", "Karbon Yarış", "clip", "smooth", "#0a0a0a", "#101010", "#191919", "#2a2a2a", "#ff1e1e", "#ff8a5c", "#262626", "#fff", "#a0a0a0", 0, "hatch", true, false, null),
            T("ocean", "Derin Okyanus", "glass", "smooth", "#00060f", "#04162a", "#082340", "#1e6091", "#00d4ff", "#00ffa3", "#0b2c4d", "#e6faff", "#7fb0c9", 20, "glow:0078c8:00ffa3", true, false, null),
            T("military", "Taktik HUD", "hud", "seg", "#070a06", "#0d130a", "#141d0f", "#6b8f3a", "#9bd13d", "#ffb400", "#1d2a14", "#e8f5d0", "#8fa575", 2, "", true, false, null),
            T("sakura", "Sakura (Pastel)", "flat", "thin", "#fde8ef", "#fff7fa", "#ffe9f0", "#f3b6c8", "#ec4899", "#fb923c", "#f6d3de", "#4a1d33", "#9a6a80", 26, "", false, false, "#d6001c")
        };

        public static Theme Get(string id)
        {
            foreach (Theme t in All) if (t.Id == id) return t;
            return All[0];
        }
    }

    /// <summary>Hava durumu ve namaz vakitleri (yalnızca pano açıkken, 15 dakikada bir).</summary>
    internal static class Extras
    {
        public static volatile string WxTemp = "--", WxDesc = "";
        public static string[] Prayer = { "05:48", "07:14", "12:57", "15:58", "18:29", "19:49" };
        public static string[] PrayerNames = { "İmsak", "Güneş", "Öğle", "İkindi", "Akşam", "Yatsı" };
        public static string[] PrayerNamesEn = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };
        public static string GetPrayerName(int i) { return L.IsEn ? PrayerNamesEn[i] : PrayerNames[i]; }
        private static System.Threading.Timer _timer;
        private static string _city = "istanbul";

        public static void Start(string city)
        {
            _city = city;
            Stop();
            _timer = new System.Threading.Timer(delegate { Fetch(); }, null, 500, 15 * 60 * 1000);
        }

        public static void Stop()
        {
            if (_timer != null) { _timer.Dispose(); _timer = null; }
        }

        // wttr.in açıklamasını Türkçeye çevirir (zaten Türkçe ise dokunmaz).
        private static string TrWx(string d)
        {
            if (string.IsNullOrEmpty(d)) return "";
            string[][] map =
            {
                new[] { "patchy rain nearby", "Yer yer yağmurlu" }, new[] { "patchy light rain", "Yer yer hafif yağmur" }, new[] { "patchy light drizzle", "Yer yer çise" },
                new[] { "patchy snow nearby", "Yer yer karlı" }, new[] { "patchy sleet nearby", "Yer yer karla karışık yağmur" }, new[] { "patchy freezing drizzle nearby", "Yer yer donan çise" },
                new[] { "thundery outbreaks in nearby", "Yakında gök gürültülü fırtına" }, new[] { "thundery outbreaks possible", "Gök gürültülü fırtına olası" },
                new[] { "partly cloudy", "Parçalı bulutlu" }, new[] { "partly sunny", "Parçalı güneşli" }, new[] { "overcast", "Kapalı" }, new[] { "cloudy", "Bulutlu" },
                new[] { "sunny", "Güneşli" }, new[] { "clear", "Açık" }, new[] { "mist", "Puslu" }, new[] { "freezing fog", "Dondurucu sis" }, new[] { "fog", "Sisli" },
                new[] { "light drizzle", "Çise" }, new[] { "drizzle", "Çise" }, new[] { "light rain shower", "Hafif sağanak" }, new[] { "moderate or heavy rain shower", "Şiddetli sağanak" },
                new[] { "torrential rain shower", "Çok şiddetli sağanak" }, new[] { "rain shower", "Sağanak" }, new[] { "light rain", "Hafif yağmur" }, new[] { "moderate rain at times", "Ara sıra orta yağmur" },
                new[] { "moderate rain", "Orta şiddetli yağmur" }, new[] { "heavy rain at times", "Ara sıra şiddetli yağmur" }, new[] { "heavy rain", "Şiddetli yağmur" },
                new[] { "light sleet", "Hafif karla karışık yağmur" }, new[] { "sleet", "Karla karışık yağmur" }, new[] { "light snow showers", "Hafif kar sağanağı" },
                new[] { "light snow", "Hafif kar" }, new[] { "moderate snow", "Orta şiddetli kar" }, new[] { "heavy snow", "Yoğun kar" }, new[] { "blowing snow", "Kar fırtınası" },
                new[] { "blizzard", "Kar fırtınası" }, new[] { "ice pellets", "Buz taneleri" }, new[] { "thundery", "Gök gürültülü" }, new[] { "rain", "Yağmurlu" }, new[] { "snow", "Karlı" }
            };
            string l = d.ToLowerInvariant();
            foreach (string[] m in map) if (l == m[0]) return m[1];
            foreach (string[] m in map) if (l.Contains(m[0])) return m[1];
            return d;
        }

        private static void Fetch()
        {
            string rawCity = _city ?? "istanbul";
            string cityPart = rawCity, countryPart = "Turkey";
            int cm = rawCity.IndexOf(',');
            if (cm > 0) { cityPart = rawCity.Substring(0, cm).Trim(); countryPart = rawCity.Substring(cm + 1).Trim(); }
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    string lang = L.IsEn ? "en" : "tr";
                    string w = wc.DownloadString("https://wttr.in/" + Uri.EscapeDataString(cityPart) + "?m&format=%t|%C&lang=" + lang).Trim();
                    string[] p = w.Split(new[] { '|' });
                    if (p.Length >= 2) { WxTemp = p[0].Replace("+", ""); WxDesc = L.IsEn ? p[1].Trim() : TrWx(p[1].Trim()); }
                }
            }
            catch { }
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    string j = wc.DownloadString("https://api.aladhan.com/v1/timingsByCity?city=" + Uri.EscapeDataString(cityPart) + "&country=" + Uri.EscapeDataString(countryPart) + "&method=13");
                    string[] keys = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };
                    var res = new string[6];
                    for (int i = 0; i < 6; i++)
                    {
                        Match m = Regex.Match(j, "\"" + keys[i] + "\":\"(\\d\\d:\\d\\d)");
                        if (!m.Success) return;
                        res[i] = m.Groups[1].Value;
                    }
                    Prayer = res;
                }
            }
            catch { }
        }
    }
}
