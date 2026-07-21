# 카메라 캡처/전송 경로 추가 최적화 계획

작성일: 2026-07-20

작성 모델: Claude Opus 4.8

상태: A0/A1/A2 1차 구현 반영 (`9dce43a`) — 실기기 Profiler·정확도 A/B 검증 대기

대상: `CameraCaptureSource`, `MobileWorkoutPrototypeView`, `JointTrackingController`, `MediaPipePoseTrackingProvider`(`TryReadFallbackPixels`/`BuildFrame`), iOS `AHCMediaPipePoseBridge.swift` 입력 경계

포지셔닝: **1차 비동기 추론(`.liveStream + detectAsync`) 전환 이후에 남은 "카메라 프레임 취득 → 네이티브 입력 전달" 구간의 다음 단계 카메라 특화 최적화**

---

## 0. 이 문서의 위치와 상태

이 문서는 구현 전에 작성한 계획서다. 사용자 승인 후 A0(계측)·A1(설정 통일)·A2(추론용 downscale)가 커밋 `9dce43a`에 반영되었다. A3~A5는 아직 미착수이며, downscale 기본값(480×360)과 최종 성능 판정은 실기기 계측 후 확정한다.

이미 완료되었거나 다른 문서가 다루는 범위는 이 문서에서 다시 구현하지 않는다.

- `docs/CameraPoseTrackingOptimizationPlan.md`: iOS 카메라/추론/TTS 비동기화 1차 계획. `.liveStream + detectAsync` 전환, single-flight, fresh-frame gate, TTS 안정화가 1차 반영됨. 그 문서의 P4가 `GetPixels32`/해상도/native capture를 **조건부**로만 언급하며 실기기 검증 대기 상태다.
- `docs/remaining-optimization-plan.md`: provider 경계 GC(JSON 파싱·`BuildFrame`), FPS 3중 불일치로 인한 분석 window 시간 왜곡, JSONL 로그 등 **카메라 캡처 자체보다 후단(post-capture)** 이슈.
- `docs/pose-runtime-optimization.md`: 자세 후처리(안정화→feature→규칙→로그) C# zero-alloc 완료.
- `docs/CameraPoseLifecycleRecoveryPlan.md`, `docs/ios-black-screen-editor-vs-device.md`: 카메라 생명주기 복구와 iOS 검은 화면 진단.

이 문서는 위 문서들이 **조건부로 미룬 카메라 입력 경계**를 하나의 계획으로 모으고, "측정 없이 해상도/FPS를 확정하지 않는다"는 기존 원칙을 그대로 유지한 채 단계적 실행안을 제시한다.

핵심 요약: **추론 자체는 비동기가 되었지만, 그 앞단인 `GetPixels32` 전체 프레임 CPU readback과 프리뷰=추론 해상도 결합은 그대로 남아 있다. 이 구간은 추론 FPS와 무관하게 매 성공 프레임마다 고정 비용을 만들며, 발열·배터리·메인 스레드 spike의 잔여 원인 후보다.**

---

## 1. 왜 추가 카메라 최적화가 필요한가

1차 최적화는 "동기 `detect`가 Unity 메인 스레드를 막는다"는 가장 큰 구조적 병목을 겨냥했고, iOS는 `.liveStream + detectAsync`로 전환되었다(`AHCMediaPipePoseBridge.swift`, `MediaPipePoseTrackingProvider`의 `IAsyncPoseEstimator` 경로). 이로써 **네이티브 추론 시간**은 더 이상 Unity 프레임을 직접 블로킹하지 않는다.

그러나 다음 사실이 남아 있다.

- 추론이 비동기가 되어도, MediaPipe에 넘길 입력 픽셀은 여전히 **Unity 메인 스레드에서 `GetPixels32`로 동기 CPU readback** 된다(`MediaPipePoseTrackingProvider.TryReadFallbackPixels` 809행). 이 호출은 GPU→CPU 동기 복사라 짧지 않을 수 있고, 추론을 async로 옮겨도 이 readback은 메인 스레드에 남는다.
- readback 데이터량은 **추론 FPS가 아니라 프리뷰 해상도**에 비례한다. 프리뷰와 추론이 같은 `WebCamTexture`를 공유하므로(§2), 프리뷰를 위해 필요한 해상도가 곧 추론 입력 해상도가 되어 버린다.
- 이 비용은 관절 개수·정확도와 무관한 "고정 오버헤드"라서, 후처리 zero-alloc(`pose-runtime-optimization.md`)이나 provider GC 최적화(`remaining-optimization-plan.md`)로는 줄지 않는다.
- 실시간 코칭에서 중요한 지표는 평균 FPS가 아니라 **p95 프레임 시간과 결과 age**다. `GetPixels32`가 만드는 산발적 long-frame은 평균 FPS에 잘 드러나지 않는다.

즉 1차가 "추론을 메인 스레드에서 뺐다"면, 이 문서는 "추론에 **입력을 밀어 넣는 카메라 경로** 자체의 고정 비용과 해상도 결합을 다음 단계로 줄인다". 단, 아래 모든 항목은 P0 계측이 실제 비중을 증명하기 전에는 수치를 확정하지 않는다.

---

## 2. 현재 카메라 파이프라인과 데이터 비용

### 2.1 파이프라인 구조

```text
AVFoundation (iOS) / OS 카메라
   │  (Unity WebCamTexture 하나가 소유)
   ▼
WebCamTexture ───────────────┬────────────────────────────┐
   │ (프리뷰 표시)            │ (추론 입력)                 │
   ▼                          ▼                             │
RawImage / preview UI     JointTrackingController          │
 (mobileCameraFps 20)      .TrackingLoop (mobilePoseFps 8) │
                              │                             │
                              │ TryReserveFreshCameraFrame  │
                              │  (didUpdateThisFrame /       │
                              │   updateCount / textureId)   │
                              ▼                             │
                        EstimateCurrentFrame               │
                              │ PreviewTexture 전달          │
                              ▼                             │
              MediaPipePoseTrackingProvider                 │
                .TryReadFallbackPixels                      │
                  │ webCamTexture.GetPixels32(fallbackPixels)◀── 전체 RGBA CPU readback
                  ▼                                          │
              Color32[] (재사용 버퍼) + rotation + mirror    │
                  │ TrySubmitFrame(pixels,w,h,ts,mirror,rot) │
                  ▼                                          │
        IOSMediaPipePoseEstimator  ──►  Swift 네이티브 bridge │
                  │ (.liveStream + detectAsync, 비동기)       │
                  ▼                                          │
        TryGetLatestResult ──► LandmarkFrame(JSON) ──► BuildFrame(33 TrackedJoint)
                  ▼
        JointTrackingFrame ──► 후처리/분석/UI/로그
```

포인트: **프리뷰와 추론이 동일한 `WebCamTexture` 인스턴스를 공유**한다(`CameraCaptureSource.WebCamTexture`/`PreviewTexture`, `JointTrackingController.EstimateCurrentFrame`이 `cameraSource.PreviewTexture`를 그대로 provider에 전달). 따라서 프리뷰 품질을 위한 해상도가 곧 추론 readback 크기를 결정한다.

### 2.2 현재 기본 설정값 (코드 확인)

| 값 | 위치 | 비고 |
| --- | --- | ---: |
| 카메라 640×480 @ 20 FPS | `Main.unity`(`mobileCameraWidth/Height/Fps`), `MobileWorkoutPrototypeView` SerializeField 기본값 | 모바일 런타임 실제값 |
| Pose 8 FPS | `Main.unity` `mobilePoseFps`, `ConfigureSamplingRate(8)` | 추론 요청 주기 |
| UI 렌더 30 FPS | 앱 렌더링 목표 | 프레임 예산 약 33.3 ms |
| `CameraCaptureSource` 기본 1280×720 @ 30 | `CameraCaptureSource.cs` 11~13행 SerializeField | 모바일 UI가 `ConfigureCapture`로 덮어씀 |
| iOS 하드 클램프 640×480 | `CameraCaptureSource.StartCameraRoutine` 362~370행(`#if UNITY_IOS`) | 요청이 640×480 초과면 강제로 낮춤 |

`MobileWorkoutPrototypeView`는 시작 시 `cameraSource.ConfigureCapture(mobileCameraWidth, mobileCameraHeight, mobileCameraFps)`와 `trackingController.ConfigureSamplingRate(mobilePoseFps)`를 호출한다(1933~1934행). 즉 실제 모바일 값은 640×480@20/Pose 8이며, `CameraCaptureSource`의 1280×720 기본값은 다른 경로(예: Inspector 직접 사용, 에디터, 다른 씬)에서만 유효하다.

### 2.3 데이터 비용

640×480 RGBA 한 프레임:

```text
640 × 480 × 4 bytes = 1,228,800 bytes ≈ 1.17 MiB
```

Pose 8 FPS에서 `GetPixels32`가 통과시키는 관리 배열 데이터량:

```text
1.17 MiB × 8 = 약 9.38 MiB/s
```

이 값은 **readback만** 계산한 것으로, GPU→CPU 동기화 대기, 네이티브 bridge로의 추가 복사, `MPImage`/`CVPixelBuffer` wrapper 생성, 회전/미러 처리, JSON 파싱은 포함하지 않는다. `fallbackPixels` 배열 자체는 재사용되므로(809행) 배열 할당은 아니지만, GPU→CPU 복사 비용과 메인 스레드 점유는 매 프레임 그대로 발생한다.

추론 입력만 필요하다면 프리뷰 화질과 별개로 훨씬 작은 해상도로도 충분할 수 있다(참고, 확정 아님):

| 해상도 | RGBA 1프레임 | 640×480 대비 | 8 FPS 초당 |
| --- | ---: | ---: | ---: |
| 640×480 | 약 1.17 MiB | 100% | 약 9.38 MiB/s |
| 480×360 | 약 0.66 MiB | 56.25% | 약 5.27 MiB/s |
| 320×240 | 약 0.29 MiB | 25% | 약 2.34 MiB/s |

정확도(원거리 전신, 작은 손목·발목)에 대한 영향은 반드시 A/B로 확인해야 하므로, 위 표는 목표가 아니라 후보 범위다.

---

## 3. 이미 끝난 것 vs 이번 문서 범위

### 3.1 이미 반영/완료 (이 문서에서 다시 구현하지 않음)

- iOS 추론 비동기화(`.liveStream + detectAsync`), single-flight, 취소 generation — `CameraPoseTrackingOptimizationPlan.md` 1차 반영.
- 동일 카메라 프레임 재추론 방지 게이트 — `JointTrackingController.TryReserveFreshCameraFrame`(`didUpdateThisFrame` + `updateCount` + textureId).
- Pose 처리율을 프리뷰보다 낮게 분리(20 vs 8), 픽셀 배열(`fallbackPixels`) 재사용.
- 후처리 zero-alloc — `pose-runtime-optimization.md`.
- provider 경계 GC(JSON name, `BuildFrame` 풀링), FPS/window 시간 왜곡, JSONL — `remaining-optimization-plan.md`가 소유.

### 3.2 이번 문서 범위 (카메라 입력 경계 특화)

- `GetPixels32` 전체 프레임 CPU readback의 계측과 대안(추론용 downscale, `AsyncGPUReadback`, native capture).
- 프리뷰 해상도 = 추론 해상도 결합의 분리 가능성.
- 요청 해상도/FPS 설정 원천 분산(파일별 기본값 불일치)의 통일.
- 기존 fresh-frame gate의 잔여 이슈(readback 위치, 프리뷰 downscale 미연동).
- 회전/미러/포맷 변환 비용의 계측과 경계 정리.

### 3.3 명시적 비범위

- 자세 규칙/rep 판정 의미 변경, UI 디자인 개편, 서버 업로드 도입.
- provider GC(JSON→binary) 자체 — `remaining-optimization-plan.md` / `CameraPoseTrackingOptimizationPlan.md` P3 소유.
- TTS 경로 — 별도 문서.

---

## 4. 발견/병목 목록 (우선순위 표)

우선순위는 "위험 대비 효과"와 "선행 계측 필요성"을 함께 반영한다. 모든 순위는 P0 계측 결과에 따라 조정될 수 있다.

| # | 발견 | 성격 | 코드 근거 | 위험 | 우선순위 |
| --- | --- | --- | --- | --- | --- |
| C0 | 카메라 입력 경계 **구간별 계측 부재** | 관측성 | 기존 성능 수집이 평균 FPS/inference 중심 | 매우 낮음 | **P0 (선행)** |
| C1 | 요청 해상도/FPS **설정 원천 분산·기본값 불일치** | 유지보수/일관성 | `CameraCaptureSource` 1280×720 vs `Main.unity`/`MobileWorkoutPrototypeView` 640×480, iOS 하드클램프 | 낮음 | 높음(저위험) |
| C2 | **프리뷰 해상도 = 추론 해상도 결합** | 성능/정확도 | 프리뷰·추론이 같은 `WebCamTexture` 공유 | 중 | 높음(계측 후) |
| C3 | **`GetPixels32` 전체 프레임 CPU readback** | 성능/발열 | `TryReadFallbackPixels` 809행, `TryGetPixels32` 506행, `TryCaptureJpeg` 489행 | 중~높음 | 높음(계측 후) |
| C4 | fresh-frame **게이트 잔여 이슈** | 최신성 | `TryReserveFreshCameraFrame`는 있으나 readback은 여전히 full-res·메인 스레드 | 낮음 | 중간 |
| C5 | **회전/미러/포맷 변환 비용** | 성능/정확성 | `ResolveImageRotation`, `mirrorXOutput`/`invertYOutput`, `verticallyMirrored` 전달 | 낮음~중 | 중간(계측 후) |
| C6 | (조건부) **AsyncGPUReadback / downscale / native AVFoundation capture** | 대규모 최적화 | `GetPixels32` 경로 전면 대체 | 높음 | 최후(앞 단계로 부족할 때) |

---

## 5. 발견별 상세 (문제 · 코드 근거 · 제안 · 장단점 · 대안)

### C0. 카메라 입력 경계 구간별 계측 (P0, 선행 필수)

**문제.** 현재 성능 수치는 평균 Pose FPS와 평균 inference 중심이라, "프레임 취득 → readback → 네이티브 전달" 각 구간의 p95/p99를 분리해 보여주지 못한다. 어떤 항목(C2/C3/C5)이 실제 병목인지 모른 채 downscale/native capture 같은 큰 변경을 하면 회귀 위험만 커진다.

**코드 근거.** `MediaPipePoseTrackingProvider.LastInferenceMilliseconds`는 provider 전체 소요(`EstimatePose` 시작~종료)만 재고, `GetPixels32` 자체, 회전/미러, 네이티브 submit 반환은 분리되어 있지 않다. `JointTrackingController.LastInferenceMilliseconds`도 provider 왕복 전체다.

**제안.** 다음 구간에 `ProfilerMarker` / `os_signpost`를 추가하고 p50/p95/p99를 수집한다.

1. fresh-frame gate 대기(`TryReserveFreshCameraFrame`가 false를 반환하며 소비한 프레임 수)
2. `GetPixels32` readback 시간(단독)
3. 회전/미러/포맷 정리 시간
4. C#→네이티브 `TrySubmitFrame` 반환 시간
5. submit → `TryGetLatestResult` 성공까지의 result age
6. `BuildFrame`(33 관절 생성)

추가 지표: latest result age, 의도적 skip 대 오류 drop 분리, thermal state, 640×480 대비 downscale 후보 해상도의 정확도.

**장점.** 이후 모든 결정을 수치로 정당화한다. 회귀 원인을 좁힌다.
**단점.** 계측 코드 자체의 오버헤드(마커는 경미). Development build 값과 Release 체감을 분리해야 한다.
**대안 비교.**

| 대안 | 판단 |
| --- | --- |
| 지금 바로 downscale/native capture 착수 | 비권장(측정 없는 대규모 변경) |
| 평균 지표만 유지 | 비권장(long-frame 원인 미파악) |
| 구간 마커 + 실기기 p95 | **권장** |

### C1. 요청 해상도/FPS 설정 원천 분산·기본값 불일치 (저위험, 1순위)

**문제.** 해상도/FPS 기본값이 파일마다 다르다. `CameraCaptureSource`는 1280×720@30을 기본으로 갖고(11~13행), 모바일 실제값은 `Main.unity`/`MobileWorkoutPrototypeView`의 640×480@20으로 `ConfigureCapture`가 덮으며(1933행), iOS는 별도로 640×480 하드 클램프를 한다(`StartCameraRoutine` 362~370행). 이 세 곳이 서로 다른 의도를 가지면 "실제로 카메라에 요청되는 해상도"를 한눈에 알기 어렵고, 튜닝 시 한 곳만 바꾸는 실수가 생긴다.

**코드 근거.**
- `CameraCaptureSource.cs` 11~13행: `requestedWidth=1280, requestedHeight=720, requestedFps=30`.
- `CameraCaptureSource.cs` 362~370행: iOS에서 `width>640||height>480`이면 640×480으로 강제.
- `MobileWorkoutPrototypeView.cs` 109~112행 + 1933~1934행: 640×480@20, Pose 8로 override.
- `Main.unity` 459~462행: 동일 값이 씬에 직렬화됨.

**제안 (저위험, 의미 불변).**
- 모바일 성능 값을 단일 원천(기존 `RuntimeQualityController`/performance profile, `RuntimeQualityController.cs`의 `Apply`가 이미 `ConfigureCapture`를 호출)에서 관리하고, `CameraCaptureSource`의 SerializeField 기본값을 실제 모바일 기본과 모순되지 않게 정리하거나 주석으로 "런타임 override 대상"임을 명시.
- iOS 하드 클램프는 유지하되, "요청값이 클램프와 다를 때 로그로 한 번 알림" 정도만 추가해 원천 불일치를 관측 가능하게 함.
- `remaining-optimization-plan.md`의 FPS 단일화(발견 2)와 정합. 단, 그 문서는 Pose/analysis FPS를, 이 문서는 카메라 해상도/FPS를 다룬다.

**장점.** 튜닝·A/B가 한 곳에서 가능. 회귀 위험 거의 없음. C2/C3 A/B의 전제 조건.
**단점.** 기능 개선이 아니라 정리라 체감 성능 변화는 없음.
**대안 비교.**

| 대안 | 위험 | 판단 |
| --- | --- | --- |
| 단일 profile로 통일 + 클램프 로그 | 매우 낮음 | **권장** |
| SerializeField 기본값만 640×480로 수정 | 매우 낮음 | 임시(원천 분산은 남음) |
| 현행 유지 | 0 | 비권장(튜닝 실수 지속) |

### C2. 프리뷰 해상도 = 추론 해상도 결합

**문제.** 프리뷰와 추론이 같은 `WebCamTexture`를 공유하므로, 사용자가 화면 구도를 보기 위한 해상도가 곧 추론 readback 크기가 된다. 추론은 320×240으로 충분할 수 있어도, 프리뷰를 위해 640×480을 유지하면 readback도 640×480로 이뤄진다. 즉 "추론 정확도에 필요한 해상도"와 "프리뷰 화질에 필요한 해상도"를 독립적으로 고를 수 없다.

**코드 근거.** `JointTrackingController.EstimateCurrentFrame` 391~395행이 `cameraSource.PreviewTexture`(= `WebCamTexture`)를 그대로 provider에 넘기고, provider는 그 텍스처 전체를 `GetPixels32`한다.

**제안 (계측 후, 저위험부터).**
- 1순위: 프리뷰는 640×480 유지, **추론 입력만** GPU에서 480×360 또는 320×240으로 downscale한 별도 소스를 provider에 전달. downscale은 `Graphics.Blit` → 작은 `RenderTexture` → readback 경로로 구현하면 CPU readback 데이터량이 §2.3 표만큼 줄어든다.
- 이때 좌표계는 정규화(0~1) 기준이라 landmark 좌표 자체는 해상도 무관하지만, 회전/미러 매핑과 오버레이 정렬을 반드시 재검증.

**장점.** 프리뷰 화질을 희생하지 않고 readback·네이티브 복사량을 줄임. 발열·배터리 개선 기대.
**단점.** downscale RenderTexture와 Blit가 GPU 부하를 약간 추가. 작은 관절/원거리 정확도 저하 가능 → A/B 필수.
**대안 비교.**

| 대안 | readback 감소 | 정확도 위험 | 판단 |
| --- | --- | --- | --- |
| 추론 전용 downscale(프리뷰 분리) | 큼 | 중(A/B 필요) | **1순위(계측 후)** |
| 프리뷰까지 함께 낮춤 | 큼 | 중 + 화질 저하 | 차선 |
| 결합 유지 | 0 | 0 | C3가 무해로 확인되면 유지 |

### C3. `GetPixels32` 전체 프레임 CPU readback

**문제.** 매 성공 프레임마다 GPU→CPU 동기 readback이 Unity 메인 스레드에서 일어난다. 추론을 async로 옮겨도 이 readback은 남으며, 데이터량은 해상도에 비례(§2.3)한다. 산발적 long-frame과 발열의 잔여 원인 후보다.

**코드 근거.**
- `MediaPipePoseTrackingProvider.TryReadFallbackPixels` 809행: `webCamTexture.GetPixels32(fallbackPixels)`(버퍼 재사용).
- `CameraCaptureSource.TryGetPixels32` 506행, `TryCaptureJpeg` 489행, `RemoteApiPoseTrackingProvider` 117행도 `GetPixels32` 사용(경로별로 목적이 다름: JPEG/원격은 hot path가 아닐 수 있음 → 계측으로 분리).

**제안 (단계적, 계측 후).**
1. C2의 추론용 downscale로 readback **데이터량**을 먼저 줄인다(가장 안전).
2. readback **위치/방식**을 개선: `AsyncGPUReadback`로 GPU→CPU 복사를 비동기화하여 메인 스레드 대기를 제거(단, 1프레임 지연과 완료 콜백 소유권 관리 필요).
3. 재사용 `NativeArray` + `AsyncGPUReadback.RequestIntoNativeArray`로 관리 힙 압력과 복사를 함께 줄이는 방식 A/B.
4. iOS 포맷/회전을 검증한 `CVPixelBuffer` 직접 경로는 C6(native capture)로 승격.

**장점.** 메인 스레드 readback 대기 제거 가능(2/3). 데이터량 감소(1).
**단점.** `AsyncGPUReadback`는 결과 지연·콜백 수명 관리가 필요하고, 기존 동기 흐름(`TryReserveFreshCameraFrame` → 즉시 submit)과 타이밍 계약이 바뀐다. 잘못 적용하면 stale 프레임 submit 위험.
**대안 비교.**

| 대안 | 메인 스레드 hitch | 위험 | 판단 |
| --- | --- | --- | --- |
| downscale로 데이터량만 축소 | 부분 감소 | 낮음 | **1순위** |
| `AsyncGPUReadback`(+NativeArray) | 크게 감소 | 중(지연·수명) | 2순위(계측이 정당화 시) |
| native capture로 readback 제거 | 제거 | 높음 | C6 최후 |
| 현행 동기 `GetPixels32` 유지 | 남음 | 0 | 계측에서 무해 확인 시 |

### C4. fresh-frame 게이트의 잔여 이슈

**문제.** 동일 프레임 재추론은 이미 `TryReserveFreshCameraFrame`가 막는다(중복 추론 방지는 완료). 다만 게이트를 통과한 뒤에도 readback은 여전히 full-res·메인 스레드 동기이며, 게이트는 "프리뷰 downscale"이나 "AsyncGPUReadback 완료 여부"와는 연동되지 않는다. 즉 게이트는 **중복**은 막지만 **비용**은 줄이지 않는다.

**코드 근거.** `JointTrackingController.TryReserveFreshCameraFrame` 340~366행은 textureId/`didUpdateThisFrame`/`updateCount`만 검사하고, 이후 `EstimateCurrentFrame`이 곧바로 동기 readback으로 이어진다.

**제안.**
- C2/C3 도입 시 게이트와 readback 완료의 관계를 명시적 계약으로 정의(예: AsyncGPUReadback 요청 in-flight 중에는 새 게이트 통과를 보류하거나, 최신 요청만 유지).
- 게이트 자체 로직은 유지(iOS에서 `didUpdateThisFrame`가 authoritative라는 기존 주석 근거를 존중).

**장점.** C3의 비동기화와 충돌 없이 최신성 유지.
**단점.** 상태가 늘어 테스트 조합 증가.
**대안 비교.** 게이트를 시간 기준으로 바꾸는 것은 `remaining-optimization-plan.md`의 window 이슈와 별개이며, 여기서는 "게이트-readback 계약"만 다룬다.

### C5. 회전/미러/포맷 변환 비용

**문제.** iOS `WebCamTexture`는 `videoRotationAngle`과 `videoVerticallyMirrored`를 가지며, 프레임을 네이티브로 넘길 때 회전/미러 메타데이터를 함께 전달한다. downscale(C2)이나 readback 방식 변경(C3) 시 이 회전/미러 매핑이 깨지면 좌우 관절/오버레이가 잘못 정렬될 수 있다. 또한 RGBA↔네이티브가 기대하는 포맷(예: 32BGRA/`CVPixelBuffer`) 변환이 추가 복사를 만들 수 있다.

**코드 근거.**
- `MediaPipePoseTrackingProvider.ResolveImageRotation` 958~968행(`imageRotationDegrees` 우선, 아니면 `webCamTexture.videoRotationAngle`).
- `TryReadFallbackPixels` 810~811행이 `rotationAngle`, `verticallyMirrored`를 읽어 `TrySubmitFrame`에 전달(540~547행).
- `mirrorXOutput`/`invertYOutput`(36~37행, `BuildFrame` 850~851행)로 출력 좌표를 뒤집음.

**제안 (계측 후).**
- 회전/미러는 가능하면 **한 곳**(네이티브 또는 C#)에서만 적용하고, 이중 변환이 있는지 계측으로 확인.
- 포맷 변환 비용은 native capture(C6)에서 근본 해결되므로, 그 전까지는 "변환이 실제 p95에 유의미한가"만 측정.

**장점.** 좌표 정확성 유지, 불필요한 이중 변환 제거 가능.
**단점.** 회전/미러는 기기·방향 조합이 많아 회귀 위험 → 테스트 매트릭스 필요.
**대안 비교.**

| 대안 | 판단 |
| --- | --- |
| 변환 지점 단일화 + 계측 | 권장 |
| native `CVPixelBuffer`로 포맷/회전 위임 | C6에서 |
| 현행 유지 | 계측 무해 시 유지 |

### C6. (조건부) AsyncGPUReadback 전면화 / native AVFoundation capture

**문제.** 위 단계로도 `GetPixels32`/readback/포맷 변환이 p95의 큰 비중을 계속 차지하면, Unity `WebCamTexture` 대신 iOS AVFoundation `CMSampleBuffer`/`CVPixelBuffer`를 MediaPipe에 직접 전달하는 캡처 경로 교체가 마지막 수단이다.

**코드 근거.** 현재는 Unity `WebCamTexture`가 카메라를 단독 소유하며, provider는 그 텍스처에서만 픽셀을 읽는다(§2.1). native capture는 이 소유권 전체를 바꾼다.

**제안.**
- 진입 조건: P0 계측 + C2/C3/C5 적용 후에도 목표 미달일 때만.
- native가 `CVPixelBuffer`를 직접 소유·전달하면 `GetPixels32`와 CPU 복사를 제거할 수 있으나, 프리뷰 표시(Unity 텍스처 공유), 카메라 권한/생명주기, 회전, 전/후면 전환을 모두 네이티브 기준으로 재작성해야 한다.

**장점.** readback·포맷 변환 원인을 근본 제거.
**단점.** 가장 큰 재작성. Unity `WebCamTexture`와 AVFoundation이 카메라를 **동시에 소유하면 안 됨**(충돌). 프리뷰·권한·회전·생명주기 회귀 위험이 크다.
**대안 비교.**

| 대안 | 효과 | 위험 | 판단 |
| --- | --- | --- | --- |
| downscale + AsyncGPUReadback(C2/C3) | 큼 | 중 | 우선 |
| native AVFoundation capture | 최대 | 높음 | **최후, 앞 단계로 부족할 때만** |

---

## 6. 권장 실행 순서

각 단계는 독립적으로 롤백 가능한 단위로 나누고, 정확도·생명주기 회귀를 성능과 같은 기준으로 검사한다.

1. **P0 — 카메라 입력 경계 계측 (C0).** `GetPixels32`, 회전/미러, native submit, result age를 구간별 p95로 확정. 변경 전 capture 보관.
2. **저위험 설정 통일 (C1).** 해상도/FPS 단일 원천 정리 + iOS 클램프 로그. 체감 변화 없음, A/B 전제 확보.
3. **추론용 downscale (C2).** 프리뷰 유지, 추론 입력만 480×360/320×240 A/B. 정확도 gate 통과 확인.
4. **readback 경로 개선 (C3, C4).** downscale로 데이터량을 줄인 뒤에도 메인 스레드 hitch가 남으면 `AsyncGPUReadback`(+`NativeArray`) 도입, 게이트-readback 계약 정리.
5. **회전/미러/포맷 정리 (C5).** 변환 지점 단일화, 이중 변환 제거(계측이 정당화할 때).
6. **native AVFoundation capture (C6) — 최후.** 위 단계로 목표 미달일 때만. 캡처 소유권 전체 교체.

원칙: 측정 없이 최종 해상도/FPS를 확정하지 않는다. 큰 변경(C6)은 앞 단계 실측이 필요성을 증명할 때만 진행한다.

---

## 7. 성공 지표

아래 값은 P0 baseline 전의 초기 제안이며, 최소 지원 기기와 실제 분포를 확인한 뒤 확정한다.

| 지표 | 초기 목표 | 측정 방법 |
| --- | ---: | --- |
| main-thread CPU frame p95 | ≤ 28 ms(standard) / ≤ 33.3 ms(low) | Unity Profiler(v-sync 대기 제외) |
| `GetPixels32`(또는 대체 readback) 단독 시간 p95 | baseline 대비 유의미 감소, 절대값은 P0 후 확정 | `ProfilerMarker` |
| 100 ms 이상 멈춤 | warm-up 후 0회 | long-frame 카운트 |
| latest result age p95 | baseline 대비 악화 없음 | submit→result 타임스탬프 |
| 유효 Pose FPS | standard ≥ 10, low ≥ 8 | `JointTrackingController.PoseFps` |
| thermal state | 10분 내 critical 0회 | `ProcessInfo.thermalState` |
| 배터리/에너지 | downscale 후 baseline 대비 개선(회귀 없음) | Instruments Energy |
| 정확도 회귀 | detection success 하락 ≤ 2%p, golden rep count 동일 | 결정론 QA + golden clip |
| safety false negative | 0건 | safety fixture |
| 원거리/작은 관절 정확도 | downscale 후보 해상도가 별도 gate 통과 | 전신·원거리·빠른 스쿼트 A/B |

측정 원칙: 평균 하나만 쓰지 않고 p50/p95/p99와 최악 run을 기록. Development 측정과 Release 체감을 구분. 의도적으로 버린 카메라 프레임은 오류 drop과 분리 집계.

---

## 8. 비목표 / 리스크 / 롤백

### 8.1 비목표

- 추론 FPS를 카메라/UI FPS와 같게 만들기.
- 모든 카메라 프레임을 반드시 처리하기(latest-only 유지).
- 측정 전에 native AVFoundation 캡처 재작성부터 시작하기.
- 성능을 위해 안전 관련 자세 오류를 누락시키기.
- 화질을 위해 프리뷰까지 무조건 낮추기(추론과 프리뷰는 분리 가능해야 함).

### 8.2 리스크

- downscale/native capture에서 회전·미러 매핑이 깨져 좌우 관절/오버레이 오정렬.
- `AsyncGPUReadback`의 1프레임 지연·콜백 수명 오관리로 stale 프레임 submit.
- 추론 해상도 축소로 원거리·작은 관절 정확도 저하.
- native capture 시 Unity `WebCamTexture`와 AVFoundation의 카메라 이중 소유 충돌.
- 설정 원천 통일 중 다른 씬/에디터 경로의 기본값 회귀.

### 8.3 롤백 단위

- 추론 downscale → 프리뷰=추론 결합 원복.
- `AsyncGPUReadback` → 동기 `GetPixels32` 원복.
- 회전/미러 단일화 → 기존 이중 경로 원복.
- native capture → Unity `WebCamTexture` 경로 원복.
- 설정 통일 → 파일별 기본값 원복.

중단 기준(하나라도 발생 시 해당 단계 기본 활성화 중단): 새 crash/hang 1건, main-thread p95가 baseline 대비 10% 이상 악화, detection success 2%p 초과 하락, safety false negative, 좌우/오버레이 오정렬, thermal critical.

---

## 9. 관련 문서

- `docs/CameraPoseTrackingOptimizationPlan.md` — 카메라/추론/TTS 비동기화 1차 계획과 P0~P6, P4(조건부 capture/해상도).
- `docs/remaining-optimization-plan.md` — provider 경계 GC, FPS/window 시간 왜곡, JSONL(후단).
- `docs/pose-runtime-optimization.md` — 후처리 zero-alloc 완료와 소유권 규칙.
- `docs/CameraPoseLifecycleRecoveryPlan.md` — 카메라 생명주기 복구.
- `docs/ios-black-screen-editor-vs-device.md` — iOS 검은 화면 에디터 대 실기기 차이.
- `docs/current-pose-decision-logic.md` — 자세 판정·rep 품질 기준(정확도 회귀 판정 근거).

외부 근거(요약): Unity `AsyncGPUReadback`/`ProfilerRecorder`, Apple AVFoundation `CVPixelBuffer`/`CMSampleBuffer`와 프레임 드랍 처리(TN2445), MediaPipe iOS live stream 입력 가이드. 실제 개선 폭은 반드시 P0 실기기 계측으로 확인한다.

---

## 10. 승인 체크리스트

사용자는 아래 항목을 개별로 승인/보류할 수 있다. 승인된 항목만 GPT-5.6 Sol로 구현한다.

- [x] **A0.** P0 카메라 입력 경계 계측(C0) — 구현됨 (`ProfilerMarker` + `Last*Milliseconds`).
- [x] **A1.** 해상도/FPS 설정 원천 통일 + iOS 클램프 로그(C1) — 구현됨.
- [x] **A2.** 프리뷰 유지 + 추론 입력 downscale(C2) — 기본 480×360, Inspector로 ON/OFF 가능. 실기기 정확도 A/B 대기.
- [ ] **A3.** downscale 후 잔여 hitch가 확인되면 `AsyncGPUReadback`/`NativeArray`(C3/C4) 도입 허용.
- [ ] **A4.** 회전/미러/포맷 변환 지점 단일화(C5)를 계측 근거 하에 진행.
- [ ] **A5.** native AVFoundation capture(C6)는 **최후 수단**으로만, 앞 단계 실측이 부족을 증명할 때 별도 승인.
- [ ] **A6.** downscale 후보 해상도(480×360 / 320×240)와 정확도 gate 기준을 P0 결과로 확정하는 데 동의.
- [x] **A7.** 각 단계는 독립 롤백 단위로 나누고, 정확도·생명주기 회귀를 성능과 동일 기준으로 검사하는 데 동의.

승인 후 구현 시: Windows/Android/iOS 스크립트 컴파일 통과, 결정론 QA 통과, golden 입력에서 rep/phase/feedback 동일(다운스케일에 따른 의도된 정확도 변화는 별도 기록), `git diff --check` 통과를 검증 기준으로 한다.
