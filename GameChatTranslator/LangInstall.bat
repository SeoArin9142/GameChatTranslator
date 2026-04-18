@echo off
echo ===================================================
echo 게임 번역기용 윈도우 OCR 언어팩 자동 설치기
echo (설치 중 인터넷 연결이 필요하며, 몇 분 정도 소요됩니다)
echo ===================================================
echo.
echo [1/3] 일본어 설치 중...
powershell -Command "Install-Language -Language ja-JP"
echo [2/3] 중국어(간체) 설치 중...
powershell -Command "Install-Language -Language zh-CN"
echo [3/3] 러시아어 설치 중...
powershell -Command "Install-Language -Language ru-RU"
echo.
echo 모든 설치가 완료되었습니다. 변경 사항 적용을 위해 컴퓨터를 재부팅 해주세요!
pause