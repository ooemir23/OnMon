using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ProSystemMonitor
{
    /// <summary>GitHub'daki en son sürümü denetler, exe'yi indirip kendini değiştirir ve yeniden başlar.</summary>
    internal static class Updater
    {
        public const int AppVersion = 20;
        private const string RawBase = "https://raw.githubusercontent.com/ooemir23/OnMon/main/";

        public static volatile int LatestVersion;
        public static volatile string LatestNotes = "";
        public static volatile string Status = "Henüz denetlenmedi";
        public static volatile bool Busy;
        private static string _sha = "";
        private static string _file = "ProSystemMonitor.exe";

        private static string FlagPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "noupdate.flag"); }
        }

        public static bool AutoCheck
        {
            get { try { return !File.Exists(FlagPath); } catch { return true; } }
            set
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FlagPath));
                    if (value) { if (File.Exists(FlagPath)) File.Delete(FlagPath); }
                    else File.WriteAllText(FlagPath, "1");
                }
                catch { }
            }
        }

        public static bool UpdateAvailable { get { return LatestVersion > AppVersion; } }

        private static WebClient Client()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls; } catch { }
            var wc = new WebClient();
            wc.Encoding = Encoding.UTF8;
            wc.Headers[HttpRequestHeader.UserAgent] = "OnMon-Updater";
            wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            return wc;
        }

        /// <summary>version.txt dosyasını okur. Hata olursa false döner ve Status'a yazar.</summary>
        public static bool Check()
        {
            try
            {
                string txt;
                using (WebClient wc = Client())
                    txt = wc.DownloadString(RawBase + "version.txt?t=" + DateTime.UtcNow.Ticks);
                int ver = 0; string notes = "", sha = "", file = "ProSystemMonitor.exe";
                foreach (string raw in txt.Split(new[] { '\n' }))
                {
                    string line = raw.Trim();
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant(), v = line.Substring(i + 1).Trim();
                    if (k == "version") int.TryParse(v, out ver);
                    else if (k == "notes") notes = v.Replace("\\n", "\r\n");
                    else if (k == "sha256") sha = v.ToLowerInvariant();
                    else if (k == "file" && v.Length > 0) file = v;
                }
                if (ver <= 0) { Status = "Sürüm bilgisi okunamadı"; return false; }
                LatestVersion = ver; LatestNotes = notes; _sha = sha; _file = file;
                Status = ver > AppVersion ? "Yeni sürüm var: v" + ver : "Güncelsin (v" + AppVersion + ")";
                return true;
            }
            catch (Exception ex)
            {
                Status = "Denetlenemedi: " + ex.Message;
                Program.Log("güncelleme-denetimi", ex);
                return false;
            }
        }

        /// <summary>Yeni exe'yi indirir, çalışan exe ile değiştirir ve uygulamayı yeniden başlatır. Hata varsa mesaj döner, başarılıysa null.</summary>
        public static string ApplyUpdate()
        {
            if (Busy) return "Güncelleme zaten sürüyor";
            Busy = true;
            try
            {
                if (!UpdateAvailable) return "Yeni sürüm yok";
                string exe = Application.ExecutablePath;
                string dir = Path.GetDirectoryName(exe);
                string tmp = Path.Combine(dir, "update.tmp");
                Status = "İndiriliyor…";
                using (WebClient wc = Client())
                    wc.DownloadFile(RawBase + _file + "?t=" + DateTime.UtcNow.Ticks, tmp);

                byte[] data = File.ReadAllBytes(tmp);
                if (data.Length < 50000 || data[0] != (byte)'M' || data[1] != (byte)'Z')
                { try { File.Delete(tmp); } catch { } return "İndirilen dosya geçersiz"; }
                if (_sha.Length > 0)
                {
                    string h;
                    using (var sha = SHA256.Create())
                        h = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                    if (h != _sha) { try { File.Delete(tmp); } catch { } return "Doğrulama başarısız (SHA-256 uyuşmuyor)"; }
                }

                // Çalışan exe silinemez ama yeniden adlandırılabilir.
                string old = exe + ".old";
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                File.Move(exe, old);
                try { File.Move(tmp, exe); }
                catch { try { File.Move(old, exe); } catch { } throw; }

                Status = "Yeniden başlatılıyor…";
                var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & start \"\" \"" + exe + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(psi);
                Program.ExitApp();
                return null;
            }
            catch (Exception ex)
            {
                Program.Log("güncelleme", ex);
                Status = "Güncelleme başarısız: " + ex.Message;
                return ex.Message;
            }
            finally { Busy = false; }
        }

        /// <summary>Önceki güncellemeden kalan .old dosyasını temizler.</summary>
        public static void Cleanup()
        {
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
        }

        /// <summary>Arka planda denetim: açılıştan 45 sn sonra, sonra 6 saatte bir.</summary>
        public static void StartBackground()
        {
            var t = new Thread(delegate ()
            {
                Thread.Sleep(45000);
                int lastNotified = 0;
                while (true)
                {
                    try
                    {
                        if (AutoCheck && Check() && UpdateAvailable && LatestVersion != lastNotified)
                        {
                            lastNotified = LatestVersion;
                            Program.ShowBalloon("Yeni sürüm hazır: v" + LatestVersion + ". Ayarlar > Güncelleme sayfasından güncelleyebilirsin.");
                        }
                    }
                    catch { }
                    Thread.Sleep(6 * 3600 * 1000);
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.Start();
        }
    }
}
