@echo off
setlocal EnableExtensions

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0OcrInstall.ps1" -Action Uninstall -Engine PaddleOCR
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if "%EXIT_CODE%"=="0" (
    echo [OK] PaddleOCR removal finished.
) else (
    echo [FAIL] PaddleOCR removal failed.
)

if /i not "%~1"=="--no-pause" pause
exit /b %EXIT_CODE%
