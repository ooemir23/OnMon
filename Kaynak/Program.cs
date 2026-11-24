using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace ProSystemMonitor
{
    static class Program
    {
        public static int Port = 17500;
        public static volatile int PollIntervalMs = 2000;
        public static double CollectMs;
        public static volatile bool DashVisible;
        public static string LatestJson = "{}";
        private static NotifyIcon _trayIcon;
        private static TelemetryCollector _collector;
        private static HttpListener _listener;
        private static readonly ConcurrentDictionary<Guid, StreamWriter> _sseClients = new ConcurrentDictionary<Guid, StreamWriter>();
        public static bool HasSseClients { get { return !_sseClients.IsEmpty; } }
        private static volatile bool _running = true;

        [STAThread]
        static void Main(string[] args)
        {
            L.Init();
            if (args != null && args.Contains("--selftest"))
            {
                int rc = RunSelfTest();
                Environment.ExitCode = rc;
                return;
            }

            string pendingFile = null;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (!string.IsNullOrEmpty(a) && a.EndsWith(".onmon", StringComparison.OrdinalIgnoreCase) && File.Exists(a))
                    {
                        pendingFile = a;
                        break;
                    }
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\ProSystemMonitor_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    if (!string.IsNullOrEmpty(pendingFile))
                    {
                        try
                        {
                            Directory.CreateDirectory(AppData.Dir);
                            File.WriteAllText(Path.Combine(AppData.Dir, "pending_import.txt"), pendingFile, Encoding.UTF8);
                        }
                        catch { }
                    }
                    SignalFirstInstance();
                    return;
                }

                Application.ThreadException += (o, e) => Log("ui-thread", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log("unhandled", e.ExceptionObject as Exception);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                LowerOwnPriority();
                try { Log("baslangic", new Exception("açıldı; args=" + string.Join(" ", args ?? new string[0]) + "; bilgisayar açık kalma=" + (Environment.TickCount / 60000) + " dk")); } catch { }
                _ui = new Control(); _ui.CreateControl(); var hnd = _ui.Handle;
                Hotkeys.Init();
                _showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\ProSystemMonitor_ShowDash");
                new Thread(() => { while (_running) { try { if (_showEvt.WaitOne(1000)) { LaunchDashboardOnSecondMonitor(); CheckPendingImport(); } } catch { } } }) { IsBackground = true }.Start();
                _collector = new TelemetryCollector();

                Thread serverThread = new Thread(HttpServerLoop) { IsBackground = true };
                serverThread.Start();

                SetupTrayIcon();

                // Sensör/WMI başlatma yavaş olabilir: arayüzü bekletmeden arka planda yap.
                Thread pollThread = new Thread(() =>
                {
                    try
                    {
                        _collector.Initialize();
                        Snap s0 = _collector.CollectSnap();
                        LastSnap = s0;
                        if (!_sseClients.IsEmpty) LatestJson = _collector.BuildJson(s0);
                        TrimProcessMemory();
                    }
                    catch { }
                    TelemetryLoop();
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                pollThread.Start();
                Updater.Cleanup();
                new Thread(() => { Thread.Sleep(20000); RepairAutostart(); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
                Updater.StartBackground();

                if (!string.IsNullOrEmpty(pendingFile))
                {
                    try
                    {
                        Directory.CreateDirectory(AppData.Dir);
                        File.WriteAllText(Path.Combine(AppData.Dir, "pending_import.txt"), pendingFile, Encoding.UTF8);
                    }
                    catch { }
                }

                if (args == null || !args.Contains("--silent") || !string.IsNullOrEmpty(pendingFile))
                {
                    LaunchDashboardOnSecondMonitor();
                    CheckPendingImport();
                }

                Application.Run();

                _running = false;
                try { if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); } } catch { }
                try { if (_listener != null) _listener.Stop(); } catch { }
                try { if (_collector != null) _collector.Dispose(); } catch { }
            }
        }

        private static int RunSelfTest()
        {
            string prevLang = L.Lang;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 1. CfgMgr32 peripheral refresh
                var sw = Stopwatch.StartNew();
                Peripherals.Refresh();
                sw.Stop();
                Console.WriteLine("CfgMgr32_Refresh_Ms=" + sw.ElapsedMilliseconds + " Status=" + Peripherals.Status);
                if (sw.ElapsedMilliseconds > 5000) throw new Exception("CfgMgr32 slow: " + sw.ElapsedMilliseconds);

                // 2. Universal multi-GPU scoring & brand detection checks
                if (TelemetryCollector.ScoreGpuName("AMD Radeon RX 7900 XTX") <= TelemetryCollector.ScoreGpuName("AMD Radeon(TM) Graphics"))
                    throw new Exception("AMD discrete vs iGPU score failed");
                if (TelemetryCollector.ScoreGpuName("NVIDIA GeForce RTX 4070 Laptop GPU") <= TelemetryCollector.ScoreGpuName("Intel(R) Iris(R) Xe Graphics"))
                    throw new Exception("NVIDIA discrete vs Intel iGPU score failed");
                if (TelemetryCollector.ScoreGpuName("Intel(R) Arc(TM) B580 Graphics") <= TelemetryCollector.ScoreGpuName("Intel(R) UHD Graphics 770"))
                    throw new Exception("Intel Arc discrete vs UHD iGPU score failed");
                if (TelemetryCollector.ScoreGpuName("Parsec Virtual Display Adapter") >= 0)
                    throw new Exception("Virtual display adapter not filtered");

                if (TelemetryCollector.ResolveBrandName("LENOVO", "82WQ Legion Pro 7", "LENOVO", "LNVNB161216") != "LENOVO")
                    throw new Exception("Brand LENOVO failed");
                if (TelemetryCollector.ResolveBrandName("Micro-Star International Co., Ltd.", "MS-7E16", "Micro-Star International", "MAG X670E TOMAHAWK") != "MSI")
                    throw new Exception("Brand MSI failed");
                if (TelemetryCollector.ResolveBrandName("HP", "OMEN by HP Gaming Laptop 16", "HP", "8BCA") != "HP")
                    throw new Exception("Brand HP failed");
                if (TelemetryCollector.ResolveBrandName("MONSTER", "Tulpar T7 V20.6", "MONSTER", "Tulpar") != "MONSTER")
                    throw new Exception("Brand MONSTER failed");
                if (TelemetryCollector.ResolveBrandName("System manufacturer", "System Product Name", "Gigabyte Technology Co., Ltd.", "B650 AORUS ELITE AX") != "GIGABYTE")
                    throw new Exception("Brand GIGABYTE failed");

                using (var dash = new DashboardForm())
                {
                    dash.WReg();
                    string[] allBuiltIn = {
                        "zen", "minimalring", "bento", "esports", "extreme", "creator", "panoramic", "vertical",
                        "classic", "hud", "cockpit", "aida", "studio"
                    };

                    // 3. Test all 13 built-in layouts + all UI forms in both TR and EN
                    foreach (string lang in new[] { "tr", "en" })
                    {
                        L.Lang = lang;
                        foreach (string id in allBuiltIn)
                        {
                            var ws = dash.WTemplate(id, 1920, 1080);
                            int visCount = 0;
                            foreach (WInst x in ws)
                            {
                                if (!x.Vis) continue;
                                visCount++;
                                if (x.X < 0 || x.Y < 0 || x.X + x.W > 1920.5f || x.Y + x.H > 1080.5f)
                                    throw new Exception("Layout " + id + " out of 1920x1080 bounds: " + x.Id + " (" + x.X + "," + x.Y + "," + x.W + "," + x.H + ")");
                            }
                            if (visCount == 0) throw new Exception("Layout " + id + " has 0 visible widgets");
                            using (Bitmap bmp = dash.RenderLayoutThumbnail(ws, 1920, 1080, Themes.All[0], 0, "", 1f, 1f, 240, 135))
                            {
                                if (bmp == null || bmp.Width != 240 || bmp.Height != 135)
                                    throw new Exception("Thumbnail failed for " + id + " (" + lang + ")");
                            }
                        }

                        using (var st = new StudioForm(dash))
                        using (var gl = new GalleryForm(dash))
                        using (var sf = new SettingsForm(dash))
                        {
                            if (st.Controls.Count == 0 || gl.Controls.Count == 0 || sf.Controls.Count == 0)
                                throw new Exception("Forms empty (" + lang + ")");
                            for (int pi = 0; pi < sf.PageCount; pi++)
                                sf.ShowPage(pi);
                        }
                    }

                    // 4. ONMON-SABLON 2 export + round-trip + scale 1920x1080 -> 800x480
                    var srcWidgets = dash.WTemplate("bento", 1920, 1080);
                    string txt = dash.BuildTemplateText("TestBento", "TestUser", "TestDesc", srcWidgets, 1920, 1080, Themes.All[2], 0, "", 1.0f, 1.0f, true, false, 4);
                    if (!txt.StartsWith("ONMON-SABLON 2") || txt.IndexOf("thumb=") < 0)
                        throw new Exception("Invalid ONMON-SABLON 2 header");

                    string tmp = Path.Combine(Path.GetTempPath(), "onmon_selftest_" + Guid.NewGuid().ToString("N") + ".onmon");
                    try
                    {
                        File.WriteAllText(tmp, txt, new UTF8Encoding(false));
                        LayoutMeta meta = dash.ReadTemplateMeta(tmp, true);
                        if (meta == null || meta.Thumb == null || meta.Name != "TestBento" || meta.Author != "TestUser" || meta.WidgetCount == 0)
                            throw new Exception("ReadTemplateMeta failed");
                        if (meta.Thumb != null) meta.Thumb.Dispose();

                        List<WInst> loaded; int cw, ch, bgc, margin; float gz, gf; string themeId, bgimg, nm, au, ds; bool topBar, hideStrip;
                        if (!dash.LoadTemplateData(tmp, out loaded, out cw, out ch, out gz, out gf, out bgc, out themeId, out bgimg, out topBar, out hideStrip, out margin, out nm, out au, out ds))
                            throw new Exception("LoadTemplateData failed");
                        if (cw != 1920 || ch != 1080 || themeId != Themes.All[2].Id || loaded.Count == 0)
                            throw new Exception("Round-trip mismatch");

                        float kx = 800f / cw, ky = 480f / ch;
                        foreach (WInst x in loaded)
                        {
                            x.X = (float)Math.Round(x.X * kx); x.Y = (float)Math.Round(x.Y * ky);
                            x.W = (float)Math.Round(x.W * kx); x.H = (float)Math.Round(x.H * ky);
                            if (x.Vis && (x.X + x.W > 802 || x.Y + x.H > 482))
                                throw new Exception("Scale out of bounds: " + x.Id + " (" + x.X + "," + x.Y + "," + x.W + "," + x.H + ")");
                        }
                    }
                    finally { try { File.Delete(tmp); } catch { } }
                }

                Console.WriteLine("SELFTEST_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST_FAIL: " + ex);
                return 1;
            }
            finally
            {
                L.Lang = prevLang;
            }
        }

        // Uygulamanın sistemi/oyunu yormaması: düşük öncelik + Windows verimlilik modu (EcoQoS).
        private static void LowerOwnPriority()
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            try
            {
                IntPtr st = Marshal.AllocHGlobal(12);
                Marshal.WriteInt32(st, 0, 1);   // Version
                Marshal.WriteInt32(st, 4, 1);   // ControlMask: PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                Marshal.WriteInt32(st, 8, 1);   // StateMask: throttling açık
                Win32Native.SetProcessInformation(Process.GetCurrentProcess().Handle, 4, st, 12);
                Marshal.FreeHGlobal(st);
            }
            catch { }
        }

        public static void ShowBalloon(string text)
        {
            try { if (_trayIcon != null) _trayIcon.ShowBalloonTip(4000, "OnMon", text, ToolTipIcon.Info); } catch { }
        }

        // ---------- Uyarılar ----------
        private static readonly Dictionary<string, DateTime> _alertLast = new Dictionary<string, DateTime>();
        private static string AlertFlagPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProSystemMonitor", "alerts_off"); }
        }
        public static bool AlertsEnabled = true;

        public static void Alert(string key, string text)
        {
            if (!AlertsEnabled) return;
            try
            {
                lock (_alertLast)
                {
                    DateTime last;
                    if (_alertLast.TryGetValue(key, out last) && (DateTime.UtcNow - last).TotalMinutes < 5) return;
                    _alertLast[key] = DateTime.UtcNow;
                }
                if (_trayIcon != null) _trayIcon.ShowBalloonTip(6000, "OnMon - Uyarı", text, ToolTipIcon.Warning);
            }
            catch { }
        }

        public static void SetAlertsEnabled(bool on)
        {
            AlertsEnabled = on;
            try
            {
                string f = AlertFlagPath;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                if (on) { if (File.Exists(f)) File.Delete(f); } else File.WriteAllText(f, "off");
            }
            catch { }
        }

        // ---------- Windows ile başlat (Görev Zamanlayıcı, yönetici izniyle, UAC sormadan) ----------
        private const string AutostartTask = "ProSystemMonitor_Autostart";

        private static string LastSchtasksText = "";
        private static int RunSchtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    LastSchtasksText = (so + " " + se).Trim();
                    if (!p.WaitForExit(15000)) return -1;
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        /// <summary>Otomatik başlatma görevi başka/silinmiş bir exe yolunu gösteriyorsa (sürüm dosyası değişince) şu anki exe'ye günceller.</summary>
        public static void RepairAutostart()
        {
            try
            {
                SetRunKey(true);
                var qi = new ProcessStartInfo("schtasks.exe", "/Query /TN " + AutostartTask + " /V /FO LIST") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.Default };
                string o; int rc;
                using (Process q = Process.Start(qi)) { o = q.StandardOutput.ReadToEnd(); if (!q.WaitForExit(15000)) { try { q.Kill(); } catch { } return; } rc = q.ExitCode; }
                if (rc != 0) return;   // görev yok (kullanıcı kapatmış)
                string exe = Application.ExecutablePath;
                bool ok = o.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ok)
                {
                    Log("otomatik-baslat", new Exception("görev yolu eski, güncelleniyor -> " + exe));
                    bool done = SetAutostart(true);
                    Log("otomatik-baslat", new Exception("güncelleme " + (done ? "başarılı" : "BAŞARISIZ")));
                }
            }
            catch (Exception ex) { Log("otomatik-baslat", ex); }
        }

        public static bool IsAutostartEnabled()
        {
            return RunSchtasks("/Query /TN " + AutostartTask) == 0;
        }

        private static void SetRunKey(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("OnMon", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("OnMon", false);
                }
            }
            catch { }
        }

        public static bool SetAutostart(bool on)
        {
            SetRunKey(on);   // yedek yöntem: Görev Zamanlayıcı çalışmazsa Windows'un Run anahtarı başlatır
            if (!on) return RunSchtasks("/Delete /TN " + AutostartTask + " /F") == 0;

            string exe = System.Security.SecurityElement.Escape(Application.ExecutablePath);
            string dir = System.Security.SecurityElement.Escape(Path.GetDirectoryName(Application.ExecutablePath));
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT15S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"A\"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" +
                "<AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><Enabled>true</Enabled></Settings>" +
                "<Actions Context=\"A\"><Exec><Command>" + exe + "</Command><WorkingDirectory>" + dir + "</WorkingDirectory></Exec></Actions>" +
                "</Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "psm_task.xml");
            try
            {
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                int rc = RunSchtasks("/Create /TN " + AutostartTask + " /XML \"" + tmp + "\" /F");
                if (rc == 0) return true;
                Log("otomatik-baslat", new Exception("schtasks /Create kod=" + rc + " : " + LastSchtasksText));
                // yedek: mevcut görevin sadece çalıştırılacak yolunu değiştir
                rc = RunSchtasks("/Change /TN " + AutostartTask + " /TR \"\\\"" + Application.ExecutablePath + "\\\"\"");
                if (rc != 0) Log("otomatik-baslat", new Exception("schtasks /Change kod=" + rc + " : " + LastSchtasksText));
                return rc == 0;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static void TrimProcessMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                Win32Native.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static void RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--silent",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                if (_trayIcon != null) _trayIcon.Visible = false;
                Application.Exit();
            }
            catch { }
        }

        private static void TelemetryLoop()
        {
            int tickCount = 0;
            while (_running)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Snap snap = _collector.CollectSnap();
                    LastSnap = snap;
                    if (!_sseClients.IsEmpty)
                    {
                        LatestJson = _collector.BuildJson(snap);
                        BroadcastSse(LatestJson);
                    }
                    PushDash();
                    tickCount++;
                    if (tickCount % 300 == 0) TrimProcessMemory();
                }
                catch { }
                sw.Stop();
                CollectMs = CollectMs * 0.8 + sw.Elapsed.TotalMilliseconds * 0.2;
                int targetMs = Math.Max(300, Math.Min(5000, PollIntervalMs));
                if (!DashVisible && _sseClients.IsEmpty) targetMs = 5000;   // pano görünmüyorsa yalnızca uyarılar için seyrek oku
                int sleepMs = Math.Max(150, targetMs - (int)sw.ElapsedMilliseconds);
                Thread.Sleep(sleepMs);
            }
        }

        private static void BroadcastSse(string json)
        {
            if (_sseClients.IsEmpty) return;
            string payload = "data: " + json + "\n\n";
            foreach (var kvp in _sseClients)
            {
                try
                {
                    kvp.Value.Write(payload);
                    kvp.Value.Flush();
                }
                catch
                {
                    StreamWriter removed;
                    _sseClients.TryRemove(kvp.Key, out removed);
                    try { if (removed != null) removed.Dispose(); } catch { }
                }
            }
        }

        private static void HttpServerLoop()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                _listener.Prefixes.Add("http://localhost:" + Port + "/");
                _listener.Start();
            }
            catch
            {
                try
                {
                    if (_trayIcon != null)
                        _trayIcon.ShowBalloonTip(5000, "OnMon",
                            "Port " + Port + " açılamadı. Başka bir kopya çalışıyor olabilir.", ToolTipIcon.Warning);
                }
                catch { }
                return;
            }

            // Tek bir bozuk istek tüm sunucuyu kapatmasın: her istek kendi try/catch'inde.
            while (_running && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch (HttpListenerException) { if (!_running) break; }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                // Yalnızca yerel istekler: DNS-rebinding ve başka sitelerden gelen çapraz istekleri engelle
                string hostOnly = (ctx.Request.UserHostName ?? "").Split(new[] { ':' })[0].ToLowerInvariant();
                string origin = ctx.Request.Headers["Origin"];
                bool hostOk = hostOnly == "127.0.0.1" || hostOnly == "localhost";
                bool originOk = string.IsNullOrEmpty(origin) ||
                                origin.StartsWith("http://127.0.0.1:" + Port, StringComparison.OrdinalIgnoreCase) ||
                                origin.StartsWith("http://localhost:" + Port, StringComparison.OrdinalIgnoreCase);
                if (!hostOk || !originOk)
                {
                    ctx.Response.StatusCode = 403;
                    ctx.Response.OutputStream.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
                if (path == "/api/stream")
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/event-stream; charset=utf-8";
                    ctx.Response.Headers.Add("Cache-Control", "no-cache");
                    ctx.Response.SendChunked = true;

                    if (LastSnap != null && _sseClients.IsEmpty && _collector != null)
                        LatestJson = _collector.BuildJson(LastSnap);

                    var writer = new StreamWriter(ctx.Response.OutputStream, new UTF8Encoding(false));
                    writer.Write("data: " + LatestJson + "\n\n");
                    writer.Flush();

                    Guid clientId = Guid.NewGuid();
                    _sseClients[clientId] = writer;
                    return;
                }

                if (path == "/api/config")
                {
                    string intervalStr = ctx.Request.QueryString["interval"];
                    int parsed;
                    if (!string.IsNullOrEmpty(intervalStr) && int.TryParse(intervalStr, out parsed))
                    {
                        PollIntervalMs = Math.Max(300, Math.Min(5000, parsed));
                    }
                    byte[] ok = Encoding.UTF8.GetBytes("{\"ok\":true,\"interval\":" + PollIntervalMs + "}");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    ctx.Response.ContentLength64 = ok.Length;
                    ctx.Response.OutputStream.Write(ok, 0, ok.Length);
                    ctx.Response.OutputStream.Close();
                    return;
                }

                if (path == "/api/telemetry")
                {
                    string json = (LastSnap != null && _collector != null) ? _collector.BuildJson(LastSnap) : LatestJson;
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    ctx.Response.ContentLength64 = data.Length;
                    ctx.Response.OutputStream.Write(data, 0, data.Length);
                    ctx.Response.OutputStream.Close();
                    return;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string htmlPath = Path.Combine(baseDir, "wwwroot", "index.html");
                if (File.Exists(htmlPath))
                {
                    byte[] htmlBytes = File.ReadAllBytes(htmlPath);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
                    ctx.Response.ContentLength64 = htmlBytes.Length;
                    ctx.Response.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        public static void Log(string where, Exception ex)
        {
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hata.log");
                File.AppendAllText(f, DateTime.Now.ToString("s") + " [" + where + "] " + ex + "\r\n");
            }
            catch { }
        }

        // ---------- Yerel pano ----------
        public static volatile DashboardForm Dash;
        public static volatile Snap LastSnap;
        private static Control _ui;
        private static EventWaitHandle _showEvt;

        private static void PushDash()
        {
            DashboardForm d = Dash; Snap s = LastSnap;
            if (d == null || s == null || _ui == null) return;
            try { _ui.BeginInvoke((MethodInvoker)delegate { DashboardForm x = Dash; if (x != null && !x.IsDisposed) x.Push(s); }); } catch { }
        }

        public static void LaunchDashboardOnSecondMonitor()
        {
            if (_ui == null) return;
            try
            {
                _ui.BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (Dash != null && !Dash.IsDisposed) { Dash.PlaceOnSecondMonitor(); Dash.Show(); if (Dash.WindowState == FormWindowState.Minimized) Dash.WindowState = FormWindowState.Normal; return; }
                        var f = new DashboardForm();
                        Dash = f;
                        f.PlaceOnSecondMonitor();
                        f.Show();
                        Log("pano", new Exception("pano açıldı " + f.Bounds));
                    }
                    catch (Exception ex) { Log("pano", ex); Dash = null; }
                });
            }
            catch (Exception ex) { Log("pano-invoke", ex); }
        }

        private static void CheckPendingImport()
        {
            if (_ui == null) return;
            try
            {
                string pf = Path.Combine(AppData.Dir, "pending_import.txt");
                if (!File.Exists(pf)) return;
                string file = File.ReadAllText(pf, Encoding.UTF8).Trim();
                try { File.Delete(pf); } catch { }
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
                _ui.BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (Dash != null && !Dash.IsDisposed) Dash.ImportAndApplyExternalTemplate(file);
                    }
                    catch (Exception ex) { Log("import", ex); }
                });
            }
            catch { }
        }

        private static void SignalFirstInstance()
        {
            try { EventWaitHandle.OpenExisting("Global\\ProSystemMonitor_ShowDash").Set(); } catch { }
        }

        private static void TrayUpdateCheck()
        {
            if (!Updater.Check()) { _ui.BeginInvoke((MethodInvoker)delegate { MessageBox.Show(Updater.Status, "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Warning); }); return; }
            if (!Updater.UpdateAvailable) { _ui.BeginInvoke((MethodInvoker)delegate { MessageBox.Show(Updater.Status + "\r\n\r\n" + L.T("Son değişiklikler:\r\n", "Recent changes:\r\n") + Updater.Changelog, "OnMon", MessageBoxButtons.OK, MessageBoxIcon.Information); }); return; }
            _ui.BeginInvoke((MethodInvoker)delegate
            {
                string msg = L.T(
                    "Yeni sürüm: v" + Updater.LatestVersion + " (şu an v" + Updater.AppVersion + ")\r\n\r\n" + Updater.LatestNotes + "\r\n\r\nŞimdi güncellenip yeniden başlatılsın mı?",
                    "New version: v" + Updater.LatestVersion + " (current v" + Updater.AppVersion + ")\r\n\r\n" + Updater.LatestNotes + "\r\n\r\nUpdate and restart now?");
                if (MessageBox.Show(msg, "OnMon", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    ThreadPool.QueueUserWorkItem(_ => { string err = Updater.ApplyUpdate(); if (err != null) ShowBalloon(L.T("Güncelleme başarısız: ", "Update failed: ") + err); });
            });
        }

        public static void RequestSensorDump() { if (_collector != null) _collector.RequestSensorDump(); }
        public static string SensorReport() { return _collector != null ? _collector.SensorReportText() : L.T("(toplayıcı hazır değil)", "(collector not ready)"); }

        /// <summary>Kullanıcı çıkış isterse önce onay sorar.</summary>
        public static void ExitAppAsk()
        {
            var r = MessageBox.Show(
                L.T("OnMon'dan çıkılsın mı?\n\nPano ve tepsi simgesi kapanır.", "Exit OnMon?\n\nDashboard and tray icon will close."),
                L.T("Çıkış", "Exit"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly);
            if (r == DialogResult.Yes) ExitApp();
        }

        public static void ExitApp()
        {
            try { _running = false; if (_trayIcon != null) { _trayIcon.Visible = false; } } catch { }
            Application.Exit();
        }

        public static void LaunchBrowserDashboard()
        {
            try { Process.Start(new ProcessStartInfo("http://127.0.0.1:" + Port) { UseShellExecute = true }); } catch { }
        }

        public static void LaunchEdgeDashboard()
        {
            try
            {
                Screen[] screens = Screen.AllScreens;
                Screen targetScreen = screens.FirstOrDefault(s => !s.Primary) ?? screens.First();
                Rectangle bounds = targetScreen.Bounds;
                string url = "http://127.0.0.1:" + Port;

                string[] browserPaths = new[]
                {
                    @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                    @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
                };

                string browserExe = browserPaths.FirstOrDefault(File.Exists);
                if (browserExe != null)
                {
                    string profileDir = Path.Combine(Path.GetTempPath(), "ProSystemMonitor_AppProfile");
                    string args = string.Format(
                        "--app=\"{0}\" --user-data-dir=\"{1}\" --window-position={2},{3} --window-size={4},{5} --start-fullscreen --force-device-scale-factor=1 --high-dpi-support=1 --disable-features=Translate,BackForwardCache --disable-extensions --disable-sync --disable-component-update --disable-background-networking --disable-default-apps --disable-breakpad --no-pings --no-first-run --no-default-browser-check",
                        url, profileDir, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = browserExe,
                        Arguments = args,
                        UseShellExecute = false
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private static void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "OnMon",
                Icon = CreateRogTrayIcon(),
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => LaunchDashboardOnSecondMonitor();
            RebuildTrayMenu();
        }

        public static void RebuildTrayMenu()
        {
            if (_trayIcon == null) return;
            Action build = delegate
            {
                if (_trayIcon == null) return;
                ContextMenuStrip oldMenu = _trayIcon.ContextMenuStrip;
                var menu = new ContextMenuStrip();

                var open2ndItem = new ToolStripMenuItem(L.T("Panoyu Aç (yerel, 2. monitör)", "Open Dashboard (local, 2nd monitor)"));
                open2ndItem.Font = new Font(open2ndItem.Font, FontStyle.Bold);
                open2ndItem.Click += (s, e) => LaunchDashboardOnSecondMonitor();

                var studioItem = new ToolStripMenuItem(L.T("🎨 Düzen Stüdyosu…", "🎨 Layout Studio…"));
                studioItem.Click += (s, e) => { if (Dash != null && !Dash.IsDisposed) _ui.BeginInvoke((MethodInvoker)delegate { Dash.OpenStudio(); }); };

                var galleryItem = new ToolStripMenuItem(L.T("📁 Düzen Galerisi / Düzen Ekle…", "📁 Layout Gallery / Import…"));
                galleryItem.Click += (s, e) => { if (Dash != null && !Dash.IsDisposed) _ui.BeginInvoke((MethodInvoker)delegate { Dash.OpenGallery(); }); };

                var openBrowserItem = new ToolStripMenuItem(L.T("Tarayıcıda Aç (127.0.0.1:" + Port + ")", "Open in Browser (127.0.0.1:" + Port + ")"));
                openBrowserItem.Click += (s, e) => LaunchBrowserDashboard();

                var dumpItem = new ToolStripMenuItem(L.T("Sensör Listesini Kaydet (sensorler.txt)", "Save Sensor Dump (sensorler.txt)"));
                dumpItem.Click += (s, e) => { if (_collector != null) _collector.RequestSensorDump(); };

                try { AlertsEnabled = !File.Exists(AlertFlagPath); } catch { }
                var alertItem = new ToolStripMenuItem(L.T("Isı Uyarıları (CPU 90° / GPU 85° / Disk 70°)", "Thermal Alerts (CPU 90° / GPU 85° / Disk 70°)")) { Checked = AlertsEnabled };
                alertItem.Click += (s, e) => { SetAlertsEnabled(!AlertsEnabled); alertItem.Checked = AlertsEnabled; };

                var autoItem = new ToolStripMenuItem(L.T("Windows ile Başlat", "Start with Windows"));
                autoItem.Click += (s, e) =>
                {
                    bool want = !autoItem.Checked;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        bool ok = SetAutostart(want);
                        ShowBalloon(ok
                            ? (want ? L.T("Windows ile başlatma açıldı.", "Autostart enabled.") : L.T("Windows ile başlatma kapatıldı.", "Autostart disabled."))
                            : L.T("İşlem başarısız (yönetici izni gerekli).", "Failed (administrator privileges required)."));
                    });
                    autoItem.Checked = want;
                };
                menu.Opening += (s, e) => { try { autoItem.Checked = IsAutostartEnabled(); } catch { } };

                var langItem = new ToolStripMenuItem("🌐 Dil / Language");
                var trIt = new ToolStripMenuItem("Türkçe") { Checked = !L.IsEn };
                var enIt = new ToolStripMenuItem("English") { Checked = L.IsEn };
                trIt.Click += (s, e) => L.Set("tr", Dash);
                enIt.Click += (s, e) => L.Set("en", Dash);
                langItem.DropDownItems.Add(trIt);
                langItem.DropDownItems.Add(enIt);

                menu.Items.Add(open2ndItem);
                menu.Items.Add(studioItem);
                menu.Items.Add(galleryItem);
                menu.Items.Add(L.T("Edge ile Aç (daha çok RAM)", "Open in Edge App (higher RAM)"), null, (s, e) => LaunchEdgeDashboard());
                menu.Items.Add(openBrowserItem);

                var sndItem = new ToolStripMenuItem(L.T("Ses Cihazı", "Audio Output"));
                sndItem.DropDownOpening += (s, e) =>
                {
                    sndItem.DropDownItems.Clear();
                    AudioSwitch.Refresh();
                    foreach (AudioDevice d in AudioSwitch.Devices)
                    {
                        AudioDevice dd = d;
                        var it = new ToolStripMenuItem(d.Name) { Checked = d.IsDefault };
                        it.Click += (s2, e2) => ThreadPool.QueueUserWorkItem(x => AudioSwitch.SetDefault(dd.Id));
                        sndItem.DropDownItems.Add(it);
                    }
                    if (sndItem.DropDownItems.Count == 0) sndItem.DropDownItems.Add(new ToolStripMenuItem(L.T("Cihaz bulunamadı", "No device found")) { Enabled = false });
                };
                sndItem.DropDownItems.Add("...");
                menu.Items.Add(sndItem);
                menu.Items.Add(langItem);
                menu.Items.Add(new ToolStripSeparator());
                var updItem = new ToolStripMenuItem(L.T("Güncellemeleri Denetle…", "Check for Updates…"));
                updItem.Click += (s, e) => ThreadPool.QueueUserWorkItem(_ => TrayUpdateCheck());
                menu.Items.Add(updItem);
                menu.Items.Add(autoItem);
                menu.Items.Add(alertItem);
                menu.Items.Add(dumpItem);

                if (!IsAdministrator())
                {
                    var adminItem = new ToolStripMenuItem(L.T("Yönetici Olarak Yeniden Başlat (tam sensör erişimi)", "Restart as Administrator (full sensor access)"));
                    adminItem.Click += (s, e) => RestartAsAdmin();
                    menu.Items.Add(adminItem);
                }

                var exitItem = new ToolStripMenuItem(L.T("Çıkış", "Exit"));
                exitItem.Click += (s, e) => ExitAppAsk();

                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(exitItem);

                _trayIcon.ContextMenuStrip = menu;
                if (oldMenu != null) { try { oldMenu.Dispose(); } catch { } }
            };
            if (_ui != null && _ui.IsHandleCreated && _ui.InvokeRequired) _ui.BeginInvoke(build);
            else build();
        }

        public static Icon TrayIcon() { return CreateRogTrayIcon(); }

        private static Icon CreateRogTrayIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush bg = new SolidBrush(Color.FromArgb(240, 15, 16, 22)))
                using (Pen border = new Pen(Color.FromArgb(255, 255, 69, 0), 2.5f))
                {
                    g.FillEllipse(bg, 2, 2, 28, 28);
                    g.DrawEllipse(border, 2, 2, 28, 28);
                }
                using (Font f = new Font("Segoe UI", 11, FontStyle.Bold))
                using (Brush txt = new SolidBrush(Color.FromArgb(255, 255, 130, 0)))
                {
                    g.DrawString("O", f, txt, 7, 5);
                }
                IntPtr hIcon = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
                finally { Win32Native.DestroyIcon(hIcon); }
            }
        }
    }

    public class DriveTelemetry
    {
        public string Name;
        public double TempC;
        public string Mount;
        public double UsedPct;
        public double FreeGb;
        public double TotalGb;
        public string Letter;
    }

    public class CoreTelemetry
    {
        public int Index;
        public double ClockMhz;
        public double LoadPct;
    }

    public class ProcItem
    {
        public string Name;
        public double MemMb;
        public double Cpu;
    }

    public class TelemetryCollector : IDisposable
    {
        private string _brandName = "PC";
        private string _moboModel = "";
        private string _moboChipset = "";
        private string _moboBios = "";
        private string _cpuModel = "";
        private int _physicalCores = 8;
        private double _cpuBaseMhz = 4000.0;
        private string _gpuModel = "";
        private string _memSpeedText = "DDR";
        private bool _isLaptop = false;
        private string _ipAddress = "127.0.0.1";
        private List<string> _detectedDriveNames = new List<string>();

        private bool _nvmlAvailable = false;
        private IntPtr _nvmlDevice = IntPtr.Zero;
        private uint _gpuMaxLinkGen = 4;
        private uint _gpuMaxLinkWidth = 16;

        private IntPtr _pdhQuery = IntPtr.Zero;
        private readonly List<IntPtr> _corePerfCounters = new List<IntPtr>();
        private readonly List<List<IntPtr>> _coreLoadCounters = new List<List<IntPtr>>();
        private readonly FpsMonitor _fps = new FpsMonitor();
        private double _cachedDimmTemp = double.NaN;
        private LhmReading _lhmCache;
        private DateTime _lhmCacheTime = DateTime.MinValue;
        private DateTime _lastAudioPoll = DateTime.MinValue;
        private int _cachedVolume = -1;
        private volatile bool _dumpRequested;
        private DateTime _lastMemPoll = DateTime.MinValue;
        private IntPtr _totalCpuUtilCounter = IntPtr.Zero;
        private IntPtr _diskReadCounter = IntPtr.Zero, _diskWriteCounter = IntPtr.Zero;

        private long _prevBytesSent = -1;
        private long _prevBytesRecv = -1;
        private DateTime _prevNetTime = DateTime.UtcNow;

        private Computer _lhmComputer;
        private bool _lhmInitialized = false;
        private DateTime _lhmLastTry = DateTime.MinValue, _lhmFirstOk = DateTime.MinValue;
        private bool _autoDumped;
        public static volatile string LhmError = "";
        /// <summary>Kullanıcının fan eşlemesi: [CPU, Kasa1, Kasa2, Pompa] → SuperIO fan numarası (0 = otomatik).</summary>
        public static volatile int[] FanMap = new int[4];
        /// <summary>Anakart SuperIO fanlarının anlık RPM değerleri (dizin = fan numarası, NaN = yok).</summary>
        public static volatile double[] FanRaw = new double[8];
        private DateTime _lastStoragePoll = DateTime.MinValue;
        private DateTime _lastSlowPoll = DateTime.MinValue;
        private DateTime _lastLhmGpuPoll = DateTime.MinValue;
        private double _cachedLhmGpuHot = double.NaN, _cachedLhmGpuFan = double.NaN, _cachedLhmGpuVolt = double.NaN;
        private readonly Dictionary<string, double> _cachedDriveTemps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private bool _boardNow = true;
        private DateTime _lastBoardPoll = DateTime.MinValue;
        private List<ProcItem> _cachedTopProcs = new List<ProcItem>();
        private List<ProcItem> _cachedTopCpu = new List<ProcItem>();
        private double _selfMb, _selfCpu;
        private double _selfPrevCpu = -1;
        private DateTime _selfPrevAt = DateTime.MinValue;
        private readonly Dictionary<int, double> _procPrev = new Dictionary<int, double>();
        private DateTime _procPrevAt = DateTime.MinValue;
        private List<DriveTelemetry> _cachedPartitions = new List<DriveTelemetry>();
        private volatile int _pingMs = -1;
        private readonly Dictionary<string, List<string>> _driveLetters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Session Min/Max Trackers
        private double _cpuTempMin = 999, _cpuTempMax = 0;
        private double _gpuTempMin = 999, _gpuTempMax = 0;
        private double _cpuPeakMhz = 0, _gpuPeakMhz = 0;
        private double _cpuPeakW = 0, _gpuPeakW = 0;

        public void Initialize()
        {
            DetectStaticHardware();
            InitNvml();
            InitPdhCounters();
            FetchPublicOrLocalIpAsync();
            StartBackgroundPingLoop();
            try { _fps.Start(); } catch { }
        }

        private void StartBackgroundPingLoop()
        {
            Task.Run(async () =>
            {
                using (var pinger = new Ping())
                {
                    while (true)
                    {
                        try
                        {
                            var reply = await pinger.SendPingAsync("1.1.1.1", 1500);
                            _pingMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
                        }
                        catch { _pingMs = -1; }
                        await Task.Delay(4000);
                    }
                }
            });
        }

        private static bool IsJunkOem(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            string l = s.Trim().ToLowerInvariant();
            return l.Length < 2 || l.Contains("to be filled") || l.Contains("system manufacturer") ||
                   l.Contains("system product") || l.Contains("default string") || l == "oem" ||
                   l == "undefined" || l == "standard" || l == "not available" || l == "none";
        }

        internal static string ResolveBrandName(string sysManuf, string sysModel, string boardManuf, string boardProd)
        {
            string combined = ((boardManuf ?? "") + " " + (sysManuf ?? "") + " " + (sysModel ?? "") + " " + (boardProd ?? "")).Trim();
            if (Has(combined, "ALIENWARE")) return "ALIENWARE";
            if (Has(combined, "ASUS") || Has(combined, "ASUSTeK")) return "ASUS";
            if (Has(combined, "MSI") || Has(combined, "Micro-Star")) return "MSI";
            if (Has(combined, "GIGABYTE")) return "GIGABYTE";
            if (Has(combined, "ASRock")) return "ASROCK";
            if (Has(combined, "LENOVO") || Has(combined, "Legion") || Has(combined, "ThinkPad")) return "LENOVO";
            if (Has(combined, "OMEN") || Has(combined, "Victus") || Has(combined, "Hewlett-Packard") || Regex.IsMatch(combined, @"\bHP\b", RegexOptions.IgnoreCase)) return "HP";
            if (Has(combined, "DELL")) return "DELL";
            if (Has(combined, "ACER") || Has(combined, "Predator") || Has(combined, "Nitro")) return "ACER";
            if (Has(combined, "RAZER")) return "RAZER";
            if (Has(combined, "MONSTER") || Has(combined, "Tulpar") || Has(combined, "Abra")) return "MONSTER";
            if (Has(combined, "CASPER") || Has(combined, "Excalibur")) return "CASPER";
            if (Has(combined, "NZXT")) return "NZXT";
            if (Has(combined, "EVGA")) return "EVGA";
            if (Has(combined, "BIOSTAR")) return "BIOSTAR";
            if (Has(combined, "COLORFUL")) return "COLORFUL";
            if (Has(combined, "MAXSUN")) return "MAXSUN";
            if (Has(combined, "SAPPHIRE")) return "SAPPHIRE";
            if (Has(combined, "SAMSUNG")) return "SAMSUNG";
            if (Has(combined, "HUAWEI")) return "HUAWEI";
            if (Has(combined, "Surface") || Has(combined, "Microsoft")) return "SURFACE";
            if (Has(combined, "Apple")) return "APPLE";
            if (Has(combined, "Framework")) return "FRAMEWORK";
            if (Has(combined, "Valve") || Has(combined, "Steam Deck")) return "VALVE";
            if (Has(combined, "SONY") || Has(combined, "VAIO")) return "SONY";
            if (Has(combined, "Dynabook") || Has(combined, "TOSHIBA")) return "DYNABOOK";
            if (Has(combined, "FUJITSU")) return "FUJITSU";
            if (Regex.IsMatch(combined, @"\bLG\b", RegexOptions.IgnoreCase)) return "LG";

            string src = !IsJunkOem(sysManuf) ? sysManuf : (!IsJunkOem(boardManuf) ? boardManuf : "");
            if (!string.IsNullOrEmpty(src))
                return src.Trim().Split(new[] { ' ', ',', '.' }, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
            return "PC";
        }

        internal static int ScoreGpuName(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            string l = name.ToLowerInvariant();
            if (l.Contains("parsec") || l.Contains("virtual") || l.Contains("microsoft basic") ||
                l.Contains("remote desktop") || l.Contains("rdp") || l.Contains("citrix") ||
                l.Contains("anydesk") || l.Contains("luminon") || l.Contains("indirect display") || l.Contains("mirror"))
                return -1;
            if (l.Contains("nvidia") || l.Contains("geforce") || l.Contains("rtx") || l.Contains("gtx") || l.Contains("quadro") || l.Contains("tesla"))
                return 100;
            if (l.Contains("radeon rx") || l.Contains("radeon pro") || l.Contains("radeon vii") || l.Contains("vega 56") || l.Contains("vega 64") ||
                Regex.IsMatch(l, @"\b(rx\s*\d{3,4}|r9\s+\d{3}|r7\s+\d{3}|hd\s*7\d{3})"))
                return 100;
            if (Regex.IsMatch(l, @"\barc\b"))
                return 95;
            if (l.Contains("radeon(tm) graphics") || l.Equals("amd radeon graphics") || l.Contains("radeon graphics") ||
                Regex.IsMatch(l, @"\bradeon\s+\d{3}m\b") || l.Contains("radeon vega") ||
                l.Contains("uhd graphics") || l.Contains("iris") || l.Contains("hd graphics") || l.Equals("intel(r) graphics"))
                return 15;
            return 50;
        }

        private void DetectStaticHardware()
        {
            string sysManuf = "", sysModel = "", boardManuf = "", boardProd = "";
            try
            {
                using (var cs = new ManagementObjectSearcher("SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject obj in cs.Get())
                    {
                        sysManuf = (obj["Manufacturer"] ?? "").ToString().Trim();
                        sysModel = (obj["Model"] ?? "").ToString().Trim();
                        if (obj["PCSystemType"] != null)
                        {
                            int pt = Convert.ToInt32(obj["PCSystemType"]);
                            if (pt == 2 || pt == 8) _isLaptop = true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        boardManuf = (obj["Manufacturer"] ?? "").ToString().Trim();
                        boardProd = (obj["Product"] ?? "").ToString().Trim();
                    }
                }
            }
            catch { }

            _brandName = ResolveBrandName(sysManuf, sysModel, boardManuf, boardProd);
            _moboModel = FormatMoboName(_brandName, boardProd, sysModel);

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        _moboBios = (obj["SMBIOSBIOSVersion"] ?? "").ToString().Trim();
                    }
                }
            }
            catch { }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string rawCpu = (obj["Name"] ?? "").ToString().Trim();
                        rawCpu = Regex.Replace(rawCpu, @"\s+\d+-Core Processor.*$", "", RegexOptions.IgnoreCase).Trim();
                        rawCpu = Regex.Replace(rawCpu, @"\s+w/\s+Radeon.*$", "", RegexOptions.IgnoreCase).Trim();
                        _cpuModel = rawCpu;

                        if (obj["NumberOfCores"] != null)
                            _physicalCores = Math.Max(1, Convert.ToInt32(obj["NumberOfCores"]));
                        if (obj["MaxClockSpeed"] != null)
                        {
                            double maxClk = Convert.ToDouble(obj["MaxClockSpeed"]);
                            if (maxClk > 500) _cpuBaseMhz = maxClk;
                        }

                        string searchBoard = _moboModel + " " + boardProd;
                        if (_cpuModel.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string chipMatch = Regex.Match(searchBoard, @"\b(WRX\d+|TRX\d+|[ABX]\d{3}E?)\b", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
                            if (!string.IsNullOrEmpty(chipMatch))
                                _moboChipset = "AMD " + chipMatch + " Chipset";
                            else if (_isLaptop || Regex.IsMatch(_cpuModel, @"\b\d{4}[UHSX]{1,2}\b|\bAI\s+\d+", RegexOptions.IgnoreCase))
                                _moboChipset = "AMD Ryzen SoC";
                            else
                                _moboChipset = "AMD Chipset";
                        }
                        else if (_cpuModel.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string chipMatch = Regex.Match(searchBoard, @"\b([ZBHQW]\d{3}[A-Z]?|X299|X99)\b", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
                            if (!string.IsNullOrEmpty(chipMatch))
                                _moboChipset = "Intel " + chipMatch + " Chipset";
                            else if (_cpuModel.IndexOf("Ultra", StringComparison.OrdinalIgnoreCase) >= 0)
                                _moboChipset = "Intel Core Ultra SoC";
                            else if (_isLaptop)
                                _moboChipset = "Intel Mobile SoC";
                            else if (Regex.IsMatch(_cpuModel, @"i[3579]-1[234]\d{3}", RegexOptions.IgnoreCase))
                                _moboChipset = "Intel 600/700 Chipset";
                            else
                                _moboChipset = "Intel Chipset";
                        }
                        else
                        {
                            _moboChipset = "System SoC";
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        uint clk = 0;
                        uint memType = 0;
                        if (obj["ConfiguredClockSpeed"] != null) clk = Convert.ToUInt32(obj["ConfiguredClockSpeed"]);
                        if (clk == 0 && obj["Speed"] != null) clk = Convert.ToUInt32(obj["Speed"]);
                        if (obj["SMBIOSMemoryType"] != null) memType = Convert.ToUInt32(obj["SMBIOSMemoryType"]);
                        if (clk > 0 || memType > 0)
                        {
                            string ddrType;
                            if (memType == 34 || memType == 35) ddrType = "DDR5";
                            else if (memType == 26) ddrType = "DDR4";
                            else if (memType == 24) ddrType = "DDR3";
                            else if (memType >= 20 && memType <= 22) ddrType = "DDR2";
                            else ddrType = clk >= 4400 ? "DDR5" : (clk >= 2133 ? "DDR4" : "DDR3");
                            _memSpeedText = clk > 0 ? string.Format("{0}-{1}", ddrType, clk) : ddrType;
                            break;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var searcher = new ManagementObjectSearcher(@"\\.\root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk"))
                {
                    var drives = new List<string>();
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = (obj["FriendlyName"] ?? "").ToString().Trim();
                        ushort busType = obj["BusType"] != null ? Convert.ToUInt16(obj["BusType"]) : (ushort)0;
                        ulong size = obj["Size"] != null ? Convert.ToUInt64(obj["Size"]) : 0UL;
                        if (busType == 7 || size < 16000000000UL) continue;
                        if (!string.IsNullOrEmpty(name)) drives.Add(name);
                    }
                    _detectedDriveNames = drives.Take(6).ToList();
                }
            }
            catch { }

            // Fiziksel disk -> sürücü harfleri eşlemesi (kapasite doğru diske yazılsın)
            try
            {
                using (var disks = new ManagementObjectSearcher("SELECT DeviceID, Model FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject disk in disks.Get())
                    {
                        string model = (disk["Model"] ?? "").ToString().Trim();
                        if (model.Length == 0) continue;
                        string devId = (disk["DeviceID"] ?? "").ToString().Replace("\\", "\\\\");
                        var letters = new List<string>();
                        using (var parts = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + devId + "'} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                        {
                            foreach (ManagementObject part in parts.Get())
                            {
                                string pid = (part["DeviceID"] ?? "").ToString();
                                using (var lds = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + pid + "'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                                {
                                    foreach (ManagementObject ld in lds.Get())
                                        letters.Add((ld["DeviceID"] ?? "").ToString());
                                }
                            }
                        }
                        string mNorm = NormName(model);
                        string match = _detectedDriveNames.FirstOrDefault(n =>
                        {
                            string nNorm = NormName(n);
                            return model.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   n.IndexOf(model, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   (mNorm.Length > 0 && nNorm.Length > 0 && (mNorm.Contains(nNorm) || nNorm.Contains(mNorm)));
                        });
                        if (match != null) _driveLetters[match] = letters;
                    }
                }
            }
            catch { }

            // Sistem sürücüsünü (C:) barındıran diski en başa al
            if (_detectedDriveNames.Count > 1)
            {
                _detectedDriveNames = _detectedDriveNames.OrderBy(d =>
                {
                    List<string> ltrs;
                    if (_driveLetters.TryGetValue(d, out ltrs) && ltrs.Any(l => l.Equals("C:", StringComparison.OrdinalIgnoreCase))) return 0;
                    return 1;
                }).ToList();
            }
        }

        private string FormatMoboName(string brand, string product, string sysModel)
        {
            bool crypticBoard = IsJunkOem(product) || (product.IndexOf(' ') < 0 && product.IndexOf('-') < 0 && product.Length <= 12);
            string p = (crypticBoard && !IsJunkOem(sysModel)) ? sysModel : (!IsJunkOem(product) ? product : (!IsJunkOem(sysModel) ? sysModel : brand));
            p = Regex.Replace(p, @"\bROG\b", "ROG", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bSTRIX\b", "Strix", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bGAMING\b", "Gaming", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bWIFI\b", "WiFi", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bTUF\b", "TUF", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bAORUS\b", "AORUS", RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(brand) && brand != "PC" && p.IndexOf(brand, StringComparison.OrdinalIgnoreCase) < 0)
            {
                string prettyBrand = brand.Equals("ASUS", StringComparison.OrdinalIgnoreCase) ? "ASUS" : brand;
                return prettyBrand + " " + p;
            }
            return p;
        }

        private void InitNvml()
        {
            try
            {
                if (NvmlNative.nvmlInit_v2() == 0)
                {
                    if (NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out _nvmlDevice) == 0)
                    {
                        _nvmlAvailable = true;
                        StringBuilder sb = new StringBuilder(128);
                        if (NvmlNative.nvmlDeviceGetName(_nvmlDevice, sb, (uint)sb.Capacity) == 0)
                        {
                            _gpuModel = sb.ToString().Trim();
                        }

                        NvmlNative.nvmlDeviceGetMaxPcieLinkGeneration(_nvmlDevice, out _gpuMaxLinkGen);
                        NvmlNative.nvmlDeviceGetMaxPcieLinkWidth(_nvmlDevice, out _gpuMaxLinkWidth);
                    }
                }
            }
            catch { _nvmlAvailable = false; }

            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, AdapterRAM FROM Win32_VideoController"))
                {
                    int bestScore = -1;
                    long bestRam = -1;
                    string bestModel = "";
                    foreach (ManagementObject o in s.Get())
                    {
                        string name = (o["Name"] ?? "").ToString().Trim();
                        string pnp = (o["PNPDeviceID"] ?? "").ToString().ToUpperInvariant();
                        long ram = 0;
                        try { if (o["AdapterRAM"] != null) ram = Convert.ToInt64(o["AdapterRAM"]); } catch { }

                        int sc = ScoreGpuName(name);
                        if (sc < 0) continue;
                        if (_nvmlAvailable && pnp.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (ram >= 3000000000L && sc < 90) sc = 90;

                        string candidate = name;
                        Match subMatch = Regex.Match(pnp, @"SUBSYS_([0-9A-F]{8})");
                        if (subMatch.Success && sc >= 50)
                        {
                            uint subsysId = Convert.ToUInt32(subMatch.Groups[1].Value, 16);
                            candidate = ResolveSubvendorGpuName(string.IsNullOrEmpty(_gpuModel) || !_nvmlAvailable ? name : _gpuModel, subsysId);
                        }

                        if (sc > bestScore || (sc == bestScore && ram > bestRam))
                        {
                            bestScore = sc;
                            bestRam = ram;
                            bestModel = candidate;
                        }
                    }
                    if (!string.IsNullOrEmpty(bestModel))
                        _gpuModel = bestModel;
                }
            }
            catch { }
        }

        private string ResolveSubvendorGpuName(string baseName, uint subsysId)
        {
            ushort vendorId = (ushort)(subsysId & 0xFFFF);
            string cleanBase = baseName.Replace("NVIDIA ", "").Replace("AMD ", "").Trim();
            string prefix = "";
            switch (vendorId)
            {
                case 0x1043: prefix = "ASUS"; break;
                case 0x1462: prefix = "MSI"; break;
                case 0x1458: prefix = "Gigabyte"; break;
                case 0x1849: prefix = "ASRock"; break;
                case 0x1DA2: prefix = "Sapphire"; break;
                case 0x148C: prefix = "PowerColor"; break;
                case 0x1682: prefix = "XFX"; break;
                case 0x10B0: prefix = "Gainward"; break;
                case 0x1569: prefix = "Palit"; break;
                case 0x19DA: prefix = "Zotac"; break;
                case 0x3842: prefix = "EVGA"; break;
                case 0x196E: prefix = "PNY"; break;
                case 0x1771: prefix = "Inno3D"; break;
                case 0x172F: prefix = "Sparkle"; break;
                case 0x17AA: prefix = "Lenovo"; break;
                case 0x103C: prefix = "HP"; break;
                case 0x1028: prefix = "Dell"; break;
                case 0x1025: prefix = "Acer"; break;
                default: return baseName;
            }
            if (cleanBase.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                return cleanBase;
            return prefix + " " + cleanBase;
        }

        private void InitPdhCounters()
        {
            try
            {
                if (PdhNative.PdhOpenQueryW(IntPtr.Zero, IntPtr.Zero, out _pdhQuery) == 0)
                {
                    if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _totalCpuUtilCounter) != 0)
                    {
                        if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _totalCpuUtilCounter) != 0)
                            _totalCpuUtilCounter = IntPtr.Zero;
                    }
                    if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out _diskReadCounter) != 0) _diskReadCounter = IntPtr.Zero;
                    if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _diskWriteCounter) != 0) _diskWriteCounter = IntPtr.Zero;
                    int logicalTotal = Environment.ProcessorCount;
                    int threadsPerCore = Math.Max(1, (int)Math.Ceiling((double)logicalTotal / Math.Max(1, _physicalCores)));
                    for (int i = 0; i < _physicalCores; i++)
                    {
                        int firstLogical = Math.Min(logicalTotal - 1, i * threadsPerCore);
                        IntPtr handlePerf;
                        string pathPerf = string.Format("\\Processor Information(0,{0})\\% Processor Performance", firstLogical);
                        if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, pathPerf, IntPtr.Zero, out handlePerf) != 0)
                            handlePerf = IntPtr.Zero;
                        _corePerfCounters.Add(handlePerf);

                        // Çekirdeğin tüm SMT iş parçacıklarının ortalaması
                        var loads = new List<IntPtr>();
                        for (int t = 0; t < threadsPerCore && (firstLogical + t) < logicalTotal; t++)
                        {
                            IntPtr handleLoad;
                            string pathLoad = string.Format("\\Processor({0})\\% Processor Time", firstLogical + t);
                            if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, pathLoad, IntPtr.Zero, out handleLoad) == 0)
                                loads.Add(handleLoad);
                        }
                        _coreLoadCounters.Add(loads);
                    }
                    PdhNative.PdhCollectQueryData(_pdhQuery);
                }
            }
            catch { }
        }

        private void FetchPublicOrLocalIpAsync()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var addr = ni.GetIPProperties().UnicastAddresses
                            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (addr != null)
                        {
                            _ipAddress = addr.Address.ToString();
                            break;
                        }
                    }
                }
            }
            catch { }

            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (var wc = new WebClient())
                    {
                        string extIp = wc.DownloadString("https://api.ipify.org").Trim();
                        if (Regex.IsMatch(extIp, @"^\d{1,3}(\.\d{1,3}){3}$"))
                        {
                            _ipAddress = extIp;
                        }
                    }
                }
                catch { }
            });
        }

        private void PollSlowMetricsIfNeeded()
        {
            if ((DateTime.UtcNow - _lastSlowPoll).TotalSeconds < 10.0) return;
            _lastSlowPoll = DateTime.UtcNow;

            // 1. Partitions capacity (instant via DriveInfo)
            try
            {
                var parts = new List<DriveTelemetry>();
                foreach (var di in DriveInfo.GetDrives())
                {
                    if (!di.IsReady || di.DriveType != DriveType.Fixed) continue;
                    double totalGb = di.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = di.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double usedPct = totalGb > 0 ? ((totalGb - freeGb) / totalGb) * 100.0 : 0;
                    string label = string.IsNullOrEmpty(di.VolumeLabel) ? di.Name.TrimEnd(new[] { '\\' }) : (di.Name.TrimEnd(new[] { '\\' }) + " " + di.VolumeLabel);
                    parts.Add(new DriveTelemetry
                    {
                        Letter = di.Name.TrimEnd(new[] { '\\' }),
                        Mount = label,
                        TotalGb = totalGb,
                        FreeGb = freeGb,
                        UsedPct = usedPct
                    });
                }
                _cachedPartitions = parts;
            }
            catch { }

            // 2. Kendi bellek/CPU yükümüz (sıfır maliyet, ek süreç taraması yapmadan)
            try
            {
                using (Process selfP = Process.GetCurrentProcess())
                {
                    DateTime nowS = DateTime.UtcNow;
                    _selfMb = selfP.WorkingSet64 / (1024.0 * 1024.0);
                    double sCpu = selfP.TotalProcessorTime.TotalSeconds;
                    if (_selfPrevCpu >= 0 && _selfPrevAt != DateTime.MinValue)
                    {
                        double sdt = (nowS - _selfPrevAt).TotalSeconds;
                        if (sdt > 0.2 && sCpu >= _selfPrevCpu)
                            _selfCpu = (sCpu - _selfPrevCpu) / (sdt * Environment.ProcessorCount) * 100.0;
                    }
                    _selfPrevCpu = sCpu;
                    _selfPrevAt = nowS;
                }
            }
            catch { }

            // 3. En çok kaynak kullanan işlemler (yalnızca ilgili widget veya web istemcisi aktifken)
            DashboardForm dsh = Program.Dash;
            bool needProcs = (Program.DashVisible && dsh != null && !dsh.IsDisposed && (dsh.WVis("apps") || dsh.WVis("procs"))) || Program.HasSseClients;
            if (!needProcs) { _procPrev.Clear(); _procPrevAt = DateTime.MinValue; return; }
            try
            {
                var procs = Process.GetProcesses();
                DateTime nowT = DateTime.UtcNow;
                double dt = _procPrevAt == DateTime.MinValue ? 0 : (nowT - _procPrevAt).TotalSeconds;
                var cur = new Dictionary<int, double>();
                bool wantCpu = dsh != null && !dsh.IsDisposed && dsh.WVis("apps");
                if (!wantCpu) { _procPrev.Clear(); _procPrevAt = DateTime.MinValue; }
                int selfId = Process.GetCurrentProcess().Id;
                var all = new List<ProcItem>();
                foreach (Process p in procs)
                {
                    try
                    {
                        if (p.Id <= 4) continue;
                        string n = p.ProcessName;
                        double mb = p.WorkingSet64 / (1024.0 * 1024.0), cpuSec = 0, pct = 0;
                        if (wantCpu) { try { cpuSec = p.TotalProcessorTime.TotalSeconds; } catch { cpuSec = -1; } } else cpuSec = -1;
                        double prev;
                        if (cpuSec >= 0)
                        {
                            cur[p.Id] = cpuSec;
                            if (dt > 0.5 && _procPrev.TryGetValue(p.Id, out prev) && cpuSec >= prev) pct = (cpuSec - prev) / (dt * Environment.ProcessorCount) * 100.0;
                        }
                        if (p.Id == selfId) { _selfMb = mb; _selfCpu = pct; }
                        if (!string.IsNullOrEmpty(n) && n != "Memory Compression") all.Add(new ProcItem { Name = n, MemMb = mb, Cpu = pct });
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                _procPrev.Clear(); foreach (var kv in cur) _procPrev[kv.Key] = kv.Value; _procPrevAt = nowT;
                var grouped = all.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ProcItem { Name = g.Key, MemMb = g.Sum(i => i.MemMb), Cpu = g.Sum(i => i.Cpu) }).ToList();
                var top = grouped.OrderByDescending(x => x.MemMb).Take(5).ToList();
                _cachedTopCpu = grouped.Where(x => !x.Name.Equals("Idle", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Cpu).Take(3).ToList();
                _cachedTopProcs = top;
            }
            catch { }
        }

        private double _lastGpuMemClockMhz;
        private double _lastGpuFanPct;
        private double _lastNetUpTotalMb, _lastNetDownTotalMb;
        private string _lastSensorSource = "";
        private double _lastDimTemp;

        public string CollectJson()
        {
            return BuildJson(CollectSnap());
        }

        public Snap CollectSnap()
        {
            PollSlowMetricsIfNeeded();

            Win32Native.MEMORYSTATUSEX mem = new Win32Native.MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(Win32Native.MEMORYSTATUSEX));
            Win32Native.GlobalMemoryStatusEx(ref mem);
            double memTotalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double memFreeGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            double memUsedGb = Math.Max(0, memTotalGb - memFreeGb);
            double memLoadPct = mem.dwMemoryLoad;

            ulong tickMs = Win32Native.GetTickCount64();
            TimeSpan upSpan = TimeSpan.FromMilliseconds(tickMs);
            string uptimeStr = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)upSpan.TotalHours, upSpan.Minutes, upSpan.Seconds);

            int processCount = 0;
            Win32Native.PERFORMANCE_INFORMATION perfInfo = new Win32Native.PERFORMANCE_INFORMATION();
            perfInfo.cb = (uint)Marshal.SizeOf(typeof(Win32Native.PERFORMANCE_INFORMATION));
            if (Win32Native.GetPerformanceInfo(out perfInfo, perfInfo.cb))
            {
                processCount = (int)perfInfo.ProcessCount;
            }

            if ((DateTime.UtcNow - _lastAudioPoll).TotalSeconds >= 15.0)
            {
                _lastAudioPoll = DateTime.UtcNow;
                _cachedVolume = AudioVolumeReader.GetMasterVolumePercent();
                AudioSwitch.Refresh();
            }
            int soundVol = _cachedVolume;

            double cpuLoadPct = double.NaN, diskReadMBs = double.NaN, diskWriteMBs = double.NaN;
            double maxCoreMhz = double.NaN;
            var cores = new List<CoreTelemetry>(_physicalCores);
            if (_pdhQuery != IntPtr.Zero && PdhNative.PdhCollectQueryData(_pdhQuery) == 0)
            {
                PdhNative.PDH_FMT_COUNTERVALUE val;
                if (_totalCpuUtilCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_totalCpuUtilCounter, PdhNative.PDH_FMT_DOUBLE, IntPtr.Zero, out val) == 0)
                {
                    cpuLoadPct = Math.Min(100.0, Math.Max(0.0, val.doubleValue));
                }

                if (_diskReadCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_diskReadCounter, PdhNative.PDH_FMT_DOUBLE, IntPtr.Zero, out val) == 0)
                    diskReadMBs = Math.Max(0, val.doubleValue) / (1024.0 * 1024.0);
                if (_diskWriteCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_diskWriteCounter, PdhNative.PDH_FMT_DOUBLE, IntPtr.Zero, out val) == 0)
                    diskWriteMBs = Math.Max(0, val.doubleValue) / (1024.0 * 1024.0);

                for (int i = 0; i < _corePerfCounters.Count; i++)
                {
                    double mhz = _cpuBaseMhz;
                    if (_corePerfCounters[i] != IntPtr.Zero &&
                        PdhNative.PdhGetFormattedCounterValue(_corePerfCounters[i], PdhNative.PDH_FMT_DOUBLE, IntPtr.Zero, out val) == 0)
                    {
                        if (val.doubleValue > 10.0)
                            mhz = Math.Round(_cpuBaseMhz * (val.doubleValue / 100.0));
                    }
                    double cLoad = double.IsNaN(cpuLoadPct) ? 0 : cpuLoadPct, sum = 0;
                    int n = 0;
                    foreach (IntPtr h in _coreLoadCounters[i])
                    {
                        if (PdhNative.PdhGetFormattedCounterValue(h, PdhNative.PDH_FMT_DOUBLE, IntPtr.Zero, out val) == 0)
                        {
                            sum += Math.Min(100.0, Math.Max(0.0, val.doubleValue));
                            n++;
                        }
                    }
                    if (n > 0) cLoad = sum / n;
                    cores.Add(new CoreTelemetry { Index = i + 1, ClockMhz = mhz, LoadPct = cLoad });
                }
            }

            var gpuThrottle = new List<string>();
            double gpuLoadPct = 0, gpuClockMhz = 0, gpuMemClockMhz = 0;
            double gpuTemp = double.NaN, gpuHotspotTemp = double.NaN;
            double gpuVramUsedMb = 0, gpuVramTotalMb = 0;
            double gpuFanPct = 0, gpuFanRpm = _nvmlAvailable ? 0 : double.NaN;
            double gpuPowerW = double.NaN, gpuVolt = double.NaN;
            string gpuBusType = string.Format("PCI-E {0}.0 x{1}", _gpuMaxLinkGen, _gpuMaxLinkWidth);

            if (_nvmlAvailable && _nvmlDevice != IntPtr.Zero)
            {
                NvmlNative.nvmlUtilization_t util;
                if (NvmlNative.nvmlDeviceGetUtilizationRates(_nvmlDevice, out util) == 0)
                    gpuLoadPct = util.gpu;

                uint tempU;
                if (NvmlNative.nvmlDeviceGetTemperature(_nvmlDevice, 0, out tempU) == 0)
                    gpuTemp = tempU;

                uint clkU;
                if (NvmlNative.nvmlDeviceGetClockInfo(_nvmlDevice, 0, out clkU) == 0)
                    gpuClockMhz = clkU;
                if (NvmlNative.nvmlDeviceGetClockInfo(_nvmlDevice, 2, out clkU) == 0)
                    gpuMemClockMhz = clkU;

                NvmlNative.nvmlMemory_t vram;
                if (NvmlNative.nvmlDeviceGetMemoryInfo(_nvmlDevice, out vram) == 0)
                {
                    gpuVramTotalMb = vram.total / (1024.0 * 1024.0);
                    gpuVramUsedMb = vram.used / (1024.0 * 1024.0);
                }

                uint fanU;
                if (NvmlNative.nvmlDeviceGetFanSpeed(_nvmlDevice, out fanU) == 0)
                    gpuFanPct = fanU;

                uint pwrMilliW;
                if (NvmlNative.nvmlDeviceGetPowerUsage(_nvmlDevice, out pwrMilliW) == 0)
                    gpuPowerW = pwrMilliW / 1000.0;

                try
                {
                    ulong reasons;
                    int rcT;
                    try { rcT = NvmlNative.nvmlDeviceGetCurrentClocksThrottleReasons(_nvmlDevice, out reasons); }
                    catch (EntryPointNotFoundException) { rcT = NvmlNative.nvmlDeviceGetCurrentClocksEventReasons(_nvmlDevice, out reasons); }
                    if (rcT == 0)
                    {
                        if ((reasons & (0x4UL | 0x80UL)) != 0) gpuThrottle.Add(L.T("GPU güç sınırı", "GPU power limit"));
                        if ((reasons & (0x20UL | 0x40UL)) != 0) gpuThrottle.Add(L.T("GPU ısı sınırı", "GPU thermal limit"));
                        else if ((reasons & 0x8UL) != 0) gpuThrottle.Add(L.T("GPU donanım yavaşlatma", "GPU HW slowdown"));
                    }
                }
                catch { }

                uint curGen, curWidth;
                if (NvmlNative.nvmlDeviceGetCurrPcieLinkGeneration(_nvmlDevice, out curGen) == 0 &&
                    NvmlNative.nvmlDeviceGetCurrPcieLinkWidth(_nvmlDevice, out curWidth) == 0)
                {
                    gpuBusType = string.Format("PCI-E {0}.0 x{1} @ {2}.0 x{3}", _gpuMaxLinkGen, _gpuMaxLinkWidth, curGen, curWidth);
                }
            }

            string sensorSource = L.T("Yerleşik: LHM + NVML | ", "Built-in: LHM + NVML | ") + _fps.StatusText +
                                  (Program.IsAdministrator() ? "" : L.T(" | YÖNETİCİ DEĞİL", " | NOT ADMIN")) +
                                  (string.IsNullOrEmpty(LhmError) ? "" : L.T(" | LHM HATA: ", " | LHM ERR: ") + LhmError);

            // Okunamayan sensörler NaN kalır; uydurma/tahmini değer gösterilmez.
            LhmReading lhm = GetLhmCached();

            if ((double.IsNaN(cpuLoadPct) || cpuLoadPct == 0) && !double.IsNaN(lhm.CpuTotalLoad))
                cpuLoadPct = Math.Min(100.0, Math.Max(0.0, lhm.CpuTotalLoad));
            if (double.IsNaN(cpuLoadPct)) cpuLoadPct = 0;

            double moboTemp = lhm.MoboTemp, cpuTemp = lhm.CpuTemp, cpuDiodeTemp = lhm.CpuDiodeTemp, dimmTemp = lhm.DimmTemp;
            double cpuFanRpm = lhm.CpuFan, chaFan1Rpm = lhm.Cha1, chaFan2Rpm = lhm.Cha2, aioPumpRpm = lhm.Pump;
            double cpuPowerW = lhm.CpuPower, cpuVolt = lhm.CpuVolt, socVolt = lhm.SocVolt, v12Volt = lhm.V12;

            // GPU: NVML varsa öncelikli, eksik kalanlar (ve AMD/Intel/hibrit kartlar) LHM'den tamamlanır.
            if (double.IsNaN(gpuTemp)) gpuTemp = lhm.GpuTemp;
            gpuHotspotTemp = lhm.GpuHot;
            if (double.IsNaN(gpuPowerW)) gpuPowerW = lhm.GpuPower;
            gpuFanRpm = lhm.GpuFan;
            gpuVolt = lhm.GpuVolt;
            if (!_nvmlAvailable)
            {
                if (!string.IsNullOrEmpty(lhm.GpuHwName) && ScoreGpuName(lhm.GpuHwName) > ScoreGpuName(_gpuModel))
                    _gpuModel = lhm.GpuHwName;
                gpuBusType = (lhm.GpuScore > 0 && lhm.GpuScore <= 20) ? L.T("Dahili GPU", "Integrated GPU") : "PCI-E";
                if (!double.IsNaN(lhm.GpuLoad)) gpuLoadPct = lhm.GpuLoad;
                if (!double.IsNaN(lhm.GpuClock)) gpuClockMhz = lhm.GpuClock;
                if (!double.IsNaN(lhm.GpuMemClock)) gpuMemClockMhz = lhm.GpuMemClock;
                if (!double.IsNaN(lhm.VramUsedMb)) gpuVramUsedMb = lhm.VramUsedMb;
                if (!double.IsNaN(lhm.VramTotalMb)) gpuVramTotalMb = lhm.VramTotalMb;
            }

            // CPU çekirdek saatleri ve yükleri: Intel P/E hibrit veya AMD çekirdeklerinin tamamını eşle
            if (lhm.CoreClocks.Count > cores.Count)
            {
                cores.Clear();
                foreach (var kv in lhm.CoreClocks.OrderBy(k => k.Key))
                {
                    double cl = cpuLoadPct;
                    lhm.CoreLoads.TryGetValue(kv.Key, out cl);
                    cores.Add(new CoreTelemetry { Index = kv.Key, ClockMhz = Math.Round(kv.Value), LoadPct = cl });
                }
            }
            else if (cores.Count == 0 && lhm.CoreClocks.Count > 0)
            {
                foreach (var kv in lhm.CoreClocks.OrderBy(k => k.Key))
                {
                    double cl = 0;
                    lhm.CoreLoads.TryGetValue(kv.Key, out cl);
                    cores.Add(new CoreTelemetry { Index = kv.Key, ClockMhz = Math.Round(kv.Value), LoadPct = cl });
                }
            }
            else
            {
                foreach (CoreTelemetry c in cores)
                {
                    double real;
                    if (lhm.CoreClocks.TryGetValue(c.Index, out real)) c.ClockMhz = Math.Round(real);
                    double lhmLoad;
                    if (c.LoadPct <= 0.05 && lhm.CoreLoads.TryGetValue(c.Index, out lhmLoad)) c.LoadPct = lhmLoad;
                }
            }
            if (cores.Count > 0)
            {
                var activeCores = cores.Where(c => c.LoadPct >= 5.0).ToList();
                maxCoreMhz = activeCores.Count > 0 ? activeCores.Max(c => c.ClockMhz) : cores.Max(c => c.ClockMhz);
            }

            var drives = BuildDrives();

            // Merge partition capacity info into drives
            for (int i = 0; i < drives.Count; i++)
            {
                List<string> letters;
                if (_driveLetters.TryGetValue(drives[i].Name, out letters) && letters.Count > 0)
                {
                    double totG = 0, freeG = 0;
                    var labels = new List<string>();
                    foreach (var pt in _cachedPartitions)
                    {
                        if (letters.Contains(pt.Letter, StringComparer.OrdinalIgnoreCase))
                        {
                            totG += pt.TotalGb; freeG += pt.FreeGb; labels.Add(pt.Mount);
                        }
                    }
                    if (labels.Count > 0)
                    {
                        drives[i].Mount = string.Join(", ", labels);
                        drives[i].TotalGb = totG;
                        drives[i].FreeGb = freeG;
                        drives[i].UsedPct = totG > 0 ? ((totG - freeG) / totG) * 100.0 : 0;
                        continue;
                    }
                }
            }

            // Update Min/Max session stats
            if (cpuTemp > 10) { if (cpuTemp < _cpuTempMin) _cpuTempMin = cpuTemp; if (cpuTemp > _cpuTempMax) _cpuTempMax = cpuTemp; }
            if (gpuTemp > 10) { if (gpuTemp < _gpuTempMin) _gpuTempMin = gpuTemp; if (gpuTemp > _gpuTempMax) _gpuTempMax = gpuTemp; }
            if (maxCoreMhz > _cpuPeakMhz) _cpuPeakMhz = maxCoreMhz;
            if (gpuClockMhz > _gpuPeakMhz) _gpuPeakMhz = gpuClockMhz;
            if (cpuPowerW > _cpuPeakW) _cpuPeakW = cpuPowerW;
            if (gpuPowerW > _gpuPeakW) _gpuPeakW = gpuPowerW;

            string activeGame;
            double frameTimeMs;
            double fpsLow1;
            double fps = _fps.Read(out activeGame, out frameTimeMs, out fpsLow1);

            if (!double.IsNaN(cpuTemp) && cpuTemp >= 93) gpuThrottle.Add(L.T("CPU ısı sınırı", "CPU thermal limit"));
            string throttleText = string.Join(", ", gpuThrottle);

            // Uyarılar (tray bildirimi, 5 dk bekleme süresiyle)
            if (cpuTemp >= 90) Program.Alert("cpu", L.T("CPU sıcaklığı yüksek: ", "High CPU temperature: ") + Math.Round(cpuTemp) + "°C");
            if (gpuTemp >= 85) Program.Alert("gpu", L.T("GPU sıcaklığı yüksek: ", "High GPU temperature: ") + Math.Round(gpuTemp) + "°C");
            foreach (var dr in drives)
                if (dr.TempC >= 70) Program.Alert("disk" + dr.Name, dr.Name + L.T(" sıcaklığı yüksek: ", " high temperature: ") + Math.Round(dr.TempC) + "°C");

            double netUpKbs = 0, netDownKbs = 0, netUpTotalMb = 0, netDownTotalMb = 0;
            ReadNetworkStats(out netUpKbs, out netDownKbs, out netUpTotalMb, out netDownTotalMb);

            double sysBat = double.NaN;
            bool sysBatChg = false;
            try
            {
                PowerStatus ps = System.Windows.Forms.SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown && ps.BatteryLifePercent >= 0f && ps.BatteryLifePercent <= 1.0f)
                {
                    sysBat = Math.Round(ps.BatteryLifePercent * 100.0);
                    sysBatChg = ps.PowerLineStatus == PowerLineStatus.Online || (ps.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
                }
            }
            catch { }

            _lastGpuMemClockMhz = gpuMemClockMhz;
            _lastGpuFanPct = gpuFanPct;
            _lastNetUpTotalMb = netUpTotalMb;
            _lastNetDownTotalMb = netDownTotalMb;
            _lastSensorSource = sensorSource;
            _lastDimTemp = dimmTemp;

            return new Snap
            {
                Brand = _brandName, MoboModel = _moboModel, Chipset = _moboChipset, Bios = _moboBios, CpuModel = _cpuModel,
                GpuModel = _gpuModel, GpuBus = gpuBusType, MemSpeed = _memSpeedText, Uptime = uptimeStr, Ip = _ipAddress,
                Game = activeGame ?? "", Throttle = throttleText, Source = sensorSource,
                Sound = soundVol < 0 ? double.NaN : soundVol, Procs = processCount, Ping = _pingMs < 0 ? double.NaN : _pingMs,
                MemTotal = memTotalGb * 1024.0, MemUsed = memUsedGb * 1024.0, MemFree = memFreeGb * 1024.0, MemLoad = memLoadPct, MoboTemp = moboTemp,
                CpuLoad = cpuLoadPct, CpuTemp = cpuTemp, CpuClock = maxCoreMhz, CpuFan = cpuFanRpm, Cha1 = chaFan1Rpm, Cha2 = chaFan2Rpm, Pump = aioPumpRpm,
                CpuW = cpuPowerW, CpuV = cpuVolt, GpuW = gpuPowerW, GpuV = gpuVolt,
                GpuLoad = gpuLoadPct, GpuTemp = gpuTemp, GpuClock = gpuClockMhz, VramUsed = gpuVramUsedMb, VramTotal = gpuVramTotalMb, GpuFan = gpuFanRpm,
                Fps = fps, FrameMs = frameTimeMs, Low1 = fpsLow1,
                NetUp = netUpKbs, NetDown = netDownKbs, NetUpTot = netUpTotalMb, NetDownTot = netDownTotalMb, DiskR = diskReadMBs, DiskW = diskWriteMBs,
                Cores = cores.ToArray(), Drives = drives.ToArray(), TopProcs = _cachedTopProcs.ToArray(), TopCpu = _cachedTopCpu.ToArray(), SelfMb = _selfMb, SelfCpu = _selfCpu,
                GpuHot = gpuHotspotTemp, CpuDiode = cpuDiodeTemp, DimmTemp = dimmTemp, SocV = socVolt, V12 = v12Volt,
                SysBat = sysBat, SysBatChg = sysBatChg
            };
        }

        public string BuildJson(Snap s)
        {
            if (s == null) return "{}";
            StringBuilder sb = new StringBuilder(3072);
            sb.Append('{');
            AppendJsonStr(sb, "sensorSource", s.Source);
            AppendJsonStr(sb, "brandName", s.Brand);
            AppendJsonStr(sb, "moboModel", s.MoboModel);
            AppendJsonStr(sb, "moboChipset", s.Chipset);
            AppendJsonStr(sb, "moboBios", s.Bios);
            AppendJsonStr(sb, "cpuModel", s.CpuModel);
            AppendJsonStr(sb, "gpuModel", s.GpuModel);
            AppendJsonStr(sb, "gpuBusType", s.GpuBus);
            AppendJsonStr(sb, "memSpeedText", s.MemSpeed);
            AppendJsonStr(sb, "uptime", s.Uptime);
            AppendJsonNum(sb, "soundVolume", s.Sound);
            AppendJsonNum(sb, "processCount", s.Procs);
            AppendJsonNum(sb, "pingMs", s.Ping);

            AppendJsonNum(sb, "memTotalGb", s.MemTotal / 1024.0);
            AppendJsonNum(sb, "memUsedGb", s.MemUsed / 1024.0);
            AppendJsonNum(sb, "memFreeGb", s.MemFree / 1024.0);
            AppendJsonNum(sb, "memLoadPct", s.MemLoad);
            AppendJsonNum(sb, "dimmTemp", s.DimmTemp);

            AppendJsonNum(sb, "moboTemp", s.MoboTemp);
            AppendJsonNum(sb, "socVolt", s.SocV);
            AppendJsonNum(sb, "v12Volt", s.V12);

            AppendJsonNum(sb, "cpuLoadPct", s.CpuLoad);
            AppendJsonNum(sb, "cpuTemp", s.CpuTemp);
            AppendJsonNum(sb, "cpuTempMin", _cpuTempMin < 500 ? _cpuTempMin : s.CpuTemp);
            AppendJsonNum(sb, "cpuTempMax", _cpuTempMax);
            AppendJsonNum(sb, "cpuDiodeTemp", s.CpuDiode);
            AppendJsonNum(sb, "cpuClockMhz", s.CpuClock);
            AppendJsonNum(sb, "cpuPeakMhz", _cpuPeakMhz);
            AppendJsonNum(sb, "cpuFanRpm", s.CpuFan);
            AppendJsonNum(sb, "chaFan1Rpm", s.Cha1);
            AppendJsonNum(sb, "chaFan2Rpm", s.Cha2);
            AppendJsonNum(sb, "aioPumpRpm", s.Pump);

            sb.Append("\"cores\":[");
            for (int i = 0; i < s.Cores.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"index\":").Append(s.Cores[i].Index)
                  .Append(",\"clockMhz\":").Append(JsonNum(s.Cores[i].ClockMhz, "0"))
                  .Append(",\"loadPct\":").Append(JsonNum(s.Cores[i].LoadPct, "0.0"))
                  .Append('}');
            }
            sb.Append("],");

            AppendJsonNum(sb, "gpuLoadPct", s.GpuLoad);
            AppendJsonNum(sb, "gpuTemp", s.GpuTemp);
            AppendJsonNum(sb, "gpuTempMin", _gpuTempMin < 500 ? _gpuTempMin : s.GpuTemp);
            AppendJsonNum(sb, "gpuTempMax", _gpuTempMax);
            AppendJsonNum(sb, "gpuHotspotTemp", s.GpuHot);
            AppendJsonNum(sb, "gpuClockMhz", s.GpuClock);
            AppendJsonNum(sb, "gpuMemClockMhz", _lastGpuMemClockMhz);
            AppendJsonNum(sb, "gpuPeakMhz", _gpuPeakMhz);
            AppendJsonNum(sb, "gpuVramUsedMb", s.VramUsed);
            AppendJsonNum(sb, "gpuVramTotalMb", s.VramTotal);
            AppendJsonNum(sb, "gpuFanRpm", s.GpuFan);
            AppendJsonNum(sb, "gpuFanPct", _lastGpuFanPct);
            AppendJsonNum(sb, "fps", s.Fps);
            AppendJsonNum(sb, "frameTimeMs", s.FrameMs);
            AppendJsonStr(sb, "activeGame", s.Game);
            AppendJsonNum(sb, "fpsLow1", s.Low1);
            AppendJsonStr(sb, "throttle", s.Throttle);
            AppendJsonNum(sb, "diskReadMBs", s.DiskR);
            AppendJsonNum(sb, "diskWriteMBs", s.DiskW);

            sb.Append("\"drives\":[");
            for (int i = 0; i < s.Drives.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(EscapeJson(s.Drives[i].Name))
                  .Append("\",\"tempC\":").Append(JsonNum(s.Drives[i].TempC, "0"))
                  .Append(",\"mount\":\"").Append(EscapeJson(s.Drives[i].Mount ?? ""))
                  .Append("\",\"usedPct\":").Append(JsonNum(s.Drives[i].UsedPct, "0.0"))
                  .Append(",\"freeGb\":").Append(JsonNum(s.Drives[i].FreeGb, "0.0"))
                  .Append(",\"totalGb\":").Append(JsonNum(s.Drives[i].TotalGb, "0.0"))
                  .Append('}');
            }
            sb.Append("],");

            sb.Append("\"topProcs\":[");
            for (int i = 0; i < s.TopProcs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(EscapeJson(s.TopProcs[i].Name))
                  .Append("\",\"memMb\":").Append(s.TopProcs[i].MemMb.ToString("0", CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("],");

            AppendJsonNum(sb, "cpuPowerW", s.CpuW);
            AppendJsonNum(sb, "cpuPeakW", _cpuPeakW);
            AppendJsonNum(sb, "cpuVolt", s.CpuV);
            AppendJsonNum(sb, "gpuPowerW", s.GpuW);
            AppendJsonNum(sb, "gpuPeakW", _gpuPeakW);
            AppendJsonNum(sb, "gpuVolt", s.GpuV);

            AppendJsonStr(sb, "ipAddress", s.Ip);
            AppendJsonNum(sb, "batteryPct", s.SysBat);
            sb.Append("\"batteryCharging\":").Append(s.SysBatChg ? "true" : "false").Append(',');
            AppendJsonNum(sb, "netUpKbs", s.NetUp);
            AppendJsonNum(sb, "netDownKbs", s.NetDown);
            AppendJsonNum(sb, "netUpTotalMb", s.NetUpTot);
            AppendJsonNum(sb, "netDownTotalMb", s.NetDownTot, isLast: true);
            sb.Append('}');

            return sb.ToString();
        }

        private sealed class LhmReading
        {
            public double MoboTemp = double.NaN, CpuTemp = double.NaN, CpuDiodeTemp = double.NaN;
            public double DimmTemp = double.NaN;
            public double CpuFan = double.NaN, Cha1 = double.NaN, Cha2 = double.NaN, Pump = double.NaN;
            public double CpuVolt = double.NaN, CpuVid = double.NaN, SocVolt = double.NaN, V12 = double.NaN;
            public double CpuPower = double.NaN, CpuTotalLoad = double.NaN;
            public double GpuTemp = double.NaN, GpuHot = double.NaN, GpuFan = double.NaN, GpuVolt = double.NaN, GpuPower = double.NaN;
            public double GpuLoad = double.NaN, GpuClock = double.NaN, GpuMemClock = double.NaN;
            public double VramUsedMb = double.NaN, VramTotalMb = double.NaN;
            public HardwareType GpuType = HardwareType.Motherboard;
            public bool GpuChosen;
            public int GpuScore = -1;
            public string GpuHwName = "";
            public readonly Dictionary<int, double> CoreClocks = new Dictionary<int, double>();
            public readonly Dictionary<int, double> CoreEffClocks = new Dictionary<int, double>();
            public readonly Dictionary<int, double> CoreLoads = new Dictionary<int, double>();
            public readonly List<KeyValuePair<string, double>> BoardTemps = new List<KeyValuePair<string, double>>();
            public readonly List<KeyValuePair<string, double>> BoardFans = new List<KeyValuePair<string, double>>();
            public readonly List<KeyValuePair<string, double>> BoardVolts = new List<KeyValuePair<string, double>>();

            public void ResetGpu()
            {
                GpuTemp = GpuHot = GpuFan = GpuVolt = GpuPower = double.NaN;
                GpuLoad = GpuClock = GpuMemClock = VramUsedMb = VramTotalMb = double.NaN;
            }
        }

        private static bool Has(string name, string part)
        {
            return name != null && name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double? Val(ISensor s)
        {
            if (!s.Value.HasValue) return null;
            float f = s.Value.Value;
            if (float.IsNaN(f) || float.IsInfinity(f)) return null;
            return f;
        }

        public void RequestSensorDump() { _dumpRequested = true; _lhmCacheTime = DateTime.MinValue; }

        // Anakart/SuperIO/CPU okumaları pahalı: en fazla 2 saniyede bir yapılır (NVML ve PDH her turda canlı).
        private LhmReading GetLhmCached()
        {
            if (_lhmCache != null && (DateTime.UtcNow - _lhmCacheTime).TotalMilliseconds < 1900) return _lhmCache;
            _lhmCache = ReadLibreHardwareSensors();
            _lhmCacheTime = DateTime.UtcNow;
            return _lhmCache;
        }

        private static string RegVal(string key, string name)
        {
            try { object o = Microsoft.Win32.Registry.GetValue(key, name, null); return o == null ? null : o.ToString(); } catch { return null; }
        }

        private static bool RegKeyExists(string path)
        {
            try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path)) return k != null; } catch { return false; }
        }

        /// <summary>Hangi sensör grubunun çalıştığını ve olası nedeni (sürücü engeli vb.) metin olarak verir.</summary>
        public string SensorReportText()
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("Yönetici: " + (Program.IsAdministrator() ? "evet" : "HAYIR"));
                sb.AppendLine("Çalışma süresi (bu program): " + (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMinutes.ToString("0") + " dk");
                sb.AppendLine("WinRing0 sürücü kaydı: " + (RegKeyExists(@"SYSTEM\CurrentControlSet\Services\WinRing0_1_2_0") ? "var" : "YOK (yüklenemedi/engellendi)"));
                sb.AppendLine("PawnIO sürücü kaydı: " + (RegKeyExists(@"SYSTEM\CurrentControlSet\Services\PawnIO") ? "var" : "yok"));
                string hv = RegVal(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
                sb.AppendLine("Bellek bütünlüğü (HVCI): " + (hv == "1" ? "AÇIK (ring0 sürücüsünü engelleyebilir)" : (hv == "0" ? "kapalı" : "bilinmiyor")));
                string bl = RegVal(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\CI\Config", "VulnerableDriverBlocklistEnable");
                sb.AppendLine("Güvenlik açığı olan sürücü listesi: " + (bl == "1" ? "AÇIK (WinRing0'ı engeller)" : (bl == "0" ? "kapalı" : "bilinmiyor/varsayılan")));
                if (_lhmComputer == null) { sb.AppendLine("LibreHardwareMonitor başlatılmadı."); return sb.ToString(); }
                int cpuT = 0, cpuP = 0, cpuC = 0, cpuV = 0, fans = 0, fansOn = 0, volts = 0, mbT = 0, sio = 0, stor = 0;
                foreach (IHardware hw in _lhmComputer.Hardware)
                    CountHw(hw, ref cpuT, ref cpuP, ref cpuC, ref cpuV, ref fans, ref fansOn, ref volts, ref mbT, ref sio, ref stor);
                sb.AppendLine("CPU sıcaklık sensörü (değeri >0): " + cpuT + (cpuT > 0 ? "  ✔" : "  ✖"));
                sb.AppendLine("CPU güç sensörü (>0): " + cpuP + (cpuP > 0 ? "  ✔" : "  ✖"));
                sb.AppendLine("CPU saat sensörü (LHM): " + cpuC + (cpuC > 0 ? "  ✔" : "  ✖ (yaklaşık değer kullanılır)"));
                sb.AppendLine("CPU voltaj sensörü: " + cpuV + (cpuV > 0 ? "  ✔" : "  ✖"));
                sb.AppendLine("SuperIO/EC çipi bulundu: " + sio + (sio > 0 ? "  ✔" : "  ✖ (fan/voltaj okunamaz)"));
                sb.AppendLine("Fan sensörü: " + fans + " (dönen: " + fansOn + ")");
                sb.AppendLine("Anakart voltaj sensörü: " + volts + "   Anakart sıcaklık: " + mbT);
                sb.AppendLine("Disk sensörü: " + stor);
                if (cpuT == 0 && cpuP == 0 && sio == 0 && string.IsNullOrEmpty(LhmError))
                    sb.AppendLine("→ Muhtemel neden: ring0 sürücüsü yüklenemedi (Windows engelledi). Çözüm için ayarlardaki Tanılama notlarına bak.");
            }
            catch (Exception ex) { sb.AppendLine("Rapor hatası: " + ex.Message); }
            return sb.ToString();
        }

        private static void CountHw(IHardware hw, ref int cpuT, ref int cpuP, ref int cpuC, ref int cpuV, ref int fans, ref int fansOn, ref int volts, ref int mbT, ref int sio, ref int stor)
        {
            HardwareType t = hw.HardwareType;
            if (t == HardwareType.SuperIO || t == HardwareType.EmbeddedController) sio++;
            if (t == HardwareType.Storage) stor += hw.Sensors.Length;
            foreach (ISensor s in hw.Sensors)
            {
                double v = s.Value.HasValue && !double.IsNaN(s.Value.Value) ? s.Value.Value : 0;
                if (t == HardwareType.Cpu)
                {
                    if (s.SensorType == SensorType.Temperature && v > 1) cpuT++;
                    else if (s.SensorType == SensorType.Power && v > 1) cpuP++;
                    else if (s.SensorType == SensorType.Clock && v > 100) cpuC++;
                    else if (s.SensorType == SensorType.Voltage && v > 0.1) cpuV++;
                }
                else if (t == HardwareType.SuperIO || t == HardwareType.EmbeddedController || t == HardwareType.Motherboard || t == HardwareType.Cooler)
                {
                    if (s.SensorType == SensorType.Fan) { fans++; if (v > 0) fansOn++; }
                    else if (s.SensorType == SensorType.Voltage) volts++;
                    else if (s.SensorType == SensorType.Temperature) mbT++;
                }
            }
            foreach (IHardware sub in hw.SubHardware) CountHw(sub, ref cpuT, ref cpuP, ref cpuC, ref cpuV, ref fans, ref fansOn, ref volts, ref mbT, ref sio, ref stor);
        }

        private void DumpSensors()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("ProSystemMonitor sensör listesi - " + DateTime.Now);
                sb.AppendLine(SensorReportText());
                foreach (IHardware hw in _lhmComputer.Hardware) DumpHw(hw, sb, 0);
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensorler.txt");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static void DumpHw(IHardware hw, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 2);
            sb.AppendLine(pad + "[" + hw.HardwareType + "] " + hw.Name);
            foreach (ISensor s in hw.Sensors)
                sb.AppendLine(pad + "   " + s.SensorType + " | " + s.Name + " = " + (s.Value.HasValue ? s.Value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "null"));
            foreach (IHardware sub in hw.SubHardware) DumpHw(sub, sb, depth + 1);
        }

        private LhmReading ReadLibreHardwareSensors()
        {
            var r = new LhmReading();
            try
            {
                if (!_lhmInitialized && (DateTime.UtcNow - _lhmLastTry).TotalSeconds >= 30)
                {
                    // Başarısız olsa bile her saniye yeniden denemeyelim (30 sn'de bir dene).
                    _lhmLastTry = DateTime.UtcNow;
                    try
                    {
                        _lhmComputer = new Computer
                        {
                            IsCpuEnabled = true,
                            IsGpuEnabled = true,
                            IsMotherboardEnabled = true,
                            IsStorageEnabled = true,
                            IsMemoryEnabled = true
                        };
                        _lhmComputer.Open();
                        _lhmInitialized = true;
                        _lhmFirstOk = DateTime.UtcNow;
                        LhmError = "";
                    }
                    catch (Exception ex)
                    {
                        try { if (_lhmComputer != null) _lhmComputer.Close(); } catch { }
                        _lhmComputer = null;
                        LhmError = ex.GetType().Name + ": " + ex.Message;
                        Program.Log("LHM açılamadı", ex);
                    }
                }
                if (_lhmComputer == null) return r;

                bool pollStorageNow = (DateTime.UtcNow - _lastStoragePoll).TotalSeconds >= 15;
                if (pollStorageNow) _lastStoragePoll = DateTime.UtcNow;
                bool pollMemNow = (DateTime.UtcNow - _lastMemPoll).TotalSeconds >= 5;
                if (pollMemNow) { _lastMemPoll = DateTime.UtcNow; _cachedDimmTemp = double.NaN; }

                _boardNow = (DateTime.UtcNow - _lastBoardPoll).TotalSeconds >= 3;
                if (_boardNow) _lastBoardPoll = DateTime.UtcNow;
                foreach (IHardware hw in _lhmComputer.Hardware)
                    LhmVisit(hw, r, pollStorageNow, pollMemNow);

                ResolveBoardSensors(r);
                if (!_autoDumped && _lhmFirstOk != DateTime.MinValue && (DateTime.UtcNow - _lhmFirstOk).TotalSeconds > 12) { _autoDumped = true; _dumpRequested = true; }
                if (_dumpRequested) { _dumpRequested = false; DumpSensors(); }
            }
            catch { }

            if (!double.IsNaN(_cachedDimmTemp)) r.DimmTemp = _cachedDimmTemp;
            return r;
        }

        private void LhmVisit(IHardware hw, LhmReading r, bool pollStorageNow, bool pollMemNow)
        {
            HardwareType t = hw.HardwareType;
            if (t == HardwareType.Storage && !pollStorageNow) return;
            if (t == HardwareType.Memory && !pollMemNow) return;

            if (_nvmlAvailable && t == HardwareType.GpuNvidia)
            {
                if ((DateTime.UtcNow - _lastLhmGpuPoll).TotalSeconds < 4.0)
                {
                    r.GpuChosen = true; r.GpuType = t;
                    r.GpuHot = _cachedLhmGpuHot; r.GpuFan = _cachedLhmGpuFan; r.GpuVolt = _cachedLhmGpuVolt;
                    return;
                }
                _lastLhmGpuPoll = DateTime.UtcNow;
            }

            bool board = t == HardwareType.Motherboard || t == HardwareType.SuperIO || t == HardwareType.EmbeddedController || t == HardwareType.Cooler;
            if (!(board && !_boardNow)) { try { hw.Update(); } catch { return; } }

            switch (t)
            {
                case HardwareType.Cpu: ReadCpu(hw, r); break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    ReadGpu(hw, r);
                    if (_nvmlAvailable && t == HardwareType.GpuNvidia)
                    {
                        _cachedLhmGpuHot = r.GpuHot; _cachedLhmGpuFan = r.GpuFan; _cachedLhmGpuVolt = r.GpuVolt;
                    }
                    break;
                case HardwareType.Storage: ReadStorage(hw); break;
                case HardwareType.Memory: ReadMemory(hw); break;
                case HardwareType.Motherboard:
                case HardwareType.SuperIO:
                case HardwareType.EmbeddedController:
                case HardwareType.Cooler: ReadBoard(hw, r); break;
            }

            foreach (IHardware sub in hw.SubHardware)
                LhmVisit(sub, r, pollStorageNow, pollMemNow);
        }

        private void ReadCpu(IHardware hw, LhmReading r)
        {
            double coreAvgTemp = double.NaN, coreMaxTemp = double.NaN, ccdTemp = double.NaN;
            double vidMax = double.NaN, svi2Volt = double.NaN;
            foreach (ISensor s in hw.Sensors)
            {
                double? vn = Val(s);
                if (!vn.HasValue) continue;
                double v = vn.Value;
                string n = s.Name ?? "";

                switch (s.SensorType)
                {
                    case SensorType.Temperature:
                        if (v <= 1.0) break;
                        if (Has(n, "Tctl") || Has(n, "Tdie") && !Has(n, "CCD") || Has(n, "Package")) r.CpuTemp = v;
                        else if (Has(n, "CCD")) { if (double.IsNaN(ccdTemp) || v > ccdTemp) ccdTemp = v; }
                        else if (Has(n, "Core Max")) coreMaxTemp = v;
                        else if (Has(n, "Core Average")) coreAvgTemp = v;
                        break;
                    case SensorType.Power:
                        if (v > 1.0 && Has(n, "Package")) r.CpuPower = v;
                        break;
                    case SensorType.Voltage:
                        if (v <= 0 || v >= 2.0) break;
                        if (Has(n, "SoC")) { if (double.IsNaN(r.SocVolt)) r.SocVolt = v; }
                        else if (!Has(n, "VID") && ((Has(n, "SVI2") && Has(n, "Core")) || (Has(n, "SVI3") && Has(n, "Core")) || n.Equals("CPU Core", StringComparison.OrdinalIgnoreCase) || n.Equals("Vcore", StringComparison.OrdinalIgnoreCase) || n.Equals("Core Voltage", StringComparison.OrdinalIgnoreCase)))
                            svi2Volt = v;
                        else if (Has(n, "VID") || Has(n, "Core")) { if (v > 0.25 && (double.IsNaN(vidMax) || v > vidMax)) vidMax = v; }
                        break;
                    case SensorType.Load:
                        if (n.Equals("CPU Total", StringComparison.OrdinalIgnoreCase)) r.CpuTotalLoad = v;
                        else
                        {
                            Match ml = Regex.Match(n, @"^(?:CPU |P-|E-|LP E-)?Core #(\d+)", RegexOptions.IgnoreCase);
                            if (ml.Success && !Has(n, "Max") && !Has(n, "Average"))
                            {
                                int idx = int.Parse(ml.Groups[1].Value, CultureInfo.InvariantCulture);
                                if (idx >= 1 && idx <= 64 && (!r.CoreLoads.ContainsKey(idx) || v > r.CoreLoads[idx]))
                                    r.CoreLoads[idx] = v;
                            }
                        }
                        break;
                    case SensorType.Clock:
                        {
                            Match me = Regex.Match(n, @"^(?:CPU |P-|E-|LP E-)?Core #(\d+)\s*\(Effective\)$", RegexOptions.IgnoreCase);
                            if (me.Success && v > 100)
                                r.CoreEffClocks[int.Parse(me.Groups[1].Value, CultureInfo.InvariantCulture)] = v;
                            else if (!Has(n, "Effective") && !Has(n, "Average") && !Has(n, "Max"))
                            {
                                Match m = Regex.Match(n, @"^(?:CPU |P-|E-|LP E-)?Core #(\d+)", RegexOptions.IgnoreCase);
                                if (m.Success && v > 100)
                                    r.CoreClocks[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = v;
                            }
                        }
                        break;
                }
            }
            if (double.IsNaN(r.CpuTemp)) r.CpuTemp = !double.IsNaN(coreMaxTemp) ? coreMaxTemp : coreAvgTemp;
            r.CpuDiodeTemp = !double.IsNaN(ccdTemp) ? ccdTemp : coreMaxTemp;
            r.CpuVolt = svi2Volt;
            r.CpuVid = vidMax;
        }

        private void ReadGpu(IHardware hw, LhmReading r)
        {
            int sc = hw.HardwareType == HardwareType.GpuNvidia ? 110 : ScoreGpuName(hw.Name);
            if (sc < 0) return;

            double candVramTotal = double.NaN;
            foreach (ISensor s0 in hw.Sensors)
            {
                if (s0.SensorType == SensorType.SmallData && ((s0.Name ?? "").Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase) || (s0.Name ?? "").Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase)))
                {
                    double? vt = Val(s0);
                    if (vt.HasValue) candVramTotal = vt.Value;
                }
            }
            if (!double.IsNaN(candVramTotal) && candVramTotal >= 3072 && sc < 90) sc = 95;

            if (r.GpuChosen)
            {
                bool better = sc > r.GpuScore || (sc == r.GpuScore && !double.IsNaN(candVramTotal) && (double.IsNaN(r.VramTotalMb) || candVramTotal > r.VramTotalMb));
                if (!better) return;
                r.ResetGpu();
            }
            r.GpuChosen = true;
            r.GpuType = hw.HardwareType;
            r.GpuScore = sc;
            r.GpuHwName = hw.Name ?? "";

            double d3dMax = double.NaN, sharedUsed = double.NaN;
            foreach (ISensor s in hw.Sensors)
            {
                double? vn = Val(s);
                if (!vn.HasValue) continue;
                double v = vn.Value;
                string n = s.Name ?? "";
                switch (s.SensorType)
                {
                    case SensorType.Temperature:
                        if (Has(n, "Hot Spot")) r.GpuHot = v;
                        else if (n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || (double.IsNaN(r.GpuTemp) && n.Equals("GPU VR SoC", StringComparison.OrdinalIgnoreCase))) r.GpuTemp = v;
                        else if (double.IsNaN(r.GpuHot) && (Has(n, "Memory Junction") || n.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase))) r.GpuHot = v;
                        break;
                    case SensorType.Power:
                        if (Has(n, "Package") || Has(n, "Board") || Has(n, "Total") || Has(n, "PPT") || Has(n, "ASIC") || n.Equals("GPU Power", StringComparison.OrdinalIgnoreCase)) r.GpuPower = v;
                        else if (double.IsNaN(r.GpuPower) && n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) r.GpuPower = v;
                        break;
                    case SensorType.Fan:
                        if (double.IsNaN(r.GpuFan) || v > r.GpuFan) r.GpuFan = v;
                        break;
                    case SensorType.Voltage:
                        if (v > 0 && v < 2.0 && (Has(n, "Core Voltage") || n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || double.IsNaN(r.GpuVolt))) r.GpuVolt = v;
                        break;
                    case SensorType.Load:
                        if (n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) r.GpuLoad = v;
                        else if (n.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase) && (double.IsNaN(d3dMax) || v > d3dMax)) d3dMax = v;
                        break;
                    case SensorType.Clock:
                        if (n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || (double.IsNaN(r.GpuClock) && n.Equals("GPU Shader", StringComparison.OrdinalIgnoreCase))) r.GpuClock = v;
                        else if (n.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase)) r.GpuMemClock = v;
                        break;
                    case SensorType.SmallData:
                        if (n.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase) || n.Equals("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
                        {
                            if (v > 0 || double.IsNaN(r.VramUsedMb)) r.VramUsedMb = v;
                        }
                        else if (n.Equals("D3D Shared Memory Used", StringComparison.OrdinalIgnoreCase))
                        {
                            sharedUsed = v;
                        }
                        else if (n.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase) || n.Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase))
                        {
                            if (v > 0 || double.IsNaN(r.VramTotalMb)) r.VramTotalMb = v;
                        }
                        break;
                }
            }
            if (double.IsNaN(r.GpuLoad) && !double.IsNaN(d3dMax)) r.GpuLoad = d3dMax;
            if ((double.IsNaN(r.VramUsedMb) || r.VramUsedMb <= 0) && !double.IsNaN(sharedUsed)) r.VramUsedMb = sharedUsed;
        }

        private void ReadBoard(IHardware hw, LhmReading r)
        {
            foreach (ISensor s in hw.Sensors)
            {
                double? vn = Val(s);
                if (!vn.HasValue) continue;
                string n = s.Name ?? "";
                switch (s.SensorType)
                {
                    case SensorType.Temperature: r.BoardTemps.Add(new KeyValuePair<string, double>(n, vn.Value)); break;
                    case SensorType.Fan: r.BoardFans.Add(new KeyValuePair<string, double>(n, vn.Value)); break;
                    case SensorType.Voltage: r.BoardVolts.Add(new KeyValuePair<string, double>(n, vn.Value)); break;
                }
            }
        }

        private void ResolveBoardSensors(LhmReading r)
        {
            // --- Anakart sıcaklığı: isimle öncelik, yoksa Temperature #1 veya makul aralıktaki ilk CPU olmayan sensör ---
            string[] prefer = { "Motherboard", "System", "SYSTIN", "T_Sensor", "VRM", "MB", "Board", "Chipset", "PCH" };
            foreach (string pref in prefer)
            {
                foreach (var kv in r.BoardTemps)
                {
                    if (Has(kv.Key, pref) && kv.Value > 10 && kv.Value < 100) { r.MoboTemp = kv.Value; break; }
                }
                if (!double.IsNaN(r.MoboTemp)) break;
            }
            if (double.IsNaN(r.MoboTemp))
            {
                foreach (var kv in r.BoardTemps)
                {
                    if (kv.Key.Equals("Temperature #1", StringComparison.OrdinalIgnoreCase) && kv.Value >= 18 && kv.Value <= 85) { r.MoboTemp = kv.Value; break; }
                }
            }
            if (double.IsNaN(r.MoboTemp))
            {
                foreach (var kv in r.BoardTemps)
                {
                    if (!Has(kv.Key, "CPU") && !Has(kv.Key, "Core") && !Has(kv.Key, "Water") && kv.Value >= 20 && kv.Value <= 80) { r.MoboTemp = kv.Value; break; }
                }
            }

            // --- Fanlar ---
            foreach (var kv in r.BoardFans)
            {
                string n = kv.Key; double v = kv.Value;
                if (Has(n, "Pump") || Has(n, "AIO") || Has(n, "Opt") || Has(n, "Water"))
                {
                    if (v > 0 && !Has(n, "Flow")) r.Pump = v;
                }
                else if (Has(n, "CPU")) r.CpuFan = v;
                else if (Has(n, "Chassis") || Has(n, "Cha"))
                {
                    if (double.IsNaN(r.Cha1)) r.Cha1 = v; else if (v > 0 || double.IsNaN(r.Cha2)) r.Cha2 = v;
                }
                else
                {
                    // Genel SuperIO adları: Fan #1 = CPU, #2/#3.. kasa, #5/#6 pompa (önceki davranış)
                    Match m = Regex.Match(n, @"#(\d+)$");
                    if (!m.Success) continue;
                    int idx = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (idx == 1) { if (double.IsNaN(r.CpuFan)) r.CpuFan = v; }
                    else if (idx == 2) { if (double.IsNaN(r.Cha1)) r.Cha1 = v; }
                    else if (idx == 3 || idx == 4) { if (v > 0 || double.IsNaN(r.Cha2)) r.Cha2 = v; }
                    else if (idx == 5 || idx == 6) { if (v > 0 && double.IsNaN(r.Pump)) r.Pump = v; }
                }
            }

            // Ham fan değerleri (ayarlardaki fan eşlemesi için) ve kullanıcı eşlemesi
            var raw = new double[8];
            for (int i = 0; i < raw.Length; i++) raw[i] = double.NaN;
            foreach (var kv in r.BoardFans)
            {
                Match mm = Regex.Match(kv.Key, @"#(\d+)$");
                int ix;
                if (mm.Success && int.TryParse(mm.Groups[1].Value, out ix) && ix >= 1 && ix < raw.Length) raw[ix] = kv.Value;
            }
            FanRaw = raw;
            int[] fm = FanMap;
            if (fm != null && fm.Length >= 4)
            {
                if (fm[0] > 0 && fm[0] < raw.Length && !double.IsNaN(raw[fm[0]])) r.CpuFan = raw[fm[0]];
                if (fm[1] > 0 && fm[1] < raw.Length && !double.IsNaN(raw[fm[1]])) r.Cha1 = raw[fm[1]];
                if (fm[2] > 0 && fm[2] < raw.Length && !double.IsNaN(raw[fm[2]])) r.Cha2 = raw[fm[2]];
                if (fm[3] > 0 && fm[3] < raw.Length && !double.IsNaN(raw[fm[3]])) r.Pump = raw[fm[3]];
            }

            // --- Voltajlar ---
            foreach (var kv in r.BoardVolts)
            {
                string n = kv.Key; double v = kv.Value;
                if ((Has(n, "+12V") || n.Equals("12V", StringComparison.OrdinalIgnoreCase)) && v > 8 && v < 16) r.V12 = v;
                else if (double.IsNaN(r.SocVolt) && Has(n, "SoC") && v > 0 && v < 2) r.SocVolt = v;
                else if (double.IsNaN(r.CpuVolt) && (Has(n, "Vcore") || n.Equals("CPU Core", StringComparison.OrdinalIgnoreCase)) && v > 0.5 && v < 2.0) r.CpuVolt = v;
            }
            if (double.IsNaN(r.CpuVolt) && !double.IsNaN(r.CpuVid) && r.CpuVid > 0.55)
                r.CpuVolt = r.CpuVid;
        }

        private void ReadStorage(IHardware hw)
        {
            double best = double.NaN;
            int bestPrio = -1;
            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                string n = s.Name ?? "";
                if (Has(n, "Warning") || Has(n, "Critical") || Has(n, "Limit") || Has(n, "Resolution")) continue;
                double? vn = Val(s);
                if (!vn.HasValue || vn.Value <= 0 || vn.Value >= 100) continue;
                int prio = n.Equals("Composite Temperature", StringComparison.OrdinalIgnoreCase) ? 2 :
                           (n.Equals("Temperature", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                if (prio > bestPrio) { bestPrio = prio; best = vn.Value; }
            }
            if (!double.IsNaN(best)) _cachedDriveTemps[hw.Name] = best;
        }

        private void ReadMemory(IHardware hw)
        {
            // Yalnızca gerçek DIMM sıcaklıkları: Limit/Resolution/Warning/Critical eşiklerini kesinlikle dışla.
            double best = double.NaN;
            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                string n = s.Name ?? "";
                if (Has(n, "Limit") || Has(n, "Resolution") || Has(n, "Warning") || Has(n, "Critical")) continue;
                double? vn = Val(s);
                if (vn.HasValue && vn.Value > 5.0 && vn.Value < 105.0 && (double.IsNaN(best) || vn.Value > best)) best = vn.Value;
            }
            if (!double.IsNaN(best) && (double.IsNaN(_cachedDimmTemp) || best > _cachedDimmTemp)) _cachedDimmTemp = best;
        }

        private static string NormName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private bool TryGetDriveTemp(string driveName, out double temp)
        {
            temp = double.NaN;
            double t;
            if (_cachedDriveTemps.TryGetValue(driveName, out t)) { temp = t; return true; }
            string a = NormName(driveName);
            if (a.Length == 0) return false;
            foreach (var kv in _cachedDriveTemps)
            {
                string b = NormName(kv.Key);
                if (b.Length == 0) continue;
                if (a.Contains(b) || b.Contains(a)) { temp = kv.Value; return true; }
            }
            return false;
        }

        private List<DriveTelemetry> BuildDrives()
        {
            var drives = new List<DriveTelemetry>();
            IEnumerable<string> names = _detectedDriveNames;
            if (_detectedDriveNames.Count == 0)
                names = _cachedDriveTemps.Keys.Take(6).ToList();

            foreach (string dName in names)
            {
                double t;
                if (!TryGetDriveTemp(dName, out t)) t = double.NaN;
                drives.Add(new DriveTelemetry { Name = dName, TempC = t });
            }
            return drives;
        }

        private void ReadNetworkStats(out double upKbs, out double downKbs, out double upTotalMb, out double downTotalMb)
        {
            upKbs = 0; downKbs = 0; upTotalMb = 0; downTotalMb = 0;
            try
            {
                long sent = 0, recv = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        var stats = ni.GetIPStatistics();
                        sent += stats.BytesSent;
                        recv += stats.BytesReceived;
                    }
                }

                DateTime now = DateTime.UtcNow;
                double elapsedSec = (now - _prevNetTime).TotalSeconds;
                if (_prevBytesSent >= 0 && elapsedSec > 0.1)
                {
                    upKbs = Math.Max(0, (sent - _prevBytesSent) / 1024.0 / elapsedSec);
                    downKbs = Math.Max(0, (recv - _prevBytesRecv) / 1024.0 / elapsedSec);
                }
                _prevBytesSent = sent;
                _prevBytesRecv = recv;
                _prevNetTime = now;

                upTotalMb = sent / (1024.0 * 1024.0);
                downTotalMb = recv / (1024.0 * 1024.0);
            }
            catch { }
        }

        private static void AppendJsonStr(StringBuilder sb, string key, string val, bool isLast = false)
        {
            sb.Append('"').Append(key).Append("\":\"").Append(EscapeJson(val ?? "")).Append('"');
            if (!isLast) sb.Append(',');
        }

        private static void AppendJsonNum(StringBuilder sb, string key, double val, bool isLast = false)
        {
            sb.Append('"').Append(key).Append("\":").Append(JsonNum(val, "0.###"));
            if (!isLast) sb.Append(',');
        }

        private static string JsonNum(double v, string fmt)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            try { if (_pdhQuery != IntPtr.Zero) PdhNative.PdhCloseQuery(_pdhQuery); } catch { }
            try { _fps.Dispose(); } catch { }
            try { if (_nvmlAvailable) NvmlNative.nvmlShutdown(); } catch { }
            try { if (_lhmComputer != null) _lhmComputer.Close(); } catch { }
        }
    }

    /// <summary>
    /// RTSS/AIDA64 olmadan FPS ölçümü: Windows ETW (Event Tracing for Windows) üzerinden
    /// DXGI / D3D9 / DxgKrnl "Present" olaylarını sayar (PresentMon ile aynı yöntem).
    /// Yönetici hakkı gerekir; yoksa FPS 0 görünür, diğer sensörler etkilenmez.
    /// </summary>
    internal sealed class FpsMonitor : IDisposable
    {
        private const string SessionName = "ProSystemMonitor_FpsSession";
        // Vulkan/OpenGL yedeği (DxgKrnl) çok sayıda olay üretebilir; düşük kaynak kullanımı için kapalı.
        private const bool UseKernelFallback = false;
        private static readonly Guid DxgiGuid = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3D9Guid = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private static readonly Guid DxgKrnlGuid = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");

        private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dwm", "explorer", "msedge", "msedgewebview2", "chrome", "firefox", "brave", "opera", "ProSystemMonitor",
            "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
            "TextInputHost", "LockApp", "Widgets", "WidgetService", "GameBar", "GameBarFTServer", "steamwebhelper",
            "Discord", "Spotify", "NVIDIA Overlay", "NVIDIA app", "nvcontainer", "RuntimeBroker", "devenv", "Code",
            // Duvar kâğıdı / medya / yayın uygulamaları oyun değildir
            "wallpaper32", "wallpaper64", "wallpaperengine", "webwallpaper32", "webwallpaper64", "ui32", "Lively", "RainWallpaper",
            "obs64", "obs32", "vlc", "mpv", "mpc-hc", "mpc-hc64", "PotPlayerMini", "PotPlayerMini64", "Taskmgr", "Steam", "SteamVR",
            "EpicGamesLauncher", "Battle.net", "GalaxyClient", "RiotClientServices", "Origin", "EADesktop", "UbisoftConnect"
        };

        // Bir swap chain'in Present zaman damgaları (olayın kendi QPC zamanı; ETW tamponu toplu teslim ettiği için varış zamanı kullanılmaz).
        private sealed class Series
        {
            public readonly List<long> T = new List<long>(4096);
            public int Head;
            public long Newest;
            public void Add(long ts, long keep)
            {
                T.Add(ts); if (ts > Newest) Newest = ts;
                while (Head < T.Count && Newest - T[Head] > keep) Head++;
                if (Head > 8192) { T.RemoveRange(0, Head); Head = 0; }
            }
            public int Count { get { return T.Count - Head; } }
        }

        private sealed class PidStat
        {
            public readonly Dictionary<ulong, Series> Sw = new Dictionary<ulong, Series>();   // DXGI / D3D9, swap chain başına
            public readonly Series Kernel = new Series();                                     // DxgKrnl (Vulkan/OpenGL yedeği)
            public long LastSeen;                                                              // son olayın geliş zamanı (donmuş oyun tespiti)
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void EventRecordCallback(IntPtr eventRecord);

        private readonly Dictionary<uint, PidStat> _stats = new Dictionary<uint, PidStat>();
        private readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();
        private readonly object _gate = new object();
        private EventRecordCallback _callback;
        private IntPtr _logfile = IntPtr.Zero;
        private ulong _session;
        private ulong _trace;
        private Thread _thread;
        private volatile bool _active;
        private volatile bool _disposed;
        private long _eventCount;
        public volatile string Status = "ETW başlatılıyor";
        public string StatusText { get { return Status + (_active ? ", olay: " + Interlocked.Read(ref _eventCount) : ""); } }

        public void Start()
        {
            if (IntPtr.Size != 8) { Status = "ETW kapalı: 32-bit işlem"; return; }   // ETW yapıları 64-bit için tanımlı
            _thread = new Thread(Run) { IsBackground = true, Name = "FpsEtw" };
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                StopSessionByName();

                IntPtr props;
                ulong handle;
                uint rc = StartSession(out props, out handle);
                if (rc == 183)   // ERROR_ALREADY_EXISTS (önceki çalışmadan kalmış oturum)
                {
                    Marshal.FreeHGlobal(props);
                    StopSessionByName();
                    rc = StartSession(out props, out handle);
                }
                Marshal.FreeHGlobal(props);
                if (rc != 0) { Status = rc == 5 ? "ETW kapalı: yönetici değil" : "ETW hata " + rc; return; }
                _session = handle;

                Guid g1 = DxgiGuid, g2 = D3D9Guid, g3 = DxgKrnlGuid;
                if (EnableFiltered(g1, 42) != 0)
                    EtwNative.EnableTraceEx2(_session, ref g1, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
                if (EnableFiltered(g2, 1) != 0)
                    EtwNative.EnableTraceEx2(_session, ref g2, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
                if (UseKernelFallback) EtwNative.EnableTraceEx2(_session, ref g3, 1, 4, 0x1, 0, 0, IntPtr.Zero);

                _callback = OnEvent;
                var lf = new EtwNative.EVENT_TRACE_LOGFILE();
                lf.LoggerName = Marshal.StringToHGlobalUni(SessionName);
                lf.ProcessTraceMode = EtwNative.PROCESS_TRACE_MODE_REAL_TIME | EtwNative.PROCESS_TRACE_MODE_EVENT_RECORD;
                lf.EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callback);
                _logfile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(EtwNative.EVENT_TRACE_LOGFILE)));
                Marshal.StructureToPtr(lf, _logfile, false);

                ulong th = EtwNative.OpenTraceW(_logfile);
                if (th == ulong.MaxValue || th == 0xFFFFFFFFUL) { Status = "ETW OpenTrace hatası"; StopSessionByName(); return; }
                _trace = th;
                _active = true;
                Status = "ETW açık";

                // Oturum kapanana kadar bloklar.
                EtwNative.ProcessTrace(new ulong[] { th }, 1, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { Status = "ETW istisna: " + ex.GetType().Name; }
            finally { _active = false; }
        }

        private static uint StartSession(out IntPtr props, out ulong handle)
        {
            props = AllocProperties();
            return EtwNative.StartTraceW(out handle, SessionName, props);
        }

        // EVENT_TRACE_PROPERTIES (x64): boyut 120, oturum adı 120. ofsetten başlar.
        private static IntPtr AllocProperties()
        {
            int size = 120 + 2 * (SessionName.Length + 1) + 2048;
            IntPtr p = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, p, size);
            Marshal.WriteInt32(p, 0, size);              // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);                // Wnode.ClientContext = QPC saati
            Marshal.WriteInt32(p, 44, 0x00020000);       // Wnode.Flags = WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);               // BufferSize (KB)
            Marshal.WriteInt32(p, 52, 4);                // MinimumBuffers
            Marshal.WriteInt32(p, 56, 16);               // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x00000100);       // LogFileMode = EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);                // FlushTimer (sn)
            Marshal.WriteInt32(p, 112, 0);               // LogFileNameOffset
            Marshal.WriteInt32(p, 116, 120);             // LoggerNameOffset
            char[] name = (SessionName + "\0").ToCharArray();
            Marshal.Copy(name, 0, new IntPtr(p.ToInt64() + 120), name.Length);
            return p;
        }

        private static void StopSessionByName()
        {
            IntPtr props = IntPtr.Zero;
            try
            {
                props = AllocProperties();
                EtwNative.ControlTraceW(0, SessionName, props, 1);   // EVENT_TRACE_CONTROL_STOP
            }
            catch { }
            finally { if (props != IntPtr.Zero) Marshal.FreeHGlobal(props); }
        }

        private uint EnableFiltered(Guid provider, ushort eventId)
        {
            IntPtr ids = IntPtr.Zero, desc = IntPtr.Zero, prm = IntPtr.Zero;
            try
            {
                // EVENT_FILTER_EVENT_ID { BOOLEAN FilterIn; UCHAR Reserved; USHORT Count; USHORT Events[1]; }
                ids = Marshal.AllocHGlobal(8);
                Marshal.WriteByte(ids, 0, 1); Marshal.WriteByte(ids, 1, 0);
                Marshal.WriteInt16(ids, 2, 1); Marshal.WriteInt16(ids, 4, (short)eventId); Marshal.WriteInt16(ids, 6, 0);
                // EVENT_FILTER_DESCRIPTOR { ULONGLONG Ptr; ULONG Size; ULONG Type; }
                desc = Marshal.AllocHGlobal(16);
                Marshal.WriteInt64(desc, 0, ids.ToInt64()); Marshal.WriteInt32(desc, 8, 6); Marshal.WriteInt32(desc, 12, unchecked((int)0x80000200));
                // ENABLE_TRACE_PARAMETERS (x64): Version@0, EnableProperty@4, ControlFlags@8, SourceId@12, EnableFilterDesc@32, FilterDescCount@40
                prm = Marshal.AllocHGlobal(48);
                for (int i = 0; i < 48; i += 4) Marshal.WriteInt32(prm, i, 0);
                Marshal.WriteInt32(prm, 0, 2);
                Marshal.WriteInt64(prm, 32, desc.ToInt64());
                Marshal.WriteInt32(prm, 40, 1);
                return EtwNative.EnableTraceEx2(_session, ref provider, 1, 5, ulong.MaxValue, 0, 0, prm);
            }
            catch { return 1; }
            finally
            {
                if (prm != IntPtr.Zero) Marshal.FreeHGlobal(prm);
                if (desc != IntPtr.Zero) Marshal.FreeHGlobal(desc);
                if (ids != IntPtr.Zero) Marshal.FreeHGlobal(ids);
            }
        }

        private void OnEvent(IntPtr rec)
        {
            try
            {
                // EVENT_RECORD (x64): HeaderFlags @4, ProcessId @12, TimeStamp @16, ProviderId.Data1 @24, EventDescriptor.Id @40,
                // UserDataLength @86, UserData @96
                int provider = Marshal.ReadInt32(rec, 24);
                int id = Marshal.ReadInt16(rec, 40) & 0xFFFF;
                bool dxgi = false, d3d9 = false, kernel = false;
                if (provider == unchecked((int)0xCA11C036)) dxgi = (id == 42);           // DXGI Present_Start
                else if (provider == 0x783ACA0A) d3d9 = (id == 1);                       // D3D9 Present_Start
                else if (provider == unchecked((int)0x802EC45A)) kernel = (id == 184);   // DxgKrnl Present_Info
                if (!dxgi && !d3d9 && !kernel) return;

                uint pid = (uint)Marshal.ReadInt32(rec, 12);
                if (pid == 0 || pid == 4) return;

                ulong swap = 0;
                if (dxgi)
                {
                    // Payload: pSwapChain (işaretçi, hedef işlemin boyutunda), Flags (uint), SyncInterval (uint)
                    int hflags = Marshal.ReadInt16(rec, 4) & 0xFFFF;
                    int ps = (hflags & 0x20) != 0 ? 4 : 8;
                    int udLen = Marshal.ReadInt16(rec, 86) & 0xFFFF;
                    IntPtr ud = Marshal.ReadIntPtr(rec, 96);
                    if (ud != IntPtr.Zero && udLen >= ps + 4)
                    {
                        swap = ps == 8 ? (ulong)Marshal.ReadInt64(ud, 0) : (ulong)(uint)Marshal.ReadInt32(ud, 0);
                        uint pflags = (uint)Marshal.ReadInt32(ud, ps);
                        if ((pflags & 0x1) != 0) return;   // DXGI_PRESENT_TEST: gerçek kare değil
                    }
                }

                Interlocked.Increment(ref _eventCount);
                long ts = Marshal.ReadInt64(rec, 16);       // QPC (ClientContext=1)
                long arrived = Stopwatch.GetTimestamp();
                long keep = 31 * Stopwatch.Frequency;
                lock (_gate)
                {
                    PidStat st;
                    if (!_stats.TryGetValue(pid, out st)) { st = new PidStat(); _stats[pid] = st; }
                    st.LastSeen = arrived;
                    if (kernel) st.Kernel.Add(ts, keep);
                    else
                    {
                        Series sr;
                        if (!st.Sw.TryGetValue(swap, out sr)) { if (st.Sw.Count >= 8) return; sr = new Series(); st.Sw[swap] = sr; }
                        sr.Add(ts, keep);
                    }
                }
            }
            catch { }
        }

        private static readonly string[] GameDirs =
        {
            "\\steamapps\\common\\", "\\steamlibrary\\", "\\epic games\\", "\\program files\\epic games\\", "\\gog galaxy\\games\\", "\\gog games\\", "\\riot games\\",
            "\\ubisoft game launcher\\games\\", "\\ea games\\", "\\origin games\\", "\\xboxgames\\",
            "\\rockstar games\\", "\\battle.net\\", "\\blizzard\\", "\\games\\", "\\oyunlar\\", "\\game\\"
        };
        private readonly Dictionary<uint, bool> _gamePath = new Dictionary<uint, bool>();

        private bool IsGamePath(uint pid)
        {
            bool r;
            if (_gamePath.TryGetValue(pid, out r)) return r;
            r = false;
            IntPtr h = Win32Native.OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int len = sb.Capacity;
                    if (Win32Native.QueryFullProcessImageName(h, 0, sb, ref len))
                    {
                        string path = sb.ToString().ToLowerInvariant();
                        foreach (string d in GameDirs) if (path.Contains(d)) { r = true; break; }
                        if (path.Contains("wallpaper_engine") || path.Contains("wallpaper engine") || path.Contains("\\wallpaper")) r = false;
                    }
                }
                finally { Win32Native.CloseHandle(h); }
            }
            _gamePath[pid] = r;
            return r;
        }

        private string NameOf(uint pid)
        {
            string n;
            if (_names.TryGetValue(pid, out n)) return n;
            try { using (Process p = Process.GetProcessById((int)pid)) n = p.ProcessName; }
            catch { n = ""; }
            _names[pid] = n;
            return n;
        }

        // Zaman damgalarından ortalama kare hızı: (kare-1) / geçen süre. Pencere, serinin en yeni olayında biter.
        private static bool Rate(Series s, out double fps, out double avgMs)
        {
            fps = 0; avgMs = 0;
            if (s.Count < 2) return false;
            long f = Stopwatch.Frequency, newest = s.T[s.T.Count - 1];
            for (int pass = 0; pass < 2; pass++)
            {
                long w = pass == 0 ? f : 3 * f;   // düşük FPS'te pencereyi 3 sn'ye genişlet
                int i = s.T.Count - 1;
                while (i > s.Head && newest - s.T[i - 1] <= w) i--;
                int n = s.T.Count - i;
                if (n >= 5 || pass == 1)
                {
                    long span = newest - s.T[i];
                    if (n < 2 || span <= 0) return false;
                    fps = (n - 1) * (double)f / span;
                    avgMs = (span * 1000.0 / f) / (n - 1);
                    return true;
                }
            }
            return false;
        }

        private bool _kernelOn;
        private long _kernelUsed;

        private void SetKernel(bool on)
        {
            if (_kernelOn == on || _session == 0) return;
            try
            {
                Guid g = DxgKrnlGuid;
                EtwNative.EnableTraceEx2(_session, ref g, on ? 1u : 0u, 4, 0x1, 0, 0, IntPtr.Zero);
                _kernelOn = on;
            }
            catch { }
        }

        /// <summary>Son kareler arasındaki sürelerden FPS, ortalama kare süresi ve %1 low; oyun/uygulama adını döndürür.</summary>
        public double Read(out string game, out double frameMs, out double low1)
        {
            game = "";
            frameMs = 0;
            low1 = double.NaN;
            if (!_active) return 0;

            uint fgPid = 0;
            bool fgFullscreen = false;
            try
            {
                IntPtr fgWnd = Win32Native.GetForegroundWindow();
                Win32Native.GetWindowThreadProcessId(fgWnd, out fgPid);
                Win32Native.RECT rc;
                if (fgWnd != IntPtr.Zero && Win32Native.GetWindowRect(fgWnd, out rc))
                {
                    Rectangle sb = Screen.FromHandle(fgWnd).Bounds;
                    int il = Math.Max(sb.Left, rc.Left), it = Math.Max(sb.Top, rc.Top);
                    int ir = Math.Min(sb.Right, rc.Right), ib = Math.Min(sb.Bottom, rc.Bottom);
                    if (ir > il && ib > it && sb.Width > 0 && sb.Height > 0)
                    {
                        double areaRatio = ((double)(ir - il) * (ib - it)) / ((double)sb.Width * sb.Height);
                        fgFullscreen = areaRatio >= 0.85;
                    }
                }
            }
            catch { }

            long now = Stopwatch.GetTimestamp();
            long f = Stopwatch.Frequency;
            long stale = 2 * f + f / 2;   // ETW tamponları ~1 sn'de bir teslim edilir; bundan uzun sessizlik = kare yok
            double bestFps = 0, bestMs = 0;
            uint bestPid = 0;
            string bestName = "";
            bool bestIsFg = false, bestKernel = false;
            Series bestSeries = null;
            bool fgHasDxgi = false;
            var dead = new List<uint>();
            uint self = (uint)Process.GetCurrentProcess().Id;

            lock (_gate)
            {
                foreach (var kv in _stats)
                {
                    PidStat st = kv.Value;
                    if (now - st.LastSeen > 10 * f) { dead.Add(kv.Key); continue; }

                    // DXGI/D3D9: en hızlı swap chain (oyun içi bindirmeler/ikincil zincirler FPS'i şişirmesin diye tek zincir)
                    Series chosen = null; double fps = 0, avgMs = 0; bool useKernel = false;
                    foreach (Series sr in st.Sw.Values)
                    {
                        if (now - sr.Newest > stale) continue;
                        double r, ms;
                        if (Rate(sr, out r, out ms) && r > fps) { fps = r; avgMs = ms; chosen = sr; }
                    }
                    if (chosen != null && kv.Key == fgPid) fgHasDxgi = true;
                    if (chosen == null && _kernelOn && now - st.Kernel.Newest <= stale)
                    {
                        double r, ms;
                        if (Rate(st.Kernel, out r, out ms)) { fps = r; avgMs = ms; chosen = st.Kernel; useKernel = true; }
                    }
                    if (chosen == null) continue;

                    string name = NameOf(kv.Key);
                    if (name.Length == 0 || Ignored.Contains(name) || kv.Key == self) continue;

                    bool isFg = kv.Key == fgPid;
                    // Yalnızca oyunlar: bilinen oyun klasörlerindeki exe'ler veya ön planda >= %85 ekran kaplayan (çerçevesiz/tam ekran) 3D pencere.
                    if (!IsGamePath(kv.Key) && !(isFg && fgFullscreen && fps > 5.0)) continue;
                    if (fps < 1 || fps > 1000) continue;

                    if ((isFg && !bestIsFg) || (isFg == bestIsFg && fps > bestFps))
                    {
                        bestFps = fps; bestMs = avgMs; bestName = name; bestIsFg = isFg; bestPid = kv.Key; bestSeries = chosen; bestKernel = useKernel;
                    }
                }
                foreach (uint d in dead) { _stats.Remove(d); _names.Remove(d); _gamePath.Remove(d); }

                if (bestSeries != null && bestSeries.Count > 20)
                {
                    // Gerçek %1 low: son 5 sn'de (yetersizse 10 sn) toplam sürenin en yavaş %1'lik kısmındaki karelerin ortalama FPS'i
                    var gaps = new List<double>(bestSeries.Count);
                    double totalMs = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        gaps.Clear();
                        totalMs = 0;
                        long prev = 0, cut = bestSeries.Newest - (pass == 0 ? 5 * f : 10 * f);
                        for (int i = bestSeries.Head; i < bestSeries.T.Count; i++)
                        {
                            long t = bestSeries.T[i];
                            if (t < cut) continue;
                            if (prev != 0)
                            {
                                double g = (t - prev) * 1000.0 / f;
                                if (g > 0 && g < 2000) { gaps.Add(g); totalMs += g; }
                            }
                            prev = t;
                        }
                        if (gaps.Count >= 60) break;
                    }
                    if (gaps.Count >= 20 && totalMs > 0)
                    {
                        gaps.Sort();
                        double targetMs = totalMs * 0.01;
                        double worstSum = 0;
                        int worstCount = 0;
                        for (int i = gaps.Count - 1; i >= 0; i--)
                        {
                            worstSum += gaps[i];
                            worstCount++;
                            if (worstSum >= targetMs) break;
                        }
                        if (worstCount > 0 && worstSum > 0)
                        {
                            double avgWorst1PctMs = worstSum / worstCount;
                            low1 = 1000.0 / avgWorst1PctMs;
                        }
                    }
                }
            }

            // Vulkan/OpenGL oyunları DXGI Present üretmez: ön planda oyun var ama DXGI olayı yoksa DxgKrnl'i geçici aç.
            if (bestKernel) _kernelUsed = now;
            if (!_kernelOn && fgPid != 0 && fgPid != self && !fgHasDxgi && bestPid == 0)
            {
                string fn = NameOf(fgPid);
                if (fn.Length > 0 && !Ignored.Contains(fn) && (IsGamePath(fgPid) || fgFullscreen)) { SetKernel(true); _kernelUsed = now; }
            }
            else if (_kernelOn && now - _kernelUsed > 60 * f) SetKernel(false);

            game = bestName;
            frameMs = bestMs > 0 ? bestMs : (bestFps > 0 ? 1000.0 / bestFps : 0);
            return Math.Round(bestFps);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
            try { if (_trace != 0) EtwNative.CloseTrace(_trace); } catch { }
            StopSessionByName();
        }
    }

    internal static class EtwNative
    {
        public const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        public const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Version;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public ulong ProcessorTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 172)]
            public byte[] TimeZone;          // TIME_ZONE_INFORMATION
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE_LOGFILE
        {
            public IntPtr LogFileName;
            public IntPtr LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public IntPtr EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        public static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll")]
        public static extern uint EnableTraceEx2(ulong traceHandle, ref Guid providerId, uint controlCode, byte level,
            ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        public static extern ulong OpenTraceW(IntPtr logfile);

        [DllImport("advapi32.dll")]
        public static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        public static extern uint CloseTrace(ulong traceHandle);
    }

    internal static class Win32Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonpaged;
            public IntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, IntPtr info, int size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref int size);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);
    }

    internal static class PdhNative
    {
        public const uint PDH_FMT_DOUBLE = 0x00000200;

        [StructLayout(LayoutKind.Explicit)]
        public struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double doubleValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhOpenQueryW(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        public static extern uint PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        public static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, IntPtr lpdwType, out PDH_FMT_COUNTERVALUE pValue);

        [DllImport("pdh.dll")]
        public static extern uint PdhCloseQuery(IntPtr hQuery);
    }

    internal static class NvmlNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlUtilization_t
        {
            public uint gpu;
            public uint memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlMemory_t
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        public static extern int nvmlInit_v2();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)]
        public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
        public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")]
        public static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint clock);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFanSpeed")]
        public static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint speed);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
        public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrentClocksThrottleReasons")]
        public static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrentClocksEventReasons")]
        public static extern int nvmlDeviceGetCurrentClocksEventReasons(IntPtr device, out ulong reasons);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMaxPcieLinkGeneration")]
        public static extern int nvmlDeviceGetMaxPcieLinkGeneration(IntPtr device, out uint maxLinkGen);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMaxPcieLinkWidth")]
        public static extern int nvmlDeviceGetMaxPcieLinkWidth(IntPtr device, out uint maxLinkWidth);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkGeneration")]
        public static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr device, out uint currLinkGen);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkWidth")]
        public static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr device, out uint currLinkWidth);
    }

    internal static class AudioVolumeReader
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out int pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
        }

        public static int GetMasterVolumePercent()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                IMMDevice dev;
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out dev) == 0 && dev != null)
                {
                    Guid epVolGuid = typeof(IAudioEndpointVolume).GUID;
                    object obj;
                    if (dev.Activate(ref epVolGuid, 23, IntPtr.Zero, out obj) == 0 && obj != null)
                    {
                        var epVol = (IAudioEndpointVolume)obj;
                        float level;
                        int hr = epVol.GetMasterVolumeLevelScalar(out level);
                        try { Marshal.ReleaseComObject(epVol); } catch { }
                        try { Marshal.ReleaseComObject(dev); } catch { }
                        try { Marshal.ReleaseComObject(enumerator); } catch { }
                        if (hr == 0) return (int)Math.Round(level * 100.0f);
                    }
                }
            }
            catch { }
            return -1;
        }
    }
}
