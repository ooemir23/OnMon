using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace ProSystemMonitor
{
    internal static class AppData
    {
        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }
    }

    /// <summary>Oyun oturumu: başlangıç/bitiş algılar, ortalama FPS ve zirve değerleri toplar, oturumu CSV'ye yazar.</summary>
    internal static class GameSession
    {
        public static volatile bool Active;
        public static string Game = "";
        public static DateTime StartUtc;
        public static double AvgFps, MinFps = double.NaN, MaxFps, Low1, MaxCpuT = double.NaN, MaxGpuT = double.NaN, MaxW;
        public static readonly List<string[]> History = new List<string[]>();
        private static long _n; private static double _sum;
        private static DateTime _lastSeen;
        private static bool _loaded;

        public static string CsvPath { get { return Path.Combine(AppData.Dir, "oturumlar.csv"); } }

        public static TimeSpan Elapsed { get { return Active ? DateTime.UtcNow - StartUtc : TimeSpan.Zero; } }

        public static void LoadHistory()
        {
            if (_loaded) return; _loaded = true;
            try
            {
                if (!File.Exists(CsvPath)) return;
                string[] lines = File.ReadAllLines(CsvPath, Encoding.UTF8);
                for (int i = Math.Max(1, lines.Length - 200); i < lines.Length; i++)
                {
                    string[] p = lines[i].Split(new[] { ';' });
                    if (p.Length >= 9) History.Add(p);
                }
            }
            catch { }
        }

        /// <summary>Her saniye çağrılır. +1: oturum başladı, -1: oturum bitti, 0: değişmedi.</summary>
        public static int Update(Snap s)
        {
            bool playing = !string.IsNullOrEmpty(s.Game) && s.Fps > 1;
            if (playing)
            {
                _lastSeen = DateTime.UtcNow;
                int ev = 0;
                if (!Active)
                {
                    Active = true; Game = s.Game; StartUtc = DateTime.UtcNow;
                    _n = 0; _sum = 0; MinFps = double.NaN; MaxFps = 0; MaxCpuT = double.NaN; MaxGpuT = double.NaN; MaxW = 0; AvgFps = 0; Low1 = 0;
                    ev = 1;
                }
                _n++; _sum += s.Fps; AvgFps = _sum / _n;
                if (double.IsNaN(MinFps) || s.Fps < MinFps) MinFps = s.Fps;
                if (s.Fps > MaxFps) MaxFps = s.Fps;
                if (!double.IsNaN(s.Low1)) Low1 = s.Low1;
                if (!double.IsNaN(s.CpuTemp) && (double.IsNaN(MaxCpuT) || s.CpuTemp > MaxCpuT)) MaxCpuT = s.CpuTemp;
                if (!double.IsNaN(s.GpuTemp) && (double.IsNaN(MaxGpuT) || s.GpuTemp > MaxGpuT)) MaxGpuT = s.GpuTemp;
                double w = (double.IsNaN(s.CpuW) ? 0 : s.CpuW) + (double.IsNaN(s.GpuW) ? 0 : s.GpuW);
                if (w > MaxW) MaxW = w;
                return ev;
            }
            if (Active && (DateTime.UtcNow - _lastSeen).TotalSeconds > 20)
            {
                Active = false;
                Save();
                return -1;
            }
            return 0;
        }

        private static void Save()
        {
            try
            {
                TimeSpan d = _lastSeen - StartUtc;
                if (d.TotalSeconds < 60) return;
                CultureInfo ci = CultureInfo.InvariantCulture;
                string[] row =
                {
                    StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", ci), Game, ((int)d.TotalMinutes).ToString(ci),
                    AvgFps.ToString("0", ci), Low1.ToString("0", ci), double.IsNaN(MinFps) ? "" : MinFps.ToString("0", ci),
                    double.IsNaN(MaxCpuT) ? "" : MaxCpuT.ToString("0", ci), double.IsNaN(MaxGpuT) ? "" : MaxGpuT.ToString("0", ci), MaxW.ToString("0", ci)
                };
                bool head = !File.Exists(CsvPath);
                using (var w = new StreamWriter(CsvPath, true, new UTF8Encoding(true)))
                {
                    if (head) w.WriteLine("Başlangıç;Oyun;Süre(dk);Ort FPS;%1 Düşük;Min FPS;Maks CPU °C;Maks GPU °C;Maks Güç W");
                    w.WriteLine(string.Join(";", row));
                }
                LoadHistory();
                History.Add(row);
            }
            catch (Exception ex) { Program.Log("oturum-kaydı", ex); }
        }
    }

    internal sealed class AudioProfile
    {
        public string Name = "", OutId = "", OutName = "";
        public int Volume = -1;      // 0..100, -1 = değiştirme
        public int MicMute = -1;     // 1 kapalı, 0 açık, -1 değiştirme
    }

    /// <summary>Tek tuşla uygulanan ses profilleri (çıkış cihazı + ses seviyesi + mikrofon durumu).</summary>
    internal static class AudioProfiles
    {
        public static readonly List<AudioProfile> All = new List<AudioProfile>();
        private static bool _loaded;
        public static string PathFile { get { return Path.Combine(AppData.Dir, "profiller.txt"); } }

        public static void Load()
        {
            if (_loaded) return; _loaded = true;
            try
            {
                if (!File.Exists(PathFile)) return;
                foreach (string line in File.ReadAllLines(PathFile, Encoding.UTF8))
                {
                    string[] p = line.Split(new[] { '|' });
                    if (p.Length < 5) continue;
                    int v, m; int.TryParse(p[3], out v); int.TryParse(p[4], out m);
                    All.Add(new AudioProfile { Name = p[0], OutId = p[1], OutName = p[2], Volume = v, MicMute = m });
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(PathFile, All.Select(p => p.Name.Replace("|", "/") + "|" + p.OutId + "|" + p.OutName.Replace("|", "/") + "|" + p.Volume + "|" + p.MicMute).ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>Şu anki çıkış cihazı, ses seviyesi ve mikrofon durumunu profil olarak kaydeder.</summary>
        public static AudioProfile CaptureCurrent(string name)
        {
            AudioSwitch.Refresh(); AudioSwitch.PollVolume(); AudioSwitch.PollMic();
            AudioDevice def = AudioSwitch.Devices.FirstOrDefault(d => d.IsDefault);
            var p = new AudioProfile
            {
                Name = name, OutId = def != null ? def.Id : "", OutName = def != null ? def.Name : "",
                Volume = AudioSwitch.VolAvailable ? (int)Math.Round(AudioSwitch.VolLevel * 100) : -1,
                MicMute = AudioSwitch.MicAvailable ? (AudioSwitch.MicMuted ? 1 : 0) : -1
            };
            All.RemoveAll(x => x.Name == name);
            All.Add(p); Save();
            return p;
        }

        public static void Remove(string name) { All.RemoveAll(x => x.Name == name); Save(); }

        public static void Apply(AudioProfile p)
        {
            try
            {
                if (p.OutId.Length > 0) AudioSwitch.SetDefault(p.OutId);
                if (p.Volume >= 0) { Thread.Sleep(250); AudioSwitch.SetVolume(p.Volume / 100f); }
                AudioSwitch.PollMic();
                if (p.MicMute >= 0 && AudioSwitch.MicAvailable && (AudioSwitch.MicMuted ? 1 : 0) != p.MicMute) AudioSwitch.ToggleMicMute();
                AudioSwitch.PollMic(); AudioSwitch.PollVolume();
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
            catch (Exception ex) { Program.Log("ses-profili", ex); }
        }
    }

    /// <summary>Bluetooth klavye/fare/kulaklık pil yüzdeleri (Windows'un cihaz özelliğinden; kablosuz alıcılı cihazlar çıkmayabilir).</summary>
    internal static class Peripherals
    {
        public static volatile KeyValuePair<string, int>[] Items = new KeyValuePair<string, int>[0];
        public static volatile string Status = "Henüz okunmadı";
        private static int _started;
        private static volatile bool _busy;

        private static KeyValuePair<string, int>[] _pnp = new KeyValuePair<string, int>[0];
        private static KeyValuePair<string, int>[] _rog = new KeyValuePair<string, int>[0];
        private static void Combine() { Items = _rog.Concat(_pnp).ToArray(); }

        /// <summary>ROG Cetra alıcısının vendor raporunu (0xCC: bayt5=sol, 6=sağ, 7=kutu) sürekli dinler.</summary>
        private static void RogListen()
        {
            while (true)
            {
                try
                {
                    HidSharp.HidDevice dev = null;
                    foreach (HidSharp.HidDevice d in HidSharp.DeviceList.Local.GetHidDevices(0x0B05, 0x1AD3))
                        if (d.DevicePath.ToLowerInvariant().Contains("col04")) { dev = d; break; }
                    HidSharp.HidStream st;
                    if (dev != null && dev.TryOpen(out st))
                    {
                        using (st)
                        {
                            st.ReadTimeout = 60000;
                            byte[] buf = new byte[Math.Max(8, dev.GetMaxInputReportLength())];
                            while (true)
                            {
                                int c;
                                try { c = st.Read(buf); } catch (TimeoutException) { continue; }
                                try
                                {
                                    string lp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hid_log.txt");
                                    if (!File.Exists(lp) || new FileInfo(lp).Length < 60000)
                                        File.AppendAllText(lp, DateTime.Now.ToString("HH:mm:ss") + "  " + BitConverter.ToString(buf, 0, Math.Min(c, 24)) + "\r\n");
                                }
                                catch { }
                                if (c >= 8 && buf[0] == 0xCC && buf[1] == 0x12 && buf[2] == 0x09 && (buf[5] + buf[6] + buf[7]) > 0 && buf[5] <= 100 && buf[6] <= 100 && buf[7] <= 100)
                                {
                                    _rog = new[] {
                                        new KeyValuePair<string, int>("Cetra Sol", buf[5]),
                                        new KeyValuePair<string, int>("Cetra Sağ", buf[6]),
                                        new KeyValuePair<string, int>("Cetra Kutu", buf[7]) };
                                    Combine();
                                }
                            }
                        }
                    }
                }
                catch { }
                Thread.Sleep(30000);
            }
        }

        public static void StartBackground()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            new Thread(RogListen) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
            var t = new Thread(delegate ()
            {
                Thread.Sleep(30000);
                while (true)
                {
                    try { if (Program.Dash != null) Refresh(); } catch { }
                    Thread.Sleep(10 * 60 * 1000);
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.Start();
        }

        /// <summary>Kulaklık gibi cihazların Windows'ta hangi özellikleri bildirdiğini piller.txt dosyasına yazar (pil verisini bulmak için).</summary>
        public static void DumpAsync()
        {
            var t = new Thread(delegate ()
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piller.txt");
                    string ps = "$ErrorActionPreference='SilentlyContinue';" +
                        "$o = @();" +
                        "Get-PnpDevice -PresentOnly | Where-Object { $_.FriendlyName -match 'CETRA|SpeedNova|ROG|Bluetooth|Headset|Headphone|Kulakl|Earbud|Xbox|Controller' } | ForEach-Object { " +
                        "$o += '=== ' + $_.FriendlyName + ' | ' + $_.Class + ' | ' + $_.InstanceId; " +
                        "Get-PnpDeviceProperty -InstanceId $_.InstanceId | ForEach-Object { $v = $_.Data; if ($v -is [array]) { $v = ($v | Select-Object -First 6) -join ',' }; $o += '   ' + $_.KeyName + ' = ' + $v } }; " +
                        "$o | Out-File -Encoding UTF8 -FilePath '" + path.Replace("'", "''") + "'";
                    string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
                    var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc)
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    using (Process p = Process.Start(psi)) { if (!p.WaitForExit(90000)) { try { p.Kill(); } catch { } } }
                    Status = "piller.txt yazıldı";
                }
                catch (Exception ex) { Status = "Yazılamadı: " + ex.Message; }
            }) { IsBackground = true };
            t.Start();
        }

        /// <summary>ROG Cetra dongle'ının HID arayüzlerini (rapor tanımları + ham giriş raporları) hid.txt dosyasına yazar.</summary>
        public static void HidDumpAsync()
        {
            var t = new Thread(delegate ()
            {
                var sb = new StringBuilder();
                try
                {
                    sb.AppendLine("ROG Cetra HID dökümü - " + DateTime.Now);
                    foreach (HidSharp.HidDevice d in HidSharp.DeviceList.Local.GetHidDevices(0x0B05, 0x1AD3))
                    {
                        sb.AppendLine();
                        sb.AppendLine("=== " + d.DevicePath);
                        try { sb.AppendLine("Ürün: " + d.GetProductName() + " | IN=" + d.GetMaxInputReportLength() + " OUT=" + d.GetMaxOutputReportLength() + " FEAT=" + d.GetMaxFeatureReportLength()); } catch (Exception ex) { sb.AppendLine("Ürün okunamadı: " + ex.Message); }
                        try
                        {
                            HidSharp.Reports.ReportDescriptor rd = d.GetReportDescriptor();
                            foreach (HidSharp.Reports.DeviceItem di in rd.DeviceItems)
                            {
                                sb.AppendLine("  Cihaz öğesi usages: " + string.Join(",", di.Usages.GetAllValues().Select(u => "0x" + u.ToString("X8")).ToArray()));
                                foreach (HidSharp.Reports.Report r in di.Reports)
                                {
                                    sb.AppendLine("  Rapor " + r.ReportType + " id=" + r.ReportID + " uzunluk=" + r.Length);
                                    foreach (HidSharp.Reports.DataItem it in r.DataItems)
                                        sb.AppendLine("     öğe usages=" + string.Join(",", it.Usages.GetAllValues().Select(u => "0x" + u.ToString("X8")).ToArray()) + " bit=" + it.ElementBits + " adet=" + it.ElementCount + " aralık=" + it.LogicalMinimum + ".." + it.LogicalMaximum);
                                }
                            }
                        }
                        catch (Exception ex) { sb.AppendLine("  Rapor tanımı okunamadı: " + ex.Message); }
                        HidSharp.HidStream st;
                        if (!d.TryOpen(out st)) { sb.AppendLine("  Açılamadı (başka uygulama kullanıyor olabilir)"); continue; }
                        using (st)
                        {
                            st.ReadTimeout = 700;
                            var end = DateTime.UtcNow.AddSeconds(6);
                            int n = 0;
                            while (DateTime.UtcNow < end && n < 40)
                            {
                                try
                                {
                                    byte[] buf = new byte[Math.Max(8, d.GetMaxInputReportLength())];
                                    int c = st.Read(buf);
                                    sb.AppendLine("  GİRİŞ: " + BitConverter.ToString(buf, 0, c)); n++;
                                }
                                catch (TimeoutException) { }
                                catch (Exception ex) { sb.AppendLine("  okuma hatası: " + ex.Message); break; }
                            }
                            for (int id = 0; id < 256 && d.GetMaxFeatureReportLength() > 0; id += 1)
                            {
                                if (id > 8 && id != 0x5A && id != 0xFF) continue;
                                try
                                {
                                    byte[] fb = new byte[d.GetMaxFeatureReportLength()]; fb[0] = (byte)id;
                                    st.GetFeature(fb);
                                    sb.AppendLine("  ÖZELLİK id=" + id + ": " + BitConverter.ToString(fb));
                                }
                                catch { }
                            }
                        }
                    }
                    Status = "hid.txt yazıldı";
                }
                catch (Exception ex) { sb.AppendLine("HATA: " + ex); Status = "HID hatası: " + ex.Message; }
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hid.txt"), sb.ToString(), Encoding.UTF8); } catch { }
            }) { IsBackground = true };
            t.Start();
        }

        public static void RefreshAsync()
        {
            var t = new Thread(delegate () { try { Refresh(); } catch { } }) { IsBackground = true };
            t.Start();
        }

        public static void Refresh()
        {
            if (_busy) return; _busy = true;
            try
            {
                string ps = "$ErrorActionPreference='SilentlyContinue';" +
                    "Get-PnpDevice -PresentOnly | ForEach-Object { $p = Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2'; " +
                    "if ($p -and $p.Data -ne $null -and $p.Data -is [byte]) { $_.FriendlyName + '|' + $p.Data } }";
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden };
                var list = new List<KeyValuePair<string, int>>();
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } }
                    foreach (string line in o.Split(new[] { '\n' }))
                    {
                        int i = line.LastIndexOf('|'); int v;
                        if (i <= 0 || !int.TryParse(line.Substring(i + 1).Trim(), out v) || v < 0 || v > 100) continue;
                        string name = line.Substring(0, i).Trim();
                        if (name.Length > 0 && !list.Any(x => x.Key == name)) list.Add(new KeyValuePair<string, int>(name, v));
                    }
                }
                _pnp = list.ToArray(); Combine();
                Status = list.Count > 0 ? list.Count + " cihaz" : "Pil bilgisi veren cihaz bulunamadı";
            }
            catch (Exception ex) { Status = "Okunamadı: " + ex.Message; }
            finally { _busy = false; }
        }
    }
}
