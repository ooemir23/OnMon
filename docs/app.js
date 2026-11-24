(function () {
  var T = {
    tr: {
      nav_feat: "Özellikler",
      nav_cmp: "Farkı Ne?",
      nav_start: "Kurulum",
      nav_dl: "İndir v60",
      hero_badge: "⚡ Saf Native C# + GDI+ · Electron Yok · Tamamen Ücretsiz",
      hero_title: "Bilgisayarının Gerçek Nabzı, Tüy Kadar Hafif.",
      hero_sub: "İkinci monitörünüz, kasa içi mini ekranınız veya doğrudan oyununuz için ultra düşük CPU kullanımlı profesyonel donanım izleme, gerçek %1 Low FPS sayacı, görsel düzen stüdyosu ve ses/medya kontrol merkezi.",
      cta_dl: "⬇ Hemen İndir (v60 .exe)",
      cta_gh: "GitHub Kaynak Kodu",
      st_cpu: "Boşta CPU",
      st_thm: "Düzen & Tema",
      st_fps: "ETW Gerçek FPS",
      st_drv: "İmzalı PawnIO",
      sim_dash: "Pano",
      sim_hud: "Mini HUD",
      sim_fan: "Fan 1120 RPM",
      sim_fps_h: "OYUN · REALTIME FPS",
      sim_med_h: "MEDYA & AĞ",
      sim_hud_note: "✨ Tek monitör veya tam ekran oyunlar için WS_EX_TRANSPARENT katmanıyla fare tıklamalarını oyuna geçirir; köşesi ve opaklığı özelleştirilebilir.",
      feat_t: "Her Şey Tek Hafif Uygulamada",
      feat_s: "Ağır tarayıcı motorları veya ücretli lisanslar olmadan, donanım sensörlerinden düzen tasarımına kadar ihtiyacınız olan her şey.",
      f1_t: "Evrensel Donanım Desteği",
      f1_d: "ASUS, MSI, Gigabyte, Lenovo, HP, Dell, Monster, Casper... Tüm PC ve dizüstü markaları, NVIDIA/AMD/Intel Arc GPU'lar ve Intel hibrit P/E çekirdekleri ile tam uyumlu.",
      f2_t: "Windows 11 Bellek Bütünlüğü Uyumlu",
      f2_d: "Eski güvensiz WinRing0 sürücülerine bağımlı kalmaz. LibreHardwareMonitor 0.9.6 + imzalı PawnIO ve NVML ile HVCI açıkken bile CPU watt ve anakart sensörlerini okur.",
      f3_t: "Gerçek %1 Low FPS & Mini HUD",
      f3_d: "PresentMon / ETW tabanlı FPS ve gerçek %1 Low takibi + otomatik oyun oturumu CSV kaydı. Tek monitörlü sistemlerde tıklamayı geçiren Mini Overlay HUD desteği.",
      f4_t: "3 Panelli Düzen Stüdyosu",
      f4_d: "Sürükle-bırak taşıma, mıknatıslı hizalama, Ctrl+Z geri alma, özel arka plan resmi/kasa ekranı çözünürlüğü (800×480, 1920×1080 vb.) ve widget başına tam renk/stil ayarı.",
      f5_t: "Topluluk Galerisi & .onmon Paylaşımı",
      f5_d: "Tasarımlarınızı tüm görselleri ve önizlemesiyle tek bir .onmon dosyasına aktarın, paylaşın veya GitHub tabanlı Topluluk Galerisi'nden tek tıkla indirin.",
      f6_t: "Ses Mikseri & Medya Kontrolü",
      f6_d: "Alt şeritten veya widget üzerinden tek tıkla hoparlör/kulaklık değiştirin, ses & mikrofon seviyesini kontrol edin ve Spotify/YouTube/VLC parçalarını panodan yönetin.",
      f7_t: "Otomatik Gece Modu & Birimler",
      f7_d: "Belirlediğiniz saatlerde parlaklığı otomatik kısan Gece Modu, °C/°F sıcaklık birimi, 12s/24s saat, hava durumu ve isteğe bağlı namaz vakti göstergesi.",
      f8_t: "Canlı Tepsi Simgesi & Özel Uyarılar",
      f8_d: "Bildirim tepsisinde canlı CPU/GPU °C rozeti ve anlık sensör özeti. Özelleştirilebilir CPU, GPU, Disk, RAM, Pil ve Ping uyarı eşikleri + sesli ikaz.",
      f9_t: "SMART Sağlık, Wi-Fi, Peak & Yedekleme",
      f9_d: "SSD SMART sağlık %, native Wi-Fi SSID/sinyal, kalan pil süresi/Watt, oturum Min/Maks (Peak) takibi ve tek tıkla .onmonbak yedekleme/geri yükleme.",
      cmp_t: "Diğer Uygulamalardan Farkı Ne?",
      cmp_s: "Neden birden fazla araç kurmak yerine tek başına OnMon tercih edilir?",
      c_col: "Özellik",
      c_r1: "Fiyat & Açık Kaynak OTA",
      c_free: "Tamamen Ücretsiz + OTA",
      c_paid: "Ücretli Lisans",
      c_closed: "Ücretsiz (Kapalı)",
      c_ok_skin: "Ücretsiz (Eklenti Gerekir)",
      c_r2: "Win 11 Bellek Bütünlüğü (PawnIO)",
      c_yes: "Yerleşik + 1 Tık Kurulum",
      c_part: "Kısmi / Sürücü Bağımlı",
      c_ext: "Harici HWiNFO/LHM Gerekir",
      c_r3: "İkinci Monitör + Oyun İçi Mini HUD",
      c_both: "İkisi de Tek Uygulamada",
      c_panel_only: "Yalnız SensorPanel",
      c_osd_only: "Yalnız OSD",
      c_desk_only: "Yalnız Masaüstü",
      c_r4: "Sürükle-Bırak Düzen Stüdyosu & Galeri",
      c_studio: "3 Panelli Stüdyo + Topluluk",
      c_old_ed: "Eski Piksel Editörü",
      c_ini: ".ini Dosya Düzenleme",
      c_r5: "Ses Cihazı, Mikrofon, Medya & Win+D",
      c_yes2: "Tam Entegre",
      c_plugin: "Eklenti Gerekir",
      st_t: "3 Adımda Hazır",
      st_s: "Kurulum sihirbazı birkaç saniyede dilinizi, temanızı, düzeninizi ve monitörünüzü ayarlar.",
      s1_t: "İndirin ve Çalıştırın",
      s1_d: "ProSystemMonitor.exe dosyasını başlatın. İlk açılışta 3 adımlı İlk Kurulum Sihirbazı dilinizi (TR/EN), şablonunuzu ve ekranınızı sorar.",
      s2_t: "PawnIO Sürücüsünü Doğrulayın",
      s2_d: "Sihirbazdan veya Ayarlar > Tanılama bölümünden tek tıkla imzalı PawnIO sürücüsünü kurarak tüm CPU watt ve anakart fan sensörlerini açın.",
      s3_t: "Özelleştirin ve Paylaşın",
      s3_d: "Düzen Galerisi'nden topluluk şablonları indirin veya Düzen Stüdyosu'nda kendi ekranınızı tasarlayıp .onmon olarak paylaşın.",
      ban_t: "Tamamen Ücretsiz, Açık Kaynak ve OTA Güncel",
      ban_d: "OnMon yeni özellikleri ve topluluk düzenlerini doğrudan GitHub üzerinden alır. İstediğiniz zaman Ayarlar > Güncelleme sayfasından tek tıkla güncelleyin."
    },
    en: {
      nav_feat: "Features",
      nav_cmp: "Why Us?",
      nav_start: "Quick Start",
      nav_dl: "Download v60",
      hero_badge: "⚡ Pure Native C# + GDI+ · Zero Electron · 100% Free",
      hero_title: "Your PC's True Pulse, Featherweight.",
      hero_sub: "Ultra-low CPU hardware telemetry, true 1% Low FPS counter, visual 3-panel Layout Studio, and audio/media command center built for secondary displays, case screens, and in-game HUDs.",
      cta_dl: "⬇ Download Now (v60 .exe)",
      cta_gh: "GitHub Repository",
      st_cpu: "Idle CPU",
      st_thm: "Layouts & Themes",
      st_fps: "ETW True FPS",
      st_drv: "Signed PawnIO",
      sim_dash: "Dashboard",
      sim_hud: "Mini HUD",
      sim_fan: "Fan 1120 RPM",
      sim_fps_h: "GAME · REALTIME FPS",
      sim_med_h: "MEDIA & NETWORK",
      sim_hud_note: "✨ Built with WS_EX_TRANSPARENT so clicks pass straight through to your game on single-monitor setups; corner & opacity customizable.",
      feat_t: "Everything in One Featherlight App",
      feat_s: "No bloated Chromium/Electron overhead or paid license walls — everything from hardware sensors to a full layout designer.",
      f1_t: "Universal Hardware Support",
      f1_d: "Works seamlessly across ASUS, MSI, Gigabyte, Lenovo, HP, Dell, Acer, Razer, NVIDIA/AMD/Intel Arc GPUs, Intel hybrid P/E cores, and laptop batteries.",
      f2_t: "Windows 11 Core Isolation Ready",
      f2_d: "No longer blocked by HVCI/WinRing0 restrictions. Uses LibreHardwareMonitor 0.9.6 + signed PawnIO driver and NVML for full CPU wattage and SuperIO fan telemetry.",
      f3_t: "True 1% Low FPS & Mini HUD",
      f3_d: "ETW/PresentMon-grade FPS and real 1% Low metrics with automatic game session CSV logging, plus a click-through in-game Mini Overlay HUD.",
      f4_t: "3-Panel Visual Layout Studio",
      f4_d: "Drag-and-drop canvas with snap alignment guides, Ctrl+Z undo, custom chassis screen sizes (800×480, 1920×1080, etc.), and per-widget styling.",
      f5_t: "Community Gallery & .onmon Sharing",
      f5_d: "Bundle any custom dashboard with embedded images and live preview thumbnail into a single .onmon file, or download community layouts in 1 click.",
      f6_t: "Audio Switcher & Media Player",
      f6_d: "Switch speakers/headsets in one click, control volume & mic mute from your screen, and play/pause/skip Spotify, YouTube, or VLC right from the dashboard.",
      f7_t: "Auto Night Mode & Regional Units",
      f7_d: "Scheduled Night Mode dims your display automatically at bedtime, alongside °C / °F switching, 12h / 24h clocks, local weather, and optional prayer times.",
      f8_t: "Live Tray Temp Icon & Custom Alerts",
      f8_d: "Glance at live CPU/GPU temperature directly in your Windows system tray, with customizable thresholds for CPU, GPU, Disk, RAM, Battery, and Ping.",
      f9_t: "SMART Health, Wi-Fi, Peaks & Backup",
      f9_d: "Read SSD SMART health %, native Wi-Fi SSID/signal, remaining battery runtime & Wattage, session Min/Max peaks, and export/restore all configs in one .onmonbak file.",
      cmp_t: "How Does It Compare?",
      cmp_s: "Why install multiple heavy tools when one lightweight binary handles sensors, layouts, FPS, and audio?",
      c_col: "Feature",
      c_r1: "Price & Open-Source OTA",
      c_free: "100% Free + Built-in OTA",
      c_paid: "Paid License",
      c_closed: "Free (Closed Source)",
      c_ok_skin: "Free (Needs Plugins)",
      c_r2: "Win 11 Core Isolation (PawnIO)",
      c_yes: "Built-in + 1-Click Install",
      c_part: "Partial / Driver Dependent",
      c_ext: "Needs HWiNFO / LHM",
      c_r3: "2nd Screen Dashboard + In-Game Mini HUD",
      c_both: "Both in One App",
      c_panel_only: "SensorPanel Only",
      c_osd_only: "OSD Only",
      c_desk_only: "Desktop Only",
      c_r4: "Drag & Drop Studio + Online Gallery",
      c_studio: "3-Panel Studio + Community",
      c_old_ed: "Legacy Pixel Editor",
      c_ini: "Manual .ini Editing",
      c_r5: "Audio Switcher, Mic, Media & Win+D Safe",
      c_yes2: "Fully Integrated",
      c_plugin: "Requires Scripts",
      st_t: "Ready in 3 Steps",
      st_s: "The First-Run Onboarding Wizard configures your language, starter layout, theme, and target display in seconds.",
      s1_t: "Download & Launch",
      s1_d: "Run ProSystemMonitor.exe. On first launch, the 3-step Setup Wizard asks your preferred language (TR/EN), starter layout, and monitor.",
      s2_t: "Verify Signed PawnIO Driver",
      s2_d: "Optionally install the signed PawnIO driver in one click from the wizard or Diagnostics tab to unlock full CPU package power and board fan headers.",
      s3_t: "Customize & Share",
      s3_d: "Pick from 14 built-in presets, browse the Community Gallery, or design your own canvas in Layout Studio and share it as a .onmon file.",
      ban_t: "Free Forever, Open Source & Always Updated",
      ban_d: "Fetches updates and community templates directly from GitHub. Update seamlessly inside Settings > Updates with SHA-256 verification."
    }
  };

  var themes = {
    rog:   { a1: "#ff4a00", a2: "#ffa21a", panel: "#0d0f15", line: "#2c2c34" },
    frost: { a1: "#00c8ff", a2: "#7df9ff", panel: "#081420", line: "#103048" },
    cyber: { a1: "#fcee0a", a2: "#00f0ff", panel: "#0c1326", line: "#1b4460" },
    nzxt:  { a1: "#8b5cf6", a2: "#d08bff", panel: "#12152a", line: "#3a2f66" },
    oled:  { a1: "#38bdf8", a2: "#a5f3fc", panel: "#040507", line: "#1e222b" }
  };

  function setLang(lang) {
    lang = (lang === "en") ? "en" : "tr";
    document.documentElement.lang = lang;
    try { localStorage.setItem("onmon_web_lang", lang); } catch (e) {}
    var dict = T[lang];
    document.querySelectorAll("[data-i18n]").forEach(function (el) {
      var k = el.getAttribute("data-i18n");
      if (dict[k]) el.textContent = dict[k];
    });
    document.querySelectorAll(".lang-btn").forEach(function (b) {
      b.classList.toggle("active", b.getAttribute("data-lang") === lang);
    });
  }

  document.querySelectorAll(".lang-btn").forEach(function (b) {
    b.addEventListener("click", function () { setLang(b.getAttribute("data-lang")); });
  });

  var savedLang = null;
  try { savedLang = localStorage.getItem("onmon_web_lang"); } catch (e) {}
  if (!savedLang) {
    savedLang = (navigator.language || "").toLowerCase().indexOf("tr") === 0 ? "tr" : "en";
  }
  setLang(savedLang);

  // Simulator theme switcher
  var shell = document.getElementById("simShell");
  document.querySelectorAll(".swatch").forEach(function (sw) {
    sw.addEventListener("click", function () {
      var k = sw.getAttribute("data-theme");
      var t = themes[k] || themes.rog;
      shell.style.setProperty("--sim-a1", t.a1);
      shell.style.setProperty("--sim-a2", t.a2);
      shell.style.setProperty("--sim-panel", t.panel);
      shell.style.setProperty("--sim-line", t.line);
      document.querySelectorAll(".swatch").forEach(function (x) { x.classList.toggle("active", x === sw); });
    });
  });

  // Simulator mode switcher (Dash vs Mini HUD)
  var dashEl = document.getElementById("simDash");
  var hudEl = document.getElementById("simHud");
  document.querySelectorAll(".mode-btn").forEach(function (mb) {
    mb.addEventListener("click", function () {
      var m = mb.getAttribute("data-simmode");
      document.querySelectorAll(".mode-btn").forEach(function (x) { x.classList.toggle("active", x === mb); });
      if (m === "hud") { dashEl.classList.add("hidden"); hudEl.classList.remove("hidden"); }
      else { hudEl.classList.add("hidden"); dashEl.classList.remove("hidden"); }
    });
  });

  // Animated simulated telemetry
  var tick = 0;
  setInterval(function () {
    tick++;
    var cpuL = 22 + Math.round(Math.sin(tick * 0.7) * 9 + 7);
    var cpuT = 49 + Math.round(cpuL * 0.18);
    var gpuL = 68 + Math.round(Math.cos(tick * 0.5) * 14);
    var gpuT = 57 + Math.round(gpuL * 0.08);
    var fps = 132 + Math.round(Math.sin(tick * 0.9) * 16 + gpuL * 0.1);
    var low1 = fps - 24;
    var ram = 45 + (tick % 4 === 0 ? 1 : 0);

    document.getElementById("sCpuL").textContent = cpuL + "%";
    document.getElementById("sCpuT").textContent = cpuT + "°C";
    document.getElementById("bCpu").style.width = cpuL + "%";
    document.getElementById("sCpuW").textContent = (36 + cpuL * 0.45).toFixed(1) + " W";

    document.getElementById("sGpuL").textContent = gpuL + "%";
    document.getElementById("sGpuT").textContent = gpuT + "°C";
    document.getElementById("bGpu").style.width = gpuL + "%";
    document.getElementById("sGpuW").textContent = (140 + gpuL * 0.72).toFixed(1) + " W";

    document.getElementById("sFps").textContent = fps + " FPS";
    document.getElementById("sLow1").textContent = "1% Low " + low1;
    document.getElementById("sFrame").textContent = (1000 / fps).toFixed(1) + " ms";
    document.getElementById("bFps").style.width = Math.min(100, Math.round(fps / 2.2)) + "%";
    document.getElementById("sRam").textContent = "RAM " + ram + "%";

    document.getElementById("hFps").textContent = fps + " FPS (" + low1 + ")";
    document.getElementById("hCpu").textContent = "CPU " + cpuT + "°C " + cpuL + "%";
    document.getElementById("hGpu").textContent = "GPU " + gpuT + "°C " + gpuL + "%";
    document.getElementById("hRam").textContent = "RAM " + ram + "%";
  }, 1400);
})();
