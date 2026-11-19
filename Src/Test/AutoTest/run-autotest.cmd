@echo off
setlocal

rem One-click wrapper for run-autotest.ps1. Double-click it, or pass the same arguments:
rem   run-autotest.cmd -Platform WPF -Watch

where pwsh >nul 2>nul
if %errorlevel%==0 (
  set "PWSH=pwsh"
) else (
  set "PWSH=powershell"
)

"%PWSH%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-autotest.ps1" %*
set "CODE=%errorlevel%"

echo.
if not "%CODE%"=="0" (
  echo Acceptance suite failed with exit code %CODE%.
) else (
  echo Acceptance suite passed.
)

rem Keep the window open when this was launched by double-click, so the verdict is readable.
pause
exit /b %CODE%
