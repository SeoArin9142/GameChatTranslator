@echo off
title GameChatTranslator OCR Language Pack Installer
setlocal EnableExtensions EnableDelayedExpansion

echo ===================================================
echo   GameChatTranslator OCR Language Pack Installer
echo ===================================================
echo.
echo This script installs optional Windows OCR language packs.
echo Select only the languages you need for in-game chat translation.
echo.
echo Available languages:
echo   1. English             en-US
echo   2. Japanese            ja-JP
echo   3. Chinese Simplified  zh-CN
echo   4. Russian             ru-RU
echo.
echo NOTE:
echo - Run this file as Administrator.
echo - Internet connection is required.
echo - Installation can take several minutes.
echo - Reboot Windows after installation.
echo.

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo [WARN] Administrator permission was not detected.
    echo        If installation fails, close this window and run as Administrator.
    echo.
)

call :AskLanguage INSTALL_EN "English" "en-US"
call :AskLanguage INSTALL_JA "Japanese" "ja-JP"
call :AskLanguage INSTALL_ZH "Chinese Simplified" "zh-CN"
call :AskLanguage INSTALL_RU "Russian" "ru-RU"

set "SELECTED="
if /i "!INSTALL_EN!"=="Y" set "SELECTED=!SELECTED! en-US"
if /i "!INSTALL_JA!"=="Y" set "SELECTED=!SELECTED! ja-JP"
if /i "!INSTALL_ZH!"=="Y" set "SELECTED=!SELECTED! zh-CN"
if /i "!INSTALL_RU!"=="Y" set "SELECTED=!SELECTED! ru-RU"

if not defined SELECTED (
    echo.
    echo No language was selected. Nothing to install.
    pause
    exit /b 0
)

echo.
echo Selected language packs:!SELECTED!
set "CONFIRM="
set /p "CONFIRM=Install selected language packs? (Y/N): "
if /i not "!CONFIRM!"=="Y" (
    echo Installation canceled.
    pause
    exit /b 0
)

set "FAILED=N"

if /i "!INSTALL_EN!"=="Y" call :InstallLanguage "English" "en-US"
if /i "!INSTALL_JA!"=="Y" call :InstallLanguage "Japanese" "ja-JP"
if /i "!INSTALL_ZH!"=="Y" call :InstallLanguage "Chinese Simplified" "zh-CN"
if /i "!INSTALL_RU!"=="Y" call :InstallLanguage "Russian" "ru-RU"

echo.
echo ===================================================
if /i "!FAILED!"=="Y" (
    echo   Some language packs failed to install.
    echo   Check the messages above, then run this file as Administrator again if needed.
) else (
    echo   Selected language pack installation completed.
)
echo ===================================================
echo.
echo Installing OCR language packs can also add keyboard input methods.
echo You can optionally keep only the Korean keyboard input method.
echo OCR recognition languages remain installed even if extra keyboard inputs are removed.
echo.
set "CLEAN_INPUTS="
set /p "CLEAN_INPUTS=Keep only the Korean keyboard input method? (Y/N): "
if /i "!CLEAN_INPUTS!"=="Y" (
    echo.
    echo Cleaning keyboard input language list...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$list = New-WinUserLanguageList 'ko-KR';" ^
      "Set-WinUserLanguageList -LanguageList $list -Force"
    if errorlevel 1 (
        echo [WARN] Failed to clean keyboard input language list.
    ) else (
        echo [OK] Keyboard input language list was limited to Korean only.
    )
) else (
    echo Keyboard input language list was not changed.
)

echo.
echo All tasks finished.
echo Please reboot Windows to activate OCR language recognition.
echo.
pause
exit /b 0

:AskLanguage
set "%~1=N"
set "ANSWER="
echo.
set /p "ANSWER=Install %~2 OCR language pack (%~3)? (Y/N): "
if /i "!ANSWER!"=="Y" set "%~1=Y"
exit /b 0

:InstallLanguage
echo.
echo [Install] %~1 (%~2)
set "INSTALL_RESULT=[OK]"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$lang = '%~2';" ^
  "$installed = $false;" ^
  "try {" ^
  "  Install-Language -Language $lang -ErrorAction Stop | Out-Null;" ^
  "} catch {" ^
  "  Write-Host $_.Exception.Message;" ^
  "}" ^
  "try {" ^
  "  $installed = @(Get-InstalledLanguage | Where-Object { $_.Language -eq $lang }).Count -gt 0;" ^
  "} catch {" ^
  "  Write-Host $_.Exception.Message;" ^
  "  exit 1;" ^
  "}" ^
  "if ($installed) { exit 0 } else { exit 1 }"
if errorlevel 1 (
    set "FAILED=Y"
    set "INSTALL_RESULT=[FAIL]"
)
echo !INSTALL_RESULT! %~1 (%~2)
exit /b 0
