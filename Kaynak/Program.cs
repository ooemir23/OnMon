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
        public static volatile int PollIntervalMs = 1000;
        public static string LatestJson = "{}";
        private static NotifyIcon _trayIcon;
        private static TelemetryCollector _collector;
        private static HttpListener _listener;
        private static readonly ConcurrentDictionary<Guid, StreamWriter> _sseClients = new ConcurrentDictionary<Guid, StreamWriter>();
        private static volatile bool _running = true;

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\ProSystemMonitor_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    SignalFirstInstance();
                    return;
                }

                Application.ThreadException += (o, e) => Log("ui-thread", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log("unhandled", e.ExceptionObject as Exception);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                LowerOwnPriority();
                _ui = new Control(); _ui.CreateControl(); var hnd = _ui.Handle;
                _showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\ProSystemMonitor_ShowDash");
                new Thread(() => { while (_running) { try { if (_showEvt.WaitOne(1000)) LaunchDashboardOnSecondMonitor(); } catch { } } }) { IsBackground = true }.Start();
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
                        LatestJson = _collector.CollectJson();
                        TrimProcessMemory();
                    }
                    catch { }
                    TelemetryLoop();
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                pollThread.Start();

                if (args == null || !args.Contains("--silent"))
                {
                    LaunchDashboardOnSecondMonitor();
                }

                Application.Run();

                _running = false;
                try { if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); } } catch { }
                try { if (_listener != null) _listener.Stop(); } catch { }
                try { if (_collector != null) _collector.Dispose(); } catch { }
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
            try { if (_trayIcon != null) _trayIcon.ShowBalloonTip(4000, "ROG Pro System Monitor", text, ToolTipIcon.Info); } catch { }
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
                if (_trayIcon != null) _trayIcon.ShowBalloonTip(6000, "ROG Pro System Monitor - Uyarı", text, ToolTipIcon.Warning);
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
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000)) return -1;
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        public static bool IsAutostartEnabled()
        {
            return RunSchtasks("/Query /TN " + AutostartTask) == 0;
        }

        public static bool SetAutostart(bool on)
        {
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
                return RunSchtasks("/Create /TN " + AutostartTask + " /XML \"" + tmp + "\" /F") == 0;
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
                    LatestJson = _collector.CollectJson();
                    BroadcastSse(LatestJson);
                    PushDash();
                    tickCount++;
                    if (tickCount % 300 == 0) TrimProcessMemory();
                }
                catch { }
                sw.Stop();
                int targetMs = Math.Max(300, Math.Min(5000, PollIntervalMs));
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
                        _trayIcon.ShowBalloonTip(5000, "ROG Pro System Monitor",
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
                    byte[] data = Encoding.UTF8.GetBytes(LatestJson);
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

        private static void SignalFirstInstance()
        {
            try { EventWaitHandle.OpenExisting("Global\\ProSystemMonitor_ShowDash").Set(); } catch { }
        }

        public static void RequestSensorDump() { if (_collector != null) _collector.RequestSensorDump(); }

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
                Text = "ROG Pro System Monitor",
                Icon = CreateRogTrayIcon(),
                Visible = true
            };

            var menu = new ContextMenuStrip();
            var open2ndItem = new ToolStripMenuItem("Panoyu Aç (yerel, 2. monitör)");
            open2ndItem.Font = new Font(open2ndItem.Font, FontStyle.Bold);
            open2ndItem.Click += (s, e) => LaunchDashboardOnSecondMonitor();

            var openBrowserItem = new ToolStripMenuItem("Tarayıcıda Aç (127.0.0.1:17500)");
            openBrowserItem.Click += (s, e) => Process.Start(new ProcessStartInfo("http://127.0.0.1:" + Port) { UseShellExecute = true });

            var dumpItem = new ToolStripMenuItem("Sensör Listesini Kaydet (sensorler.txt)");
            dumpItem.Click += (s, e) => { if (_collector != null) _collector.RequestSensorDump(); };

            try { AlertsEnabled = !File.Exists(AlertFlagPath); } catch { }
            var alertItem = new ToolStripMenuItem("Isı Uyarıları (CPU 90° / GPU 85° / Disk 70°)") { Checked = AlertsEnabled };
            alertItem.Click += (s, e) => { SetAlertsEnabled(!AlertsEnabled); alertItem.Checked = AlertsEnabled; };

            var autoItem = new ToolStripMenuItem("Windows ile Başlat");
            autoItem.Click += (s, e) =>
            {
                bool want = !autoItem.Checked;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    bool ok = SetAutostart(want);
                    ShowBalloon(ok ? (want ? "Windows ile başlatma açıldı." : "Windows ile başlatma kapatıldı.") : "İşlem başarısız (yönetici izni gerekli).");
                });
                autoItem.Checked = want;
            };
            menu.Opening += (s, e) => { try { autoItem.Checked = IsAutostartEnabled(); } catch { } };

            menu.Items.Add(open2ndItem);
            menu.Items.Add("Edge ile Aç (daha çok RAM)", null, (s, e) => LaunchEdgeDashboard());
            menu.Items.Add("Tarayıcıda Aç", null, (s, e) => LaunchBrowserDashboard());
            menu.Items.Add(openBrowserItem);
            var sndItem = new ToolStripMenuItem("Ses Cihazı");
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
                if (sndItem.DropDownItems.Count == 0) sndItem.DropDownItems.Add(new ToolStripMenuItem("Cihaz bulunamadı") { Enabled = false });
            };
            sndItem.DropDownItems.Add("...");
            menu.Items.Add(sndItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autoItem);
            menu.Items.Add(alertItem);
            menu.Items.Add(dumpItem);

            if (!IsAdministrator())
            {
                var adminItem = new ToolStripMenuItem("Yönetici Olarak Yeniden Başlat (tam sensör erişimi)");
                adminItem.Click += (s, e) => RestartAsAdmin();
                menu.Items.Add(adminItem);
            }

            var exitItem = new ToolStripMenuItem("Çıkış");
            exitItem.Click += (s, e) =>
            {
                _trayIcon.Visible = false;
                Application.Exit();
            };

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => LaunchDashboardOnSecondMonitor();
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
                    g.DrawString("P", f, txt, 8, 5);
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
    }

    public class TelemetryCollector : IDisposable
    {
        private string _brandName = "ASUS";
        private string _moboModel = "Anakart Algılanıyor...";
        private string _moboChipset = "AMD Chipset";
        private string _moboBios = "";
        private string _cpuModel = "CPU Algılanıyor...";
        private int _physicalCores = 8;
        private double _cpuBaseMhz = 4201.0;
        private string _gpuModel = "GPU Algılanıyor...";
        private string _memSpeedText = "DDR5";
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
        private readonly Dictionary<string, double> _cachedDriveTemps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private List<ProcItem> _cachedTopProcs = new List<ProcItem>();
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

        private void DetectStaticHardware()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string manuf = (obj["Manufacturer"] ?? "").ToString().Trim();
                        string prod = (obj["Product"] ?? "").ToString().Trim();

                        if (manuf.IndexOf("ASUS", StringComparison.OrdinalIgnoreCase) >= 0) _brandName = "ASUS";
                        else if (manuf.IndexOf("MSI", StringComparison.OrdinalIgnoreCase) >= 0 || manuf.IndexOf("Micro-Star", StringComparison.OrdinalIgnoreCase) >= 0) _brandName = "MSI";
                        else if (manuf.IndexOf("GIGABYTE", StringComparison.OrdinalIgnoreCase) >= 0) _brandName = "GIGABYTE";
                        else if (manuf.IndexOf("ASRock", StringComparison.OrdinalIgnoreCase) >= 0) _brandName = "ASROCK";
                        else if (!string.IsNullOrEmpty(manuf)) _brandName = manuf.Split(new[] { ' ' })[0].ToUpperInvariant();

                        _moboModel = FormatMoboName(_brandName, prod);
                    }
                }
            }
            catch { }

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
                        _cpuModel = rawCpu;

                        if (obj["NumberOfCores"] != null)
                            _physicalCores = Math.Max(1, Convert.ToInt32(obj["NumberOfCores"]));
                        if (obj["MaxClockSpeed"] != null)
                        {
                            double maxClk = Convert.ToDouble(obj["MaxClockSpeed"]);
                            if (maxClk > 500) _cpuBaseMhz = maxClk;
                        }

                        if (_cpuModel.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string chipMatch = Regex.Match(_moboModel, @"\b([ABX]\d{3}E?)\b", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
                            if (!string.IsNullOrEmpty(chipMatch))
                                _moboChipset = string.Format("AMD {0}, AMD K19.6 FCH, AMD K19.6 IMC", chipMatch);
                            else
                                _moboChipset = "AMD Chipset, FCH & IMC";
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
                        if (obj["ConfiguredClockSpeed"] != null) clk = Convert.ToUInt32(obj["ConfiguredClockSpeed"]);
                        if (clk == 0 && obj["Speed"] != null) clk = Convert.ToUInt32(obj["Speed"]);
                        if (clk > 0)
                        {
                            string ddrType = clk >= 4400 ? "DDR5" : (clk >= 2133 ? "DDR4" : "DDR");
                            _memSpeedText = string.Format("{0}-{1}", ddrType, clk);
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
                    _detectedDriveNames = drives
                        .OrderBy(d => d.IndexOf("KINGSTON", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 :
                                      d.IndexOf("KIOXIA", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 :
                                      d.IndexOf("hynix", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 3)
                        .Take(4)
                        .ToList();
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
                        string match = _detectedDriveNames.FirstOrDefault(n =>
                            model.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf(model, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (match != null) _driveLetters[match] = letters;
                    }
                }
            }
            catch { }
        }

        private string FormatMoboName(string brand, string product)
        {
            if (string.IsNullOrEmpty(product)) return brand;
            string p = product;
            p = Regex.Replace(p, @"\bROG\b", "ROG", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bSTRIX\b", "Strix", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bGAMING\b", "Gaming", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bWIFI\b", "WiFi", RegexOptions.IgnoreCase);
            p = Regex.Replace(p, @"\bTUF\b", "TUF", RegexOptions.IgnoreCase);
            if (brand.Equals("ASUS", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("Asus", StringComparison.OrdinalIgnoreCase))
            {
                return "Asus " + p;
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

            if (string.IsNullOrEmpty(_gpuModel) || _gpuModel.StartsWith("GPU ") || _gpuModel.StartsWith("NVIDIA "))
            {
                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                    {
                        foreach (ManagementObject o in s.Get())
                        {
                            string name = (o["Name"] ?? "GPU").ToString().Trim();
                            string pnp = (o["PNPDeviceID"] ?? "").ToString().ToUpperInvariant();
                            Match subMatch = Regex.Match(pnp, @"SUBSYS_([0-9A-F]{8})");
                            if (subMatch.Success)
                            {
                                uint subsysId = Convert.ToUInt32(subMatch.Groups[1].Value, 16);
                                _gpuModel = ResolveSubvendorGpuName(name, subsysId);
                            }
                            else if (_gpuModel.StartsWith("GPU "))
                            {
                                _gpuModel = name;
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        private string ResolveSubvendorGpuName(string baseName, uint subsysId)
        {
            if (subsysId == 0xF29910B0)
                return "Gainward GeForce RTX 4080 Phoenix";

            ushort vendorId = (ushort)(subsysId & 0xFFFF);
            string cleanBase = baseName.Replace("NVIDIA ", "").Trim();
            switch (vendorId)
            {
                case 0x10B0: return "Gainward " + cleanBase;
                case 0x1043: return "ASUS ROG " + cleanBase;
                case 0x1462: return "MSI " + cleanBase;
                case 0x1458: return "Gigabyte " + cleanBase;
                case 0x1569: return "Palit " + cleanBase;
                case 0x19DA: return "Zotac " + cleanBase;
                case 0x3842: return "EVGA " + cleanBase;
                case 0x196E: return "PNY " + cleanBase;
                default: return baseName;
            }
        }

        private void InitPdhCounters()
        {
            try
            {
                if (PdhNative.PdhOpenQueryW(IntPtr.Zero, IntPtr.Zero, out _pdhQuery) == 0)
                {
                    PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _totalCpuUtilCounter);
                    if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out _diskReadCounter) != 0) _diskReadCounter = IntPtr.Zero;
                    if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _diskWriteCounter) != 0) _diskWriteCounter = IntPtr.Zero;
                    int threadsPerCore = Math.Max(1, Environment.ProcessorCount / Math.Max(1, _physicalCores));
                    for (int i = 0; i < _physicalCores; i++)
                    {
                        int firstLogical = i * threadsPerCore;
                        IntPtr handlePerf;
                        string pathPerf = string.Format("\\Processor Information(0,{0})\\% Processor Performance", firstLogical);
                        if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, pathPerf, IntPtr.Zero, out handlePerf) != 0)
                            handlePerf = IntPtr.Zero;
                        _corePerfCounters.Add(handlePerf);

                        // Çekirdeğin tüm SMT iş parçacıklarının ortalaması
                        var loads = new List<IntPtr>();
                        for (int t = 0; t < threadsPerCore; t++)
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

            // 2. Top 5 Memory Processes (every 4 seconds, disposed immediately)
            try
            {
                var procs = Process.GetProcesses();
                var top = procs
                    .Where(p => p.Id > 4)
                    .Select(p =>
                    {
                        double mb = 0;
                        string n = "";
                        try { mb = p.WorkingSet64 / (1024.0 * 1024.0); n = p.ProcessName; } catch { }
                        p.Dispose();
                        return new ProcItem { Name = n, MemMb = mb };
                    })
                    .Where(x => !string.IsNullOrEmpty(x.Name) && x.Name != "Memory Compression")
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ProcItem { Name = g.Key, MemMb = g.Sum(i => i.MemMb) })
                    .OrderByDescending(x => x.MemMb)
                    .Take(5)
                    .ToList();
                _cachedTopProcs = top;
            }
            catch { }
        }

        public string CollectJson()
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

            if ((DateTime.UtcNow - _lastAudioPoll).TotalSeconds >= 3.0)
            {
                _lastAudioPoll = DateTime.UtcNow;
                _cachedVolume = AudioVolumeReader.GetMasterVolumePercent();
                AudioSwitch.Refresh();
            }
            int soundVol = _cachedVolume;

            double cpuLoadPct = 0, diskReadMBs = double.NaN, diskWriteMBs = double.NaN;
            double maxCoreMhz = double.NaN;
            var cores = new List<CoreTelemetry>(_physicalCores);
            if (_pdhQuery != IntPtr.Zero && PdhNative.PdhCollectQueryData(_pdhQuery) == 0)
            {
                PdhNative.PDH_FMT_COUNTERVALUE val;
                if (PdhNative.PdhGetFormattedCounterValue(_totalCpuUtilCounter, PdhNative.PDH_FMT_DOUBLE, IntPtr.Zero, out val) == 0)
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
                    double cLoad = cpuLoadPct, sum = 0;
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
                        if ((reasons & (0x4UL | 0x80UL)) != 0) gpuThrottle.Add("GPU güç sınırı");
                        if ((reasons & (0x20UL | 0x40UL)) != 0) gpuThrottle.Add("GPU ısı sınırı");
                        else if ((reasons & 0x8UL) != 0) gpuThrottle.Add("GPU donanım yavaşlatma");
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

            string sensorSource = "Yerleşik: LHM + NVML | " + _fps.StatusText + (Program.IsAdministrator() ? "" : " | YÖNETİCİ DEĞİL") + (string.IsNullOrEmpty(LhmError) ? "" : " | LHM HATA: " + LhmError);

            // Okunamayan sensörler NaN kalır (JSON'da null); uydurma/tahmini değer gösterilmez.
            LhmReading lhm = GetLhmCached();

            double moboTemp = lhm.MoboTemp, cpuTemp = lhm.CpuTemp, cpuDiodeTemp = lhm.CpuDiodeTemp, dimmTemp = lhm.DimmTemp;
            double cpuFanRpm = lhm.CpuFan, chaFan1Rpm = lhm.Cha1, chaFan2Rpm = lhm.Cha2, aioPumpRpm = lhm.Pump;
            double cpuPowerW = lhm.CpuPower, cpuVolt = lhm.CpuVolt, socVolt = lhm.SocVolt, v12Volt = lhm.V12;

            // GPU: NVML varsa öncelikli, eksik kalanlar (ve NVIDIA dışı kartlar) LHM'den tamamlanır.
            if (double.IsNaN(gpuTemp)) gpuTemp = lhm.GpuTemp;
            gpuHotspotTemp = lhm.GpuHot;
            if (double.IsNaN(gpuPowerW)) gpuPowerW = lhm.GpuPower;
            gpuFanRpm = lhm.GpuFan;
            gpuVolt = lhm.GpuVolt;
            if (!_nvmlAvailable)
            {
                if (!double.IsNaN(lhm.GpuLoad)) gpuLoadPct = lhm.GpuLoad;
                if (!double.IsNaN(lhm.GpuClock)) gpuClockMhz = lhm.GpuClock;
                if (!double.IsNaN(lhm.GpuMemClock)) gpuMemClockMhz = lhm.GpuMemClock;
                if (!double.IsNaN(lhm.VramUsedMb)) gpuVramUsedMb = lhm.VramUsedMb;
                if (!double.IsNaN(lhm.VramTotalMb)) gpuVramTotalMb = lhm.VramTotalMb;
            }

            // CPU çekirdek saatleri: LHM (MSR, gerçek efektif saat) varsa onu kullan, yoksa PDH tahmini kalır.
            foreach (CoreTelemetry c in cores)
            {
                double real;
                if (lhm.CoreClocks.TryGetValue(c.Index, out real)) c.ClockMhz = Math.Round(real);
            }
            maxCoreMhz = cores.Count > 0 ? cores.Max(c => c.ClockMhz) : double.NaN;

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

            if (!double.IsNaN(cpuTemp) && cpuTemp >= 93) gpuThrottle.Add("CPU ısı sınırı");
            string throttleText = string.Join(", ", gpuThrottle);

            // Uyarılar (tray bildirimi, 5 dk bekleme süresiyle)
            if (cpuTemp >= 90) Program.Alert("cpu", "CPU sıcaklığı yüksek: " + Math.Round(cpuTemp) + "°C");
            if (gpuTemp >= 85) Program.Alert("gpu", "GPU sıcaklığı yüksek: " + Math.Round(gpuTemp) + "°C");
            foreach (var dr in drives)
                if (dr.TempC >= 70) Program.Alert("disk" + dr.Name, dr.Name + " sıcaklığı yüksek: " + Math.Round(dr.TempC) + "°C");

            double netUpKbs = 0, netDownKbs = 0, netUpTotalMb = 0, netDownTotalMb = 0;
            ReadNetworkStats(out netUpKbs, out netDownKbs, out netUpTotalMb, out netDownTotalMb);

            if (Program.Dash != null)
            {
                Program.LastSnap = new Snap
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
                    Cores = cores.ToArray(), Drives = drives.ToArray(), TopProcs = _cachedTopProcs.ToArray()
                };
            }

            StringBuilder sb = new StringBuilder(3072);
            sb.Append('{');
            AppendJsonStr(sb, "sensorSource", sensorSource);
            AppendJsonStr(sb, "brandName", _brandName);
            AppendJsonStr(sb, "moboModel", _moboModel);
            AppendJsonStr(sb, "moboChipset", _moboChipset);
            AppendJsonStr(sb, "moboBios", _moboBios);
            AppendJsonStr(sb, "cpuModel", _cpuModel);
            AppendJsonStr(sb, "gpuModel", _gpuModel);
            AppendJsonStr(sb, "gpuBusType", gpuBusType);
            AppendJsonStr(sb, "memSpeedText", _memSpeedText);
            AppendJsonStr(sb, "uptime", uptimeStr);
            AppendJsonNum(sb, "soundVolume", soundVol < 0 ? double.NaN : soundVol);
            AppendJsonNum(sb, "processCount", processCount);
            AppendJsonNum(sb, "pingMs", _pingMs < 0 ? double.NaN : _pingMs);

            AppendJsonNum(sb, "memTotalGb", memTotalGb);
            AppendJsonNum(sb, "memUsedGb", memUsedGb);
            AppendJsonNum(sb, "memFreeGb", memFreeGb);
            AppendJsonNum(sb, "memLoadPct", memLoadPct);
            AppendJsonNum(sb, "dimmTemp", dimmTemp);

            AppendJsonNum(sb, "moboTemp", moboTemp);
            AppendJsonNum(sb, "socVolt", socVolt);
            AppendJsonNum(sb, "v12Volt", v12Volt);

            AppendJsonNum(sb, "cpuLoadPct", cpuLoadPct);
            AppendJsonNum(sb, "cpuTemp", cpuTemp);
            AppendJsonNum(sb, "cpuTempMin", _cpuTempMin < 500 ? _cpuTempMin : cpuTemp);
            AppendJsonNum(sb, "cpuTempMax", _cpuTempMax);
            AppendJsonNum(sb, "cpuDiodeTemp", cpuDiodeTemp);
            AppendJsonNum(sb, "cpuClockMhz", maxCoreMhz);
            AppendJsonNum(sb, "cpuPeakMhz", _cpuPeakMhz);
            AppendJsonNum(sb, "cpuFanRpm", cpuFanRpm);
            AppendJsonNum(sb, "chaFan1Rpm", chaFan1Rpm);
            AppendJsonNum(sb, "chaFan2Rpm", chaFan2Rpm);
            AppendJsonNum(sb, "aioPumpRpm", aioPumpRpm);

            sb.Append("\"cores\":[");
            for (int i = 0; i < cores.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"index\":").Append(cores[i].Index)
                  .Append(",\"clockMhz\":").Append(JsonNum(cores[i].ClockMhz, "0"))
                  .Append(",\"loadPct\":").Append(JsonNum(cores[i].LoadPct, "0.0"))
                  .Append('}');
            }
            sb.Append("],");

            AppendJsonNum(sb, "gpuLoadPct", gpuLoadPct);
            AppendJsonNum(sb, "gpuTemp", gpuTemp);
            AppendJsonNum(sb, "gpuTempMin", _gpuTempMin < 500 ? _gpuTempMin : gpuTemp);
            AppendJsonNum(sb, "gpuTempMax", _gpuTempMax);
            AppendJsonNum(sb, "gpuHotspotTemp", gpuHotspotTemp);
            AppendJsonNum(sb, "gpuClockMhz", gpuClockMhz);
            AppendJsonNum(sb, "gpuMemClockMhz", gpuMemClockMhz);
            AppendJsonNum(sb, "gpuPeakMhz", _gpuPeakMhz);
            AppendJsonNum(sb, "gpuVramUsedMb", gpuVramUsedMb);
            AppendJsonNum(sb, "gpuVramTotalMb", gpuVramTotalMb);
            AppendJsonNum(sb, "gpuFanRpm", gpuFanRpm);
            AppendJsonNum(sb, "gpuFanPct", gpuFanPct);
            AppendJsonNum(sb, "fps", fps);
            AppendJsonNum(sb, "frameTimeMs", frameTimeMs);
            AppendJsonStr(sb, "activeGame", activeGame ?? "");
            AppendJsonNum(sb, "fpsLow1", fpsLow1);
            AppendJsonStr(sb, "throttle", throttleText);
            AppendJsonNum(sb, "diskReadMBs", diskReadMBs);
            AppendJsonNum(sb, "diskWriteMBs", diskWriteMBs);

            sb.Append("\"drives\":[");
            for (int i = 0; i < drives.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(EscapeJson(drives[i].Name))
                  .Append("\",\"tempC\":").Append(JsonNum(drives[i].TempC, "0"))
                  .Append(",\"mount\":\"").Append(EscapeJson(drives[i].Mount ?? ""))
                  .Append("\",\"usedPct\":").Append(JsonNum(drives[i].UsedPct, "0.0"))
                  .Append(",\"freeGb\":").Append(JsonNum(drives[i].FreeGb, "0.0"))
                  .Append(",\"totalGb\":").Append(JsonNum(drives[i].TotalGb, "0.0"))
                  .Append('}');
            }
            sb.Append("],");

            sb.Append("\"topProcs\":[");
            for (int i = 0; i < _cachedTopProcs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(EscapeJson(_cachedTopProcs[i].Name))
                  .Append("\",\"memMb\":").Append(_cachedTopProcs[i].MemMb.ToString("0", CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("],");

            AppendJsonNum(sb, "cpuPowerW", cpuPowerW);
            AppendJsonNum(sb, "cpuPeakW", _cpuPeakW);
            AppendJsonNum(sb, "cpuVolt", cpuVolt);
            AppendJsonNum(sb, "gpuPowerW", gpuPowerW);
            AppendJsonNum(sb, "gpuPeakW", _gpuPeakW);
            AppendJsonNum(sb, "gpuVolt", gpuVolt);

            AppendJsonStr(sb, "ipAddress", _ipAddress);
            AppendJsonNum(sb, "netUpKbs", netUpKbs);
            AppendJsonNum(sb, "netDownKbs", netDownKbs);
            AppendJsonNum(sb, "netUpTotalMb", netUpTotalMb);
            AppendJsonNum(sb, "netDownTotalMb", netDownTotalMb, isLast: true);
            sb.Append('}');

            return sb.ToString();
        }

        private sealed class LhmReading
        {
            public double MoboTemp = double.NaN, CpuTemp = double.NaN, CpuDiodeTemp = double.NaN;
            public double DimmTemp = double.NaN;
            public double CpuFan = double.NaN, Cha1 = double.NaN, Cha2 = double.NaN, Pump = double.NaN;
            public double CpuVolt = double.NaN, SocVolt = double.NaN, V12 = double.NaN;
            public double CpuPower = double.NaN;
            public double GpuTemp = double.NaN, GpuHot = double.NaN, GpuFan = double.NaN, GpuVolt = double.NaN, GpuPower = double.NaN;
            public double GpuLoad = double.NaN, GpuClock = double.NaN, GpuMemClock = double.NaN;
            public double VramUsedMb = double.NaN, VramTotalMb = double.NaN;
            public HardwareType GpuType = HardwareType.Motherboard;
            public bool GpuChosen;
            public readonly Dictionary<int, double> CoreClocks = new Dictionary<int, double>();
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

        private void DumpSensors()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("ProSystemMonitor sensör listesi - " + DateTime.Now);
                foreach (IHardware hw in _lhmComputer.Hardware) DumpHw(hw, sb, 0);
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensorler.txt");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Program.ShowBalloon("Kaydedildi: " + path);
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
                if (pollMemNow) _lastMemPoll = DateTime.UtcNow;

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

            try { hw.Update(); } catch { return; }

            switch (t)
            {
                case HardwareType.Cpu: ReadCpu(hw, r); break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel: ReadGpu(hw, r); break;
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
                        else if (Has(n, "SVI2") && Has(n, "Core") || n.Equals("CPU Core", StringComparison.OrdinalIgnoreCase) || n.Equals("Vcore", StringComparison.OrdinalIgnoreCase))
                            svi2Volt = v;
                        else if (Has(n, "Core")) { if (double.IsNaN(vidMax) || v > vidMax) vidMax = v; }
                        break;
                    case SensorType.Clock:
                        {
                            // "Core #1", "CPU Core #1" ... (Bus Speed gibi sensörleri atla)
                            Match m = Regex.Match(n, @"^(?:CPU )?Core #(\d+)$", RegexOptions.IgnoreCase);
                            if (m.Success && v > 100)
                                r.CoreClocks[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = v;
                        }
                        break;
                }
            }
            if (double.IsNaN(r.CpuTemp)) r.CpuTemp = !double.IsNaN(coreMaxTemp) ? coreMaxTemp : coreAvgTemp;
            r.CpuDiodeTemp = !double.IsNaN(ccdTemp) ? ccdTemp : coreMaxTemp;
            r.CpuVolt = !double.IsNaN(svi2Volt) ? svi2Volt : vidMax;
        }

        private void ReadGpu(IHardware hw, LhmReading r)
        {
            // Birden çok GPU varsa (iGPU + dGPU) NVIDIA'yı, yoksa ilk GPU'yu kullan.
            if (r.GpuChosen)
            {
                if (!(hw.HardwareType == HardwareType.GpuNvidia && r.GpuType != HardwareType.GpuNvidia)) return;
                r.ResetGpu();
            }
            r.GpuChosen = true;
            r.GpuType = hw.HardwareType;

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
                        else if (n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) r.GpuTemp = v;
                        break;
                    case SensorType.Power:
                        if (Has(n, "Package") || n.Equals("GPU Power", StringComparison.OrdinalIgnoreCase)) r.GpuPower = v;
                        else if (double.IsNaN(r.GpuPower) && n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) r.GpuPower = v;
                        break;
                    case SensorType.Fan:
                        if (double.IsNaN(r.GpuFan) || v > r.GpuFan) r.GpuFan = v;
                        break;
                    case SensorType.Voltage:
                        if (v > 0 && v < 2.0 && (double.IsNaN(r.GpuVolt) || n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))) r.GpuVolt = v;
                        break;
                    case SensorType.Load:
                        if (n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) r.GpuLoad = v;
                        break;
                    case SensorType.Clock:
                        if (n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) r.GpuClock = v;
                        else if (n.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase)) r.GpuMemClock = v;
                        break;
                    case SensorType.SmallData:
                        if (n.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase) || n.Equals("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase)) r.VramUsedMb = v;
                        else if (n.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase) || n.Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase)) r.VramTotalMb = v;
                        break;
                }
            }
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
            // --- Anakart sıcaklığı: isimle öncelik, yoksa makul aralıktaki ilk CPU olmayan sensör ---
            string[] prefer = { "Motherboard", "System", "MB", "Board", "Chipset", "PCH" };
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
                    if (!Has(kv.Key, "CPU") && !Has(kv.Key, "Core") && !Has(kv.Key, "Water") && kv.Value > 15 && kv.Value < 90) { r.MoboTemp = kv.Value; break; }
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
                else if (double.IsNaN(r.CpuVolt) && (Has(n, "Vcore") || n.Equals("CPU Core", StringComparison.OrdinalIgnoreCase)) && v > 0 && v < 2) r.CpuVolt = v;
            }
        }

        private void ReadStorage(IHardware hw)
        {
            double best = double.NaN;
            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                double? vn = Val(s);
                if (!vn.HasValue || vn.Value <= 0) continue;
                if (double.IsNaN(best) || (s.Name ?? "").Equals("Temperature", StringComparison.OrdinalIgnoreCase)) best = vn.Value;
            }
            if (!double.IsNaN(best)) _cachedDriveTemps[hw.Name] = best;
        }

        private void ReadMemory(IHardware hw)
        {
            // DIMM sıcaklık sensörü olan modüller (varsa) için en yüksek değer.
            double best = double.NaN;
            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                double? vn = Val(s);
                if (vn.HasValue && vn.Value > 0 && (double.IsNaN(best) || vn.Value > best)) best = vn.Value;
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
                names = _cachedDriveTemps.Keys.Take(4).ToList();

            foreach (string dName in names)
            {
                if (_detectedDriveNames.Count > 3 && dName.StartsWith("ST1000", StringComparison.OrdinalIgnoreCase))
                    continue;
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

        private sealed class PidStat
        {
            public readonly Queue<long> Present = new Queue<long>();   // DXGI / D3D9
            public readonly Queue<long> Kernel = new Queue<long>();    // DxgKrnl (yedek)
            public readonly Queue<long> Hist = new Queue<long>();      // son 30 sn (1% low için)
            public long LastSeen;
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
                EtwNative.EnableTraceEx2(_session, ref g1, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
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

        private void OnEvent(IntPtr rec)
        {
            try
            {
                // EVENT_RECORD: ProcessId @12, ProviderId.Data1 @24, EventDescriptor.Id @40
                int provider = Marshal.ReadInt32(rec, 24);
                int id = Marshal.ReadInt16(rec, 40) & 0xFFFF;
                bool present = false, kernel = false;
                if (provider == unchecked((int)0xCA11C036)) present = (id == 42);        // DXGI Present_Start
                else if (provider == 0x783ACA0A) present = (id == 1);                    // D3D9 Present_Start
                else if (provider == unchecked((int)0x802EC45A)) kernel = (id == 184);   // DxgKrnl Present_Info
                if (!present && !kernel) return;

                uint pid = (uint)Marshal.ReadInt32(rec, 12);
                if (pid == 0 || pid == 4) return;
                Interlocked.Increment(ref _eventCount);
                long now = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    PidStat st;
                    if (!_stats.TryGetValue(pid, out st)) { st = new PidStat(); _stats[pid] = st; }
                    st.LastSeen = now;
                    Queue<long> q = present ? st.Present : st.Kernel;
                    q.Enqueue(now);
                    while (q.Count > 4000) q.Dequeue();
                    if (present)
                    {
                        st.Hist.Enqueue(now);
                        while (st.Hist.Count > 40000) st.Hist.Dequeue();
                    }
                }
            }
            catch { }
        }

        private static readonly string[] GameDirs =
        {
            "\\steamapps\\common\\", "\\epic games\\", "\\gog galaxy\\games\\", "\\gog games\\", "\\riot games\\",
            "\\ubisoft game launcher\\games\\", "\\ea games\\", "\\origin games\\", "\\xboxgames\\",
            "\\rockstar games\\", "\\battle.net\\", "\\blizzard\\", "\\games\\"
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

        /// <summary>Son 1 saniyedeki Present sayısından FPS ve kare süresi; oyun/uygulama adını döndürür.</summary>
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
                    fgFullscreen = rc.Left <= sb.Left && rc.Top <= sb.Top && rc.Right >= sb.Right && rc.Bottom >= sb.Bottom;
                }
            }
            catch { }

            long now = Stopwatch.GetTimestamp();
            long window = Stopwatch.Frequency;
            double bestFps = 0, bestMs = 0;
            uint bestPid = 0;
            string bestName = "";
            bool bestIsFg = false;
            var dead = new List<uint>();

            lock (_gate)
            {
                foreach (var kv in _stats)
                {
                    PidStat st = kv.Value;
                    Prune(st.Present, now, window);
                    Prune(st.Kernel, now, window);
                    Prune(st.Hist, now, 30 * window);
                    // DXGI/D3D9 varsa onu, yoksa (Vulkan/OpenGL gibi) çekirdek olaylarını kullan.
                    Queue<long> q = st.Present.Count > 0 ? st.Present : st.Kernel;
                    if (q.Count == 0)
                    {
                        if (now - st.LastSeen > 10 * window) dead.Add(kv.Key);
                        continue;
                    }

                    string name = NameOf(kv.Key);
                    if (name.Length == 0 || Ignored.Contains(name) || kv.Key == (uint)Process.GetCurrentProcess().Id) continue;

                    bool isFg = kv.Key == fgPid;
                    // Yalnızca oyunlar: bilinen oyun klasörlerindeki exe'ler veya ön planda tam ekran kaplayan pencere.
                    if (!IsGamePath(kv.Key) && !(isFg && fgFullscreen)) continue;

                    double fps, ms;
                    Rate(q, now, window, out fps, out ms);
                    if (fps < 1 || fps > 1000) continue;

                    if ((isFg && !bestIsFg) || (isFg == bestIsFg && fps > bestFps))
                    {
                        bestFps = fps; bestMs = ms; bestName = name; bestIsFg = isFg; bestPid = kv.Key;
                    }
                }
                foreach (uint d in dead) { _stats.Remove(d); _names.Remove(d); _gamePath.Remove(d); }

                PidStat bs;
                if (bestPid != 0 && _stats.TryGetValue(bestPid, out bs) && bs.Hist.Count > 120)
                {
                    // %1 low: en yavaş %1'lik karenin süresinden hesaplanan FPS (son 30 sn)
                    var gaps = new List<double>(bs.Hist.Count);
                    long prev = 0;
                    foreach (long t in bs.Hist)
                    {
                        if (prev != 0)
                        {
                            double g = (t - prev) * 1000.0 / Stopwatch.Frequency;
                            if (g > 0 && g < 2000) gaps.Add(g);
                        }
                        prev = t;
                    }
                    if (gaps.Count > 100)
                    {
                        gaps.Sort();
                        double p99 = gaps[Math.Min(gaps.Count - 1, (int)Math.Ceiling(gaps.Count * 0.99) - 1)];
                        if (p99 > 0) low1 = 1000.0 / p99;
                    }
                }
            }

            game = bestName;
            frameMs = bestMs;
            return Math.Round(bestFps);
        }

        private static void Prune(Queue<long> q, long now, long window)
        {
            while (q.Count > 0 && now - q.Peek() > window) q.Dequeue();
        }

        private static void Rate(Queue<long> q, long now, long window, out double fps, out double ms)
        {
            // Yükleme ekranlarındaki olay patlamalarını (kısa sürede çok Present) şişirmemek için
            // süreye bölmek yerine son 1 saniyedeki kare sayısı kullanılır.
            fps = q.Count;
            ms = fps > 0 ? 1000.0 / fps : 0;
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
