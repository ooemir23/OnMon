using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ProSystemMonitor
{
    /// <summary>Tüm OnMon ayarlarını ve kayıtlı şablonları tek bir .onmonbak dosyasına yedekler ve geri yükler.</summary>
    internal static class BackupManager
    {
        private const string Header = "ONMON-BACKUP 1";

        public static int ExportBackup(string destPath)
        {
            return ExportBackup(destPath, AppData.Dir);
        }

        public static int ExportBackup(string destPath, string baseDir)
        {
            var items = new List<KeyValuePair<string, byte[]>>();

            string[] files = { "ui.txt", "widgets.txt", "profiller.txt", "kisayollar.txt", "alerts_off", "noupdate.flag" };
            foreach (string f in files)
            {
                string full = Path.Combine(baseDir, f);
                if (File.Exists(full))
                    items.Add(new KeyValuePair<string, byte[]>(f, File.ReadAllBytes(full)));
            }

            string tplDir = Path.Combine(baseDir, "Sablonlar");
            if (Directory.Exists(tplDir))
            {
                foreach (string full in Directory.GetFiles(tplDir, "*.onmon"))
                {
                    string rel = "Sablonlar/" + Path.GetFileName(full);
                    items.Add(new KeyValuePair<string, byte[]>(rel, File.ReadAllBytes(full)));
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(Header);
            sb.AppendLine("created=" + DateTime.UtcNow.ToString("o"));
            sb.AppendLine("version=" + Updater.AppVersion);
            foreach (var kv in items)
            {
                sb.AppendLine("===FILE:" + kv.Key + "===");
                sb.AppendLine(Convert.ToBase64String(kv.Value));
                sb.AppendLine("===END===");
            }
            File.WriteAllText(destPath, sb.ToString(), new UTF8Encoding(false));
            return items.Count;
        }

        public static bool ImportBackup(string srcPath, out int fileCount)
        {
            return ImportBackup(srcPath, AppData.Dir, out fileCount);
        }

        public static bool ImportBackup(string srcPath, string baseDir, out int fileCount)
        {
            fileCount = 0;
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) return false;
            string[] lines = File.ReadAllLines(srcPath, new UTF8Encoding(false));
            if (lines.Length == 0 || lines[0].Trim() != Header) return false;

            Directory.CreateDirectory(baseDir);

            string curRel = null;
            var b64 = new StringBuilder();
            for (int i = 1; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.StartsWith("===FILE:") && ln.EndsWith("==="))
                {
                    curRel = ln.Substring(8, ln.Length - 11).Trim().Replace('\\', '/');
                    b64.Length = 0;
                }
                else if (ln == "===END===" && curRel != null)
                {
                    if (!curRel.Contains("..") && !Path.IsPathRooted(curRel) && curRel.Length > 0)
                    {
                        byte[] bytes = Convert.FromBase64String(b64.ToString());
                        string dest = Path.Combine(baseDir, curRel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.WriteAllBytes(dest, bytes);
                        fileCount++;
                    }
                    curRel = null;
                }
                else if (curRel != null)
                {
                    b64.Append(ln);
                }
            }
            return fileCount > 0;
        }
    }
}
