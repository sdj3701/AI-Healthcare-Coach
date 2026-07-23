# 카메라 관절 추적 최적화 기록 — 2026-07-22

상태: 코드 반영, Unity 실기기 QA 대기  
대상: `JointTrackingController`, `DevicePerformanceProfiler`, MediaPipe 기반 실시간 스쿼트 추적

이 문서는 기존 [CameraPoseTrackingOptimizationPlan.md](CameraPoseTrackingOptimizationPlan.md)의 비동기 MediaPipe·카메라 수명주기 작업을 대체하지 않는다. 이번 변경은 그 경로 위에서 **샘플 요청 주기**와 **성능 측정 방식**을 보정한 2차 최적화다.

## 1. 목표와 문제 정의

관절 추적은 카메라 프리뷰 FPS와 Pose 분석 FPS가 다르다. 프리뷰는 사용자가 화면을 자연스럽게 보도록 계속 갱신하고, Pose는 매 프레임 실행하지 않고 일정 간격으로 최신 카메라 프레임만 분석한다.

기존 `JointTrackingController`는 Pose 결과가 도착한 뒤에 다음 요청 간격을 다시 기다렸다.

```text
기존 요청 시작
  -> 이미지 읽기 + MediaPipe 추론 + 결과 처리
  -> requestIntervalSeconds 대기
  -> 다음 요청 시작
```

`requestIntervalSeconds = 1 / 12`(약 83ms), 추론 전체 시간이 40ms인 경우 실제 요청 간격은 약 123ms가 된다. 즉, Inspector에 12 FPS를 설정해도 이론상 약 8.1 FPS까지만 나온다.

이것은 안정성을 위한 단일 처리(single-flight) 자체의 문제는 아니다. 이전 추론이 끝나기 전 새 추론을 넣지 않는 원칙은 유지해야 한다. 문제는 추론이 끝난 이후에도 설정 간격을 처음부터 다시 세던 점이다.

이번 목표는 다음과 같다.

1. 추론이 목표 간격보다 빠를 때는 설정한 Pose FPS에 가깝게 샘플링한다.
2. 추론이 목표 간격보다 느릴 때는 대기열을 만들지 않고, 완료 직후 최신 카메라 프레임만 다음 추론에 쓴다.
3. 성능 벤치가 화면 FPS 단위로 같은 추론 시간을 중복 샘플링하지 않도록 고친다.
4. 평균뿐 아니라 긴 멈춤을 확인할 수 있는 p95 추론 시간을 기록한다.

## 2. 반영한 코드 변경

### 2.1 요청 종료 기준에서 요청 시작 기준으로 전환

변경 파일: `Assets/Scripts/RagHealthcare/Pose/JointTrackingController.cs`

새 흐름은 다음과 같다.

```text
Pose 요청 시작 (t0)
  -> 다음 예정 시각 = t0 + requestIntervalSeconds
  -> 단일 MediaPipe 요청 완료
  -> 예정 시각이 아직 오지 않았으면 그 시각까지 대기
  -> 이미 지났다면 즉시 다음 최신 카메라 프레임 요청
```

따라서 이상적인 실제 Pose FPS는 다음의 상한을 따른다.

```text
actualPoseFps ≈ 1 / max(requestIntervalSeconds, inferenceSeconds)
```

예시:

| 설정/측정 | 이전 방식 | 변경 후 |
| --- | ---: | ---: |
| 목표 12 FPS, 추론 40ms | 약 8.1 FPS | 최대 12 FPS |
| 목표 12 FPS, 추론 83ms | 약 6 FPS | 최대 12 FPS 부근 |
| 목표 12 FPS, 추론 130ms | 약 4.7 FPS | 최대 약 7.7 FPS |

마지막 행에서 12 FPS를 강제로 만들지는 않는다. 130ms 동안 이전 요청이 끝나지 않았기 때문에 새 요청을 쌓지 않고, 완료 시점의 최신 프레임으로 넘어간다.

### 2.2 지연으로 놓친 Pose 슬롯을 드롭으로 계상

같은 파일에서 추론 완료 시간이 다음 예정 시각보다 늦으면, 그 사이에 실제로 처리할 수 없었던 Pose 슬롯을 `DroppedFrameCount`에 더한다.

이 수치는 카메라 프레임이 없어서 건너뛴 경우와 같은 뜻은 아니다. **설정 FPS를 유지하지 못한 처리 용량 부족의 신호**다. 디버그 HUD와 벤치 리포트에서 추론 시간이 증가할 때 드롭도 함께 증가하는지 확인할 수 있다.

### 2.3 Pose 결과 단위의 벤치 측정

변경 파일: `Assets/Scripts/RagHealthcare/Performance/DevicePerformanceProfiler.cs`

기존 벤치는 매 렌더 프레임(`Update`)에 `LastInferenceMilliseconds`를 더했다. 렌더링이 30 FPS이고 Pose가 12 FPS이면, 하나의 Pose 측정값이 약 2~3번 더해질 수 있다. 그 결과 평균 추론 시간과 Pose FPS가 실제 결과 수보다 화면 프레임 속도의 영향을 받았다.

이제 profiler는 `JointTrackingController.TrackingFrameReceived` 이벤트에서만 다음을 기록한다.

- 성공한 Pose 결과 수 (`poseFrameCount`)
- 결과당 추론 시간
- 평균 추론 시간 (`averageInferenceMs`)
- 최대 추론 시간 (`maxInferenceMs`)
- p95 추론 시간 (`p95InferenceMs`)

화면 FPS는 계속 `Update`로 따로 계산한다. 따라서 "화면은 30 FPS지만 Pose가 8 FPS"인 경우도 두 값이 서로 섞이지 않는다.

### 2.4 GC를 만들지 않는 p95 근사값

p95는 5ms 단위, 0~600ms 이상 범위의 고정 히스토그램으로 계산한다. 각 결과에서 정수 버킷 하나만 증가시키며, 10분 운동 중에도 프레임별 `List<float>`나 정렬 배열을 만들지 않는다.

```text
0–4ms, 5–9ms, ..., 595–599ms, 600ms 이상
```

벤치 종료 시 누적 개수가 전체 Pose 결과의 95%에 도달하는 버킷을 p95로 기록한다.

## 3. 장점

| 변경 | 장점 | 사용자 체감 |
| --- | --- | --- |
| 시작 시각 기준 샘플링 | 빠른 기기에서 설정 FPS를 불필요하게 잃지 않는다 | 관절과 오버레이가 더 즉각적으로 따라온다 |
| single-flight 유지 | 처리 중인 네이티브 요청과 메모리 버퍼가 누적되지 않는다 | 오래된 자세를 뒤늦게 말하는 현상과 멈춤 위험이 줄어든다 |
| 최신 프레임 우선 | 느린 기기에서 과거 카메라 프레임을 줄 세우지 않는다 | 현재 자세와 가까운 피드백을 받는다 |
| 드롭 계상 | 목표 FPS 미달 원인을 평균값만으로 숨기지 않는다 | 기기별 성능 문제를 재현·분류하기 쉬워진다 |
| 결과 이벤트 단위 벤치 | 렌더 FPS와 Pose FPS가 분리되어 실제 처리율을 알 수 있다 | 성능 설정을 근거 있게 조절할 수 있다 |
| p95·최대값 기록 | 평균이 좋아도 간헐적 멈춤을 찾을 수 있다 | TTS, 발열, 카메라 전환과 겹치는 버벅임을 추적하기 쉬워진다 |
| 고정 히스토그램 | 장시간 테스트에서도 추가 GC와 정렬 비용이 거의 없다 | 벤치 자체가 성능 측정을 왜곡하지 않는다 |

## 4. 단점과 의도적인 트레이드오프

| 항목 | 단점/위험 | 완화 방법 |
| --- | --- | --- |
| Pose 요청 빈도 증가 | 빠른 기기에서는 실제 추론 횟수가 늘어 배터리·발열이 이전보다 증가할 수 있다 | `mobilePoseFps` 또는 `RuntimeQualityController`의 poseFps를 기기별로 낮춘다 |
| 최신성 우선 | 느린 기기에서는 중간 프레임을 버리므로 모든 움직임의 연속적인 궤적을 보존하지 않는다 | 실시간 코칭은 전체 영상 재생보다 현재 자세 피드백을 우선한다. 정밀 리플레이에는 별도 저장 정책이 필요하다 |
| 드롭 수 증가 가능 | 이전에는 "추론 뒤 추가 대기" 때문에 드롭이 낮아 보일 수 있었고, 이제 처리 용량 부족이 더 드러날 수 있다 | 드롭을 오류로 오해하지 말고 `p95InferenceMs`, 실제 Pose FPS와 함께 해석한다 |
| p95 근사 | 5ms 단위라 실제 p95와 최대 5ms 정도 차이날 수 있고 600ms 이상은 한 버킷에 합쳐진다 | 장시간의 무할당 측정을 우선한다. 정확한 분포가 필요하면 Unity Profiler/Xcode Instruments를 병행한다 |
| 성공 결과만 latency 샘플 | 실패·타임아웃은 p95에 직접 포함되지 않는다 | `failedFrames`, `droppedFrames`, provider health telemetry를 함께 확인한다 |
| 미측정 한계 | 이 변경은 `GetPixels32`, GPU 추론, Swift JSON 경계 자체를 없애지 않는다 | 큰 병목이 남으면 기존 계획의 native capture/바이너리 bridge 검토 순서를 따른다 |

## 5. 운영·튜닝 기준

### 권장 시작값

| 기기 상태 | 카메라 | Pose 설정 | 해석 |
| --- | --- | --- | --- |
| 표준 iPhone | 640×480 / 20–30 FPS | 12 FPS | 일반적인 실시간 스쿼트 코칭 시작값 |
| 발열/저사양 | 640×480 / 20 FPS | 8–10 FPS | 안정성·배터리 우선 |
| 고성능 검증 | 640×480 / 30 FPS | 15 FPS | p95와 드롭이 안정적일 때만 사용 |

Pose FPS는 카메라 FPS보다 높게 설정하지 않는다. 예를 들어 카메라가 20 FPS이면 Pose 30 FPS는 같은 카메라 프레임을 반복 읽거나 빈 샘플을 늘릴 뿐이다.

### 60초 스모크 테스트

1. 카메라 시작 후 전신이 보이도록 선다.
2. `60초` 벤치를 시작하고 서기·하강·바닥·상승 동작을 반복한다.
3. 결과 JSON에서 `averagePoseFps`, `averageInferenceMs`, `p95InferenceMs`, `maxInferenceMs`, `droppedFrames`, `failedFrames`를 함께 확인한다.
4. TTS를 켠 상태와 끈 상태를 각각 실행해 p95의 차이를 비교한다.

### 10분 수락 테스트

- 기존 수락 기준인 평균 Pose FPS 10 이상, 평균 추론 100ms 이하, 초당 드롭 1 이하를 확인한다.
- p95는 아직 자동 실패 기준으로 고정하지 않는다. 기기별 실측을 모은 뒤, 평균만 통과하고 p95가 높은 케이스를 기준으로 별도 한계를 정한다.
- p95가 지속적으로 목표 interval보다 크면 Pose FPS를 낮추거나 저사양 profile로 전환한다. 예: 12 FPS의 interval은 약 83ms이므로 p95가 100ms 이상이면 12 FPS 지속은 현실적으로 어렵다.

## 6. 변경하지 않은 것

이번 작업은 다음을 의도적으로 바꾸지 않았다.

- MediaPipe 모델 종류, landmark confidence, 자세 규칙의 의학적 의미
- iOS Start/Stop/카메라 전환의 lifecycle ownership
- 비동기 bridge의 single-flight·timeout recovery 정책
- 카메라 프리뷰 해상도와 화면 미러링 계약
- 원본 영상 저장·네트워크 전송 정책

이 범위를 분리한 이유는 처리 주기 최적화가 관절 좌표 의미나 생명주기 안정성을 바꾸면 회귀 원인을 분리하기 어려워지기 때문이다.

## 7. 검증 상태와 다음 단계

정적 확인:

- C# 변경은 고정 크기 배열·기존 Unity API만 사용한다.
- 벤치 JSON은 새 필드를 추가하는 하위 호환 확장이다.
- 기존 `DroppedFrameCount` 공개 API와 화면 HUD는 유지된다.

Unity가 열려 있는 동안에는 같은 프로젝트를 batchmode로 다시 열 수 없다. Unity Editor에서 컴파일이 끝난 뒤 다음 메뉴를 실행한다.

```text
AI Healthcare > Run Deterministic QA Suite
```

그 다음 실제 iPhone에서 TTS on/off 각각 60초와 10분 벤치를 수행해, 변경 전후의 평균/P95/드롭을 비교한다. 수치가 확보되기 전에는 이 문서의 FPS 값은 출시 확정값이 아니라 안전한 시작점이다.
