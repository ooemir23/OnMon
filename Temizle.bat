@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Eski OnMon surumleri siliniyor (v48 - v56)...
for %%V in (48 49 50 51 52 53 54 55 56) do (
  del /q "ProSystemMonitor_v%%V.exe" "ProSystemMonitor_v%%V.exe.config" "ProSystemMonitor_v%%V.sys" 2>nul
)
echo.
echo Tamam. Guncel surum: OnMon_v57\ProSystemMonitor_v57.exe
echo (Yedek\ klasorundeki v52 yedegi ve Kaynak\ klasoru korunur.)
pause
