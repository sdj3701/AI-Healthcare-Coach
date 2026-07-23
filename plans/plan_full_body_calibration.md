# 📌 작업 계획서: 운동 전 전신 측정 및 캘리브레이션 (Ready State) 구현

## 1. 개요 및 목표
* 운동 시작 직전 사용자의 전신이 카메라 프레임에 완전히 위치했는지 검증하고, MediaPipe Pose의 ROI(Region of Interest) 및 랜드마크 트래킹을 안정화하는 **"전신 측정/캘리브레이션 3단계 상태 머신(Ready ➔ Countdown ➔ Workout)"**을 구축합니다.
* 운동 도중 전신 이탈(Out-of-Frame) 시 자동 일시정지 및 가이드 재진입 예외 처리를 포함하여 트래킹 안정성과 자세 분석 정확도를 극대화합니다.

## 2. 주요 작업 단계 (대표 작업 리스트)
- [ ] **Step 1: Workout Tracking State Machine 설계 및 Enum 구축**
  - `RealtimeFeedbackOrchestrator.cs` 또는 상태 관리자에 `WorkoutTrackingState` Enum 정의 (`ReadyForCalibration`, `CountingDown`, `InWorkout`, `PausedOutOfFrame`)
  - 상태별 트래킹 플로우 제어 로직 구현
- [ ] **Step 2: 전신 감지 캘리브레이션(Calibration) 검증 로직 개발**
  - MediaPipe 33개 관절 랜드마크의 `Visibility` / `Presence Score` 조건 검증 (주요 관절: 머리, 어깨, 골반, 무릎, 발목 score > 0.85f)
  - 전신 충족 조건이 1.5초 이상 안정적으로 유지 시 `CountingDown` 상태로 전환
- [ ] **Step 3: 실루엣 가이드 및 캘리브레이션 UI 오버레이 연동**
  - 모바일 프리뷰 UI 상에 전신 영역 가이드 실루엣 표시
  - 전신 감지 상태 알림 (예: "카메라 뒤로 물러서주세요" ➔ "전신 감지 완료! 3초 후 시작합니다")
- [ ] **Step 4: Out-of-Frame 예외 처리 및 자동 일시정지 로직**
  - 운동 중 관절 가시성(Visibility) 저하 시 `PausedOutOfFrame` 상태로 전환하여 오작동 방지
- [ ] **Step 5: Linear PBI 등록 및 동기화**
  - Linear GraphQL API를 통해 PBI 이슈 생성 및 계획서 연동

## 3. 예상 예외 사항 및 제약 조건과 코드 구현이유
* **사전 전신 측정 도입 이유**: MediaPipe Pose는 첫 프레임에서 전신 Detector가 잡은 ROI를 바탕으로 연속 Tracker를 실행하므로, 전신이 온전히 들어온 상태에서 시작해야 운동 중 랜드마크 튐(Jitter)과 각도 왜곡이 방지됩니다.
* **1.5초 유지 조건 도입 이유**: 일시적인 관절 인식 순간 반응으로 인한 오작동을 방지하고 필터(EMA)가 충분히 안정화(Warm-up)되도록 보장합니다.

## 4. 완료 정의 (Definition of Done)
- [ ] 작성한 `plan_full_body_calibration.md` 파일의 모든 작업 단계 체크박스가 `[x]`로 완료 표시되었는가?
- [ ] 수정된 Unity C# 소스 코드와 계획서가 Git에 정상적으로 커밋 및 푸시되었는가?
- [ ] 수정된 기능에 대해 빌드/테스트 에러가 없음을 확인하였는가?
- [ ] Linear PBI 이슈가 자동으로 생성/업데이트되고 'In Progress' / 'Done'으로 관리되었는가?
