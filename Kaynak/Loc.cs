using System;
using System.Globalization;
using System.IO;

namespace ProSystemMonitor
{
    internal static class L
    {
        public static string Lang = "tr";
        private static readonly CultureInfo _tr = new CultureInfo("tr-TR");
        private static readonly CultureInfo _en = new CultureInfo("en-US");

        public static bool IsEn { get { return Lang == "en"; } }
        public static CultureInfo Culture { get { return Lang == "en" ? _en : _tr; } }

        public static string T(string tr, string en)
        {
            return Lang == "en" ? en : tr;
        }

        public static void Init()
        {
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "ui.txt");
                if (File.Exists(p))
                {
                    foreach (string line in File.ReadAllLines(p))
                    {
                        if (line.StartsWith("lang=", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring(5).Trim().ToLowerInvariant();
                            if (v == "en" || v == "tr") { Lang = v; return; }
                        }
                    }
                    Lang = "tr";
                    return;
                }
                string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                Lang = string.Equals(sys, "tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
            }
            catch { Lang = "tr"; }
        }

        public static void Set(string lang, DashboardForm dash)
        {
            string next = (lang == "en") ? "en" : "tr";
            if (Lang == next && dash != null) { dash.RefreshLanguage(); return; }
            Lang = next;
            SysInfo.Reset();
            Hotkeys.RefreshStatusText();
            Program.RebuildTrayMenu();
            if (dash != null && !dash.IsDisposed)
            {
                dash.SaveSettings();
                Extras.Start(dash._city);
                dash.RefreshLanguage();
            }
        }
    }
}
