@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo OnMon klasoru temizleniyor...
for %%V in (48 49 50 51 52 53 54 55 56) do (
  del /q "ProSystemMonitor_v%%V.exe" "ProSystemMonitor_v%%V.exe.config" "ProSystemMonitor_v%%V.sys" 2>nul
)
del /q hid.txt hid_log.txt piller.txt sensorler.txt 2>nul
echo.
echo Tamam. Calistirilacak surum: OnMon_v57\ProSystemMonitor_v57.exe
echo (Yedek, Kaynak, OnMon_v57 klasorleri ve hata.log korunur.)
echo Not: Eski surum aciksa once tepsiden Cikis yapin.
pause
