# Start→Stop→Start 관절 추적 실패 추적 기록

작성일: 2026-07-21  
범위: Unity iOS 실기기, 운동 세션 UI의 Start→Stop→Start 관절 추적 수명주기  
상태: **사용자 보고 기준 아직 미해결** (아래 증상은 잔존 가능 증상으로 취급)

이 문서는 버그 수정이 아니라, 대화·커밋·기존 문서·현재 코드를 교차해 **무엇이 시도됐고 왜 아직 깨질 수 있는지**를 남긴다.

---

## 1. 증상 타임라인

대화와 커밋 메시지에서 확인된 증상 진행. 사용자는 최종적으로 **아직도 문제가 해결되지 않음**을 보고했다. 아래는 모두 **잔존 가능 증상**이다.

| 시점 | 관찰된 증상 | 대응 커밋(시도) | 잔존 가능 여부 |
| --- | --- | --- | --- |
| 초기 | 첫 Start만 추적 OK, Stop 후 재Start 실패 | `120611c` (stuck `-14` / 슬롯 정리) | 잔존 가능 |
| 이후 | 1번째 OK → 2번째는 활성화되다가 멈춤 → 3번째부터 비활성 | `30fa667` (세션 리셋 / Dispose / recovery 한도) | 잔존 가능 |
| 이후 | 2번째 Start 시 마지막 Stop 관절 포인트 위치에 고정되어 안 움직임 | `9f5997b` (overlay hide + stale drain) | 잔존 가능 |
| 현재(2026-07-21) | 사용자: 여전히 미해결 (어느 형태인지는 재확인 필요) | — | **잔존** |

### 잔존 가능 증상 (재현 시 분류용)

1. **재Start 완전 실패**: Start 후 관절이 한 번도 안 들어오거나, provider `IsReady=false` / 타임아웃 메시지.
2. **2번째만 짧게 살아 있다가 정지**: 잠깐 overlay/추적이 보이다 멈춤, 이후 busy/timeout/recovery.
3. **3번째부터 비활성**: recovery latch 또는 Initialize가 warm stuck 그래프를 재사용.
4. **마지막 Stop 포즈에 고정**: overlay가 이전 메시/이전 JSON을 보여 주거나, 새 프레임이 오지 않아 “멈춘 스켈레톤”처럼 보임.

실기기에서 어느 번호인지 로그로 구분하는 것이 다음 수정의 전제다.

---

## 2. 작성·수정한 문서/계획 (기존 docs 목록과 요지)

| 문서 | 요지와 Start/Stop 관련성 |
| --- | --- |
| `docs/CameraPoseLifecycleRecoveryPlan.md` | 좌표 미러, Start→Stop→Start 실패, 화면 재진입, 전후면 전환 복구 계획. **일반 STOP에서는 native graph를 폐기하지 않고 warm 재사용**하는 규칙을 명시. 세대 번호·restart queue·EnsureCameraReady를 요구. 단계 B까지 코드 반영으로 기록됐으나, 이후 실기기에서 재발 → 아래 3개 fix 커밋이 이 원칙과 **부분적으로 충돌**(Stop 후 hard Dispose를 강제). |
| `docs/CameraPoseTrackingOptimizationPlan.md` | Pose/TTS 성능·비동기 계획. `.liveStream + detectAsync`, `in-flight 1 + pending 1`, STOP 시 generation 무효화·drain, warm estimator 재사용을 권장. Start/Stop 안정성은 목표 중 하나이나, **물리 cancel API 부재**를 전제로 한다. |
| `docs/MediaPipeTroubleshooting.md` | 빌드·Editor Python·패키지 리스크 중심. Start→Stop→Start 런타임 race는 **직접 다루지 않음**. §8 남은 리스크는 Homuler/바이너리·실기기 검증 필요 수준. |
| `docs/current-pose-decision-logic.md` | 자세 규칙·반복 판정. Stop Camera 후 JSONL 리플레이 흐름만 언급. **재시작 stuck과 무관** (임계값/가림 완화는 `994d0bc`). |
| `docs/remaining-optimization-plan.md` | provider 경계 할당·FPS 불일치 등 잔여 성능 계획. Start/Stop 실패의 직접 원인이 아님. |
| `docs/pose-runtime-optimization.md` | C# 후처리 zero-alloc. 재시작 수명주기와 무관. |
| `docs/FeedbackMediaPipeplan.md` / `docs/TestMediaPipeplan.md` | 피드백·테스트 범위. 재시작 race 진단 문서 아님. |

핵심 긴장점: Lifecycle/Optimization 계획은 **warm 재사용**을 선호하지만, 2026-07-21 수정은 실기기 stuck을 피하려 **Stop 후 native hard reset(`AHC_PoseDispose`)** 을 강제했다. warm 경로와 hard reset 경로가 동시에 코드에 남아 있다.

---

## 3. 적용한 코드 수정 (커밋별: 의도 / 변경 요지 / 기대한 효과)

### 선행(2026-07-15 전후) — 수명주기 기반 작업

| 커밋 | 의도 | 변경 요지 | 기대한 효과 |
| --- | --- | --- | --- |
| `6639897` (2026-07-15) `fix: iOS 카메라 회전 및 Start/Stop 크래시 안정화` | Start/Stop 크래시·회전 동기화 | 카메라/Pose 생명주기 직렬화, **일반 STOP 시 PoseLandmarker 재사용**, liveStream·GPU 등 | Stop 후 재시작이 안전하고 빠르게 |
| `53baa99` (2026-07-15) `fix: 카메라 전환 및 관절 추적 재시작 안정화` | 재진입·카메라 전환 복구 | tracking epoch, cancel-drain, `CameraPoseLifecycleRecoveryPlan.md` 추가, UI overlay 분리 등 | START→STOP→START 10회 가능 |

### 2026-07-21 — Start→Stop→Start 직접 수정 (3연타)

| 커밋 | 의도 | 변경 요지 | 기대한 효과 |
| --- | --- | --- | --- |
| `120611c` (10:20) `fix(iOS): Start→Stop→Start 관절 추적 stuck -14 수정` | Stop 후 submit가 `-14`로 거절되는 문제 | `AHCMediaPipePoseBridge.cancelPending`: waiter `-16` 완료 + generation 무효화 후 **preparing/inFlight 슬롯 즉시 클리어**. warm `initialize`도 stuck 슬롯 정리, `lastSubmittedTimestamp`는 보존. | 재Start에서 논리 one-frame 슬롯이 비어 submit 성공 |
| `30fa667` (11:08) `fix(iOS): Start→Stop→Start 추적 2번째 멈춤/3번째 비활성 수정` | 2번째 멈춤·3번째 비활성 | Stop 시 iOS `requireNativeSessionReset` + `isReady=false`. `NeedsReinitialize`로 다음 Start가 `Initialize` 수행. Dispose 시 플래그 있으면 `AHC_PoseDispose`, 씬 핸드오프는 `AbandonManagedResources` 유지. `RecoverAsyncFallback` 한도 2, latch 시 세션 리셋 강제. | cancelled `detectAsync`가 warm 그래프에 남지 않음 |
| `9f5997b` (11:30) `fix(pose): Stop→Start 후 관절 오버레이가 마지막 위치에 고정되던 문제 수정` | 마지막 Stop 포즈 고정 표시 | UI: Stop/Start에서 `HidePoseOverlay`, 현재 세션 첫 live frame에서만 Visible. native: cancel 시 Ready JSON을 `PROCESS_CANCELLED`로 덮음. `DiscardPendingResults` / `DrainStaleNativeResults`. `LatestFrame=null` 보장. | stale mesh/JSON이 새 세션에 보이지 않음 |

### 인접하나 재시작 원인이 아닌 커밋

| 커밋 | 메모 |
| --- | --- |
| `994d0bc` | 팔-다리 가림 오판정 완화. Start/Stop 수명주기와 무관. |
| `9dce43a` | 추론 입력 다운스케일·계측. 성능 경로. |

검증 문구(커밋 본문): Unity iOS **실기기 Start→Stop→Start 확인은 사용자 검증 필요**로 남아 있었고, 사용자 보고상 그 검증은 아직 통과하지 않았다.

---

## 4. 검증·테스트로 확인된 것 / 안 된 것

### 확인된 것 (정적·로컬)

- 커밋별 의도된 가드가 코드에 존재함:  
  - Swift: `cancelPending` 슬롯 클리어, warm initialize 슬롯 클리어, submit `-14`, `submissionQueueCapacity`, generation/`-16` discard.  
  - Provider: `NeedsReinitialize`, `requireNativeSessionReset`, Dispose 시 Abandon vs Dispose 분기, recovery 한도 2, `DrainStaleNativeResults`.  
  - Controller: Start/Stop 시 `LatestFrame=null`, `NeedsReinitialize`면 `Initialize`.  
  - UI: `HidePoseOverlay` + 첫 live frame `Visible`.  
  - Estimator: `DiscardPendingResults`, `TryRecoverFromTimeout` → `AHC_PoseDispose` + re-init.
- 일부 커밋에서 IDE lint / QA 배치 통과가 기록됨 (`994d0bc` 등). 재시작 3커밋은 주로 정적 가드 정합성 언급.

### 확인되지 않은 것 (핵심 공백)

- iPhone 실기기에서 Start→Stop→Start **N회 반복 합격** (Lifecycle 계획 완료 기준: 10회).
- 재발 시 증상 번호(§1)와 Xcode/Unity 로그 쌍 기록.
- `submissionQueueCapacity`가 cancel 직후에도 잡혀 `-14`가 지속되는지.
- Stop이 `fallbackInitializationTask` / `fallbackRecoveryTask` 진행 중에 들어올 때의 경로.
- Dispose의 `teardownQueue` 비동기 해제와 다음 `AHC_PoseInitialize` 겹침.
- Overlay “고정”이 (a) UI Toolkit 잔존 mesh, (b) stale JSON 1회 소비, (c) 새 추론이 전혀 없는 상태인지 구분.

---

## 5. 현재 코드 경로에서 남는 유력 원인

우선순위 높은 순. 파일:심볼 기준.

### P0 — MediaPipe liveStream에 물리 cancel이 없고, 논리 슬롯과 물리 큐가 어긋날 수 있음

- `Assets/Plugins/iOS/AHCMediaPipePoseBridge.swift`
  - `cancelPending()`: generation 무효화 + `preparing*` / `inFlightSubmission` 클리어 + `publishCancellationLocked()` (`PROCESS_CANCELLED` / `-16`).
  - `submitRgba(...)`: 논리 슬롯 점유 또는 `submissionQueueCapacity`(semaphore 1) 실패 시 **`-14`**.
  - `initialize(...)` warm 경로: `poseLandmarker != nil`이면 슬롯만 비우고 **같은 PoseLandmarker 재사용**.
  - `dispose()`: `poseLandmarker = nil` 후 `teardownQueue`에서 비동기 해제.
- 왜 깨질 수 있는지: cancel은 논리 one-frame 슬롯만 비운다. `submissionQueue.async`의 `prepareAndSubmit`가 아직 `defer { submissionQueueCapacity.signal() }` 전이면 새 submit는 **슬롯이 비어도 `-14`**. 또한 cancel된 generation의 late `detectAsync` 콜백은 discard되지만, **그래프 내부 busy**는 SDK가 해제할 때까지 남을 수 있다.

### P0 — Stop 중 Initialize/Recovery task가 있으면 hard reset이 건너뛰어질 수 있음

- `MediaPipePoseTrackingProvider.CancelPendingEstimate()`: init/recovery task가 진행 중이면 bridge cancel을 호출하지 않고 `requireNativeSessionReset=true`, `isReady=false`만 세움.
- `Initialize()` 시작부: `fallbackInitializationTask != null`이면 `CompleteFallbackInitialization()`만 하고 **`Dispose()`를 거치지 않음**.
- `MarkFallbackInitialized()`: `requireNativeSessionReset = false`로 **리셋 플래그를 지움**.
- 왜 깨질 수 있는지: 첫 Start의 백그라운드 `Initialize` 중에 Stop→Start가 오면, 의도한 `AHC_PoseDispose` 없이 warm ready가 되어 **2번째 세션이 stuck 그래프를 물려받을 수 있다**. 이후 busy/timeout → `RecoverAsyncFallback` 2회 → `isReady=false` latch → **3번째 비활성** 패턴과 맞는다.

### P1 — Dispose / Abandon / warm initialize 정책 충돌

- `Dispose()`: `hardResetNativeSession`(캡처 시점의 `requireNativeSessionReset`)이 true일 때만 `IOSMediaPipePoseEstimator.Dispose()` → `AHC_PoseDispose`. 아니면 `AbandonManagedResources()`(전역 native graph 유지).
- Bridge `initialize` warm: landmarker가 살아 있으면 fresh 생성 없이 슬롯만 클리어.
- `JointTrackingController.StopTracking()` 주석은 여전히 “provider warm 유지 / OnDestroy에서만 dispose”를 말하고, provider iOS Stop은 사실상 **다음 Start에서 hard reset**을 요구.
- 왜 깨질 수 있는지: Abandon 경로가 한 번이라도 타면 managed 쪽만 버리고 native stuck이 남는다. 반대로 매 Stop마다 Dispose하면 teardown 중첩·지연으로 다음 Start가 불안정해질 수 있다.

### P1 — Recovery latch가 “비활성”을 고착

- `RecoverAsyncFallback`: `maximumConsecutiveRecoveries = 2` 초과 시 `requireNativeSessionReset=true`, `isReady=false`, 사용자에게 Stop/Start 안내.
- `TryRecoverFromTimeout`: `AHC_PoseDispose` + `InitializeNative`. Dispose/init이 겹치거나 teardown 중이면 복구 실패 가능.
- 왜 깨질 수 있는지: 2번째 세션에서 `-14`/timeout이 반복되면 UI상 “잠깐 되다 멈춤 → 이후 비활성”으로 보인다. 사용자가 Stop/Start를 해도 P0 race가 남으면 재발.

### P2 — Overlay “고정”은 입력 정지와 UI 잔존 mesh가 겹친 증상일 수 있음

- `MobileWorkoutPrototypeView.HidePoseOverlay` / `HandleTrackingFrameReceived`의 첫 프레임 `Visible`.
- `OnGeneratePoseOverlayContent`: 조건 불만족 시 early return → **UI Toolkit은 이전 mesh를 지우지 않음** (그래서 Hidden 전략을 씀).
- `IOSMediaPipePoseEstimator.DiscardPendingResults` / bridge `latestJson` 덮어쓰기.
- 왜 깨질 수 있는지: Hidden이 기기/타이밍에 실패하거나, live frame 1회만 오고 이후 submit가 막히면 “마지막 포즈에 고정”처럼 보인다. 반대로 추적이 완전 실패하면 overlay는 Hidden이라 **빈 카메라**로 보일 수도 있어, 사용자 표현과 로그를 맞춰야 한다.

### 관련 심볼 빠른 지도

```text
UI Start/Stop
  MobileWorkoutPrototypeView.StartWorkoutRoutine / StopWorkoutAndReplayRoutine
    → HidePoseOverlay
    → JointTrackingController.StartTracking / StopTracking
         → LatestFrame = null
         → CancelPendingEstimate (requireNativeSessionReset, isReady=false)
         → NeedsReinitialize ? Initialize → Dispose(AHC_PoseDispose?) → AHC_PoseInitialize
         → TrackingLoop → TrySubmitFrame
              → AHCMediaPipePoseBridge.submitRgba (-14 / capacity)
              → detectAsync → finishDetection (generation / -16)
              → DiscardPendingResults / DrainStaleNativeResults
              → RecoverAsyncFallback / TryRecoverFromTimeout
```

---

## 6. 다음에 확인할 로그/실기기 시나리오 (최소 체크리스트)

기기: 동일 iPhone, Development Build, 콘솔 연결. 커밋 HEAD와 Xcode export가 **같은 bridge**인지 확인.

### 시나리오

1. 콜드 런치 → 세션 화면 → **Start** (전신 인식될 때까지 대기) → **Stop** → 3초 대기 → **Start**.
2. 위 사이클을 **10회** 연속 (Lifecycle 계획 완료 기준).
3. Start 직후 1초 안에 Stop → 즉시 Start (빠른 토글).
4. 첫 Start가 아직 “추적 시작 중”일 때 Stop → Start (init race).
5. 2번째 Start에서 관절이 보이면 몸을 크게 움직여 **고정인지 살아있는 추적인지** 확인.

### 로그에서 볼 키워드

| 키워드 / 코드 | 의미 |
| --- | --- |
| `MediaPipe is still processing the previous frame` / native `-14` | 논리 슬롯 또는 `submissionQueueCapacity` busy |
| `PROCESS_CANCELLED` / `-16` | cancel/세대 무효화 결과 (정상 discard일 수 있음) |
| `MediaPipe remained busy after cancellation` / `asynchronous inference timed out` | poll/busy → recovery 경로 |
| `automatic recovery was already attempted` | recovery 2회 latch → 비활성 |
| `[MediaPipePoseTrackingProvider] Using ... fallback` | `MarkFallbackInitialized` (리셋 플래그 클리어 시점) |
| overlay Visible 전에 관절이 보임 | Hide 실패 또는 stale mesh |
| Start 후 landmark/frame age가 증가하지 않음 | submit 실패 또는 결과 미도착 |

### 최소 기록 항목

- [ ] 몇 번째 Start에서 깨지는지 (1/2/3+)
- [ ] 증상 유형: 완전실패 / 짧게 후 정지 / 포즈 고정 / 비활성
- [ ] 그때의 LastError 문자열
- [ ] `-14` 연속 횟수, recovery 호출 횟수
- [ ] Stop 시점에 init/recovery task가 돌고 있었는지 (로그 타임스탬프)
- [ ] Unity 커밋 해시 + iOS export 시각 (구 bridge 혼입 여부)

---

## 7. 권장 다음 수정 방향 (구현하지 말고 제안만)

1. **Stop/Start 단일 상태 머신으로 native ownership 고정**  
   Idle→Starting→Running→Stopping→Idle에서만 submit 허용. Stopping은 `cancelPending` + **submissionQueue barrier(capacity 반환 대기)** + (필요 시) `AHC_PoseDispose` 완료까지 Start를 큐잉. “warm 재사용”과 “hard reset”을 문서/코드에서 하나로 선택.

2. **init/recovery 중 Cancel 경로 수정**  
   `fallbackInitializationTask` 재부착 시 `requireNativeSessionReset`이 살아 있으면 `MarkFallbackInitialized` 전에 **반드시 Dispose→fresh Initialize**. 진행 중 Stop이 플래그를 지우지 못하게 한다.

3. **`-14` / capacity를 세션 경계 지표로 계측**  
   cancel 직후·첫 성공 submit까지 `-14` 횟수, capacity wait 실패, generation discard를 telemetry로 남겨 P0를 실측으로 확정.

4. **Overlay는 Hidden만으로 부족하면 mesh clear API 검토**  
   첫 live frame 전까지 clear/empty draw를 강제하거나, session id가 다른 LatestFrame은 그리지 않는다.

5. **실기기 합격 기준을 코드 머지 조건에 명시**  
   Start→Stop→Start 10회 + 빠른 토글 20회에서 유효 Pose frame 연속 수신, recovery latch 0.

이 문서는 조사 기록이다. 위 제안의 구현은 별도 승인·작업에서 진행한다.
