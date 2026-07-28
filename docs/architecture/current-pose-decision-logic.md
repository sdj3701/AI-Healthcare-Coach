# Current Pose Decision Logic

이 문서는 현재 `TestRagSysten` 씬 기준으로 관절 추적 결과를 어떻게 자세 피드백으로 판별하는지 정리한다.

프레임당 객체 재사용, 중앙값/통계/JSONL 최적화의 구현과 Profiler 측정 방법은 `docs/pose-runtime-optimization.md`를 참고한다.

## 결론

현재 구현은 최저점에서 `SquatBottomDecision` 하나를 순차적으로 확정한다.
`HipHeightFailed → KneeCollapseFailed → PersonalDepthFailed →
Passed` 순서로 판정하므로, 한 최저점에서 서로 반대인 깊이 TTS가 함께
예약되지 않는다. 이전의 과도한 깊이 실패 판정은 사용하지 않는다. `Passed`는 최저점에서 말하지 않고
`Ascent` 후 `Standing`으로 복귀해 전체 반복이 끝났을 때만 올바른 반복 TTS로
출력한다.

## 현재 동작 경로

`TestRagSysten.unity`의 실제 경로는 다음과 같다.

1. `CameraCaptureSource`가 카메라 프레임을 제공한다.
2. `JointTrackingController`가 `MediaPipePoseTrackingProvider`를 통해 `JointTrackingFrame`을 받는다.
3. `RealtimeFeedbackOrchestrator`가 `TrackingFrameReceived` 이벤트를 구독한다.
4. `PoseFrameNormalizer`가 visibility 기준으로 사용할 관절을 걸러낸다.
5. `PoseFeatureExtractor`가 스쿼트 feature를 계산한다.
6. `PoseWindowBuffer`가 최근 프레임을 보관한다.
7. `PoseWindowStats`가 최근 프레임 통계를 계산한다.
8. `ExercisePhaseDetector`가 스쿼트 phase를 추정한다.
9. `RealtimePoseRuleEngine`이 오류 이벤트 후보를 만든다.
10. `FeedbackPrioritizer`가 중복/간격 제한을 적용해 하나의 피드백을 고른다.
11. `FeedbackComposer`와 `RagRetriever`가 음성 문장을 만들고 `PoseFeedbackJsonReceiver`로 전달한다.

2026-07-23부터 raw landmark의 전면 촬영 품질을 먼저 평가한다. 품질이 `Good`이 아니면 안정화 프레임은 준비하되 phase, 규칙, 반복 품질 판정은 보류한다. 양쪽 어깨·골반·무릎·발목 confidence, 화면상 전신 크기, 화면 잘림, hip/shoulder 가로 폭을 함께 사용하며, 정상 프레임이 연속 3개 들어온 뒤 분석을 재개한다.

추적 품질이 끊겼다가 돌아오면 서 있는 준비 자세를 먼저 다시 확인한다. 앉은 상태나 상승 중간부터 분석이 재개된 경우에는 해당 구간을 새로운 반복으로 세지 않으며, 사용자가 다시 선 뒤 시작한 완전한 하강-바닥-상승 구간만 평가한다.

관련 코드:

- `Assets/Scripts/RagHealthcare/Pose/JointTrackingController.cs`
- `Assets/Scripts/RagHealthcare/Pose/Providers/MediaPipePoseTrackingProvider.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimeFeedbackOrchestrator.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseFeatureExtractor.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseWindowStats.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/ExercisePhaseDetector.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimePoseRuleEngine.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/FeedbackPrioritizer.cs`

## 좌표 전제

현재 표준 입력 모델은 `JointTrackingFrame`이다.

각 관절은 다음 값을 가진다.

```json
{
  "name": "left_knee",
  "x": 0.42,
  "y": 0.61,
  "z": -0.18,
  "visibility": 0.92,
  "confidence": 0.89
}
```

현재 자세 판별은 주로 `x`, `y` 2D normalized 좌표를 사용한다.

- `x`: 화면 기준 가로 좌표, `0~1`
- `y`: 화면 기준 세로 좌표, `0~1`
- `z`: MediaPipe landmark의 상대 깊이값
- `visibility`, `confidence`: 관절 신뢰도 필터링에 사용

중요: `z`는 JSON에 저장되지만 현재 `RealtimePoseRuleEngine`의 자세 판별에는 사용하지 않는다. Python MediaPipe fallback은 `worldLandmarks`도 생성하지만, 현재 표준 `JointTrackingFrame` 변환은 `landmarks`만 사용한다. 따라서 현재 구현은 실제 3D 월드 좌표 기반 판별이 아니다.

## 계산하는 Feature

`PoseFeatureExtractor`는 현재 스쿼트 기준으로 다음 feature를 계산한다.

| Feature | 계산 방식 | 목적 |
| --- | --- | --- |
| `LeftKneeAngle`, `RightKneeAngle` | hip-knee-ankle 2D 각도 | 스쿼트 깊이, phase, 좌우 대칭 |
| `AverageKneeAngle` | 좌우 무릎 각도 평균 | phase와 깊이 판단 |
| `LeftKneeValgusOffset`, `RightKneeValgusOffset` | knee와 hip-ankle 선 사이 거리 | 무릎 정렬 이탈 판단 |
| `TorsoTiltDegrees` | shoulder center와 hip center 벡터가 화면 위쪽 수직선에서 벗어난 각도 | 상체 과도한 숙임 판단 |
| `CenterBalanceOffset` | hip center x와 ankle center x 차이 | 중심 쏠림 판단 |
| `HipLevelDelta` | 좌우 hip y 차이 | 골반 높이 차이 참고 |
| `PelvicTiltRatio` | 골반선과 어깨선의 상대 기울기 (`tan(relative angle)`) | 카메라 기울기·전신 기울기에 덜 민감한 골반 비대칭 판단 |
| `ShoulderLevelDelta` | 좌우 shoulder y 차이 | 어깨 높이 차이 참고 |
| `HipCenterYVelocityPerSecond` | 이전 프레임 대비 hip center y 변화 | 동작 흐름 참고 |
| `KneeAngleVelocityDegreesPerSecond` | 이전 프레임 대비 무릎 각도 변화 | 하강/상승 phase 판단 |
| `HipToKneeDepth` | `(hipCenterY - kneeCenterY) / bodyScale` | 1차 엉덩이–무릎 높이 gate |
| `KneeWidthRatio` | 좌우 무릎 x 간격 / 좌우 발목 x 간격 | 최저점 무릎 안쪽 붕괴 판단 |

현재 실시간 규칙 엔진에서 직접 피드백으로 쓰는 항목은 무릎 정렬, 상체 기울기, 골반 기울기, 중심 균형, 좌우 무릎 대칭, 스쿼트 깊이다.

## 현재 임계값

`TestRagSysten.unity`에 저장된 `RealtimeFeedbackOrchestrator.ruleSettings` 기준이다.

| 설정 | 현재값 | 의미 |
| --- | ---: | --- |
| `analysisWindowSeconds` | `0.8` | 최근 0.8초 프레임을 보고 판단 |
| `expectedPoseFps` | `12` | 분석 창 capacity 계산 기준 FPS |
| `minimumVisibility` | `0.45` | 관절 사용 최소 신뢰도 |
| `lowConfidenceGraceSeconds` | `0.35` | 저신뢰도 시 마지막 유효 좌표 유지 시간 |
| `maximumConsecutiveOutlierFrames` | `3` | 연속 이상치로 허용하는 최대 프레임 |
| `minimumValidCoreFrameRatio` | `0.45` | 핵심 자세가 잡힌 프레임 최소 비율 |
| `minimumViolationRatio` | `0.35` | 오류가 반복됐다고 볼 최소 비율 |
| `maximumKneeValgusOffset` | `0.15` | 무릎 정렬 이탈 허용치 |
| `minimumKneeObservationRatio` | `0.6` | 좌/우 무릎 정렬·대칭 평가에 필요한 최소 관찰 비율 |
| `standingKneeAngle` | `150` | 서 있는 phase 기준 무릎 각도 |
| `standingExitKneeAngle` | `140` | Standing 이탈 히스테리시스 (이 값 미만에서 하강 시작) |
| `bottomKneeAngle` | `125` | 바닥 phase 진입 기준 무릎 각도 |
| `bottomExitKneeAngle` | `150` | Bottom→Ascent 전환 여유 각도 |
| `maximumBottomKneeAngle` | `170` | 정상 깊이 상한. 이보다 크면 얕음 계열 피드백 |
| `maximumRecognizableBottomKneeAngle` | `175` | 이보다 크면 얕음 Warning. 170~175는 Info 권고 |
| `minimumBottomKneeAngle` | `55` | 기존 깊은 각도 진단 통계 기준. 현재 합격/실패에는 사용하지 않음 |
| `maximumLeftRightKneeAngleDelta` | `18` | 좌우 무릎 각도 차이 허용치 |
| `maximumTorsoTiltDegrees` | `42` | 상체 기울기 허용 각도 |
| `maximumPelvicTiltRatio` | `0.25` | 골반선-어깨선 상대 기울기 허용치 (약 14°) |
| `maximumCenterBalanceOffset` | `0.16` | 중심 쏠림 허용치 |
| `minimumKneeWidthRatio` | `0.80` | 이 값 미만이면 무릎 안쪽 붕괴 후보 |
| `minimumKneeCollapseFrames` | `2` | 무릎 붕괴 최소 연속 프레임 |
| `minimumExcessiveDepthFrames` | `2` | 기존 깊은 각도 연속 프레임 진단값. 현재 TTS·감점에는 사용하지 않음 |
| `personalDepthFailureSampleCount` | `3` | 세션 깊이 기준 보정에 필요한 연속 적격 실패 |
| `maximumPersonalizedBottomKneeAngle` | `150` | 개인화된 무릎 각도 기준의 절대 상한 |
| `minimumPersonalizedBottomHipDrop` | `0.05` | 개인화된 골반 하강 기준의 절대 하한 |
| `phaseVelocityDeadZoneDegreesPerSecond` | `12` | phase 변화 무시 구간 |
| `duplicateCooldownSeconds` | `3` | 같은 피드백 반복 제한 |
| `minimumGlobalFeedbackIntervalSeconds` | `1.5` | 전체 피드백 최소 간격 |

## 오류 판별 규칙

### 1. 전신/핵심 관절 신뢰도 부족

최근 프레임 중 reliable squat core가 잡힌 비율이 `minimumValidCoreFrameRatio`보다 낮으면 자세 판별보다 먼저 visibility 피드백을 낸다.

Reliable squat core 조건:

- 좌우 무릎 각도 계산 가능
- 상체 기울기 계산 가능

발목 기반 중심 균형은 core 게이트에 포함하지 않으며, 중심 균형 규칙에서만 사용한다.

### 2. 무릎 정렬 이탈

각 무릎에 대해 `hip -> ankle` 선과 knee 사이의 2D 거리를 계산한다.

`Descent` / `Bottom` / `Ascent` phase에서만 평가한다. `Standing`·`Unknown`에서는 내지 않는다.

좌우를 독립적으로 평가한다. 해당 측의 관찰 비율(`Left/RightKneeObservationRatio`)이 `minimumKneeObservationRatio`(0.6) 이상이고, 해당 측의 정렬 위반 비율이 `minimumViolationRatio` 이상일 때만 이벤트를 낸다. 양쪽이 모두 조건을 만족하면 offset이 더 큰 쪽 1건만 낸다. 한쪽 다리가 팔에 가려져 관찰이 부족하면 그 측은 평가하지 않는다.

- offset ≤ `maximumKneeValgusOffset * 1.4`(허용 0.15 기준 약 0.21): `Info` (살짝 벌어짐 안내)
- offset > 그 기준: `Warning` (기존 교정 메시지)

`Info`는 `RepQualityAccumulator`에서 CorrectRep 실패로 쓰지 않는다.

### 3. 상체 과도한 기울기

좌우 어깨 midpoint와 좌우 골반 midpoint를 연결한 torso vector를 만든다.

카메라 landmark는 화면 아래쪽으로 갈수록 `y`가 커지므로, `shoulderCenter - hipCenter`의 정상 직립 기준은 수치상 `Vector2.down`이다. 2026-07-23 이전 구현 일부에서는 반대인 `Vector2.up`과 비교해 직립 자세를 약 `180°`로 계산했고, 이 값이 상체 기울기 Warning과 반복 실패로 이어질 수 있었다. 현재는 기준축을 `Vector2.down`으로 수정하고 직립 합성 자세가 약 `0°`인지 QA로 고정했다.

이 벡터의 각도가 `maximumTorsoTiltDegrees`보다 큰 프레임 비율이 `minimumViolationRatio` 이상이면 상체 기울기 피드백을 낸다.

### 4. 중심 균형 이탈

좌우 골반 midpoint의 x좌표와 좌우 발목 midpoint의 x좌표 차이를 계산한다.

이 값이 `maximumCenterBalanceOffset`보다 큰 프레임 비율이 `minimumViolationRatio` 이상이면 중심 균형 피드백을 낸다.

### 5. 골반 기울기

좌우 hip을 이은 골반선과 좌우 shoulder를 이은 어깨선의 상대 기울기를 `PelvicTiltRatio`로 사용한다. 원시 hip y 차이만 쓰면 카메라가 기울었거나 사용자가 상체와 골반을 함께 기울인 정상 프레임도 골반 오류로 판정될 수 있기 때문이다.

hip 또는 shoulder의 화면상 가로 간격이 `0.08` 미만인 옆모습/가림 프레임은 골반 수평 판정을 하지 않는다. 또한 `Descent`·`Bottom`·`Ascent` 중 핵심 스쿼트 관절이 현재 프레임에서 모두 유효할 때만 평가한다. 유효 프레임에서 `PelvicTiltRatio`가 `maximumPelvicTiltRatio`를 넘는 비율이 `minimumViolationRatio` 이상일 때만 골반 교정 피드백을 낸다.

### 6. 좌우 무릎 대칭

좌우 무릎 각도의 차이 평균을 계산한다.

양쪽 무릎 관찰 비율이 모두 `minimumKneeObservationRatio` 이상일 때만 평가한다. 한쪽이 불안정하면 대칭 판정을 건너뛴다. 양쪽이 충분히 관찰되고 각도 차이가 `maximumLeftRightKneeAngleDelta`보다 크며 위반 비율이 `minimumViolationRatio` 이상이면 좌우 무릎 굽힘이 다르다는 피드백을 낸다.

### 7. 스쿼트 최저점 순차 판정

`Bottom`에서는 다음 순서로 결과 하나만 확정한다.

1. **1차 엉덩이–무릎 높이**:
   `hipToKneeDepth >= -0.03`이 신뢰 가능한 연속 2프레임에서 확인되어야 한다.
   실패하면 `squat_depth_hip_height`와
   `엉덩이와 무릎 높이가 충분히 가까워지지 않았습니다. 엉덩이를 조금 더 내려
   주세요.`를 사용한다.
2. **무릎 안쪽 붕괴**:
   `kneeWidthRatio < 0.80`이 연속 2프레임 또는 유효 관찰의 35% 이상이며 기존
   hip-knee-ankle 정렬 offset도 위반할 때 `squat_knee_collapse`를 확정한다.
   1차 높이도 부족하지만 붕괴가 명확하면 안전을 위해 더 내려가라는 안내보다
   무릎 정렬 안내를 우선한다.
3. **개인 목표 깊이**:
   이번 반복의 최소 무릎 각도가 활성 기준(초기 `135°`) 이하이거나 서기 대비
   최대 골반 하강량이 활성 기준(초기 `0.08`) 이상이면 통과한다. 둘 다
   부족하면 `squat_depth_personal_target`과
   `정렬은 좋습니다. 현재 가능한 범위에서 조금 더 앉아 주세요.`를 사용한다.
4. 위 조건을 모두 통과하면 무릎 각도가 기존 깊은 각도 기준보다 작더라도
   `Passed`로 표시하고 최저점에서는 TTS를 보류한다. `squat_depth_excessive`
   이벤트, 감점, 세션 문제 기록은 생성하지 않는다.

깊이·정렬 판정 TTS는 한 반복당 최대 한 번만 전달한다. 사용자가 계속 내려가
자세를 고치면 대기 중이던 이전 깊이 TTS를 취소하고 판정 상태는 갱신하지만,
같은 반복에서 또 다른 최저점 TTS를 연속 재생하지 않는다.

### 8. 세션 개인 깊이 기준

1차 높이와 무릎 정렬을 통과했지만 개인 목표 깊이만 실패한 시도 중 추적 품질이
`Good`이고 상체·중심·골반의 확정 Warning이 없는 시도만 후보로 수집한다.
후보 3회의 최소 무릎 각도 범위가 `8°` 이내이고 골반 하강량 범위가 `0.02`
이내이면 다음 반복부터 아래 기준을 사용한다.

```text
activeMaximumKneeAngle =
    clamp(median(3회 최소 무릎 각도) + 3°, 135°, 150°)

activeMinimumHipDrop =
    clamp(median(3회 최대 골반 하강량) - 0.01, 0.05, 0.08)
```

세 번째 실패를 성공으로 소급하지 않는다. 새 기준은 현재 운동 세션에만
유지되며 1차 높이와 무릎 정렬 기준은 완화하지 않는다. 깊은 자세의 최소 무릎
각도는 현재 올바른 반복 여부를 제한하지 않는다.

Phase는 평균 무릎 각도와 무릎 각도 변화 속도로 추정한다.

- `AverageKneeAngle >= standingKneeAngle`(150): `Standing`
- Standing 유지: `AverageKneeAngle >= standingExitKneeAngle`(140)
- `AverageKneeAngle <= bottomKneeAngle`이고 각도 변화가 dead zone 안이면 `Bottom`
- 무릎 각도가 줄어드는 중이면 `Descent`
- 무릎 각도가 커지는 중이면 `Ascent`

## 올바른 반복 카운트

현재 화면 카운트는 `RealtimeFeedbackOrchestrator.CorrectRepCount`가 관리한다.

기본 반복 수는 `ExercisePhaseDetector`의 `RepCount`다. 이 값은 사용자가 `Bottom` phase를 거친 뒤 `Ascent -> Standing`으로 돌아오면 증가한다.

올바른 반복 수는 이 기본 반복 수와 다르게 계산한다.

1. rep가 시작되면 `RepQualityAccumulator`의 시간 기반 증거를 초기화한다.
2. `Descent`, `Bottom`, `Ascent` 중 핵심 관절이 유효한 프레임만 품질 평가에 포함한다.
3. `Info` 안내는 실패 근거에서 제외하고 `Warning`/`Critical`만 누적한다.
4. 단일 Warning은 실패로 고정하지 않는다. 같은 `RuleId`가 최소 2개 프레임에서 관찰되고, 최소 4개 유효 프레임 중 35% 이상을 차지해야 일반 오류로 확정한다. 서로 다른 일시 경고의 합이나 한 번의 오래된 75% 지속 근거만으로는 실패 처리하지 않는다.
5. `ExercisePhaseDetector`의 `RepCount`가 증가하는 순간 충분한 유효 프레임이 있고 확정 오류가 없으면 `CorrectRepCount`를 1 증가시킨다.
6. 유효 프레임이 부족한 rep는 성공/실패로 단정하지 않고 `관절 인식이 불안정해 이번 동작은 횟수에 포함하지 않았습니다.`라고 안내한다.
7. 최저점 결정이 `Passed`이고 다른 확정 오류가 없는 rep가 증가하면
   `PoseFeedbackJsonReceiver`로 `올바른 자세입니다. {N}개.` TTS 메시지를
   보낸다.
8. 일반 자세 교정 TTS는 `Descent`, `Bottom`, `Ascent`에서만 허용한다. 사용자가 가만히 선 `Standing` 또는 `Unknown` 상태에서는 새 자세 TTS를 발생시키지 않고 대기 중인 `squat_*` 안내도 취소한다. 방금 완료한 반복 수 안내는 예외로 유지한다.
9. 정확 카운트가 증가하지 않은 완료 반복은 확정 `RuleId`를 세션 문제 집계에 남겨 종료 리포트의 자세 분석에 사용한다.

화면에는 상태 패널의 `PoseTrackingStatusView`가 다음 형식으로 표시한다.

```text
Correct reps: 3/5
Phase: Standing (clean)
```

여기서 앞 숫자는 올바른 자세로 완료한 반복 수다. 목표 개수가 설정되어 있으면 뒤 숫자는 목표 개수이고, 목표가 없으면 감지된 전체 반복 수다.

TTS 문구는 `RealtimeFeedbackOrchestrator.correctRepFeedbackFormat`에서 바꿀 수 있다. 기본값은 `올바른 자세입니다. {0}개.`이고, `{0}` 자리에 올바른 반복 수가 들어간다.

### 목표 개수 입력

`PoseTrackingStatusView`는 상태 패널 아래쪽에 목표 개수 입력 UI를 런타임에 생성한다.

- `InputField`: 목표로 할 올바른 반복 개수를 입력한다.
- `확인` 버튼: 입력한 목표 개수를 `RealtimeFeedbackOrchestrator.SetCorrectRepTarget()`에 적용한다.

확인을 누르면 현재 correct count, 전체 rep count, phase/window 상태를 0부터 다시 시작한다. 이후 올바른 rep만 목표 개수까지 증가한다. 오류가 감지된 rep는 전체 rep에는 포함될 수 있지만 correct count에는 포함되지 않는다.

목표를 `0` 또는 빈 값으로 확인하면 목표 제한 없이 올바른 rep를 계속 누적한다.

모바일 운동 화면은 `repsPerSet`마다 마지막 세트 전까지 세트 휴식을 시작한다. 휴식 중에는 추적 세션을 종료하므로 phase detector의 로컬 `RepCount`는 재시작 시 초기화되지만, `RealtimeFeedbackOrchestrator.TotalRepCount`, `CorrectRepCount`, 자세 문제 집계는 유지된다. 마지막 세트의 전체 목표에 도달하면 휴식을 추가하지 않고 결과 화면으로 이동한다.

## 3D 캐릭터 리플레이

테스트용 3D 캐릭터는 외부 모델 에셋 없이 Unity primitive로 생성한다.

- 관절: `Sphere`
- 뼈대: `Capsule`
- 왼쪽/오른쪽 관절 색상 분리
- `JointTrackingFrame`의 `x`, `y`, `z`를 단순 3D 좌표로 변환

라이브 추적 중에는 `PoseAvatar3DPreview`가 `JointTrackingController.TrackingFrameReceived`를 받아 캐릭터를 즉시 움직인다.

`Stop Camera` 버튼을 누르면 다음 순서로 리플레이가 시작된다.

1. `JointTrackingController.StopTracking()`으로 추적을 멈춘다.
2. `CameraCaptureSource.StopCamera()`로 카메라를 멈춘다.
3. `SessionJsonlLogger.Flush()`로 현재 JSONL 로그를 디스크에 반영한다.
4. `PoseJsonReplayPlayer`가 현재 세션 JSONL 또는 가장 최근 `RagSessions/*.jsonl` 파일을 읽는다.
5. `"type":"frame"` 라인만 파싱해 `JointTrackingFrame`으로 변환한다.
6. timestamp 간격에 맞춰 `PoseAvatar3DPreview.RenderFrame()`으로 3D 캐릭터를 재생한다.

Stop 이후에는 `CameraPreviewDebugView`가 카메라 프리뷰 대신 `PoseJsonReplayPlayer.PreviewTexture`를 메인 프리뷰 영역에 표시한다. 따라서 별도 씬을 새로 만들 필요 없이 현재 테스트 씬에서 Stop Camera를 누르면 저장 JSON 기반 3D 리플레이를 확인할 수 있다.

카메라를 다시 시작하면 현재 리플레이는 중지되고 라이브 추적 미리보기로 돌아간다.

## 현재 씬에서 쓰지 않는 분석기

`PoseFeedbackAnalyzer`도 존재한다. 이 분석기는 단일 프레임 기준으로 무릎 정렬, 깊이, 좌우 대칭, 상체 기울기, 골반/어깨 높이, 중심 균형, 발 visibility를 검사한다.

하지만 현재 `TestRagSysten.unity`에는 `PoseFeedbackAnalyzer` 컴포넌트가 붙어 있지 않다. 씬 생성 코드도 `RealtimeFeedbackOrchestrator` 중심으로 구성한다. 따라서 현재 테스트 씬의 주된 판별 경로는 `RealtimePoseRuleEngine`이다.

## 현재 한계

- 현재 판별은 2D normalized 좌표 기반이다.
- 골반 기울기는 2D 화면 투영 기준이며, 전후 골반 회전이나 실제 골반 관절의 의학적 정렬을 측정하지 않는다.
- `z`와 `worldLandmarks`는 표준 자세 판별에 쓰지 않는다.
- 카메라 각도, 거리, 미러링, 회전에 따라 threshold 체감이 달라질 수 있다.
- "올바른 자세 점수"나 "종합 합격/불합격" 모델은 아직 없다.
- 무릎 정렬은 실제 발끝 방향 벡터가 아니라 `hip-ankle` 선 기준의 2D proxy다.
- 좌우/전후 깊이 차이는 3D가 아니라 화면상의 2D 투영으로 판단한다.

## 튜닝 체크포인트

다음 단계에서 정확도를 높이려면 아래를 우선 확인한다.

1. `worldLandmarks`를 `JointTrackingFrame`에 포함할지 결정한다.
2. 2D 규칙과 3D 규칙을 분리한다.
3. 정상 스쿼트 샘플에서 false positive가 과하지 않은지 확인한다.
4. 카메라 정면/측면 기준을 명확히 나눈다.
5. threshold를 Inspector에서 조정한 뒤 문서의 현재값도 같이 갱신한다.
6. 자세별로 "판별 가능 조건"과 "판별 불가 조건"을 분리한다.

## 2026-07-14 좌표 떨림 및 반복 오판정 완화

### 처리 순서

1. `PoseLandmarkStabilizer`가 관절별 최근 3프레임 중앙값을 구한다.
2. 중앙값에 EMA를 적용한다(에디터/비-iOS `alpha=0.35`, iOS 런타임 `Awake`에서 `0.55` 강제).
3. 한 프레임에서 정규화 좌표가 `0.12`보다 크게 이동하면 단일 이상치로 보류한다.
4. confidence가 잠깐 낮아지면 최대 `0.2초` 동안 마지막 유효 좌표를 유지한다.
5. 평활화된 좌표로 각도와 속도를 계산한다.
6. `ExercisePhaseDetector`는 정지 프레임뿐 아니라 하강 속도가 상승으로 반전되는 시점도 Bottom으로 인식한다.
7. 자세 오류는 최소 프레임과 지속 비율을 만족할 때만 rep 실패로 확정한다.

### 현재 튜닝값

- landmark EMA: 비-iOS `0.35` / iOS `0.55`
- 단일 관절 최대 이동: `0.12`
- 저신뢰도 grace: `0.35초`
- 연속 이상치 허용: `3`프레임
- 분석 창: `0.8초`
- 최소 visibility: `0.45`
- 최소 규칙 평가 프레임: `6`
- 최소 rep 유효 프레임: `4`
- Pose 추론: 카메라와 동일 `640×480`(inference downscale 끔), Pose `12` FPS, MediaPipe confidence `0.40`
- rep Warning 비율: `35%`
- 고지속 경고 진단 비율: `75%` (한 프레임만으로 감점하지 않음)
- 동일 Warning 최소 관찰: `2`프레임
- Critical 확인 프레임: `2`
- Standing 진입/이탈: `150도 / 140도`
- 기본 Bottom: `125도`
- Bottom 이탈: `150도`
- 인식 가능한 Bottom 최대: `175도`
- 정상 깊이 최대: `170도`
- 무릎 valgus 허용: `0.15` (Standing 미평가; 경미=Info / 심각=Warning)
- 무릎 최소 관찰 비율: `0.6`
- 속도 dead zone: `12도/초`

깊이 판정은 분석 창 `MinimumKneeAngle`과 이번 rep
`MinimumKneeAngleInCurrentRep`의 최솟값을 개인 목표 깊이 통과 여부에
사용한다. `HasReachedBottomInCurrentRep`이면 얕음 안내를 내지 않으며, 기존
최소 각도보다 깊어져도 Warning을 생성하지 않는다. Bottom 미인식(rare)일 때만
170~175° Info / 175° 초과 Warning을 내며, persistence는 얕은 깊이 위반
비율을 사용한다.

## 2026-07-27 깊은 스쿼트 판정 정책

- 실기기 테스트 결과에 따라 기존 `squat_depth_excessive` 실패 판정을
  폐기했다.
- 1차 엉덩이-무릎 높이, 무릎 안쪽 정렬, 개인 목표 깊이를 통과하면 기존
  `55°` 미만의 깊은 자세도 `Passed`다.
- 깊은 자세는 `CorrectRepCount`에 포함되며 반복 완료 시
  `올바른 자세입니다. {N}개.` 경로를 사용한다.
- `squat_depth_excessive`, `squat_depth_deep`, `*_knee_bend_deep`는 새
  Warning이나 TTS를 만들지 않는다.
- 기존 최소 각도와 연속 프레임 통계는 진단 호환을 위해 남아 있지만 합격,
  감점, 세션 문제 집계에는 사용하지 않는다.

## 2026-07-21 팔-다리 가림 오판정 완화

팔이 다리를 가리거나 한쪽 무릎 landmark가 잠깐 흔들릴 때 무릎 정렬/대칭 오판정이 나오던 문제를 완화했다.

### 변경 요지

1. 저신뢰도 grace를 `0.2초 → 0.35초`, 연속 이상치 허용을 `1 → 3`으로 늘려 짧은 가림에 좌표를 더 오래 유지한다.
2. 무릎 valgus 허용치를 `0.08 → 0.10`으로 완화한다.
3. `minimumKneeObservationRatio=0.5`를 추가한다. 좌/우 무릎 정렬은 해당 측 관찰 비율과 위반 비율을 모두 만족할 때만 경고한다.
4. 무릎 대칭은 양쪽 관찰 비율이 모두 충분할 때만 평가하고, 한쪽이 불안정하면 스킵한다.
5. torso / center balance / squat depth / visibility 게이트 임계값은 변경하지 않았다.
6. `TestRagSysten.unity`, `Main.unity`의 `ruleSettings`를 코드 기본값과 동기화했다.

### 기대 효과

- 팔이 한쪽 무릎을 가려 landmark가 불안정해도, 관찰이 부족한 측으로는 정렬/대칭 경고를 내지 않는다.
- 실제 무릎 내전(valgus)이 지속되는 경우에는 기존처럼 해당 측 경고를 유지한다.

### 장점

- 한 프레임의 좌표 떨림이나 Info 안내가 전체 rep 실패로 고정되지 않는다.
- 바닥에서 완전히 멈추지 않고 바로 올라와도 방향 전환으로 Bottom을 감지한다.
- 짧은 confidence 저하와 단일 좌표 점프에 강하다.
- 인식 불안정, 준비, 동작 중, 깊이 확인, 교정 필요 상태를 UI에서 구분한다.
- Critical과 지속 Warning은 계속 실패로 처리하므로 안전 규칙을 약화하지 않는다.

### 단점

- 중앙값과 EMA 때문에 약 `100~250ms`의 반응 지연이 생길 수 있다.
- 매우 빠른 동작은 실제 관절 이동이 이상치로 한 프레임 보류될 수 있다.
- 고정 threshold는 체형, 카메라 각도, 운동 속도에 따라 추가 실기기 튜닝이 필요하다.
- 짧지만 실제인 오류는 지속 비율 미달로 무시될 수 있으므로 안전 규칙은 별도 Critical 기준을 유지해야 한다.
