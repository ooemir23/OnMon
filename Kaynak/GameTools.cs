using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
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
        private static string _statusRaw = "";
        public static string Status
        {
            get { return string.IsNullOrEmpty(_statusRaw) ? L.T("Henüz okunmadı", "Not polled yet") : _statusRaw; }
            set { _statusRaw = value; }
        }
        private static int _started;
        private static volatile bool _busy;

        private static KeyValuePair<string, int>[] _pnp = new KeyValuePair<string, int>[0];
        private static KeyValuePair<string, int>[] _rog = new KeyValuePair<string, int>[0];
        public static volatile bool RogOffline;
        private static volatile int _rl = -1, _rr = -1, _rc = -1, _inMask = -1;
        // CC-12-08 bayt5: bit0 = sol kulaklık kutuda, bit4 = sağ kulaklık kutuda (kutudaki kulaklık şarj olur)
        private static void RogPublish()
        {
            bool known = _inMask >= 0 && !RogOffline;
            string sl = L.T("Cetra Sol", "Cetra L") + ((known && (_inMask & 0x01) != 0) ? " \u26A1" : "");
            string sr = L.T("Cetra Sağ", "Cetra R") + ((known && (_inMask & 0x10) != 0) ? " \u26A1" : "");
            _rog = new[] {
                new KeyValuePair<string, int>(sl, _rl),
                new KeyValuePair<string, int>(sr, _rr),
                new KeyValuePair<string, int>(L.T("Cetra Kutu", "Cetra Case"), _rc) };
            Combine();
        }
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
                                if (c >= 8 && buf[0] == 0xCC && buf[1] == 0x12)
                                {
                                    if (buf[2] == 0x09 && (buf[5] + buf[6] + buf[7]) > 0 && buf[5] <= 100 && buf[6] <= 100 && buf[7] <= 100)
                                    { _rl = buf[5]; _rr = buf[6]; _rc = buf[7]; }
                                    else if (buf[2] == 0x08) _inMask = buf[5];
                                    if (buf[2] == 0x01) RogOffline = (buf[5] == 0);
                                    else if (buf[2] == 0x09) RogOffline = false;
                                    if (_rl >= 0) RogPublish();
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
                    Thread.Sleep(3 * 60 * 1000);
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.Start();
        }

        private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
        private const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
        private const uint DEVPROP_TYPE_BYTE = 0x00000003;
        private const uint DEVPROP_TYPE_UINT32 = 0x00000007;
        private const uint DEVPROP_TYPE_BOOLEAN = 0x00000011;
        private const uint DEVPROP_TYPE_STRING = 0x00000012;

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_ID_List_SizeW(out uint pulLen, string pszFilter, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_ID_ListW(string pszFilter, char[] Buffer, uint BufferLen, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_DevNode_PropertyW(uint dnDevInst, ref DEVPROPKEY PropertyKey, out uint PropertyType, byte[] PropertyBuffer, ref uint PropertyBufferSize, uint ulFlags);

        private static DEVPROPKEY KeyBattery = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
        private static DEVPROPKEY KeyConnected = new DEVPROPKEY { fmtid = new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), pid = 15 };
        private static DEVPROPKEY KeyFriendlyName = new DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
        private static DEVPROPKEY KeyDeviceDesc = new DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 2 };
        private static DEVPROPKEY KeyClass = new DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 9 };

        private static string[] EnumPresentDeviceIds()
        {
            uint len;
            if (CM_Get_Device_ID_List_SizeW(out len, null, CM_GETIDLIST_FILTER_PRESENT) != 0 || len == 0)
                return new string[0];
            char[] buf = new char[len];
            if (CM_Get_Device_ID_ListW(null, buf, len, CM_GETIDLIST_FILTER_PRESENT) != 0)
                return new string[0];
            string all = new string(buf);
            return all.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryGetDevPropBytes(uint devInst, DEVPROPKEY key, out uint propType, out byte[] data)
        {
            propType = 0;
            data = null;
            uint sz = 0;
            uint rc = CM_Get_DevNode_PropertyW(devInst, ref key, out propType, null, ref sz, 0);
            if ((rc != 0 && rc != 0x1A) || sz == 0 || sz > 4096) return false;
            byte[] buf = new byte[sz];
            rc = CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref sz, 0);
            if (rc != 0) return false;
            data = buf;
            return true;
        }

        private static string GetDevPropString(uint devInst, DEVPROPKEY key)
        {
            uint pt; byte[] data;
            if (!TryGetDevPropBytes(devInst, key, out pt, out data) || pt != DEVPROP_TYPE_STRING || data.Length < 2) return "";
            return Encoding.Unicode.GetString(data).TrimEnd('\0').Trim();
        }

        private static int GetDevPropBattery(uint devInst)
        {
            uint pt; byte[] data;
            if (!TryGetDevPropBytes(devInst, KeyBattery, out pt, out data) || data.Length == 0) return -1;
            if (pt == DEVPROP_TYPE_BYTE) return data[0];
            if (pt == DEVPROP_TYPE_UINT32 && data.Length >= 4) return (int)BitConverter.ToUInt32(data, 0);
            return -1;
        }

        private static bool IsDevConnected(uint devInst)
        {
            uint pt; byte[] data;
            if (!TryGetDevPropBytes(devInst, KeyConnected, out pt, out data)) return true;
            if (pt == DEVPROP_TYPE_BOOLEAN && data.Length >= 1) return data[0] != 0;
            return true;
        }

        /// <summary>Kulaklık gibi cihazların Windows'ta hangi özellikleri bildirdiğini piller.txt dosyasına yazar (pil verisini bulmak için).</summary>
        public static void DumpAsync()
        {
            var t = new Thread(delegate ()
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piller.txt");
                    var sb = new StringBuilder();
                    string[] keys = { "cetra", "speednova", "rog", "bluetooth", "headset", "headphone", "kulakl", "earbud", "xbox", "controller" };
                    foreach (string id in EnumPresentDeviceIds())
                    {
                        uint devInst;
                        if (CM_Locate_DevNodeW(out devInst, id, CM_LOCATE_DEVNODE_NORMAL) != 0) continue;
                        string name = GetDevPropString(devInst, KeyFriendlyName);
                        if (name.Length == 0) name = GetDevPropString(devInst, KeyDeviceDesc);
                        string cls = GetDevPropString(devInst, KeyClass);
                        string low = name.ToLowerInvariant();
                        bool match = false;
                        foreach (string k in keys) { if (low.Contains(k)) { match = true; break; } }
                        int bat = GetDevPropBattery(devInst);
                        if (!match && bat < 0) continue;
                        sb.AppendLine("=== " + name + " | " + cls + " | " + id);
                        sb.AppendLine("   Connected = " + IsDevConnected(devInst));
                        if (bat >= 0) sb.AppendLine("   Battery = " + bat + "%");
                    }
                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                    Status = L.T("piller.txt yazıldı", "piller.txt written");
                }
                catch (Exception ex) { Status = L.T("Yazılamadı: ", "Write failed: ") + ex.Message; }
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
                    Status = L.T("hid.txt yazıldı", "hid.txt written");
                }
                catch (Exception ex) { sb.AppendLine("HATA: " + ex); Status = L.T("HID hatası: ", "HID error: ") + ex.Message; }
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
                var list = new List<KeyValuePair<string, int>>();
                foreach (string id in EnumPresentDeviceIds())
                {
                    uint devInst;
                    if (CM_Locate_DevNodeW(out devInst, id, CM_LOCATE_DEVNODE_NORMAL) != 0) continue;
                    int v = GetDevPropBattery(devInst);
                    if (v < 0 || v > 100) continue;
                    if (!IsDevConnected(devInst)) continue;
                    string name = GetDevPropString(devInst, KeyFriendlyName);
                    if (name.Length == 0) name = GetDevPropString(devInst, KeyDeviceDesc);
                    if (name.Length > 0 && !list.Any(x => x.Key == name))
                        list.Add(new KeyValuePair<string, int>(name, v));
                }
                _pnp = list.ToArray(); Combine();
                Status = list.Count > 0 ? list.Count + L.T(" cihaz", " devices") : L.T("Pil bilgisi veren cihaz bulunamadı", "No battery-reporting peripherals found");
            }
            catch (Exception ex) { Status = L.T("Okunamadı: ", "Read error: ") + ex.Message; }
            finally { _busy = false; }
        }
    }
}
