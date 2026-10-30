using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;

namespace ProSystemMonitor
{
    /// <summary>Sistem bilgisi penceresi için WMI verisini yalnızca pano açılınca bir kez okur (kalıcı nesne tutmaz).</summary>
    internal static class SysInfo
    {
        public static volatile List<KeyValuePair<string, string>> Rows = new List<KeyValuePair<string, string>>();
        public static DateTime Boot = DateTime.MinValue;
        public static volatile bool Loading;
        private static bool _done;

        private static string One(string cls, string prop, string where)
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT " + prop + " FROM " + cls + (where != null ? " WHERE " + where : "")))
                using (ManagementObjectCollection c = s.Get())
                    foreach (ManagementObject m in c) { using (m) { object v = m[prop]; return v == null ? "" : v.ToString().Trim(); } }
            }
            catch { }
            return "";
        }

        public static void LoadAsync(Action done)
        {
            if (_done || Loading) { if (done != null) done(); return; }
            Loading = true;
            new Thread(delegate ()
            {
                var rows = new List<KeyValuePair<string, string>>();
                try
                {
                    string bv = One("Win32_BIOS", "SMBIOSBIOSVersion", null), bd = One("Win32_BIOS", "ReleaseDate", null), bm = One("Win32_BIOS", "Manufacturer", null);
                    string date = "";
                    if (bd.Length >= 8) date = bd.Substring(6, 2) + "." + bd.Substring(4, 2) + "." + bd.Substring(0, 4);
                    if (bv.Length > 0) rows.Add(new KeyValuePair<string, string>("BIOS sürümü", (bm.Length > 0 ? bm + "  " : "") + bv + (date.Length > 0 ? "  (" + date + ")" : "")));
                    string drv = One("Win32_VideoController", "DriverVersion", "AdapterRAM IS NOT NULL AND CurrentHorizontalResolution IS NOT NULL");
                    if (drv.Length > 0) rows.Add(new KeyValuePair<string, string>("Ekran sürücüsü", drv));

                    // RAM modülleri
                    int n = 0; long total = 0; int speed = 0; string type = "";
                    using (var s = new ManagementObjectSearcher("SELECT Capacity, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                    using (ManagementObjectCollection c = s.Get())
                        foreach (ManagementObject m in c)
                        {
                            using (m)
                            {
                                n++; total += Convert.ToInt64(m["Capacity"] ?? 0);
                                if (m["ConfiguredClockSpeed"] != null) speed = Convert.ToInt32(m["ConfiguredClockSpeed"]);
                                int t = m["SMBIOSMemoryType"] != null ? Convert.ToInt32(m["SMBIOSMemoryType"]) : 0;
                                type = t == 34 ? "DDR5" : t == 26 ? "DDR4" : t == 24 ? "DDR3" : "";
                            }
                        }
                    if (n > 0) rows.Add(new KeyValuePair<string, string>("Bellek", n + " × " + (total / n / 1073741824L) + " GB  " + type + (speed > 0 ? "  " + speed + " MHz" : "")));

                    string os = One("Win32_OperatingSystem", "Caption", null), build = One("Win32_OperatingSystem", "BuildNumber", null);
                    if (os.Length > 0) rows.Add(new KeyValuePair<string, string>("Windows", os.Replace("Microsoft ", "") + "  (build " + build + ")"));
                    string boot = One("Win32_OperatingSystem", "LastBootUpTime", null);
                    if (boot.Length >= 14) try { Boot = ManagementDateTimeConverter.ToDateTime(boot); } catch { }
                }
                catch { }
                Rows = rows; _done = true; Loading = false;
                if (done != null) done();
                Program.TrimProcessMemory();
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
    }
}
