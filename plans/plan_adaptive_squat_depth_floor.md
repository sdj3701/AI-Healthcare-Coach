# Adaptive Squat Depth Floor Plan

작성일: 2026-07-26

> 2026-07-27 후속 수정: 실제 전면 카메라에서 고관절–무릎 2D 좌표가 거의 같은 정상 자세를 정확히 `0`으로 잡지 못하는 오차가 확인되어, 현재 구현은 1차 `-0.03` 높이 허용 범위와 2차 무릎 굽힘/골반 하강 검증을 함께 사용한다. 최신 동작은 `docs/architecture/current-pose-decision-logic.md`와 `docs/troubleshooting/squat-slow-depth-score-tts-2026-07-27.md`를 기준으로 한다.

## 1. 요구사항 해석

- 운동 화면에서 사용자가 실제로 수행한 스쿼트 횟수와 자세 오류가 없는 횟수를 분리해 보여준다.
- 사용자가 본인의 최저점까지 내려갔는데 고정 무릎 각도 차이 때문에 반복 동작이 계속 누락되지 않도록, 런타임에서 개인별 최저점 무릎 각도를 보정한다.
- 개인화는 현재 운동 세션의 유효 스쿼트 3회를 기준으로 갱신한다.
- 개인화된 각도 기준과 관계없이 엉덩이 관절 중심이 양 무릎 관절 중심 높이까지 내려오지 않은 동작은 횟수로 인정하지 않는다.
- 한 프레임 관절 튐으로 기준이 완화되거나 횟수가 증가하지 않도록 최소 2개 연속 안정 프레임에서 엉덩이–무릎 높이 조건을 확인한다.

## 2. 판정 설계

### 2.1 악용 방지 절대 하한선

- 화면 좌표의 아래 방향이 양수인 점을 이용해 다음 값을 신체 크기로 정규화한다.

```text
hipToKneeDepth = (hipCenterY - kneeCenterY) / bodyScale
```

- `hipToKneeDepth >= 0`이면 엉덩이가 무릎과 같거나 더 낮은 위치다.
- 이 조건이 최소 2개 연속 유효 프레임에서 관찰되어야 해당 반복을 깊이 통과로 표시한다.
- 관절 신뢰도가 낮거나 엉덩이/무릎 좌표가 누락된 프레임은 통과·학습 근거로 사용하지 않는다.
- 런타임 개인화는 이 `0` 하한선을 변경하거나 우회하지 않는다.

### 2.2 런타임 개인화

- 하한선을 통과한 완전한 반복에서 해당 반복의 최소 무릎 각도를 수집한다.
- 첫 유효 반복은 즉시 횟수로 인정하고, 이후 최대 3회의 샘플을 이용해 세션 개인 기준을 안정화한다.
- 개인화된 무릎 각도에 제한된 여유각을 더해 다음 반복의 Bottom 인식 보조값으로 사용한다.
- 개인 기준은 일시적인 추적 저하·일시정지에는 유지하고, 새 운동 세션이나 리셋에서는 초기화한다.
- 개인화된 각도는 Bottom 구간 인식을 돕는 값이며, 엉덩이–무릎 절대 하한선 대신 사용하지 않는다.

### 2.3 횟수와 자세 품질 분리

- `전체 스쿼트`: 동작 왕복과 엉덩이–무릎 깊이 하한선을 통과한 횟수다.
- `정확한 자세`: 전체 스쿼트 중 무릎 정렬, 균형, 상체 등의 확정 오류가 없는 횟수다.
- 다른 자세 피드백이 있더라도 깊이 하한선을 통과한 실제 동작은 전체 스쿼트에 남는다.
- 깊이가 부족한 동작에는 “엉덩이를 무릎 높이까지 내려가야 횟수로 인정됩니다.”라고 구체적으로 안내한다.

## 3. 구현 단계

- [x] `PoseFeatureFrame`과 `PoseFeatureExtractor`에 신체 크기 정규화 엉덩이–무릎 높이 값을 추가한다.
- [x] `RealtimePoseRuleSettings`에 절대 깊이 하한선, 연속 프레임 수, 개인화 샘플 수와 제한된 각도 여유 설정을 추가한다.
- [x] `ExercisePhaseState`와 `ExercisePhaseDetector`에 현재 깊이, 반복 내 최대 깊이, 하한선 통과 여부, 개인화 각도 및 샘플 수를 추가한다.
- [x] 첫 유효 반복부터 카운트하면서 최대 3회로 개인 Bottom 각도를 보정하고, 세션/중단별 초기화 정책을 적용한다.
- [x] 깊이 피드백을 무릎 각도 단독 기준에서 엉덩이–무릎 절대 하한선 우선 기준으로 변경한다.
- [x] 운동 화면에 전체 스쿼트 횟수, 정확 자세 횟수, 현재 깊이 수치와 개인화 기준을 구분해 표시한다.
- [x] 디버그 추적 HUD에도 깊이 통과 여부와 개인화 진행 상태를 표시한다.
- [x] 합성 관절 fixture와 QA에 동일 높이 통과, 무릎보다 높은 엉덩이 거부, 2프레임 안정성, 3회 개인화, 좌우 반전·크기 변화 불변, 저신뢰 프레임 학습 금지 테스트를 추가한다.
- [x] Unity C# 런타임/Editor 컴파일과 `HealthcareQaSuite`를 실행해 회귀 오류가 없음을 검증한다.
- [x] 관련 Linear PBI를 구현·QA 근거와 함께 Done으로 갱신한다.
- [x] 계획서 체크박스를 완료 처리하고 변경 파일을 Git 커밋 후 원격 저장소에 푸시한다.

## 4. 주요 수정 예상 파일

- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseFeatureFrame.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseFeatureExtractor.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimePoseRuleSettings.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/ExercisePhaseState.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/ExercisePhaseDetector.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimePoseRuleEngine.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimeFeedbackOrchestrator.cs`
- `Assets/Scripts/RagHealthcare/UI/MobileWorkoutPrototypeView.cs`
- `Assets/Scripts/RagHealthcare/Pose/Rendering/PoseTrackingStatusView.cs`
- `Assets/Scripts/RagHealthcare/Qa/SyntheticPoseFixtures.cs`
- `Assets/Editor/RagHealthcare/HealthcareQaSuite.cs`

## 5. 완료 기준

- 엉덩이가 무릎보다 위에 머문 동작은 무릎 각도가 크게 굽혀져도 횟수가 증가하지 않는다.
- 엉덩이가 무릎과 같은 높이 또는 아래에서 2개 연속 안정 프레임으로 관찰된 완전한 동작은 첫 시도부터 1회로 인정된다.
- 3회의 유효 반복 동안 개인 Bottom 무릎 각도 수치가 런타임으로 보정되고 이후 비슷한 동작이 안정적으로 인식된다.
- 개인화 값이 어떠해도 엉덩이–무릎 높이 하한선은 완화되지 않는다.
- 전체 스쿼트와 정확 자세 횟수가 운동 화면에서 별도로 확인된다.
- 깊이 미달 시 사용자가 무엇을 더 해야 하는지 명확한 한국어 화면/TTS 피드백이 제공된다.
- 기존 스쿼트 phase, TTS, 카메라 전환, 추적 일시정지 및 좌표 변환 QA가 계속 통과한다.

## 6. 완료 근거

- Unity 6000.3.18f1 Runtime 및 Editor C# 컴파일: 성공
- 별도 임시 Unity 프로젝트 `HealthcareQaSuite.RunBatch`: `AI_HEALTHCARE_QA_PASSED`
- 추가 QA: 첫 유효 반복 즉시 인정, 3회 개인화, 1프레임 스침 거부, 엉덩이가 무릎보다 높은 깊은 무릎각 거부, 저신뢰 프레임 학습 금지, 빠른 상승 복구, 좌표 변환 불변
- Linear: [AI-148 / PBI-111](https://linear.app/ai-healthcare-coach/issue/AI-148) 완료 코멘트 추가 및 `Done` 전환
- Git: 구현 커밋 `8c39af9`를 원격 `main`에 푸시 완료
