# GameChatTranslator

**GameChatTranslator**는 게임 화면의 채팅 영역을 캡처하고, **Windows OCR / Tesseract / EasyOCR / PaddleOCR**로 텍스트를 인식한 뒤 Google / Gemini / Local LLM 번역 엔진으로 번역 결과를 표시하는 Windows용 오버레이 도구입니다.

현재는 **스트리노바(Strinova) 한국어 클라이언트의 `[캐릭터이름]: 채팅내용` 형식**에 맞춰 개발 중인 alpha 버전입니다.

![실행 화면](./GameChatTranslator/assets/screenshot.jpg)

## 다운로드

최신 배포 파일:

[GameChatTranslator v1.0.32-alpha 다운로드](https://github.com/SeoArin9142/GameChatTranslator/releases/download/v1.0.32-alpha/GameChatTranslator_v1.0.32-alpha.zip)

릴리즈 페이지:

https://github.com/SeoArin9142/GameChatTranslator/releases

권장 배포 방식:

- 릴리즈 페이지에 `GameChatTranslator-win-Setup.exe`가 있으면 설치형 배포를 우선 사용합니다.
- 설치 경로를 직접 지정하려면 `GameChatTranslator-win-Setup.exe --installto D:\Apps\GameChatTranslator` 형식으로 실행합니다.
- 설치형으로 설치한 경우 이후 업데이트는 앱 안에서 바로 다운로드/설치/재시작할 수 있습니다.
- ZIP 파일은 수동 실행/백업용으로 사용할 수 있습니다.

## 빠른 시작

1. 릴리즈 페이지에 `GameChatTranslator-win-Setup.exe`가 있으면 먼저 실행해 설치합니다.
   - `Setup.exe`를 명령줄로 실행하는 경우 `GameChatTranslator-win-Setup.exe --installto D:\Apps\GameChatTranslator` 형식으로 경로를 지정할 수 있습니다.
   - 설치형 배포가 아직 없거나 수동 실행이 필요하면 ZIP 파일을 내려받아 압축을 풉니다.
   - 설치형으로 설치한 경우 이후 업데이트 확인 시 새 버전을 앱에서 바로 적용할 수 있습니다.
2. 기본 메인 OCR은 Windows OCR이므로 `LangInstall.bat`를 **관리자 권한**으로 실행해 필요한 Windows OCR 언어팩을 설치합니다.
   - 선택 가능: 영어, 일본어, 중국어 간체, 러시아어
3. 언어팩 설치 후 Windows를 재부팅합니다.
4. `GameChatTranslator.exe`를 실행하고 UAC 관리자 권한 요청을 승인합니다.
5. 환경설정창에서 게임 언어, 번역 엔진, **메인 OCR 엔진**, 단축키를 확인한 뒤 저장합니다.
6. `Ctrl + 0`으로 환경설정창을 열고 **영역 다시 지정** 버튼으로 채팅 캡처 영역을 지정합니다.
7. `Ctrl + -`로 1회 번역하거나, `Ctrl + =`으로 자동 번역 모드를 켭니다.

첫 실행 후 생성되는 `config.ini`, `logs`, `Captures`, OCR 진단 저장 기본 위치는 아래와 같습니다.
다만 실행 파일과 같은 폴더에 `config.ini`가 이미 있거나 `portable.mode` 파일이 있으면, 같은 폴더를 사용자 데이터 루트로 사용하는 portable 모드로 동작합니다.

```text
기본: %LocalAppData%\GameChatTranslator\
portable 모드: 실행 파일 폴더
```

## 주요 기능

| 분류 | 기능 |
|:---|:---|
| OCR | Windows OCR / Tesseract / EasyOCR / PaddleOCR 메인 OCR, 색상 필터, 글자 굵기 보정, 적응형 이진화, 빠름/자동/정확 모드 |
| 번역 | Google 무료 번역, Gemini API, LM Studio/OpenAI 호환 Local LLM |
| Local LLM | LM Studio 서버 연동, `/v1/models` 연결 테스트, 실패 시 Google fallback |
| 오버레이 | 항상 위 표시, 투명도 조절, 이동/잠금, 위치 고정 버튼, 단축키 안내 |
| 진단 | OCR 진단 화면, 후보별 OCR 결과 비교, 다중 OCR 엔진 비교, ZIP 저장, 요약/전체 텍스트 복사 |
| 로그 | 실시간 로그창, CPU/메모리 표시, OCR 처리 시간 평균, API 오류 상세 안내 |
| 설정 | 설정 프리셋, 추천 프리셋, 설정 내보내기/가져오기, 메인 OCR 선택, 진단용 OCR 선택, 저장 즉시 반영, 번역 결과 자동 복사 |
| 배포 | GitHub Actions 자동 릴리즈, Velopack Setup.exe, win-x64 self-contained ZIP, SHA256 첨부 |

## 지원 범위와 제한

이 프로그램은 모든 게임 채팅을 범용으로 번역하는 도구가 아니라, 스트리노바 채팅창에 맞춰 조정된 도구입니다.

| 항목 | 현재 기준 |
|:---|:---|
| 대상 게임 | 스트리노바(Strinova) |
| 권장 게임 언어 | 한국어 클라이언트 |
| 인식 대상 | Strinova 모드: `[캐릭터이름]: 채팅내용`, ETC 모드: OCR 전체 텍스트 |
| 제외 대상 | 시스템 메시지, 귓속말, 대기실 자체 번역 가능 채팅 |
| OCR 기준 | 메인 번역: Windows OCR / Tesseract / EasyOCR / PaddleOCR, 진단: 4개 엔진 비교 가능 |
| 실행 권한 | 관리자 권한 실행 |

## 시스템 요구사항

### 필수 조건

- Windows OCR을 사용할 수 있는 64-bit Windows
- Windows OCR 언어팩 설치
- 관리자 권한 실행 승인
- Google/Gemini 사용 시 인터넷 연결
- Local LLM 사용 시 LM Studio Local Server

선택 기능:
- Tesseract 메인 OCR 사용 시 `tesseract` 실행 파일 설치
- EasyOCR / PaddleOCR 메인 OCR 또는 진단 사용 시 Python 및 해당 패키지 설치

### 최소사양

| 항목 | 기준 |
|:---|:---|
| OS | Windows 10 2004, Build 19041 이상 |
| CPU | 4코어 이상 |
| 메모리 | 8GB 이상 |
| GPU | DirectX 11/12 지원 GPU |
| 저장공간 | 1GB 이상 |
| 디스플레이 | 1920 x 1080 이상 권장 |

위 최소사양은 **Windows OCR 또는 Tesseract 기반의 기본 번역 경로**를 기준으로 합니다.
EasyOCR / PaddleOCR를 메인 OCR 또는 진단용으로 자주 사용할 경우에는 Python 런타임과 추가 패키지 로딩 비용 때문에 권장사양 이상을 기준으로 보는 편이 맞습니다.

### 권장사양

| 항목 | 기준 |
|:---|:---|
| OS | Windows 11 64-bit |
| CPU | 6코어 12스레드 이상 |
| 메모리 | 16GB 이상 |
| GPU | 게임을 안정적으로 실행할 수 있는 외장 GPU |
| Local LLM | 7B~9B Q4/Q5 모델 기준 VRAM 8GB 이상 권장 |
| 디스플레이 | 1920 x 1080 이상, 멀티모니터 권장 |

현재 권장사양은 **4개 OCR 엔진 비교, EasyOCR / PaddleOCR 실사용, Local LLM 연결 테스트**까지 포함한 기준으로는 그대로 유지해도 됩니다.
다만 Google / Gemini + Windows OCR만 사용할 경우에는 실제 요구 사양이 이보다 낮아도 동작할 수 있습니다.

## OCR 엔진별 설치 안내

GameChatTranslator는 OCR 엔진별로 필요한 설치 조건이 다릅니다.

### 1. Windows OCR

용도:
- 기본 메인 번역 OCR
- 설치 직후 가장 먼저 동작해야 하는 기본 경로

필요한 것:
- Windows OCR 지원 OS
- `LangInstall.bat`로 필요한 OCR 언어팩 설치
- 설치 후 재부팅

설치 방법:
```text
1. LangInstall.bat 관리자 권한 실행
2. 필요한 OCR 언어 선택/설치
3. Windows 재부팅
```

주의:
- 메인 번역 경로에서 기본값입니다.
- 언어팩이 없으면 OCR 결과가 비거나 언어별 인식이 제한됩니다.

### 2. Tesseract

용도:
- 메인 번역용 대체 OCR
- Windows OCR과 런타임 전환 가능

필요한 것:
- `tesseract.exe` 설치
- 언어 데이터(`eng`, `kor`, `jpn`, `chi_sim` 등) 설치
- 필요 시 `config.ini`의 `TesseractExePath` 지정

확인 포인트:
```text
- PATH에서 tesseract 실행 가능
또는
- config.ini의 TesseractExePath에 실행 파일 경로 지정
```

주의:
- 설치되어 있지 않거나 실패하면 Windows OCR로 자동 fallback 됩니다.
- EasyOCR / PaddleOCR를 메인 OCR로 선택한 경우에도 외부 OCR 실패 시 Tesseract -> Windows OCR 순서로 자동 fallback 합니다.

### 3. EasyOCR

용도:
- 메인 번역용 실험 OCR
- OCR 진단 화면 비교용

필요한 것:
- Python 실행 환경
- `torch`
- `torchvision`
- `easyocr`

설치 예시:
```bash
py -m pip install torch torchvision easyocr
```

주의:
- 메인 번역 경로에서 선택할 수 있습니다.
- 현재는 resident worker를 lazy init으로 사용합니다. 첫 요청은 Python 기동과 모델 로딩 때문에 느릴 수 있지만, 이후 요청은 같은 워커를 재사용합니다.
- 워커가 timeout 또는 비정상 종료되면 자동 재기동 후 다음 요청부터 복구를 시도합니다.
- 메인 번역에서 실패하면 Tesseract -> Windows OCR 순서로 자동 fallback 합니다.
- OCR 진단 점수 비교용 엔진으로도 계속 사용할 수 있습니다.
- Python 실행 파일은 `EasyOcrPythonPath`로 바꿀 수 있습니다.

### 4. PaddleOCR

용도:
- 메인 번역용 실험 OCR
- OCR 진단 화면 비교용

필요한 것:
- Python 실행 환경
- `paddlepaddle`
- `paddleocr`

설치 예시:
```bash
py -m pip install paddlepaddle
py -m pip install "paddleocr[all]"
```

주의:
- 메인 번역 경로에서 선택할 수 있습니다.
- 현재는 resident worker를 lazy init으로 사용합니다. 첫 요청은 Python 기동과 모델 로딩 때문에 느릴 수 있지만, 이후 요청은 같은 워커를 재사용합니다.
- 워커가 timeout 또는 비정상 종료되면 자동 재기동 후 다음 요청부터 복구를 시도합니다.
- 메인 번역에서 실패하면 Tesseract -> Windows OCR 순서로 자동 fallback 합니다.
- OCR 진단 점수 비교용 엔진으로도 계속 사용할 수 있습니다.
- Python 실행 파일은 `PaddleOcrPythonPath`로 바꿀 수 있습니다.

## 기본 사용 흐름

### 1. OCR 언어팩 확인

환경설정창의 OCR 언어팩 상태 영역에서 한국어, 영어, 중국어, 일본어, 러시아어 OCR 설치 여부를 확인할 수 있습니다.

미설치 언어가 있으면 `LangInstall.bat`를 관리자 권한으로 실행하고 재부팅한 뒤 다시 확인합니다.

### 2. 캡처 영역 지정

`Ctrl + 0`으로 환경설정창을 연 뒤 **영역 다시 지정** 버튼을 눌러 채팅창 영역을 드래그합니다.

고DPI/멀티모니터 환경에서는 WPF 표시 좌표와 실제 캡처 픽셀 좌표를 분리해 저장합니다. 영역이 어긋나면 다시 지정하거나 OCR 진단 화면에서 원본 캡처를 확인합니다.

### 3. OCR 모드 선택

`Ctrl + =`을 누를 때마다 자동 번역 모드가 순환됩니다.

| 모드 | 설명 |
|:---|:---|
| 빠름 | 기본 색상 필터와 게임 언어 OCR만 사용합니다. 가장 빠르지만 인식 실패 가능성이 있습니다. |
| 자동 | 빠른 경로를 먼저 시도하고 실패 시 추가 전처리를 실행합니다. 기본 추천 모드입니다. |
| 정확 | 모든 전처리 후보와 OCR 언어 결과를 비교합니다. 느리지만 배경 노이즈에 강합니다. |
| OFF | 자동 번역을 중지합니다. |

### 4. 메인 OCR 엔진 선택

환경설정창의 **메인 번역용 OCR 엔진**에서 현재 번역 경로에 사용할 OCR을 선택합니다.

| 엔진 | 설명 |
|:---|:---|
| Windows OCR | 기본값입니다. Windows 언어팩 기반으로 빠르게 동작합니다. |
| Tesseract | 실험 기능입니다. 설치되어 있지 않거나 실패하면 Windows OCR로 자동 fallback 합니다. |
| EasyOCR | 실험 기능입니다. Python/패키지 설치가 필요하며, 실패하면 Tesseract -> Windows OCR 순서로 자동 fallback 합니다. |
| PaddleOCR | 실험 기능입니다. Python/패키지 설치가 필요하며, 실패하면 Tesseract -> Windows OCR 순서로 자동 fallback 합니다. |

| 엔진 | 메인 번역 사용 | 준비물 | 속도 경향 | 장점 | 주의점 |
|:---|:---|:---|:---|:---|:---|
| Windows OCR | 기본 경로 | Windows OCR 언어팩 | 빠름 | 설치만 끝나면 바로 동작, 앱 내부 의존성 적음 | 언어팩 설치/재부팅 필요, 난잡한 배경에서는 인식률 한계가 있음 |
| Tesseract | 선택 가능 | `tesseract.exe`, 언어 데이터 | 중간 | 외부 의존성이 단순하고 fallback 체인 중간 단계로 안정적 | 언어 데이터 품질 영향이 크고, Windows OCR보다 느릴 수 있음 |
| EasyOCR | 선택 가능 | Python, `torch`, `torchvision`, `easyocr` | 느림 | 난잡한 채팅 이미지나 일부 폰트에서 강한 경우가 있음 | 첫 요청은 느릴 수 있지만 resident worker 재사용으로 이후 호출 부담이 줄어듦 |
| PaddleOCR | 선택 가능 | Python, `paddlepaddle`, `paddleocr` | 느림 | 중국어 계열이나 특정 UI 폰트에서 강한 경우가 있음 | 첫 요청은 느릴 수 있지만 resident worker 재사용으로 이후 호출 부담이 줄어듦. 설치 의존성은 가장 무거운 편 |

### 4-1. EasyOCR / PaddleOCR resident worker

현재 EasyOCR / PaddleOCR는 매 요청마다 Python을 새로 종료/실행하지 않고, 엔진별 resident worker를 lazy init으로 띄운 뒤 재사용합니다.

핵심 동작:
```text
- 첫 요청: Python 기동 + 모델 로딩 때문에 상대적으로 느릴 수 있음
- 이후 요청: 같은 워커를 재사용
- timeout 또는 비정상 종료: 워커를 자동 재기동하고 기존 fallback 체인 유지
- stdout: JSON 응답 전용
- stderr: 앱 세션 로그에 요약 기록
```

현재 1차 구현 범위:
```text
- PNG 임시 파일 전달 유지
- 엔진별 별도 워커
- 순차 blocking 요청
```

추가 검토 항목:
```text
- PNG 파일 대신 메모리 직접 전달
- worker 상태 가시화 확대
- 비동기 큐/병렬 처리
```

### 5. 번역 엔진 선택

환경설정창의 **기본 번역 엔진**에서 Google / Gemini / Local LLM을 선택합니다.

선택한 번역 엔진은 `config.ini`에 저장되고, 저장 버튼을 누르면 현재 실행 중인 오버레이에도 즉시 반영됩니다. Gemini API 키가 없으면 Gemini 선택은 Google로 보정됩니다.

### 6. 번역기 방식 선택

환경설정창의 **번역기 방식**에서 OCR 결과를 어떤 기준으로 번역할지 선택합니다.

| 방식 | 설명 |
|:---|:---|
| Strinova | 기존 방식입니다. `[캐릭터이름]: 채팅내용` 형식과 `characters.txt` 캐릭터명 검증을 통과한 채팅만 번역합니다. |
| ETC | 채팅 포맷과 무관하게 OCR로 읽은 전체 내용을 하나의 번역 대상으로 사용합니다. 다른 게임이나 일반 화면 텍스트 테스트에 사용할 수 있습니다. |

## 번역 엔진

| 엔진 | 네트워크 | 비용/준비 | 장점 | 주의점 | 추천 상황 |
|:---|:---|:---|:---|:---|:---|
| Google | 인터넷 필요 | 별도 API 키 없음 | 가장 빨리 시작 가능, fallback 기본값으로 안정적 | OCR 오타 복원 능력은 제한적 | 빠른 테스트, 기본값 유지, 다른 엔진 실패 대비 |
| Gemini | 인터넷 필요 | Gemini API 키 필요 | 문맥 복원과 번역 품질이 좋아 OCR 노이즈에 강함 | API 할당량, 서버 혼잡, 모델명 설정 영향이 있음 | 품질 우선, 외부 API 사용 가능 환경 |
| Local LLM | 로컬 서버 | LM Studio 또는 OpenAI 호환 서버 필요 | 외부 API 없이 내부 서버로 번역 가능 | 모델 준비, 서버 메모리/속도, 프롬프트 튜닝이 필요 | 인터넷 제약이 있거나 로컬 실험을 하고 싶은 경우 |

### Google

- 별도 API 키 없이 빠르게 사용할 수 있습니다.
- OCR이 깨진 일본어/중국어 문장을 복원하는 능력은 제한적입니다.
- Local LLM 또는 Gemini 실패 시 fallback 용도로도 사용됩니다.

### Gemini

- Gemini API 키가 필요합니다.
- OCR 오타가 섞인 문장을 문맥 기반으로 복원하는 데 유리합니다.
- API 사용량, 서버 혼잡, 모델명 오류에 영향을 받을 수 있습니다.

### Local LLM

- LM Studio의 OpenAI 호환 Local Server를 사용합니다.
- 기본 endpoint:

```text
http://localhost:1234/v1/chat/completions
```

- 기본 model:

```text
qwen/qwen3.5-9b
```

- 환경설정창의 **Local LLM 연결 테스트** 버튼은 chat completions 주소를 기준으로 `/v1/models`를 조회해 서버 연결과 모델 ID 존재 여부를 확인합니다.

## 사용자 데이터 저장 위치

자동 업데이트 준비를 위해 실행 파일 폴더와 사용자 데이터 폴더를 분리했습니다.

```text
실행 파일 / 배포 파일
- GameChatTranslator.exe
- LangInstall.bat
- readme.txt
- 기본 characters.txt

사용자 데이터
- 기본 모드: %LocalAppData%\GameChatTranslator\config.ini
- 기본 모드: %LocalAppData%\GameChatTranslator\logs\
- 기본 모드: %LocalAppData%\GameChatTranslator\Captures\
- 기본 모드: %LocalAppData%\GameChatTranslator\OcrDiagnostics\
- 기본 모드: %LocalAppData%\GameChatTranslator\characters.txt
- portable 모드: 실행 파일과 같은 폴더의 `config.ini`, `logs`, `Captures`, `OcrDiagnostics`, `characters.txt`
```

- 실행 파일 폴더에 `config.ini`가 있거나 `portable.mode` 파일이 있으면 같은 폴더를 사용자 데이터 루트로 그대로 사용합니다.
- 그 외 기본 모드에서는 기존 ZIP 배포 버전에서 실행 폴더에 `config.ini`, `logs`, `Captures`, `characters.txt`가 있었다면 첫 실행 시 새 위치로 자동 복사합니다.
- 기본 모드에서는 이후 설정 변경, 로그 기록, 디버그 캡처 저장을 `%LocalAppData%\GameChatTranslator` 아래에 유지합니다.
- `characters.txt`를 직접 수정해 쓰는 경우에는 사용자 데이터 폴더 쪽 파일을 수정하면 됩니다.
- 설치형 Velopack 배포는 `%LocalAppData%\GameChatTranslator\current` 폴더를 교체하므로, 설정/로그는 이 사용자 데이터 폴더에 유지됩니다.
- 환경설정창의 업데이트 영역에서 현재 실행 경로를 확인하고, 경로 복사와 현재 폴더 열기를 바로 사용할 수 있습니다.
- 로컬 PC에서 실행되므로 외부 API 비용은 없지만, 모델 로드 용량과 GPU/CPU 자원을 사용합니다.

## 기본 단축키

| 기능 | 단축키 | 설명 |
|:---|:---|:---|
| 환경설정창 열기 | `Ctrl + 0` | 설정창을 열고 영역 재지정, OCR 진단, 로그창 열기 등을 실행 |
| 수동 번역 | `Ctrl + -` | 1회 즉시 번역 |
| 자동 번역 모드 | `Ctrl + =` | 빠름 → 자동 → 정확 → OFF |

그 외 기능은 환경설정창 버튼으로 실행합니다.

- OCR 진단 화면 열기
- 로그창 열기
- 영역 다시 지정 / 영역 초기화
- 이동 잠금 전환
- 번역 결과 복사

단축키는 환경설정창에서 변경할 수 있고, 저장 버튼을 누르면 현재 실행 중인 오버레이에도 즉시 반영됩니다.

## 환경설정창

환경설정창은 3열 구조로 구성되어 있습니다.

| 영역 | 내용 |
|:---|:---|
| 설정 관리 | 사용자 프리셋, 추천 프리셋, 설정 내보내기/가져오기 |
| 언어·OCR | 게임 언어, 번역 언어, 메인 OCR 엔진, 진단용 OCR 선택, OCR 언어팩 상태, OCR 진단 |
| 실행 옵션 | 투명도, 자동 번역 주기, 결과 표시 방식, 자동 복사, 영역 재지정, 이동 잠금 |
| 번역 엔진 | Google/Gemini/Local LLM 설정, API/서버 연결 확인 |

추천 프리셋은 저사양 / 빠름 / 정확도 기준으로 OCR 배율, 이진화 기준, 자동 번역 주기, 결과 표시 방식을 입력칸에 채웁니다. 저장 버튼을 누르면 단축키, 번역 엔진, 메인 OCR 선택, 타이머 값이 재시작 없이 즉시 반영됩니다.

## 진단과 로그

### OCR 진단 화면

OCR 진단 화면에서는 현재 캡처 영역을 기준으로 아래 정보를 확인할 수 있습니다.

- 원본 캡처 이미지
- 리사이즈 이미지
- Color / ColorThick / Adaptive 전처리 후보
- 후보별 OCR 결과
- 선택 후보, 점수, 처리 시간, OCR 호출 수

진단 결과는 요약 복사, 전체 텍스트 복사, ZIP 저장으로 공유할 수 있습니다. ZIP에는 앱 버전, 설정 요약, OCR 언어팩 상태, 캡처 좌표, 선택한 진단용 OCR 엔진 목록도 포함됩니다.

### 로그창

로그창은 환경설정창의 **로그창 열기** 버튼으로 열 수 있습니다.

로그창에는 번역 로그, API 오류, OCR 성능 로그, CPU/메모리 사용량이 표시됩니다. OCR 성능 로그는 다음 형태로 기록됩니다.

```text
[OCR PERF] Mode=자동, Capture=..., OCR=..., Translate=..., Total=..., FastPath=..., Fallback=...
```

번역창 상단에는 짧은 오류만 표시하고, 자세한 원인은 로그창에 남깁니다.

## config.ini 주요 항목

일반 사용자는 환경설정창에서 대부분의 값을 변경할 수 있습니다. 현재 `config.ini`는 `[Language]`, `[Translation]`, `[OCR]`, `[Display]`, `[Capture]`, `[Window]`, `[Hotkeys]`, `[Gemini]`, `[LocalLlm]`, `[Update]`, `[Debug]` 섹션으로 나뉘어 저장됩니다. 아래 표는 문제 해결이나 수동 점검이 필요할 때 참고하세요.

| 키 | 기본값 | 설명 |
|:---|:---|:---|
| `GameLanguage` | `ko` | OCR 기준 게임 언어 |
| `TargetLanguage` | `ko` | 번역 대상 언어 |
| `TranslationContentMode` | `Strinova` | 번역기 방식. `Strinova` 또는 `ETC` |
| `TranslationEngine` | `Google` | 시작 번역 엔진. `Google`, `Gemini`, `LocalLlm` |
| `MainOcrEngine` | `WindowsOcr` | 메인 번역 경로 OCR 엔진. `WindowsOcr`, `Tesseract`, `EasyOcr`, `PaddleOcr` |
| `OcrEngineSelection` | `All` | OCR 진단 점수 계산에 포함할 엔진 목록 |
| `GeminiKey` | 빈 값 | Gemini API 키 |
| `GeminiModel` | `gemini-2.5-flash` | Gemini 호출 모델 |
| `LocalLlmEndpoint` | `http://localhost:1234/v1/chat/completions` | LM Studio chat completions 주소 |
| `LocalLlmModel` | `qwen/qwen3.5-9b` | Local LLM 모델 ID |
| `LocalLlmTimeoutSeconds` | `10` | Local LLM 응답 대기 시간, 1~60초 |
| `LocalLlmMaxTokens` | `160` | Local LLM 최대 응답 토큰, 40~512 |
| `TesseractExePath` | `tesseract` | Tesseract 실행 파일 경로 |
| `TesseractLanguageCodes` | `eng+kor+jpn+chi_sim` | Tesseract 언어 코드 조합 |
| `EasyOcrPythonPath` | `python` | EasyOCR Python 실행 경로 |
| `EasyOcrLanguageCodes` | `en+ko+ja+ch_sim` | EasyOCR 언어 코드 조합 |
| `PaddleOcrPythonPath` | `python` | PaddleOCR Python 실행 경로 |
| `PaddleOcrLanguageCodes` | `en+korean+japan+ch` | PaddleOCR 언어 코드 조합 |
| `AutoTranslateInterval` | `5` | 자동 번역 주기, 1~60초 |
| `Threshold` | `120` | OCR 이진화 기준 |
| `ScaleFactor` | `3` | OCR 캡처 확대 배율, 1~4 |
| `ResultDisplayMode` | `Latest` | `Latest` 또는 `History` |
| `ResultHistoryLimit` | `5` | 누적 표시 최대 줄 수, 1~10 |
| `TranslationResultAutoClearSeconds` | `0` | 번역 결과 자동 삭제 시간, 0~60초 (`0`은 사용 안 함) |
| `AutoCopyTranslationResult` | `false` | 번역 결과 자동 복사 여부 |
| `SaveDebugImages` | `false` | 디버그 이미지 저장 여부 |
| `CheckUpdatesOnStartup` | `true` | 실행 시 업데이트 확인 여부. 설치형은 앱 내 설치, ZIP은 릴리즈 페이지 안내 |

<details>
<summary>단축키 설정 키</summary>

| 키 | 기본값 |
|:---|:---|
| `Key_OpenSettings` | `Ctrl+0` |
| `Key_Translate` | `Ctrl+-` |
| `Key_AutoTranslate` | `Ctrl+=` |

</details>

## 문제 해결

| 증상 | 확인할 것 |
|:---|:---|
| OCR 결과가 비어 있음 | OCR 언어팩 설치, 재부팅, 캡처 영역, 관리자 권한, 메인 OCR 엔진 설정 확인 |
| 캡처 위치가 어긋남 | `Ctrl + 0`으로 설정창 열기 → 영역 다시 지정, OCR 진단 화면 원본 확인 |
| Google 번역이 이상함 | OCR 원문 깨짐 여부 확인, Gemini 또는 Local LLM 사용 |
| Gemini 실패 | API 키, 모델명, 할당량, 503 서버 혼잡 여부 확인 |
| Local LLM 실패 | LM Studio 서버 ON, endpoint, model ID, 연결 테스트 결과 확인 |
| Tesseract가 안 됨 | `tesseract` 설치 여부, `TesseractExePath`, 언어 데이터 설치 확인 |
| 버튼 클릭이 안 됨 | 설정창의 이동 잠금 전환 또는 메인 창의 위치 고정 버튼으로 상태를 확인 |
| 설정이 꼬임 | 설정 가져오기 전 자동 백업 파일 확인, 필요 시 `%LocalAppData%\\GameChatTranslator\\config.ini` 삭제 후 재설정 |

## 개발 / 검증 환경

아래 사양은 개발자가 빌드, 게시, 실사용 테스트에 사용한 기준 시스템입니다. 최소/권장사양이 아닙니다.

### 환경 A - 데스크톱

| 항목 | 사양 |
|:---|:---|
| OS | Windows 11 Pro 64-bit, Build 26200 |
| 개발 도구 | Visual Studio 2026 Community |
| 대상 런타임 | .NET 8 / WPF / Windows Forms |
| CPU | AMD Ryzen 7 7800X3D, 8코어 16스레드 |
| 메모리 | DDR5 96GB |
| GPU | NVIDIA GeForce RTX 4080 SUPER, VRAM 16GB |
| DirectX | DirectX 12 |
| 주 모니터 | 2560 x 1440, 180Hz |
| 보조 모니터 | 1920 x 1080, 240Hz |

### 환경 B - 노트북

| 항목 | 사양 |
|:---|:---|
| 모델 | ASUS TUF Gaming A18 FA808UP |
| OS | Windows 11 Pro 64-bit, Build 26200 |
| 개발 도구 | Visual Studio 2026 Community |
| 대상 런타임 | .NET 8 / WPF / Windows Forms |
| CPU | AMD Ryzen 7 260 w/ Radeon 780M Graphics, 8코어 16스레드 |
| 메모리 | 32GB RAM |
| dGPU | NVIDIA GeForce RTX 5070 Laptop GPU, VRAM 8GB |
| iGPU | AMD Radeon 780M Graphics |
| DirectX | DirectX 12 |
| 내장 디스플레이 | 1920 x 1200, 144Hz |
| 외부 디스플레이 테스트 | 1920 x 1080, 60Hz |

CPU-Z / DxDiag 원본에는 PC 이름, 장치 식별자 등 민감할 수 있는 항목이 포함될 수 있으므로 저장소에는 원본 텍스트를 보관하지 않습니다.

## 개발자용 빌드

필요 도구:

- .NET 8 SDK
- Visual Studio 2026 Community 또는 호환되는 Visual Studio
- Windows targeting 지원 환경

기본 검증:

```bash
dotnet test GameChatTranslator.sln -p:EnableWindowsTargeting=true
dotnet build GameChatTranslator.sln -c Release -p:EnableWindowsTargeting=true
```

릴리즈는 태그 push 시 GitHub Actions가 자동으로 수행합니다.

```bash
git tag v1.0.32-alpha
git push origin v1.0.32-alpha
```

자동 릴리즈는 win-x64 self-contained publish 뒤 Velopack Setup.exe / nupkg / releases.win.json 과 ZIP / SHA256 생성, GitHub Release asset 업로드까지 진행합니다.

## 기여 및 문의

본 프로젝트는 **OpenAI Codex**, **Anthropic Claude**, **Google Gemini Pro**와 함께 만들어가는 초기 단계 프로젝트입니다.

스트리노바 유저분들의 피드백은 GitHub **Issues** 탭에 남겨주세요.

## 업데이트 내역

### v1.0.32-alpha

이번 버전에서는 OCR 메인 경로 확장, 번역 파이프라인 병목 완화, 설정 파일 구조 개편, 문서 정리를 한 번에 반영했습니다.

- 메인 번역 OCR 엔진에 EasyOCR / PaddleOCR를 추가하고, 실패 시 `EasyOCR/PaddleOCR -> Tesseract -> Windows OCR` fallback 체인을 적용했습니다.
- 자동 번역 및 수동 번역 경로에서 캡처/리사이즈/OCR 후보 준비 일부를 UI 스레드 밖으로 분리해 프리징 가능성을 줄였습니다.
- `config.ini`를 기능별 섹션으로 재정리하고, 기존 `[Settings]` 단일 섹션 파일도 계속 읽을 수 있게 하위 호환을 유지했습니다.
- 실행 폴더에 `config.ini`가 있거나 `portable.mode` 파일이 있으면 실행 파일 폴더를 그대로 사용하는 portable 모드를 추가했습니다.
- 기본 단축키를 `Ctrl + 0`(환경설정), `Ctrl + -`(수동 번역), `Ctrl + =`(자동 번역) 기준으로 정리하고, 기존 기본값 사용자는 자동 마이그레이션되도록 보강했습니다.
- README에 OCR 엔진 비교표, 번역 엔진 비교표, 설치 안내, 시스템 요구사항 해석 기준, 2차 개발/검증 환경(노트북)을 추가했습니다.

### v1.0.31-alpha

이번 버전에서는 `영역 초기화` 버튼에서 이모지를 제거해, 일부 릴리즈 환경에서 글씨가 보이지 않던 문제를 보수적으로 정리했습니다.

- `영역 초기화` 버튼 Content를 `🔄 영역 초기화`에서 `영역 초기화`로 단순화해 폰트 fallback 렌더링 영향을 제거했습니다.

<details>
<summary>지난 업데이트 요약</summary>

### v1.0.30-alpha

이번 버전에서는 `영역 초기화` 버튼이 일부 릴리즈 환경에서 여전히 잘리던 문제를 보정했습니다.

- `영역 초기화` 버튼의 최소 폭과 좌우 패딩을 다시 늘려, v1.0.29-alpha 설치본 기준 재현되던 글씨 잘림을 줄였습니다.

### v1.0.29-alpha

이번 버전에서는 이슈 #109 대응으로 캡처 영역 주변 UI와 번역창 배치를 정리했습니다.

- PR #110: 환경설정창의 `영역 초기화` 버튼이 잘리지 않도록 최소 폭과 패딩을 보정했습니다.
- PR #110: 번역창 위치를 캡처 영역 기준 고정 아래 배치에서 위/아래 스마트 배치로 바꿨습니다.
- PR #110: 캡처 영역과 번역창 사이 간격은 8px로 고정하고, 캡처 영역이 위치한 모니터의 work area 기준으로 화면 밖을 벗어나지 않게 clamp 처리했습니다.
- PR #111: `SizeToContent.Height` 상태에서 높이 계산이 비어 있을 때도 위치 계산이 깨지지 않도록 fallback 높이를 추가했습니다.

### v1.0.28-alpha

이번 버전에서는 OCR/ETC 경로 튜닝과 최근 사용성 개선을 한 번에 묶었습니다.

- OCR 언어팩 상태를 capability 설치 여부와 OCR 엔진 사용 가능 여부로 나눠 진단할 수 있게 정리했습니다.
- 환경설정창을 3열 배치로 확장하고 OCR 상태 표시와 단축키 순서를 정리했습니다.
- ETC 모드에서는 원문 언어를 자동 감지하고, 게임 언어 설정이 실제 source language에 쓰이지 않음을 UI에서 바로 보이게 했습니다.
- ETC 모드 OCR 전처리와 언어 선택을 강화해, 언어별 OCR 결과 중 더 읽을 만한 결과를 우선 번역하도록 조정했습니다.
- 설치 경로 열기 / 경로 복사 / 업데이트 상태 자동 초기화 / 번역 결과 자동 삭제 옵션을 추가했습니다.

### v1.0.27-alpha

이번 버전에서는 설치 경로 확인과 설치 경로 지정 안내를 정리했습니다.

- 환경설정창 업데이트 영역에서 현재 설치형 실행 경로 또는 ZIP 직접 실행 경로를 바로 확인할 수 있게 했습니다.
- 설치형 배포 문서에 `GameChatTranslator-win-Setup.exe --installto <경로>` 사용법을 추가했습니다.
- 설치 경로 지정은 `GameChatTranslator-win-Setup.exe --installto <경로>` 방식으로 안내를 정리했습니다.

### v.1.0.26-alpha

이번 버전에서는 OCR 언어팩 설치 배치파일을 실제 앱 기준에 맞게 정리했습니다.

- LangInstall.bat가 `Install-Language` 대신 Windows OCR capability(`Language.OCR~~~<lang>`)를 직접 설치/검증하도록 변경했습니다.
- 앱의 OCR 언어팩 상태 표시와 배치파일 설치 기준이 일치하도록 정리했습니다.
- 언어팩 설치 결과 출력에서 `[FAIL]` / `[OK]`가 함께 나오던 혼란을 제거했습니다.
- 키보드 입력기 정리 옵션은 `ko-KR`만 남기도록 정리해 Win+Space에서 영어 입력기가 남지 않게 했습니다.

### v.1.0.25-alpha

이번 버전에서는 설치형 배포 기준 인앱 자동 업데이트를 반영했습니다.

- Velopack WPF startup(Main) 통합을 추가했습니다.
- 설치형(Setup.exe) 배포에서는 앱 안에서 업데이트 확인, 다운로드, 재시작 적용을 수행합니다.
- ZIP 직접 실행 버전은 기존처럼 릴리즈 페이지 안내를 유지합니다.
- 환경설정창과 문서의 업데이트 안내 문구를 현재 동작 기준으로 정리했습니다.

### v.1.0.24-alpha

이번 버전에서는 설치형 배포 준비와 사용자 데이터 저장 경로 분리를 반영했습니다.

- `config.ini`, `logs`, `Captures`, OCR 진단 기본 저장 위치를 `%LocalAppData%\\GameChatTranslator\\` 로 분리했습니다.
- 기존 ZIP 실행 폴더에 있던 설정/로그/캡처/characters 파일은 첫 실행 시 새 위치로 자동 복사합니다.
- GitHub Actions 릴리즈 workflow에 Velopack 설치형 패키징 단계를 추가했습니다.
- `GameChatTranslator-Setup.exe`, `releases.win.json`, `*.nupkg` 설치형 배포 자산을 릴리즈에 함께 업로드할 수 있도록 준비했습니다.
- 설치형 배포에서는 앱 안에서 새 버전을 직접 다운로드하고 재시작해 적용할 수 있게 했습니다.

### v.1.0.22-alpha

- 기본 번역 엔진을 Google / Gemini / Local LLM 중 선택해 저장할 수 있습니다.
- `Ctrl + -`로 실행 중 번역 엔진을 바꾸면 다음 실행에도 유지됩니다.
- 환경설정창에 Local LLM 연결 테스트 버튼을 추가했습니다.
- `/v1/chat/completions` 주소를 기준으로 `/v1/models`를 조회해 LM Studio 서버와 모델 ID를 확인합니다.

### v.1.0.21-alpha

- OCR 성능 로그에 FastPath 성공/실패와 Fallback 여부를 기록했습니다.
- 로그창 평균 요약에 FastPath 성공률과 Fallback 비율을 추가했습니다.

### v.1.0.20-alpha

- 번역 API 오류 안내에 `Ctrl+=` 로그창 확인 문구를 추가했습니다.
- OCR 진단 화면 버튼을 요약 복사 / 전체 텍스트 복사 / ZIP 저장으로 정리했습니다.
- 추천 프리셋 적용 전 변경 예정값을 안내하도록 개선했습니다.

### v.1.0.19-alpha

- 번역 API 실패 시 번역창 상단에 짧은 안내를 표시합니다.
- OCR 진단 결과 텍스트 복사 기능을 추가했습니다.

### v.1.0.18-alpha

- OCR 진단 화면 가독성을 개선했습니다.
- 저사양 / 빠름 / 정확도 추천 설정 프리셋을 추가했습니다.

### v.1.0.17-alpha

- OCR 진단 화면 단축키 `Ctrl + 5`를 추가했습니다.
- OCR 진단 ZIP에 앱 버전, 설정, OCR 언어팩 상태, 캡처 좌표를 포함했습니다.

### v.1.0.16-alpha

- 환경설정창을 섹션별 2열 레이아웃으로 정리했습니다.
- OCR 진단 결과 ZIP 저장 기능을 추가했습니다.
- Gemini/Google API 오류 안내를 강화했습니다.
- GitHub Actions 릴리즈 workflow를 Node 24 기반 action으로 갱신했습니다.

### v.1.0.15-alpha

- 설정창 숫자 입력 검증을 강화했습니다.
- 시스템 요구사항 문서를 추가했습니다.
- 테스트 폴더 구조와 번역 API 호출부를 정리했습니다.

### v.1.0.14-alpha

- OCR 이미지 전처리 코드를 `OcrImagePreprocessor` / `OcrMaskProcessor`로 분리했습니다.
- 전처리 마스크 처리 테스트를 추가했습니다.

### v.1.0.13-alpha

- OCR 배율, 이진화 기준, 자동 번역 주기, 누적 표시 줄 수를 설정창에서 조정할 수 있게 했습니다.
- 설정값 범위 보정과 테스트를 확대했습니다.

### v.1.0.12-alpha

- 번역 엔진 선택과 Gemini 실패 후 Google fallback 판단을 `TranslationService`로 분리했습니다.

### v.1.0.11-alpha

- 테스트 프로젝트를 추가하고, OCR 파싱/점수화/설정/번역 문자열 처리 로직을 테스트 가능한 구조로 분리했습니다.

### v.1.0.10-alpha

- 프로젝트 폴더 구조를 Views / Core / Models / Distribution 중심으로 정리했습니다.

### v.1.0.9-alpha

- Visual Studio 게시 산출물에 `characters.txt`, `LangInstall.bat`, `readme.txt`가 포함되도록 보완했습니다.
- 번역 결과 누적 표시 기본값을 5줄, 최대값을 10줄로 조정했습니다.

### v.1.0.8-alpha 이하

- 실시간 로그창, OCR 언어팩 상태 확인, 릴리즈 자동화, OCR 전처리 개선, 자동 번역 모드, Gemini 설정 UI, DPI/멀티모니터 캡처 보정, 알파 안정화 작업이 단계적으로 반영되었습니다.

자세한 과거 변경 내용은 GitHub Releases에서 확인할 수 있습니다.

</details>
