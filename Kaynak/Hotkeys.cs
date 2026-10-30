using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ProSystemMonitor
{
    /// <summary>Genel (her uygulamada çalışan) kısayol tuşları: çıkış/giriş cihazı değiştirme, mikrofon/ses sessize alma, profil.</summary>
    internal static class Hotkeys
    {
        public static readonly string[] Ids = { "out", "mic", "micmute", "volmute", "profile" };
        public static readonly string[] Names = {
            "Ses çıkış cihazını değiştir (seçili olanlar arasında)",
            "Mikrofon / giriş cihazını değiştir (seçili olanlar arasında)",
            "Mikrofonu sessize al / aç",
            "Hoparlörü sessize al / aç",
            "Sonraki ses profili" };

        // [mod, vk]; mod: 1=Alt 2=Ctrl 4=Shift 8=Win
        public static readonly Dictionary<string, int[]> Map = new Dictionary<string, int[]>();
        public static string Status = "";

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

        private sealed class Win : NativeWindow
        {
            public Win() { CreateHandle(new CreateParams()); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312) { int i = m.WParam.ToInt32() - 1; if (i >= 0 && i < Ids.Length) Run(Ids[i]); }
                base.WndProc(ref m);
            }
        }
        private static Win _win;
        private static string FilePath { get { return Path.Combine(AppData.Dir, "kisayollar.txt"); } }

        public static void Init()
        {
            try
            {
                Load();
                _win = new Win();
                RegisterAll();
            }
            catch (Exception ex) { Status = "Kısayollar başlatılamadı: " + ex.Message; }
        }

        private static void Load()
        {
            Map.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string l in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int e = l.IndexOf('='); if (e < 1) continue;
                    string[] p = l.Substring(e + 1).Split(',');
                    int m, v;
                    if (p.Length == 2 && int.TryParse(p[0], out m) && int.TryParse(p[1], out v) && v > 0) Map[l.Substring(0, e)] = new[] { m, v };
                }
            }
            catch { }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppData.Dir);
                File.WriteAllLines(FilePath, Map.Select(kv => kv.Key + "=" + kv.Value[0] + "," + kv.Value[1]).ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        private static void RegisterAll()
        {
            if (_win == null) return;
            var bad = new List<string>();
            for (int i = 0; i < Ids.Length; i++)
            {
                UnregisterHotKey(_win.Handle, i + 1);
                int[] k;
                if (!Map.TryGetValue(Ids[i], out k)) continue;
                if (!RegisterHotKey(_win.Handle, i + 1, (uint)k[0] | 0x4000u, (uint)k[1])) bad.Add(Describe(k));
            }
            Status = bad.Count == 0 ? "" : "Başka bir uygulama kullanıyor: " + string.Join(", ", bad.ToArray());
        }

        public static void Set(string id, int mod, int vk)
        {
            // aynı tuş başka eyleme atanmışsa onu kaldır
            foreach (string o in Map.Where(kv => kv.Key != id && kv.Value[0] == mod && kv.Value[1] == vk).Select(kv => kv.Key).ToList()) Map.Remove(o);
            if (vk <= 0) Map.Remove(id); else Map[id] = new[] { mod, vk };
            Save(); RegisterAll();
        }

        public static string Describe(int[] k)
        {
            if (k == null) return "";
            var s = new List<string>();
            if ((k[0] & 2) != 0) s.Add("Ctrl");
            if ((k[0] & 1) != 0) s.Add("Alt");
            if ((k[0] & 4) != 0) s.Add("Shift");
            if ((k[0] & 8) != 0) s.Add("Win");
            s.Add(((Keys)k[1]).ToString());
            return string.Join("+", s.ToArray());
        }

        // ---------------- eylemler ----------------
        private static HashSet<string> ReadFavs(string key)
        {
            var set = new HashSet<string>();
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "ui.txt");
                if (File.Exists(p))
                    foreach (string l in File.ReadAllLines(p))
                        if (l.StartsWith(key + "=")) foreach (string x in l.Substring(key.Length + 1).Split('|')) if (x.Length > 0) set.Add(x);
            }
            catch { }
            return set;
        }

        private static int _prof = -1;

        private static void Run(string id)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (id == "out" || id == "mic")
                    {
                        AudioSwitch.Refresh();
                        bool outp = id == "out";
                        HashSet<string> fav = ReadFavs(outp ? "soundfav" : "micfav");
                        var list = new List<AudioDevice>();
                        foreach (AudioDevice d in (outp ? AudioSwitch.Devices : AudioSwitch.MicDevices))
                            if (fav.Count == 0 || fav.Contains(d.Id) || d.IsDefault) list.Add(d);
                        if (list.Count < 2) return;
                        int i = list.FindIndex(d => d.IsDefault);
                        AudioSwitch.SetDefault(list[(i + 1) % list.Count].Id);
                        AudioSwitch.InvalidateCache();
                        if (outp) { Thread.Sleep(350); try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }
                        AudioSwitch.Refresh();
                    }
                    else if (id == "micmute")
                    {
                        AudioSwitch.ToggleMicMute(); AudioSwitch.PollMic();
                        try { if (AudioSwitch.MicMuted) System.Media.SystemSounds.Hand.Play(); else System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    }
                    else if (id == "volmute") { AudioSwitch.ToggleVolMute(); AudioSwitch.PollVolume(); }
                    else if (id == "profile")
                    {
                        AudioProfiles.Load();
                        if (AudioProfiles.All.Count == 0) return;
                        _prof = (_prof + 1) % AudioProfiles.All.Count;
                        AudioProfiles.Apply(AudioProfiles.All[_prof]);
                    }
                }
                catch { }
            });
        }
    }
}
