@echo off
setlocal EnableExtensions

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0OcrInstall.ps1" -Action Uninstall -Engine Tesseract
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if "%EXIT_CODE%"=="0" (
    echo [OK] Tesseract removal finished.
) else (
    echo [FAIL] Tesseract removal failed.
)

if /i not "%~1"=="--no-pause" pause
exit /b %EXIT_CODE%
