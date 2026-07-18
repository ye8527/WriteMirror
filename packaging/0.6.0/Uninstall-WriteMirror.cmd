@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$p=Get-AppxPackage -Name WriteMirror; if($p){Remove-AppxPackage -Package $p.PackageFullName; Write-Host 'WriteMirror was uninstalled.'}else{Write-Host 'WriteMirror is not installed.'}"
echo.
pause
