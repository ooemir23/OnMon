@echo off
cd /d "%~dp0"
echo Program.cs derleniyor (64-bit, yonetici manifestli)...
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /platform:x64 /target:winexe /win32manifest:Kaynak\app.manifest /out:ProSystemMonitor.exe /r:LibreHardwareMonitorLib.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll Kaynak\Program.cs
if errorlevel 1 (echo HATA: derleme basarisiz & pause) else (echo Tamam. Baslat.bat ile calistirin. & pause)
