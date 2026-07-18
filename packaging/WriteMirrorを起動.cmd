@echo off
setlocal
set "PACKAGE_ROOT=%~dp0"
set "DEVICE_ARCH=%PROCESSOR_ARCHITECTURE%"
if defined PROCESSOR_ARCHITEW6432 set "DEVICE_ARCH=%PROCESSOR_ARCHITEW6432%"

if /I "%DEVICE_ARCH%"=="ARM64" goto ARM64
if /I "%DEVICE_ARCH%"=="AMD64" goto X64

echo Unsupported processor architecture: %DEVICE_ARCH%
echo Use Windows 11 ARM64 or x64.
pause
exit /b 1

:ARM64
start "" "%PACKAGE_ROOT%app\ARM64\WriteMirror.exe"
exit /b 0

:X64
start "" "%PACKAGE_ROOT%app\x64\WriteMirror.exe"
exit /b 0
