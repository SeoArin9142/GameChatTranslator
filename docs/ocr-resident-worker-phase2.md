# OCR Resident Worker 2차 개선 검토

이 문서는 EasyOCR / PaddleOCR resident worker 1차 적용 이후, 다음 단계로 검토할 항목을 정리한 설계 메모입니다.

현재 상태:

```text
- EasyOCR / PaddleOCR는 엔진별 resident worker를 lazy init으로 시작
- PNG 임시 파일 경로를 worker로 전달
- 요청은 worker별 순차 blocking
- timeout 시 Kill -> restart -> fallback
- stderr는 앱 로그로 요약 기록
```

이번 문서 범위:

```text
1. 메모리 직접 전달 검토
2. 비동기 큐 / 병렬 처리 검토
3. static worker 공유 풀 검토
```

## 1. 현재 병목 정리

1차 resident worker 적용으로 가장 큰 병목인 `Process.Start + 모델 로딩`은 줄었습니다.

남은 병목은 아래 순서로 봐야 합니다.

```text
1. PNG 임시 파일 생성 / 삭제 I/O
2. worker 요청 직렬 처리로 인한 대기 시간
3. 어댑터 인스턴스 단위 worker 보유 구조
```

정리:

```text
- 지금은 "콜드 스타트"는 줄었지만 "디스크 I/O"는 남아 있음
- 동시 요청이 많아지면 queueing 지연이 생길 수 있음
- 어댑터 인스턴스가 여러 개 생기면 pythonPath당 worker도 중복 생성될 수 있음
```

## 2. 메모리 직접 전달 검토

### 현재 방식

```text
C# Bitmap
-> PNG 임시 파일 저장
-> worker에 imagePath 전달
-> Python이 파일 읽기
```

장점:

```text
- 디버깅 쉬움
- Python/C# 경계가 단순함
- 실패 시 재현이 쉬움
```

단점:

```text
- 디스크 I/O 비용
- temp 파일 관리 비용
- 잦은 OCR 호출 시 SSD/파일시스템 부담
```

### 대안 A: Base64 문자열 전달

```text
C# Bitmap -> PNG/JPEG 메모리 인코딩 -> Base64 -> JSON 요청
Python -> Base64 decode -> OCR
```

장점:

```text
- 파일 I/O 제거
- 현행 line-delimited JSON 프로토콜을 유지하기 쉬움
```

단점:

```text
- Base64 인코딩/디코딩 비용
- payload 크기 증가
- 긴 요청에서 stdout/stdin 버퍼 부담 증가
```

판단:

```text
1차 개선용으로는 가능하지만 최종형으로는 아님
```

### 대안 B: length-prefixed binary/stdin 전달

```text
C# -> 요청 헤더(JSON) + 바이너리 이미지 바이트 전달
Python -> 길이 기준으로 읽고 decode
```

장점:

```text
- 파일 I/O 제거
- Base64 오버헤드 제거
- 메모리 전달 효율이 가장 좋음
```

단점:

```text
- 현재 line-delimited JSON 프로토콜을 깨야 함
- 구현/디버깅 복잡도 상승
- framing 오류 시 복구가 더 어려움
```

판단:

```text
성능은 가장 좋지만 2차 후반 또는 3차 범위가 맞음
```

### 대안 C: NamedPipe + 메시지 프레이밍

장점:

```text
- stdio보다 명시적 채널 분리 가능
- 확장성 좋음
```

단점:

```text
- 구현 범위 큼
- 현재 구조 대비 리팩토링 비용 큼
```

판단:

```text
지금 바로는 과함
```

### 메모리 전달 권장안

```text
권장 순서:
1. static worker 공유 풀
2. queue 정책 정리
3. 그 다음 메모리 직접 전달
```

구현 우선순위는 아래가 맞습니다.

```text
- 당장: PNG 유지
- 다음 단계 후보: Base64 전송 실험
- 장기 목표: binary framing 또는 pipe 기반 전환
```

이유:

```text
worker 중복 생성과 queue 정책이 먼저 정리되지 않으면
메모리 전달로 바꿔도 구조 복잡도만 먼저 올라감
```

## 3. 비동기 큐 / 병렬 처리 검토

### 현재 방식

```text
- worker 1개당 요청 1개씩 순차 처리
- SemaphoreSlim(1,1)으로 프로토콜 혼선 방지
```

장점:

```text
- 안정적
- 응답/요청 매칭이 단순
- timeout/restart 처리 용이
```

단점:

```text
- 요청이 몰리면 대기 시간이 늘어남
- 오래 걸리는 OCR 1건이 뒤 요청을 막음
```

### 대안 A: latest-only queue

```text
- 새 요청이 오면 대기 중 오래된 요청은 버리고 최신 요청만 유지
```

적합한 경우:

```text
- 자동 번역처럼 "최신 화면이 중요"한 경로
```

장점:

```text
- backlog 감소
- 사용자 체감 지연 감소
```

단점:

```text
- 진단/배치 처리에는 부적합
- 어떤 요청이 버려졌는지 로그가 필요
```

판단:

```text
메인 자동 번역 경로에는 가장 현실적
```

### 대안 B: worker pool 병렬 처리

```text
- 같은 엔진 worker를 2개 이상 띄워 요청 분산
```

장점:

```text
- 병렬 처리량 증가
```

단점:

```text
- Python 메모리 사용량 증가
- 모델을 프로세스마다 중복 로드
- GPU/CPU 자원 경합 가능
```

판단:

```text
EasyOCR / PaddleOCR에서는 비용이 큼
1차 이후 바로 도입할 항목은 아님
```

### 대안 C: 비동기 큐 + 요청 ID + 다중 outstanding 요청

장점:

```text
- 프로토콜 확장성 좋음
- 장기적으로 병렬/취소/우선순위 지원 가능
```

단점:

```text
- 현재 line-delimited JSON + 순차 worker 구조와 충돌
- 구현/검증 범위 큼
```

판단:

```text
지금 당장보다 장기 설계 항목
```

### 비동기 큐 / 병렬 처리 권장안

```text
1. 메인 번역 경로에 latest-only queue 검토
2. 진단 경로는 순차 유지
3. worker pool은 보류
4. 다중 outstanding 요청 프로토콜은 장기 과제
```

## 4. static worker 공유 풀 검토

### 현재 구조

EasyOCR / PaddleOCR 어댑터 각각이 아래 형태를 가집니다.

```text
Dictionary<string, PersistentPythonOcrWorker> workerByPythonPath
```

이 구조는 어댑터 인스턴스 단위입니다.

문제:

```text
- 어댑터 인스턴스가 여러 개 생기면 같은 pythonPath로 worker가 중복 생성될 수 있음
- 수명 관리가 각 어댑터 Dispose에 묶여 있음
```

### 대안 A: 엔진별 static 공유 풀

키 예시:

```text
(engineType, pythonExecutablePath, runnerScriptPath)
```

장점:

```text
- 같은 엔진/경로 조합에서 worker 재사용 극대화
- 중복 worker 생성 방지
```

단점:

```text
- Dispose 시점이 복잡해짐
- 참조 카운트 또는 lease 개념 필요
```

### 대안 B: MainWindow 수준 싱글톤 보유

장점:

```text
- 구현 단순
- 현재 앱 구조에서는 이해하기 쉬움
```

단점:

```text
- OCR 어댑터 재사용성과 결합도가 떨어짐
- 테스트가 어려워짐
```

### static 공유 풀 권장안

```text
공용 WorkerRegistry 추가
- key: engineType + pythonPath + runnerScriptPath
- value: shared PersistentPythonOcrWorker handle
- lease / release 또는 reference count 관리
```

이 방식이 맞는 이유:

```text
- 현재 어댑터 구조를 크게 깨지 않음
- 중복 worker 생성 문제를 직접 해결
- 메모리 전달/큐 설계와도 충돌하지 않음
```

## 5. 최종 권장 순서

```text
1. static worker 공유 풀
2. 메인 번역 경로 latest-only queue 검토
3. resident worker 상태 로그/메트릭 보강
4. 메모리 직접 전달 실험
5. 장기적으로 binary framing / pipe / 다중 outstanding 요청 검토
```

## 6. 다음 PR 후보

### PR A

```text
제목 예시:
기능: OCR shared worker registry 2차 적용

범위:
- EasyOCR / PaddleOCR static 공유 풀
- reference count 또는 lease 기반 수명 관리
- 종료 시 정리 검증
```

### PR B

```text
제목 예시:
기능: 자동 번역 latest-only OCR queue 검토 적용

범위:
- 메인 자동 번역 경로에서 최신 요청 우선 정책
- 진단 경로는 기존 순차 유지
```

### PR C

```text
제목 예시:
실험: OCR 메모리 직접 전달 경로 추가

범위:
- Base64 또는 binary framing 실험
- feature flag 또는 별도 adapter로 비교
```

## 7. 한 줄 결론

```text
2차 개선의 첫 구현은 메모리 전달보다 static worker 공유 풀과 latest-only queue가 우선입니다.
```
