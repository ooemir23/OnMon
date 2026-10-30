using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ProSystemMonitor
{
    /// <summary>Bilgisayarı belirli saatte / süre sonra kapatma veya yeniden başlatma zamanlayıcısı (Windows'un shutdown.exe zamanlayıcısını kullanır).</summary>
    internal static class ShutdownTimer
    {
        public static int Action;            // 0 kapalı, 1 kapat, 2 yeniden başlat
        public static DateTime Target;       // yerel saat
        public static bool Force = true;
        public static string Error = "";

        private static string FilePath { get { return Path.Combine(AppData.Dir, "zamanlayici.txt"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string[] p = File.ReadAllText(FilePath).Trim().Split('|');
                long ticks; int a;
                if (p.Length >= 3 && int.TryParse(p[0], out a) && long.TryParse(p[1], out ticks))
                {
                    DateTime t = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                    if (a > 0 && t > DateTime.Now) { Action = a; Target = t; Force = p[2] == "1"; }
                    else Clear();
                }
            }
            catch { }
        }

        private static void Clear()
        {
            Action = 0;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        public static TimeSpan Remaining { get { return Action == 0 ? TimeSpan.Zero : Target - DateTime.Now; } }
        public static bool Active { get { return Action != 0 && Target > DateTime.Now; } }

        private static int Run(string args)
        {
            var psi = new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (Process p = Process.Start(psi)) { if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return -1; } return p.ExitCode; }
        }

        public static bool Start(int action, DateTime target, bool force)
        {
            Error = "";
            try
            {
                int secs = (int)Math.Ceiling((target - DateTime.Now).TotalSeconds);
                if (secs < 15) { Error = "Süre en az 15 saniye olmalı."; return false; }
                Run("/a");   // varsa önceki zamanlayıcıyı iptal et
                int rc = Run((action == 2 ? "/r" : "/s") + " /t " + secs + (force ? " /f" : "") + " /c \"OnMon zamanlayıcısı\"");
                if (rc != 0) { Error = "Zamanlayıcı kurulamadı (kod " + rc + ")."; return false; }
                Action = action; Target = target; Force = force;
                Directory.CreateDirectory(AppData.Dir);
                File.WriteAllText(FilePath, action + "|" + target.ToUniversalTime().Ticks + "|" + (force ? "1" : "0"), Encoding.UTF8);
                return true;
            }
            catch (Exception ex) { Error = ex.Message; return false; }
        }

        public static void Cancel()
        {
            try { Run("/a"); } catch { }
            Clear();
        }

        public static string Format(TimeSpan r)
        {
            if (r.TotalSeconds <= 0) return "";
            int mins = (int)Math.Ceiling(r.TotalMinutes);
            if (mins <= 1) return "<1dk";
            return mins >= 60 ? (mins / 60) + "sa " + (mins % 60) + "dk" : mins + "dk";
        }
    }
}
