# 잔여 최적화 분석 및 구현 계획

작성일: 2026-07-20
작성 모델: Claude Opus 4.8
대상: `MediaPipePoseTrackingProvider`, `IOSMediaPipePoseEstimator`, `RealtimeFeedbackOrchestrator`, `SessionJsonlLogger`, 자세 분석 window 및 실시간 UI/TTS 경로

## 0. 이 문서의 위치

이 문서는 실제 코드를 변경하기 전에 작성한 최적화 계획서다. 이미 완료된 최적화는 다음 두 문서에 정리되어 있으며, 본 문서는 그 이후에 **아직 남아 있는 병목**을 코드 근거와 함께 정리한다.

- `docs/pose-runtime-optimization.md`: 자세 후처리(안정화→feature→규칙→로그) C# 할당 제거 (완료)
- `docs/CameraPoseTrackingOptimizationPlan.md`: iOS 카메라/추론/TTS 비동기화 1차 반영 (실기기 검증 대기)

핵심 요약: **하류(post-processing) 경로는 이미 zero-alloc이지만, 그 바로 앞단인 provider 경계에서 매 프레임 대량 할당이 남아 있고, FPS 설정 불일치가 분석 window의 시간 범위를 왜곡한다.**

이 문서에 적힌 구현은 아직 코드에 반영되지 않았다. 반영은 사용자 승인 후 별도로 진행한다.

---

## 1. 발견 요약

| # | 항목 | 성격 | 현재 상태 | 우선순위 |
| --- | --- | --- | --- | --- |
| 1 | provider 경계 할당(JSON 파싱 + BuildFrame) | GC 압력 | 미착수(문서상 §5.1 인지) | 높음(계측 후) |
| 2 | FPS 3중 불일치 → 분석 window 시간 왜곡 | 정확도 + 성능 | 미착수 | 높음(저위험) |
| 3 | JSONL 최종 줄 문자열과 Escape 할당 | I/O/GC | 계획만 있음 | 중간 |
| 4 | rep 카운트 TTS, FindFirstObjectByType, UI 문자열 | 소규모 GC | 미착수 | 낮음 |

---

## 2. 발견 1: provider 경계의 매 프레임 할당

### 2.1 무엇이 문제인가

자세 후처리 경로는 워밍업 후 객체를 재사용하도록 최적화됐다. 그러나 그 경로로 데이터가 들어오기 직전, 즉 native 결과를 C# 객체로 만드는 provider 경계에서는 매 성공 프레임마다 객체가 새로 생성된다. 하류를 zero-alloc으로 만들어도 상류에서 GC 압력이 그대로 발생한다.

### 2.2 코드 근거

iOS 결과 파싱:

- `Assets/Scripts/MediaPipe/IOSMediaPipePoseEstimator.cs`의 `TryParseLatestFrame()`이 `jsonBuffer.ToString()`으로 JSON 문자열 한 개를 만들고 `JsonUtility.FromJson<LandmarkFrame>(json)`을 호출한다.
- `Assets/Scripts/MediaPipe/LandmarkFrame.cs`에서 `landmarks`는 `PoseLandmark[]`이고 `PoseLandmark`는 struct지만 `name` 필드가 string이라, JSON 파싱 중 관절 이름 문자열 33개가 생성된다.

프레임 빌드:

- `Assets/Scripts/RagHealthcare/Pose/Providers/MediaPipePoseTrackingProvider.cs`의 `BuildFrame()`이 `new TrackedJoint[33]`, 관절마다 `new TrackedJoint`, `Guid.NewGuid().ToString("N")`, `new JointTrackingFrame`을 생성한다.
- `Assets/Scripts/RagHealthcare/Pose/JointTrackingFrame.cs`에서 `TrackedJoint`와 `JointTrackingFrame`은 모두 **class**(heap 할당)다.

### 2.3 정량 추정 (코드 구조 기준, Profiler 실측 아님)

| 구간 | 프레임당 할당 |
| --- | ---: |
| JSON 문자열(`ToString`) | 1 (약 2~4KB) |
| `JsonUtility.FromJson` (LandmarkFrame + 배열 + name 33 등) | 약 36 |
| `BuildFrame` (배열 1 + TrackedJoint 33 + Guid 문자열 1 + Frame 1) | 약 36 |
| 합계 | 약 70개 이상/프레임 |

모바일 8 FPS 기준 **초당 약 560~590개**. 이 값은 실제 누락 관절 수, world landmark 포함 여부, 오류율에 따라 달라진다.

### 2.4 구현 방식 (제안, 3단계)

단계 A — 관절 이름 제거 (가장 저렴, 1순위):

- Swift/JSON에서 `name`을 보내지 않거나 파싱하지 않고, C#은 `PoseJointNames.MediaPipe33` 고정 index 매핑만 사용한다.
- `BuildFrame`은 이미 `names[i]`를 static 배열에서 참조하므로, JSON name 파싱만 제거하면 관절 이름 문자열 33개/프레임이 사라진다.

단계 B — 프레임/관절 풀링:

- `MediaPipePoseTrackingProvider`가 `TrackedJoint[33]`와 `JointTrackingFrame`을 1~2개 재사용 슬롯으로 보관한다.
- `Guid.NewGuid().ToString("N")`를 증가 sequence(long)로 교체한다.
- 단, subscriber(렌더러, replay, 로그)가 과거 프레임을 참조할 수 있으므로 **수명 계약**을 명시하거나 double-buffer(2슬롯)로 한 프레임 지연 참조를 허용한다.

단계 C — JSON 제거(binary bridge):

- `docs/CameraPoseTrackingOptimizationPlan.md` P3와 동일. versioned C ABI 고정 landmark 배열로 Swift가 직접 기록, C#은 reusable 배열로 읽는다.

### 2.5 장점

- 실시간 hot path의 초당 수백 개 할당을 제거해 GC pause와 발열·배터리를 줄인다.
- 단계 A는 위험이 거의 없고 즉시 효과가 있다.
- 하류 zero-alloc 최적화의 실제 이득을 GC 지표에서 온전히 드러낸다.

### 2.6 단점

- 단계 B/C는 프레임 소유권 계약이 필요하다. 잘못 적용하면 렌더러/replay가 과거 프레임 대신 최신 값을 보게 되는 데이터 경쟁이 생긴다.
- 단계 C는 Swift/C# struct alignment, 버전 호환, endianness, buffer lifetime 관리 부담이 크고 사람이 디버깅하기 어렵다.

### 2.7 대안 비교

| 대안 | GC 감소 | 위험 | 판단 |
| --- | --- | --- | --- |
| A. name 필드만 제거 | 중 | 매우 낮음 | 1순위 |
| B. frame/joint 풀링 + sequence id | 대 | 중(소유권 계약) | 계측이 GC 병목 확인 시 |
| C. JSON→binary C ABI | 최대 | 높음 | P2-B 이후 정당화될 때만 |
| 아무것도 안 함 | 0 | 0 | 계측에서 GC 무해로 확인되면 유지 |

문서 `pose-runtime-optimization.md` §5.1은 이 경계를 "subscriber 소유권 문제 때문에 의도적으로 미착수"로 남겼다. 따라서 B/C는 반드시 P0 실기기 계측이 GC 비용을 증명한 뒤 진행한다.

---

## 3. 발견 2: FPS 3중 불일치와 분석 window 시간 왜곡

### 3.1 무엇이 문제인가

성능/샘플링 관련 FPS 값이 세 곳에 분산되어 있고, 분석 window 크기가 실제 추론 속도와 다른 값으로 계산된다. 그 결과 의도한 분석 시간 범위(1.2초)와 실제 시간 범위(약 2.25초)가 달라져 깊이·rep 품질 판정이 왜곡된다. 이는 단순 성능 낭비가 아니라 정확도 문제다.

### 3.2 코드 근거

| 위치 | 값 | 용도 |
| --- | ---: | --- |
| `MobileWorkoutPrototypeView.mobilePoseFps` | 8 | 실제 추론 요청 주기(`ConfigureSamplingRate(8)`) |
| `RealtimeFeedbackOrchestrator.expectedPoseFps` | 15 | 분석 window 크기 계산 |
| `PoseEstimatorSettings.targetPoseFps` / provider `targetPoseFps` | 15 / 8 | native 힌트 |

window 용량 계산:

- `RealtimeFeedbackOrchestrator.CreateWindowBuffer()`는 `capacity = ceil(analysisWindowSeconds(1.2) * expectedPoseFps(15)) = 18`슬롯을 만든다.
- 실제 모바일 추론은 8 FPS이므로 18프레임이 채워지는 데 `18 / 8 = 2.25초`가 걸린다.

### 3.3 영향

- 의도한 1.2초 대신 약 2.25초 구간의 데이터로 최소 무릎 각도(깊이)와 시간 누적 경고(rep 품질)를 판정한다.
- 이미 끝난 동작의 최저점이 window에 오래 남아 rep/깊이 판정이 지연된다.
- 빠른 스쿼트에서 정확도가 저하될 수 있다. `docs/CameraPoseTrackingOptimizationPlan.md` §2.4가 이 위험을 명시적으로 경고한다.

### 3.4 구현 방식 (제안)

방식 1 — window를 timestamp 기준으로 계산 (권장):

- `PoseWindowBuffer`/`PoseWindowStats`가 프레임 개수가 아니라 각 프레임의 timestamp를 기준으로 `analysisWindowSeconds` 범위만 통계에 포함한다.
- capacity는 최대 예상 FPS 기준으로 넉넉히 잡되, 통계는 시간창으로 필터링한다.
- 이렇게 하면 실제 FPS가 8이든 12든 분석 시간 범위가 항상 1.2초로 일정하다.

방식 2 — 단일 설정 원천:

- FPS를 하나의 ScriptableObject(performance profile)에서 관리하고 UI·orchestrator·provider·QA가 모두 그 값을 참조한다.
- window capacity 계산도 그 단일 값을 사용한다.

두 방식은 함께 적용하는 것이 이상적이다. 방식 1이 정확도의 근본 해결, 방식 2가 재발 방지다.

### 3.5 장점

- 실제 추론 FPS와 무관하게 분석 시간 범위가 일정해져 깊이·rep 판정이 안정된다.
- 저위험(로직 의미 유지, 계산 기준만 시간으로 변경).
- FPS 튜닝 시 여러 파일을 동시에 수정하는 실수를 방지한다.

### 3.6 단점

- window를 시간 기준으로 바꾸면 결정론 QA fixture의 프레임 타이밍을 함께 점검해야 한다.
- 매우 낮은 FPS에서는 시간창 안 프레임 수가 적어 통계 분산이 커질 수 있어 최소 샘플 수 하한이 필요하다.

### 3.7 대안 비교

| 대안 | 정확도 | 위험 | 판단 |
| --- | --- | --- | --- |
| 시간 기준 window + 단일 설정 | 높음 | 낮음 | 권장 |
| `expectedPoseFps`만 8로 맞춤 | 부분(여전히 프레임 기준) | 매우 낮음 | 임시 완화 |
| 실제 추론 FPS를 15로 올림 | 정확도는 오르나 발열/부하 증가 | 중 | 실기기 계측 후 판단 |
| 유지 | 낮음 | 0 | 비권장 |

---

## 4. 발견 3: JSONL 최종 줄 문자열과 Escape 할당

### 4.1 무엇이 문제인가

`SessionJsonlLogger`는 관절 숫자 포맷을 `stackalloc`으로 최적화했지만, 마지막에 `StringBuilder.ToString()`으로 줄 문자열 한 개를 매번 생성하고, `Escape()`가 `Replace`를 4번 체이닝해 중간 문자열을 만들 수 있다.

### 4.2 코드 근거

- `Assets/Scripts/RagHealthcare/Rag/Logging/SessionJsonlLogger.cs`의 `LogFrame()`이 `WriteRaw(builder.ToString())`을 호출한다.
- 숫자 포맷 `AppendFloat()`는 이미 `stackalloc char[48] + TryFormat`으로 문자열을 만들지 않는다.
- `Escape()`는 `value.Replace(...).Replace(...).Replace(...).Replace(...)` 체인이다.

### 4.3 빈도

- `maxLoggedFrameRate = 5`로 throttle되어 초당 5줄. 각 줄은 33관절 기준 약 2~4KB.
- 발견 1·2보다 작지만 replay 요구 때문에 상시 발생하며 끌 수 없다.

### 4.4 구현 방식 (제안)

- `StreamWriter`에 `builder`의 내용을 `char[]`/`ReadOnlySpan<char>`로 직접 write하여 `ToString()`을 생략한다.
- joint name은 고정 집합이므로 미리 escape된 상수를 사용하거나, escape가 필요 없는 값임을 보장해 `Escape` 호출을 제거한다.
- 더 줄이려면 bounded ring buffer + background writer로 I/O spike를 메인 스레드에서 분리한다(종료 시 flush 필수).

### 4.5 장점

- 초당 5개의 큰 문자열 할당 제거, 로그로 인한 GC/I/O spike 감소.

### 4.6 단점

- background writer는 종료 직전 flush, 앱 강제 종료 시 데이터 손실, thread 동기화를 함께 설계해야 한다.
- span 직접 write는 writer의 char[] 오버로드 사용과 encoding 처리를 검증해야 한다.

### 4.7 대안 비교

| 대안 | 효과 | 위험 | 판단 |
| --- | --- | --- | --- |
| span 직접 write(ToString 제거) | 중 | 낮음 | 1순위 |
| Escape 상수화/생략 | 소 | 낮음 | 함께 적용 |
| background writer | 대 | 중(데이터 손실/동기화) | I/O spike 확인 시 |
| `logFrames=false` | 최대(로그 없음) | replay 불가 | 비권장 |

---

## 5. 발견 4: 소규모 반복 할당

### 5.1 코드 근거

- rep 카운트 TTS: `RealtimeFeedbackOrchestrator.SpeakCorrectRepCount()`가 `correctRepFeedbackFormat.Replace("{0}", countText)`, `"correct_rep_" + CorrectRepCount`, `new PoseFeedbackMessage`를 rep마다 생성한다.
- `feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>()`: receiver가 null인 동안 매 호출 씬 탐색.
- UI: `MobileWorkoutPrototypeView.RefreshDynamicText`가 5Hz로 timer/phase/FPS 문자열(`PoseFps.ToString("0.0")` 등)을 만든다(throttle 적용됨).

### 5.2 구현 방식 (제안)

- rep 카운트: 자주 쓰는 문장을 미리 포맷하거나 정수→문자열 캐시를 사용, id는 sequence 기반으로 관리.
- `FindFirstObjectByType`: 초기화 시 1회 주입(Inspector 참조 또는 Awake 캐시)으로 대체.
- UI: 값이 실제로 바뀔 때만 label을 갱신.

### 5.3 장점 / 단점 / 대안

- 장점: 저빈도지만 누적 GC를 더 줄인다.
- 단점: 효과가 작아 우선순위가 낮고, 과도한 마이크로 최적화는 가독성을 해칠 수 있다.
- 대안: P0 계측에서 이 구간이 유의미하지 않으면 현행 유지. 발견 1~3 완료 후에만 착수.

---

## 6. 권장 실행 순서

1. **P0 실기기 Profiler 계측** — 발견 1의 초당 약 590 할당이 실제 GC pause를 만드는지 수치로 확정한다(`docs/CameraPoseTrackingOptimizationPlan.md` P0 체크리스트, `DevicePerformanceProfiler` 활용).
2. **발견 2(FPS/window)** — 저위험이고 정확도에 직접 영향. 시간 기준 window + 단일 설정.
3. **발견 1 단계 A(name 제거)** — 저위험 GC 절감. 이후 계측이 정당화하면 단계 B/C.
4. **발견 3(JSONL 줄 문자열)** — I/O spike가 확인될 때.
5. **발견 4** — 위 항목 완료 후 잔여 정리.

각 항목은 독립적으로 롤백 가능한 단위로 나누어 적용하고, 결정론 QA와 정확도 회귀를 성능 개선과 같은 기준으로 검사한다.

---

## 7. 검증 기준 (구현 시)

- Windows/Android/iOS 스크립트 컴파일 통과
- 각 타깃 결정론 QA `AI_HEALTHCARE_QA_PASSED`
- 동일 golden input에서 rep count·phase·feedback 결과가 변경 전과 동일(발견 2는 window 시간 범위 정정에 따른 의도된 차이만 허용하고 별도 기록)
- iOS 실기기 Profiler에서 provider 경계 steady-state GC Alloc 감소 확인(발견 1)
- `git diff --check` 통과

---

## 8. 관련 문서

- `docs/pose-runtime-optimization.md`: 완료된 후처리 할당 최적화와 소유권 규칙
- `docs/CameraPoseTrackingOptimizationPlan.md`: 카메라/추론/TTS 비동기화 계획과 P0~P6 단계
- `docs/current-pose-decision-logic.md`: 현재 자세 판정, 깊이, rep 품질 기준
- `docs/module-architecture.md`: 전체 모듈 구조
