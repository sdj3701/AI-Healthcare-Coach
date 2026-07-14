# Current Pose Decision Logic

이 문서는 현재 `TestRagSysten` 씬 기준으로 관절 추적 결과를 어떻게 자세 피드백으로 판별하는지 정리한다.

프레임당 객체 재사용, 중앙값/통계/JSONL 최적화의 구현과 Profiler 측정 방법은 `docs/pose-runtime-optimization.md`를 참고한다.

## 결론

현재 구현은 올바른 자세를 별도 점수로 판정하지 않는다.

대신 MediaPipe 관절 프레임에서 스쿼트 관련 feature를 계산하고, 최근 분석 창 안에서 특정 오류 조건이 반복되면 피드백을 발생시킨다. 즉, 현재 기준에서 "올바른 자세"는 필수 관절이 안정적으로 보이고 아래 오류 조건이 임계값을 넘지 않는 상태에 가깝다.

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
| `TorsoTiltDegrees` | shoulder center와 hip center 벡터의 각도 | 상체 과도한 숙임 판단 |
| `CenterBalanceOffset` | hip center x와 ankle center x 차이 | 중심 쏠림 판단 |
| `HipLevelDelta` | 좌우 hip y 차이 | 골반 높이 차이 참고 |
| `ShoulderLevelDelta` | 좌우 shoulder y 차이 | 어깨 높이 차이 참고 |
| `HipCenterYVelocityPerSecond` | 이전 프레임 대비 hip center y 변화 | 동작 흐름 참고 |
| `KneeAngleVelocityDegreesPerSecond` | 이전 프레임 대비 무릎 각도 변화 | 하강/상승 phase 판단 |

현재 실시간 규칙 엔진에서 직접 피드백으로 쓰는 항목은 무릎 정렬, 상체 기울기, 중심 균형, 좌우 무릎 대칭, 스쿼트 깊이다.

## 현재 임계값

`TestRagSysten.unity`에 저장된 `RealtimeFeedbackOrchestrator.ruleSettings` 기준이다.

| 설정 | 현재값 | 의미 |
| --- | ---: | --- |
| `analysisWindowSeconds` | `1.2` | 최근 1.2초 프레임을 보고 판단 |
| `expectedPoseFps` | `15` | 분석 창 capacity 계산 기준 FPS |
| `minimumVisibility` | `0.5` | 관절 사용 최소 신뢰도 |
| `minimumValidCoreFrameRatio` | `0.55` | 핵심 자세가 잡힌 프레임 최소 비율 |
| `minimumViolationRatio` | `0.45` | 오류가 반복됐다고 볼 최소 비율 |
| `maximumKneeValgusOffset` | `0.08` | 무릎 정렬 이탈 허용치 |
| `standingKneeAngle` | `160` | 서 있는 phase 기준 무릎 각도 |
| `bottomKneeAngle` | `110` | 바닥 phase 진입 기준 무릎 각도 |
| `maximumBottomKneeAngle` | `135` | 바닥 자세에서 이보다 크면 얕음 |
| `minimumBottomKneeAngle` | `55` | 바닥 자세에서 이보다 작으면 너무 깊음 |
| `maximumLeftRightKneeAngleDelta` | `18` | 좌우 무릎 각도 차이 허용치 |
| `maximumTorsoTiltDegrees` | `35` | 상체 기울기 허용 각도 |
| `maximumCenterBalanceOffset` | `0.12` | 중심 쏠림 허용치 |
| `phaseVelocityDeadZoneDegreesPerSecond` | `8` | phase 변화 무시 구간 |
| `duplicateCooldownSeconds` | `3` | 같은 피드백 반복 제한 |
| `minimumGlobalFeedbackIntervalSeconds` | `1.5` | 전체 피드백 최소 간격 |

## 오류 판별 규칙

### 1. 전신/핵심 관절 신뢰도 부족

최근 프레임 중 reliable squat core가 잡힌 비율이 `minimumValidCoreFrameRatio`보다 낮으면 자세 판별보다 먼저 visibility 피드백을 낸다.

Reliable squat core 조건:

- 좌우 무릎 각도 계산 가능
- 상체 기울기 계산 가능
- 중심 균형 계산 가능

### 2. 무릎 정렬 이탈

각 무릎에 대해 `hip -> ankle` 선과 knee 사이의 2D 거리를 계산한다.

최근 관찰 중 이 값이 `maximumKneeValgusOffset`보다 큰 비율이 `minimumViolationRatio` 이상이면 무릎 정렬 피드백을 낸다.

### 3. 상체 과도한 기울기

좌우 어깨 midpoint와 좌우 골반 midpoint를 연결한 torso vector를 만든다.

이 벡터의 각도가 `maximumTorsoTiltDegrees`보다 큰 프레임 비율이 `minimumViolationRatio` 이상이면 상체 기울기 피드백을 낸다.

### 4. 중심 균형 이탈

좌우 골반 midpoint의 x좌표와 좌우 발목 midpoint의 x좌표 차이를 계산한다.

이 값이 `maximumCenterBalanceOffset`보다 큰 프레임 비율이 `minimumViolationRatio` 이상이면 중심 균형 피드백을 낸다.

### 5. 좌우 무릎 대칭

좌우 무릎 각도의 차이 평균을 계산한다.

이 값이 `maximumLeftRightKneeAngleDelta`보다 크면 좌우 무릎 굽힘이 다르다는 피드백을 낸다.

### 6. 스쿼트 깊이

스쿼트 phase가 `Bottom`일 때만 깊이 피드백을 낸다.

- `AverageKneeAngle > maximumBottomKneeAngle`: 너무 얕음
- `AverageKneeAngle < minimumBottomKneeAngle`: 너무 깊음

Phase는 평균 무릎 각도와 무릎 각도 변화 속도로 추정한다.

- `AverageKneeAngle >= standingKneeAngle`: `Standing`
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
4. 단일 Warning은 실패로 고정하지 않는다. 최소 6개 유효 프레임과 35% 이상 Warning 비율, 75% 이상 지속 근거 또는 2개 이상 Critical 프레임 중 하나가 있어야 실패로 확정한다.
5. `ExercisePhaseDetector`의 `RepCount`가 증가하는 순간 충분한 유효 프레임이 있고 확정 오류가 없으면 `CorrectRepCount`를 1 증가시킨다.
6. 유효 프레임이 부족한 rep는 성공/실패로 단정하지 않고 `관절 인식이 불안정해 이번 동작은 횟수에 포함하지 않았습니다.`라고 안내한다.
7. 올바른 rep가 증가하면 `PoseFeedbackJsonReceiver`로 `정확합니다. {N}개.` TTS 메시지를 보낸다.

화면에는 상태 패널의 `PoseTrackingStatusView`가 다음 형식으로 표시한다.

```text
Correct reps: 3/5
Phase: Standing (clean)
```

여기서 앞 숫자는 올바른 자세로 완료한 반복 수다. 목표 개수가 설정되어 있으면 뒤 숫자는 목표 개수이고, 목표가 없으면 감지된 전체 반복 수다.

TTS 문구는 `RealtimeFeedbackOrchestrator.correctRepFeedbackFormat`에서 바꿀 수 있다. 기본값은 `정확합니다. {0}개.`이고, `{0}` 자리에 올바른 반복 수가 들어간다.

### 목표 개수 입력

`PoseTrackingStatusView`는 상태 패널 아래쪽에 목표 개수 입력 UI를 런타임에 생성한다.

- `InputField`: 목표로 할 올바른 반복 개수를 입력한다.
- `확인` 버튼: 입력한 목표 개수를 `RealtimeFeedbackOrchestrator.SetCorrectRepTarget()`에 적용한다.

확인을 누르면 현재 correct count, 전체 rep count, phase/window 상태를 0부터 다시 시작한다. 이후 올바른 rep만 목표 개수까지 증가한다. 오류가 감지된 rep는 전체 rep에는 포함될 수 있지만 correct count에는 포함되지 않는다.

목표를 `0` 또는 빈 값으로 확인하면 목표 제한 없이 올바른 rep를 계속 누적한다.

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
2. 중앙값에 EMA `alpha=0.35`를 적용한다.
3. 한 프레임에서 정규화 좌표가 `0.08`보다 크게 이동하면 단일 이상치로 보류한다.
4. confidence가 잠깐 낮아지면 최대 `0.2초` 동안 마지막 유효 좌표를 유지한다.
5. 평활화된 좌표로 각도와 속도를 계산한다.
6. `ExercisePhaseDetector`는 정지 프레임뿐 아니라 하강 속도가 상승으로 반전되는 시점도 Bottom으로 인식한다.
7. 자세 오류는 최소 프레임과 지속 비율을 만족할 때만 rep 실패로 확정한다.

### 현재 튜닝값

- landmark EMA: `0.35`
- 단일 관절 최대 이동: `0.08`
- 저신뢰도 grace: `0.2초`
- 분석 창: `0.8초`
- 최소 visibility: `0.45`
- 최소 규칙 평가 프레임: `6`
- 최소 rep 유효 프레임: `6`
- rep Warning 비율: `35%`
- 즉시 확정 지속 비율: `75%`
- Critical 확인 프레임: `2`
- Standing 진입/이탈: `160도 / 150도`
- 기본 Bottom: `125도`
- 인식 가능한 Bottom 최대: `145도`
- 정상 깊이 최대: `135도`
- 속도 dead zone: `12도/초`

깊이 판정은 분석 창의 평균 각도가 아니라 최소 무릎 각도를 사용한다. 직전 Standing 프레임이 창에 남아 있어도 실제로 충분히 내려갔다면 얕은 스쿼트로 오판정하지 않는다.

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
