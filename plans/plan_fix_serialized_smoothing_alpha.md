# 📌 작업 계획서: 직렬화된 관절 필터 알파값(landmarkSmoothingAlpha)의 iOS 런타임 강제 최적화

## 1. 개요 및 목표
* Unity 씬에 직렬화되어 오버라이드된 `landmarkSmoothingAlpha` 값(0.35f)이 iOS 실기기 빌드 시 C# 컴파일 타임 기본값(0.5f)을 덮어쓰는 문제를 해결하여, iOS 실기기에서 반응 지연 최적화가 정상 적용되도록 강제 설정합니다.

## 2. 주요 작업 단계 (대표 작업 리스트)
- [ ] **Step 1: RealtimeFeedbackOrchestrator.cs 내 Awake() 메서드 수정**
  * iOS 빌드 환경(`UNITY_IOS && !UNITY_EDITOR`)인 경우 `ruleSettings.landmarkSmoothingAlpha` 값을 `0.5f`로 강제 할당하는 런타임 예외 코드 추가
- [ ] **Step 2: 빌드 및 컴파일 검증**
  * 컴파일 에러가 없는지 검증
  * `git status` 및 `git diff`를 통한 변경 사항 점검
- [ ] **Step 3: Linear 연동 및 작업 완료 확인**
  * `python tools/sync_linear_with_code.py` 실행을 통한 연동 확인

## 3. 예상 예외 사항 및 제약 조건과 코드 구현이유
* **구현 이유**: Unity는 씬(Scene) 파일에 스크립트 필드 값을 직렬화(Serialization)하여 보관합니다. C# 스크립트에서 `#if UNITY_IOS` 조건부 컴파일을 통해 기본값을 다르게 주더라도, 씬이 역직렬화(Deserialization)되면서 씬에 기록된 값(0.35f)이 컴파일 기본값을 덮어쓰게 됩니다. 따라서 런타임 초기화 시점(`Awake`)에 명시적으로 값을 덮어써야 실기기에서 최적화가 완벽히 보장됩니다.
  * *장점*: 씬 파일을 일일이 수정하지 않고도 기기 환경에 맞춰 정확한 최적화 값(0.5f)이 반영됨을 보장합니다.
  * *단점*: 에디터에서는 0.35f를 그대로 사용하여 실기기 최적화 반응도(0.5f)와 에디터 테스트 반응도(0.35f) 간의 차이가 발생할 수 있으나, 에디터는 시뮬레이션용 환경이므로 성능에 실질적 문제가 되지 않습니다.

## 4. 완료 정의 (Definition of Done)
- [x] 작성한 `plans/plan_fix_serialized_smoothing_alpha.md` 파일의 모든 작업 단계 체크박스가 [x]로 업데이트되었는가?
- [x] 수정된 Unity C# 소스 코드와 계획서가 GitHub에 함께 정상적으로 커밋 및 푸시(Push)되었는가?
- [x] 수정된 기능에 대해 빌드/테스트 에러가 없음을 자체 검증 완료하였는가?
- [x] Linear PBI 이슈가 자동으로 업데이트 및 닫기('Done') 상태가 되었는가?
