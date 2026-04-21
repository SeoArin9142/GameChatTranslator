# GameChatTranslator

**GameChatTranslator**는 게임 화면의 채팅 영역을 캡처하고, Windows OCR로 텍스트를 인식한 뒤 Google / Gemini / Local LLM 번역 엔진으로 한국어 번역을 표시하는 Windows용 오버레이 도구입니다.

현재는 **스트리노바(Strinova) 한국어 클라이언트의 `[캐릭터이름]: 채팅내용` 형식**에 맞춰 개발 중인 alpha 버전입니다.

![실행 화면](./GameChatTranslator/assets/screenshot.jpg)

## 다운로드

최신 배포 파일:

[GameChatTranslator v1.0.26-alpha 다운로드](https://github.com/SeoArin9142/GameChatTranslator/releases/download/v.1.0.26-alpha/GameChatTranslator_v1.0.26-alpha.zip)

릴리즈 페이지:

https://github.com/SeoArin9142/GameChatTranslator/releases

권장 배포 방식:

- 릴리즈 페이지에 `GameChatTranslator-win-Setup.exe`가 있으면 설치형 배포를 우선 사용합니다.
- 설치 경로를 직접 고르고 싶으면 릴리즈에 `*.msi` 파일이 함께 제공되는지 먼저 확인하세요.
- 설치형으로 설치한 경우 이후 업데이트는 앱 안에서 바로 다운로드/설치/재시작할 수 있습니다.
- ZIP 파일은 수동 실행/백업용으로 사용할 수 있습니다.

## 빠른 시작

1. 릴리즈 페이지에 `GameChatTranslator-win-Setup.exe`가 있으면 먼저 실행해 설치합니다.
   - 설치 경로를 직접 선택해야 하면 릴리즈에 `*.msi` 파일이 함께 제공되는지 먼저 확인한 뒤 사용합니다.
   - `Setup.exe`를 명령줄로 실행하는 경우 `--installto <경로>` 옵션으로 경로를 지정할 수 있습니다.
   - 설치형 배포가 아직 없거나 수동 실행이 필요하면 ZIP 파일을 내려받아 압축을 풉니다.
   - 설치형으로 설치한 경우 이후 업데이트 확인 시 새 버전을 앱에서 바로 적용할 수 있습니다.
2. `LangInstall.bat`를 **관리자 권한**으로 실행해 필요한 Windows OCR 언어팩을 설치합니다.
   - 선택 가능: 영어, 일본어, 중국어 간체, 러시아어
3. 언어팩 설치 후 Windows를 재부팅합니다.
4. `GameChatTranslator.exe`를 실행하고 UAC 관리자 권한 요청을 승인합니다.
5. 환경설정창에서 언어, 번역 엔진, OCR 모드, 단축키를 확인한 뒤 저장합니다.
6. 게임에서 `Ctrl + 8`로 채팅 캡처 영역을 지정합니다.
7. `Ctrl + 9`로 1회 번역하거나, `Ctrl + 0`으로 자동 번역 모드를 켭니다.

첫 실행 후 생성되는 `config.ini`, `logs`, `Captures`, OCR 진단 저장 기본 위치는 아래와 같습니다.

```text
%LocalAppData%\GameChatTranslator\
```

## 주요 기능

| 분류 | 기능 |
|:---|:---|
| OCR | Windows OCR, 색상 필터, 글자 굵기 보정, 적응형 이진화, 빠름/자동/정확 모드 |
| 번역 | Google 무료 번역, Gemini API, LM Studio/OpenAI 호환 Local LLM |
| Local LLM | LM Studio 서버 연동, `/v1/models` 연결 테스트, 실패 시 Google fallback |
| 오버레이 | 항상 위 표시, 투명도 조절, 이동/잠금, 단축키 안내 숨김 |
| 진단 | OCR 진단 화면, 후보별 OCR 결과 비교, ZIP 저장, 요약/전체 텍스트 복사 |
| 로그 | 실시간 로그창, CPU/메모리 표시, OCR 처리 시간 평균, API 오류 상세 안내 |
| 설정 | 설정 프리셋, 추천 프리셋, 설정 내보내기/가져오기, 숫자 입력 검증 |
| 배포 | GitHub Actions 자동 릴리즈, Velopack Setup.exe, win-x64 self-contained ZIP, SHA256 첨부 |

## 지원 범위와 제한

이 프로그램은 모든 게임 채팅을 범용으로 번역하는 도구가 아니라, 스트리노바 채팅창에 맞춰 조정된 도구입니다.

| 항목 | 현재 기준 |
|:---|:---|
| 대상 게임 | 스트리노바(Strinova) |
| 권장 게임 언어 | 한국어 클라이언트 |
| 인식 대상 | Strinova 모드: `[캐릭터이름]: 채팅내용`, ETC 모드: OCR 전체 텍스트 |
| 제외 대상 | 시스템 메시지, 귓속말, 대기실 자체 번역 가능 채팅 |
| OCR 기준 | Windows OCR 언어팩 |
| 실행 권한 | 관리자 권한 실행 |

## 시스템 요구사항

### 필수 조건

- Windows OCR을 사용할 수 있는 64-bit Windows
- Windows OCR 언어팩 설치
- 관리자 권한 실행 승인
- Google/Gemini 사용 시 인터넷 연결
- Local LLM 사용 시 LM Studio Local Server

### 최소사양

| 항목 | 기준 |
|:---|:---|
| OS | Windows 10 2004, Build 19041 이상 |
| CPU | 4코어 이상 |
| 메모리 | 8GB 이상 |
| GPU | DirectX 11/12 지원 GPU |
| 저장공간 | 1GB 이상 |
| 디스플레이 | 1920 x 1080 이상 권장 |

### 권장사양

| 항목 | 기준 |
|:---|:---|
| OS | Windows 11 64-bit |
| CPU | 6코어 12스레드 이상 |
| 메모리 | 16GB 이상 |
| GPU | 게임을 안정적으로 실행할 수 있는 외장 GPU |
| Local LLM | 7B~9B Q4/Q5 모델 기준 VRAM 8GB 이상 권장 |
| 디스플레이 | 1920 x 1080 이상, 멀티모니터 권장 |

## 기본 사용 흐름

### 1. OCR 언어팩 확인

환경설정창의 OCR 언어팩 상태 영역에서 한국어, 영어, 중국어, 일본어, 러시아어 OCR 설치 여부를 확인할 수 있습니다.

미설치 언어가 있으면 `LangInstall.bat`를 관리자 권한으로 실행하고 재부팅한 뒤 다시 확인합니다.

### 2. 캡처 영역 지정

`Ctrl + 8`을 누른 뒤 채팅창 영역을 드래그합니다.

고DPI/멀티모니터 환경에서는 WPF 표시 좌표와 실제 캡처 픽셀 좌표를 분리해 저장합니다. 영역이 어긋나면 다시 지정하거나 OCR 진단 화면에서 원본 캡처를 확인합니다.

### 3. OCR 모드 선택

`Ctrl + 0`을 누를 때마다 자동 번역 모드가 순환됩니다.

| 모드 | 설명 |
|:---|:---|
| 빠름 | 기본 색상 필터와 게임 언어 OCR만 사용합니다. 가장 빠르지만 인식 실패 가능성이 있습니다. |
| 자동 | 빠른 경로를 먼저 시도하고 실패 시 추가 전처리를 실행합니다. 기본 추천 모드입니다. |
| 정확 | 모든 전처리 후보와 OCR 언어 결과를 비교합니다. 느리지만 배경 노이즈에 강합니다. |
| OFF | 자동 번역을 중지합니다. |

### 4. 번역 엔진 선택

`Ctrl + -`로 번역 엔진을 순환합니다.

```text
Google → Gemini → Local LLM
```

선택한 번역 엔진은 `config.ini`에 저장되어 다음 실행에도 유지됩니다. Gemini API 키가 없으면 Gemini 단계는 건너뜁니다.

### 5. 번역기 방식 선택

환경설정창의 **번역기 방식**에서 OCR 결과를 어떤 기준으로 번역할지 선택합니다.

| 방식 | 설명 |
|:---|:---|
| Strinova | 기존 방식입니다. `[캐릭터이름]: 채팅내용` 형식과 `characters.txt` 캐릭터명 검증을 통과한 채팅만 번역합니다. |
| ETC | 채팅 포맷과 무관하게 OCR로 읽은 전체 내용을 하나의 번역 대상으로 사용합니다. 다른 게임이나 일반 화면 텍스트 테스트에 사용할 수 있습니다. |

## 번역 엔진

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
- %LocalAppData%\GameChatTranslator\config.ini
- %LocalAppData%\GameChatTranslator\logs\
- %LocalAppData%\GameChatTranslator\Captures\
- %LocalAppData%\GameChatTranslator\OcrDiagnostics\
- %LocalAppData%\GameChatTranslator\characters.txt
```

- 기존 ZIP 배포 버전에서 실행 폴더에 `config.ini`, `logs`, `Captures`, `characters.txt`가 있었다면 첫 실행 시 새 위치로 자동 복사합니다.
- 이후 설정 변경, 로그 기록, 디버그 캡처 저장은 모두 `%LocalAppData%\GameChatTranslator` 아래를 사용합니다.
- `characters.txt`를 직접 수정해 쓰는 경우에는 사용자 데이터 폴더 쪽 파일을 수정하면 됩니다.
- 설치형 Velopack 배포는 `%LocalAppData%\GameChatTranslator\current` 폴더를 교체하므로, 설정/로그는 이 사용자 데이터 폴더에 유지됩니다.
- 환경설정창의 업데이트 영역에서 현재 실행 경로를 확인할 수 있습니다.
- 로컬 PC에서 실행되므로 외부 API 비용은 없지만, 모델 로드 용량과 GPU/CPU 자원을 사용합니다.

## 기본 단축키

| 기능 | 단축키 | 설명 |
|:---|:---|:---|
| OCR 진단 화면 | `Ctrl + 5` | 현재 캡처 영역의 OCR 진단 화면 열기 |
| 번역 복사 | `Ctrl + 6` | 최근 번역 결과를 클립보드에 복사 |
| 이동/잠금 | `Ctrl + 7` | 테두리가 녹색일 때 오버레이 드래그 가능 |
| 영역 설정 | `Ctrl + 8` | 번역할 채팅 영역 지정 |
| 수동 번역 | `Ctrl + 9` | 1회 즉시 번역 |
| 자동 번역 모드 | `Ctrl + 0` | 빠름 → 자동 → 정확 → OFF |
| 번역 엔진 변경 | `Ctrl + -` | Google → Gemini → Local LLM |
| 로그창 ON/OFF | `Ctrl + =` | 실시간 로그창 표시/숨김 |
| 단축키 안내 ON/OFF | `Ctrl + F10` | 번역창 상단 단축키 안내 표시/숨김 |

단축키는 환경설정창에서 변경할 수 있습니다. 단축키 초기화 버튼은 입력칸만 기본값으로 되돌리며, 저장 버튼을 눌러야 `config.ini`에 반영됩니다.

## 환경설정창

환경설정창은 2열 구조로 구성되어 있습니다.

| 영역 | 내용 |
|:---|:---|
| 설정 관리 | 사용자 프리셋, 추천 프리셋, 설정 내보내기/가져오기 |
| 언어·OCR | 게임 언어, 번역 언어, OCR 모드, OCR 언어팩 상태, OCR 진단 |
| 실행 옵션 | 투명도, 자동 번역 주기, 결과 표시 방식, 단축키 |
| 번역 엔진 | Google/Gemini/Local LLM 설정, API/서버 연결 확인 |

추천 프리셋은 저사양 / 빠름 / 정확도 기준으로 OCR 배율, 이진화 기준, 자동 번역 주기, 결과 표시 방식을 입력칸에 채웁니다. 실제 저장은 **저장 및 게임 시작**을 눌러야 적용됩니다.

## 진단과 로그

### OCR 진단 화면

OCR 진단 화면에서는 현재 캡처 영역을 기준으로 아래 정보를 확인할 수 있습니다.

- 원본 캡처 이미지
- 리사이즈 이미지
- Color / ColorThick / Adaptive 전처리 후보
- 후보별 OCR 결과
- 선택 후보, 점수, 처리 시간, OCR 호출 수

진단 결과는 요약 복사, 전체 텍스트 복사, ZIP 저장으로 공유할 수 있습니다. ZIP에는 앱 버전, 설정 요약, OCR 언어팩 상태, 캡처 좌표도 포함됩니다.

### 로그창

`Ctrl + =`로 로그창을 열 수 있습니다.

로그창에는 번역 로그, API 오류, OCR 성능 로그, CPU/메모리 사용량이 표시됩니다. OCR 성능 로그는 다음 형태로 기록됩니다.

```text
[OCR PERF] Mode=자동, Capture=..., OCR=..., Translate=..., Total=..., FastPath=..., Fallback=...
```

번역창 상단에는 짧은 오류만 표시하고, 자세한 원인은 로그창에 남깁니다.

## config.ini 주요 항목

일반 사용자는 환경설정창에서 대부분의 값을 변경할 수 있습니다. 아래 표는 문제 해결이나 수동 점검이 필요할 때 참고하세요.

| 키 | 기본값 | 설명 |
|:---|:---|:---|
| `GameLanguage` | `ko` | OCR 기준 게임 언어 |
| `TargetLanguage` | `ko` | 번역 대상 언어 |
| `TranslationContentMode` | `Strinova` | 번역기 방식. `Strinova` 또는 `ETC` |
| `TranslationEngine` | `Google` | 시작 번역 엔진. `Google`, `Gemini`, `LocalLlm` |
| `GeminiKey` | 빈 값 | Gemini API 키 |
| `GeminiModel` | `gemini-2.5-flash` | Gemini 호출 모델 |
| `LocalLlmEndpoint` | `http://localhost:1234/v1/chat/completions` | LM Studio chat completions 주소 |
| `LocalLlmModel` | `qwen/qwen3.5-9b` | Local LLM 모델 ID |
| `LocalLlmTimeoutSeconds` | `10` | Local LLM 응답 대기 시간, 1~60초 |
| `LocalLlmMaxTokens` | `160` | Local LLM 최대 응답 토큰, 40~512 |
| `AutoTranslateInterval` | `5` | 자동 번역 주기, 1~60초 |
| `Threshold` | `120` | OCR 이진화 기준 |
| `ScaleFactor` | `3` | OCR 캡처 확대 배율, 1~4 |
| `ResultDisplayMode` | `Latest` | `Latest` 또는 `History` |
| `ResultHistoryLimit` | `5` | 누적 표시 최대 줄 수, 1~10 |
| `SaveDebugImages` | `false` | 디버그 이미지 저장 여부 |
| `CheckUpdatesOnStartup` | `true` | 실행 시 업데이트 확인 여부. 설치형은 앱 내 설치, ZIP은 릴리즈 페이지 안내 |

<details>
<summary>단축키 설정 키</summary>

| 키 | 기본값 |
|:---|:---|
| `Key_CopyResult` | `Ctrl+6` |
| `Key_OcrDiagnostic` | `Ctrl+5` |
| `Key_MoveToggle` | `Ctrl+7` |
| `Key_SelectArea` | `Ctrl+8` |
| `Key_Translate` | `Ctrl+9` |
| `Key_AutoTranslate` | `Ctrl+0` |
| `Key_EngineToggle` | `Ctrl+-` |
| `Key_LogViewer` | `Ctrl+=` |
| `Key_HotkeyGuideToggle` | `Ctrl+F10` |

</details>

## 문제 해결

| 증상 | 확인할 것 |
|:---|:---|
| OCR 결과가 비어 있음 | OCR 언어팩 설치, 재부팅, 캡처 영역, 관리자 권한 |
| 캡처 위치가 어긋남 | `Ctrl + 8`로 영역 재지정, OCR 진단 화면 원본 확인 |
| Google 번역이 이상함 | OCR 원문 깨짐 여부 확인, Gemini 또는 Local LLM 사용 |
| Gemini 실패 | API 키, 모델명, 할당량, 503 서버 혼잡 여부 확인 |
| Local LLM 실패 | LM Studio 서버 ON, endpoint, model ID, 연결 테스트 결과 확인 |
| 버튼 클릭이 안 됨 | 오버레이 클릭 관통 상태일 수 있음. `Ctrl + 7`로 이동/잠금 해제 |
| 단축키 안내가 안 보임 | `Ctrl + F10`으로 단축키 안내 표시 |
| 설정이 꼬임 | 설정 가져오기 전 자동 백업 파일 확인, 필요 시 `%LocalAppData%\\GameChatTranslator\\config.ini` 삭제 후 재설정 |

## 개발 / 검증 환경

아래 사양은 개발자가 빌드, 게시, 실사용 테스트에 사용한 기준 시스템입니다. 최소/권장사양이 아닙니다.

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
git tag v.1.0.26-alpha
git push origin v.1.0.26-alpha
```

자동 릴리즈는 win-x64 self-contained publish 뒤 Velopack Setup.exe / nupkg / releases.win.json 과 ZIP / SHA256 생성, GitHub Release asset 업로드까지 진행합니다. MSI는 릴리즈 자산에 함께 생성되는지 실제 자산 목록으로 확인한 뒤 사용하면 됩니다.

## 기여 및 문의

본 프로젝트는 **OpenAI Codex**, **Anthropic Claude**, **Google Gemini Pro**와 함께 만들어가는 초기 단계 프로젝트입니다.

스트리노바 유저분들의 피드백은 GitHub **Issues** 탭에 남겨주세요.

## 업데이트 내역

### v.1.0.26-alpha

이번 버전에서는 OCR 언어팩 설치 배치파일을 실제 앱 기준에 맞게 정리했습니다.

- LangInstall.bat가 `Install-Language` 대신 Windows OCR capability(`Language.OCR~~~<lang>`)를 직접 설치/검증하도록 변경했습니다.
- 앱의 OCR 언어팩 상태 표시와 배치파일 설치 기준이 일치하도록 정리했습니다.
- 언어팩 설치 결과 출력에서 `[FAIL]` / `[OK]`가 함께 나오던 혼란을 제거했습니다.
- 키보드 입력기 정리 옵션은 `ko-KR`만 남기도록 정리해 Win+Space에서 영어 입력기가 남지 않게 했습니다.

<details>
<summary>지난 업데이트 요약</summary>

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
