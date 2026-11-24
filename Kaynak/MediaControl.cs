using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ProSystemMonitor
{
    public sealed class MediaInfo
    {
        public string Title = "";
        public string Artist = "";
        public string AppName = "";
        public bool IsActive;
    }

    /// <summary>Çalan medya bilgisini okur ve oynatma/duraklatma/geçme medya tuşlarını gönderir.</summary>
    internal static class MediaControl
    {
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private delegate bool EnumWinProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWinProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private static string QueryProcName(uint pid)
        {
            if (pid == 0) return "";
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
                if (h == IntPtr.Zero) return "";
                var sb = new StringBuilder(260);
                int cap = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref cap) && cap > 0)
                    return Path.GetFileNameWithoutExtension(sb.ToString(0, cap)).ToLowerInvariant();
            }
            catch { }
            finally { if (h != IntPtr.Zero) try { CloseHandle(h); } catch { } }
            return "";
        }

        private static readonly object _lk = new object();
        private static DateTime _lastScan = DateTime.MinValue;
        private static MediaInfo _cached = new MediaInfo();

        private static void SendKey(byte vk)
        {
            try
            {
                keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }

        public static void PlayPause() { SendKey(VK_MEDIA_PLAY_PAUSE); }
        public static void Next() { SendKey(VK_MEDIA_NEXT_TRACK); }
        public static void Prev() { SendKey(VK_MEDIA_PREV_TRACK); }

        public static MediaInfo Read()
        {
            lock (_lk)
            {
                if ((DateTime.UtcNow - _lastScan).TotalSeconds < 2.0) return _cached;
                _lastScan = DateTime.UtcNow;

                var procMap = new Dictionary<uint, string>();
                var pathBuf = new StringBuilder(304);
                MediaInfo best = null;
                try
                {
                    EnumWindows(delegate (IntPtr hwnd, IntPtr lp)
                    {
                        if (best != null && best.AppName == "Spotify") return true;
                        if (!IsWindowVisible(hwnd)) return true;
                        int len = GetWindowTextLength(hwnd);
                        if (len < 3 || len > 300) return true;
                        pathBuf.Length = 0;
                        if (pathBuf.Capacity < len + 2) pathBuf.Capacity = len + 2;
                        GetWindowText(hwnd, pathBuf, pathBuf.Capacity);
                        string wt = pathBuf.ToString().Trim();
                        if (wt.Length == 0) return true;

                        uint pid;
                        GetWindowThreadProcessId(hwnd, out pid);
                        string pn;
                        if (!procMap.TryGetValue(pid, out pn))
                        {
                            pn = QueryProcName(pid);
                            procMap[pid] = pn;
                        }

                        MediaInfo candidate = ParseWindowMedia(pn, wt);
                        if (candidate != null)
                        {
                            if (best == null || candidate.AppName == "Spotify" || candidate.AppName == "YouTube Music")
                                best = candidate;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                catch { }

                _cached = best ?? new MediaInfo();
                return _cached;
            }
        }

        private static MediaInfo ParseWindowMedia(string proc, string title)
        {
            if (proc == "spotify")
            {
                if (string.Equals(title, "Spotify", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(title, "Spotify Free", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(title, "Spotify Premium", StringComparison.OrdinalIgnoreCase))
                    return null;
                return SplitArtistTrack(title, "Spotify");
            }
            if (proc == "vlc")
            {
                if (title.EndsWith(" - VLC media player", StringComparison.OrdinalIgnoreCase))
                    return SplitArtistTrack(title.Substring(0, title.Length - 19), "VLC");
                return null;
            }
            if (proc == "foobar2000")
            {
                if (title.Contains("[foobar2000]"))
                    return SplitArtistTrack(title.Replace("[foobar2000]", "").Trim(), "foobar2000");
                return null;
            }
            if (proc == "aimp" || proc == "musicbee" || proc == "tidal" || proc == "applemusic")
            {
                if (title.IndexOf(" - ") > 0)
                    return SplitArtistTrack(title, char.ToUpper(proc[0]) + proc.Substring(1));
                return null;
            }

            // Browsers hosting YouTube / YouTube Music / Spotify Web / SoundCloud / Deezer / Twitch
            if (proc == "chrome" || proc == "msedge" || proc == "firefox" || proc == "opera" || proc == "brave" || proc == "vivaldi")
            {
                string clean = StripBrowserSuffix(title);
                if (clean.EndsWith(" - YouTube Music", StringComparison.OrdinalIgnoreCase))
                    return SplitArtistTrack(clean.Substring(0, clean.Length - 16), "YouTube Music");
                if (clean.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    string sub = clean.Substring(0, clean.Length - 10).Trim();
                    if (sub.Length > 0 && !string.Equals(sub, "YouTube", StringComparison.OrdinalIgnoreCase))
                        return SplitArtistTrack(sub, "YouTube");
                }
                if (clean.IndexOf("Spotify", StringComparison.OrdinalIgnoreCase) >= 0 && clean.Contains(" · "))
                    return SplitDotTrack(clean.Replace(" - Spotify", ""), "Spotify Web");
                if (clean.EndsWith(" - Twitch", StringComparison.OrdinalIgnoreCase))
                    return new MediaInfo { Title = clean.Substring(0, clean.Length - 9).Trim(), Artist = "Twitch", AppName = "Twitch", IsActive = true };
                if (clean.EndsWith(" - Deezer", StringComparison.OrdinalIgnoreCase))
                    return SplitArtistTrack(clean.Substring(0, clean.Length - 9), "Deezer");
            }
            return null;
        }

        private static string StripBrowserSuffix(string t)
        {
            string[] sufs = { " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Mozilla Firefox", " - Brave", " - Opera", " - Vivaldi" };
            foreach (string s in sufs)
            {
                if (t.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    return t.Substring(0, t.Length - s.Length).Trim();
            }
            return t;
        }

        private static MediaInfo SplitArtistTrack(string s, string app)
        {
            int idx = s.IndexOf(" - ");
            if (idx > 0 && idx + 3 < s.Length)
            {
                return new MediaInfo
                {
                    Artist = s.Substring(0, idx).Trim(),
                    Title = s.Substring(idx + 3).Trim(),
                    AppName = app,
                    IsActive = true
                };
            }
            return new MediaInfo { Title = s.Trim(), Artist = app, AppName = app, IsActive = true };
        }

        private static MediaInfo SplitDotTrack(string s, string app)
        {
            int idx = s.IndexOf(" · ");
            if (idx > 0 && idx + 3 < s.Length)
            {
                return new MediaInfo
                {
                    Title = s.Substring(0, idx).Trim(),
                    Artist = s.Substring(idx + 3).Trim(),
                    AppName = app,
                    IsActive = true
                };
            }
            return new MediaInfo { Title = s.Trim(), Artist = app, AppName = app, IsActive = true };
        }
    }
}
