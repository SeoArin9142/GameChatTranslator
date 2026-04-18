@echo off
title 게임 번역기용 OCR 언어팩 설치 및 입력기 정리
setlocal enabledelayedexpansion

echo ===================================================
echo   게임 번역기용 윈도우 OCR 언어팩 자동 설치기
echo ===================================================
echo.
echo [설치 예정 목록]
echo 1. 일본어 (ja-JP)
echo 2. 중국어-간체 (zh-CN)
echo 3. 러시아어 (ru-RU)
echo.
echo * 설치 중 인터넷 연결이 필요하며, 시스템 사양에 따라 시간이 걸립니다.
echo.
set /p "choice=위 언어팩들을 설치하시겠습니까? (Y/N): "
if /i not "%choice%"=="Y" (
    echo 설치를 취소합니다.
    pause
    exit
)

echo.
echo [1/3] 일본어 설치 중...
powershell -Command "Install-Language -Language ja-JP -Confirm:$false"
echo [2/3] 중국어(간체) 설치 중...
powershell -Command "Install-Language -Language zh-CN -Confirm:$false"
echo [3/3] 러시아어 설치 중...
powershell -Command "Install-Language -Language ru-RU -Confirm:$false"

echo.
echo ===================================================
echo   언어팩 설치가 완료되었습니다.
echo ===================================================
echo.
echo 현재 설치된 언어팩 때문에 키보드 입력기가 늘어난 상태입니다.
echo 한국어(KO)와 영어(EN)를 제외한 나머지 '입력기'만 목록에서 지우시겠습니까?
echo (OCR 인식 기능은 그대로 유지됩니다)
echo.
set /p "clean_choice=입력기 목록을 정리하시겠습니까? (Y/N): "

if /i "%clean_choice%"=="Y" (
    echo.
    echo 입력기 목록을 정리 중입니다...
    :: PowerShell을 사용하여 ko-KR과 en-US만 입력 목록에 남기고 나머지는 입력기 목록에서만 제외
    powershell -Command "$list = Get-WinUserLanguageList; $list = $list | Where-Object { $_.LanguageTag -match 'ko-KR|en-US' }; Set-WinUserLanguageList -LanguageList $list -Force"
    echo [완료] 키보드 입력기 목록이 한국어와 영어로 정리되었습니다.
) else (
    echo [알림] 입력기 정리를 건너뜁니다. 키보드 목록이 유지됩니다.
)

echo.
echo 모든 과정이 완료되었습니다. 
echo 변경 사항(특히 OCR 인식 활성화)을 적용하려면 반드시 재부팅이 필요합니다!
echo.
pause