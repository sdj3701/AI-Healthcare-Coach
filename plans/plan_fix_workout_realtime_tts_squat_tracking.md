# 작업 계획서: 운동 단계 재보정 루프·TTS·스쿼트 실시간 추적 수정

> **문서 성격**: 코드 수정 전 승인용 계획서입니다. 승인 전에는 `.cs` 파일을 수정하지 않습니다.
>
> **대상 사용자 흐름**: 1) 사용자 정보 → 2) 전신 촬영/관절 안정화 → 3) 운동 선택 → 4) 운동 진행
>
> **목표**: 2단계에서 완료한 전신 보정을 4단계에서 다시 반복하지 않고, 스쿼트 중 관절 인식이 잠깐 흔들려도 회복 즉시 실시간 자세 분석과 TTS 코칭을 계속합니다.

## 1. 확인된 원인

1. `WorkoutSessionStateMachine.TickPaused()`가 운동 중 품질 저하가 기본 0.5초 이상 이어지면 항상 `ReadyForCalibration`로 되돌립니다.
2. `BeginCalibratedSession()`은 최초 상태만 `InWorkout`으로 바꾸고, “2단계 보정을 이미 완료한 운동 세션”이라는 사실을 상태 머신에 보존하지 않습니다.
3. 이 때문에 4단계에서도 `1.5초 전신 유지 → 3초 카운트다운`이 반복됩니다.
4. `RealtimeFeedbackOrchestrator`는 `InWorkout`이 아니면 규칙 분석을 중단하므로, 반복 카운트·자세 피드백·TTS가 모두 함께 멈춥니다.
5. `MobileWorkoutPrototypeView`는 4단계에서도 캘리브레이션 오버레이를 표시하여 2단계 화면처럼 보이게 합니다.
6. 반대로 세션이 완전히 종료된 상태에서는 현재 분석 게이트가 빠져 있어, 2단계 보정 완료 후 운동 선택 전에도 자세 분석/TTS가 발생할 수 있습니다. 분석 조건을 “활성 세션이면서 `InWorkout`”으로 단일화해야 합니다.
7. 현재 macOS 로그에서는 `MacOsSay` backend가 정상 선택됐고 재생 실패도 기록되지 않았지만, 최근 세션에는 phase/feedback 이벤트가 0건입니다. 따라서 현재 무음의 1차 원인은 음성 엔진보다 상위 분석/피드백 생성이 막힌 것입니다.
8. 품질 평가는 단 한 프레임의 `Degraded`도 분석 윈도우·phase·반복 상태를 즉시 초기화합니다. 깊은 스쿼트 중 몸의 투영 높이가 줄거나 관절 confidence가 잠깐 흔들릴 때 반복 추적이 끊길 수 있습니다.
9. 기존 `SquatBottom` 합성 fixture의 실제 무릎 각도는 Bottom 임계에 도달하지 않아, 현재 QA가 실제 관절 좌표 기반 스쿼트 파이프라인을 충분히 검증하지 못합니다.
10. phase 반전 감지와 “충분한 깊이 도달”이 같은 플래그로 처리되어, 175° 부근의 작은 흔들림도 반복으로 잡히고 얕은 스쿼트 안내가 억제될 수 있습니다.

## 2. 구현 범위

- [x] **상태 머신에서 전용 보정과 운동 중 추적 회복을 분리**
  - 모호한 boolean 시작 API를 전용 보정 세션과 실제 운동 세션 API로 분리합니다.
  - `BeginCalibratedSession()`으로 시작한 세션은 보정 완료 상태를 세션 종료까지 유지합니다.
  - 운동 중 품질 저하는 `PausedOutOfFrame`으로만 일시정지합니다.
  - 품질이 회복되면 새 3초 카운트다운 없이 즉시 `InWorkout`으로 복귀합니다.
  - 최초 2단계 보정 흐름에서만 `ReadyForCalibration → CountingDown → InWorkout`을 사용합니다.

- [x] **4단계 UI를 실시간 운동 화면으로 고정**
  - 4단계에서는 전신 보정용 전체 오버레이와 3초 카운트다운을 표시하지 않습니다.
  - 추적이 불안정할 때는 “전신을 화면 안에 맞춰 주세요” 같은 비차단 상태 안내만 표시합니다.
  - phase, 정확 반복 수, 최근 자세 피드백, pose FPS는 계속 갱신합니다.

- [x] **TTS 시작·전달 경로 보강**
  - 운동 START 시 `CoachTtsController.BeginSession()` 성공 여부와 활성 backend를 확인합니다.
  - 세션 시작 안내를 한 번 재생하여 음성 경로가 실제로 열렸음을 확인할 수 있게 합니다.
  - `PoseFeedbackJsonReceiver → CoachTtsController` 연결을 `Main.unity`와 씬 빌더 양쪽에서 명시적으로 유지합니다.
  - 음성 요청이 거절되거나 backend 재생이 실패하면 원인을 로그와 UI 상태에서 확인할 수 있게 합니다.
  - 동일 자세 안내의 cooldown과 중요도 우선순위는 유지해 음성이 과도하게 겹치지 않게 합니다.

- [x] **스쿼트 추적 안정화**
  - 오케스트레이터가 활성 `InWorkout` 세션에서만 분석하도록 게이트를 바로잡습니다.
  - 보정용 0.85 전신 가시성 기준을 운동 중 분석 기준으로 재사용하지 않습니다.
  - 운동 중에는 어깨·골반·무릎·발목 중심의 분석 품질 게이트와 2~3프레임 유예/히스테리시스를 사용합니다.
  - 일시적인 `Degraded`/landmark 누락은 기존 안정화 hold와 적응형 smoothing으로 흡수하고, 새 판정만 보류한 채 phase/window를 보존합니다. 지속적인 `Unavailable`에서만 hard reset합니다.
  - 서기 → 하강 → 최저점 → 상승 → 서기의 스쿼트 phase 전이가 끊기지 않도록 재진입 시 서기 자세 rearm을 유지합니다.
  - 정면 카메라에서는 서기 기준 대비 body-scale 정규화 hip drop과 무릎 각도를 함께 사용해 하강/최저점/상승을 판정합니다.
  - 동작 반전과 충분한 깊이 도달 상태를 분리하여 작은 무릎 흔들림을 반복으로 세지 않고, 얕은 전체 동작은 자세 안내 대상으로 남깁니다.
  - 화면 절대 좌표에 직접 의존하는 핵심 offset은 신체 scale로 정규화해 카메라 거리 변화의 영향을 줄입니다.
  - `Main.unity`의 스쿼트 추적/보정 직렬화 값을 현재 런타임 기본값과 맞춥니다.

- [x] **자동 QA와 회귀 테스트 추가**
  - 전용 보정은 기존처럼 1.5초 유지와 3초 카운트다운을 거치는지 확인합니다.
  - 보정 완료 운동 세션은 일시적인/장시간 프레임 이탈 후에도 `ReadyForCalibration`로 돌아가지 않는지 확인합니다.
  - 품질 회복 후 `InWorkout`으로 즉시 복귀하고 분석이 다시 허용되는지 확인합니다.
  - 실제 관절 좌표 기반 5단계 합성 스쿼트 시퀀스를 다시 만들고 stabilizer → normalizer → extractor → phase → rule까지 종단 검증합니다.
  - 정상 1회 반복, 175° 부근 노이즈 0회, 얕은 전체 동작, 빠른 최저점 반전, 100~300ms visibility 흔들림을 검증합니다.
  - 동일 자세를 scale/translate/mirror한 입력에서도 phase와 반복 결과가 유지되는지 검증합니다.
  - TTS backend 자동 선택, 세션 admission, 피드백 전달 및 중복 억제를 검증합니다.

## 3. 예상 수정 파일

- `Assets/Scripts/RagHealthcare/Pose/Session/WorkoutSessionStateMachine.cs`
- `Assets/Scripts/RagHealthcare/Pose/Session/WorkoutTrackingState.cs`
- `Assets/Scripts/RagHealthcare/UI/MobileWorkoutPrototypeView.cs`
- `Assets/Scripts/RagHealthcare/UI/Calibration/CalibrationOverlayView.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimeFeedbackOrchestrator.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseTrackingQuality.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseFeatureExtractor.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/ExercisePhaseDetector.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimePoseRuleSettings.cs`
- `Assets/Scripts/RagHealthcare/Pose/PoseFeedbackJsonReceiver.cs`
- `Assets/Scripts/RagHealthcare/Tts/CoachTtsController.cs`
- `Assets/Scripts/RagHealthcare/Qa/SyntheticPoseFixtures.cs`
- `Assets/Editor/RagHealthcare/RagSquatCoachSceneBuilder.cs`
- `Assets/Editor/RagHealthcare/HealthcareQaSuite.cs`
- `Assets/Scenes/Main.unity`

실제 진단 결과에 따라 불필요한 파일은 수정하지 않습니다.

## 4. 수용 기준

- [x] 2단계 보정 완료 후 4단계 START 시 즉시 `InWorkout`으로 진입한다.
- [x] 4단계에서 3초 보정 카운트다운이 다시 나타나지 않는다.
- [x] 스쿼트 중 잠깐 인식이 흔들려도 회복 즉시 실시간 phase/반복/피드백이 이어진다.
- [x] 정상 스쿼트의 `Standing → Descent → Bottom → Ascent → Standing`이 1회로 집계된다.
- [x] 자세 오류와 정확 반복 안내가 화면과 TTS로 전달된다.
- [x] TTS가 실패하면 실패 원인이 로그/상태에 남고 앱의 운동 추적은 계속된다.
- [x] 기존 최초 보정, 카메라 이탈 안전 정지, 중복 음성 억제 기능에 회귀가 없다.

## 5. 검증 및 완료 정의

- [x] Unity C# 컴파일 성공
- [x] `HealthcareQaSuite` 전체 통과
- [x] `Main.unity` 사용자 흐름을 상태 머신·종단 QA와 씬 직렬화 점검으로 검증
- [x] macOS Editor의 `/usr/bin/say` 또는 대상 모바일 native TTS smoke test 통과
- [x] 변경 diff 및 씬 직렬화 참조 점검
- [x] 계획서 체크박스 완료 처리
- [x] 관련 Linear PBI 업데이트
- [x] 사용자 승인 범위에 따라 Git 커밋 및 푸시

## 6. 완료 증거

- Unity 6000.3.18f1 Runtime/Editor Bee Roslyn 컴파일: exit 0
- 임시 Unity 프로젝트 `HealthcareQaSuite.RunBatch`: exit 0, `AI_HEALTHCARE_QA_PASSED`
- `Main.unity`: 운동 추적 자동 시작 차단, TTS 명시 참조, 보정/스쿼트 설정 직렬화 확인
- macOS `/usr/bin/say -v Yuna`: 한국어 운동 시작 안내 재생 exit 0
- Linear: [AI-146 / PBI-109](https://linear.app/ai-healthcare-coach/issue/AI-146) 완료 코멘트 추가 및 `Done` 전환
- Git: 최종 변경 커밋 및 `origin/main` 푸시

실제 모바일 카메라·native TTS는 대상 실기기에서 한 번 더 확인하는 것을 권장합니다.
