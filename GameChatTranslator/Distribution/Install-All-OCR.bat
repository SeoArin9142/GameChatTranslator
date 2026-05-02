@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "FAILED=0"

echo ==========================================
echo   GameChatTranslator OCR Installer
echo ==========================================
echo.
echo This script runs Windows OCR, Tesseract,
echo EasyOCR and PaddleOCR setup in sequence.
echo.

if exist "%~dp0LangInstall.bat" (
    call "%~dp0LangInstall.bat"
    if errorlevel 1 set "FAILED=1"
)

call "%~dp0Install-Tesseract.bat" --no-pause
if errorlevel 1 set "FAILED=1"

call "%~dp0Install-EasyOCR.bat" --no-pause
if errorlevel 1 set "FAILED=1"

call "%~dp0Install-PaddleOCR.bat" --no-pause
if errorlevel 1 set "FAILED=1"

echo.
echo ==========================================
if "!FAILED!"=="0" (
    echo [OK] OCR installation steps completed.
) else (
    echo [WARN] Some OCR installation steps failed.
)
echo ==========================================
echo.
pause
exit /b !FAILED!
