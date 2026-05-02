@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "FAILED=0"

echo ==========================================
echo   GameChatTranslator OCR Remover
echo ==========================================
echo.
echo This script removes EasyOCR, PaddleOCR and
echo Tesseract helper environments.
echo Windows OCR language packs are not removed here.
echo.

call "%~dp0Uninstall-Tesseract.bat" --no-pause
if errorlevel 1 set "FAILED=1"

call "%~dp0Uninstall-EasyOCR.bat" --no-pause
if errorlevel 1 set "FAILED=1"

call "%~dp0Uninstall-PaddleOCR.bat" --no-pause
if errorlevel 1 set "FAILED=1"

echo.
echo ==========================================
if "!FAILED!"=="0" (
    echo [OK] OCR removal steps completed.
) else (
    echo [WARN] Some OCR removal steps failed.
)
echo ==========================================
echo.
if /i not "%~1"=="--no-pause" pause
exit /b !FAILED!
