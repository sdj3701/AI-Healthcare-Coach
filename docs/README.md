# 📚 AI-Healthcare-Coach Documentation Index

프로젝트의 주요 설계, 최적화, 기능 명세 및 트러블슈팅 문서 목차 가이드입니다.

---

## 📁 1. 아키텍처 및 시스템 설계 (`docs/architecture/`)
시스템 전반의 통합 계획, 모듈 구조, 포즈 판정 로직 및 RAG 연동 설계 문서 모음입니다.

* [module-architecture.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/module-architecture.md) - 모듈별 아키텍처 및 레이어 구조
* [Integrationplan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/Integrationplan.md) - 전체 시스템 통합 마일스톤 및 계획
* [ragSystemplan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/ragSystemplan.md) - RAG 지식 기반 및 실시간 룰 엔진 연동 구조
* [current-pose-decision-logic.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/current-pose-decision-logic.md) - 현재 적용된 관절 3D/2D 자세 판정 알고리즘
* [linear-implementation-matrix.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/linear-implementation-matrix.md) - Linear PBI 및 구현 매트릭스
* [safety-constrained-prompt-template.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/safety-constrained-prompt-template.md) - 헬스케어 AI 안전 제약 프롬프트 템플릿
* [implementation_plan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/implementation_plan.md) - 시스템 구현 종합 레퍼런스
* [PBI109_PBI110_ImplementationPlan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/architecture/PBI109_PBI110_ImplementationPlan.md) - PBI-109/110 신규 사용자 경험 구현 계획서

---

## 📁 2. 최적화 및 런타임 성능 (`docs/optimization/`)
관절 추적 및 카메라 입력의 프레임 레이트, 메모리(GC), 발열 최적화 문서입니다.

### 📷 카메라 최적화 문서 (`docs/optimization/camera/`)
* [CameraPoseLifecycleRecoveryPlan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/optimization/camera/CameraPoseLifecycleRecoveryPlan.md) - 백그라운드 전환 및 카메라 복구 라이프사이클
* [CameraPoseTrackingOptimizationPlan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/optimization/camera/CameraPoseTrackingOptimizationPlan.md) - 카메라-포즈 추적 종합 최적화 마스터 플랜
* [FrontCameraPoseStabilityPlan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/optimization/camera/FrontCameraPoseStabilityPlan.md) - 전면 카메라 바디 컷오프 및 노이즈 안정화
* [camera-capture-optimization-plan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/optimization/camera/camera-capture-optimization-plan.md) - 캡처 해상도 및 프레임 버퍼 최적화
* [camera-pose-tracking-optimization-2026-07-22.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/optimization/camera/camera-pose-tracking-optimization-2026-07-22.md) - 최신 카메라-포즈 링버퍼 및 필터 지연시간 개선

### ⚡ 포즈 및 런타임 최적화 (`docs/optimization/`)
* [pose-runtime-optimization.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/optimization/pose-runtime-optimization.md) - iOS/모바일 포즈 추론 및 smoothingAlpha 최적화
* [remaining-optimization-plan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/optimization/remaining-optimization-plan.md) - 잔여 로드맵 최적화 리스트

---

## 📁 3. 세부 기능 구현 문서 (`docs/features/`)
TTS, STT, 피드백 UI 및 오버레이 등 서브시스템별 구현 계획입니다.

* [FeedbackMediaPipeplan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/features/FeedbackMediaPipeplan.md) - MediaPipe 랜드마크 기반 피드백 연동
* [SpeechTextplan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/features/SpeechTextplan.md) - STT 음성 인식 및 파이프라인
* [TTSCreateplan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/features/TTSCreateplan.md) - TTS 음성 피드백 생성기
* [TestMediaPipeplan.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/features/TestMediaPipeplan.md) - MediaPipe 단체 테스트 하네스
* [mobile-workout-prototype-ui.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/features/mobile-workout-prototype-ui.md) - 모바일 UI Toolkit 워크아웃 뷰

---

## 📁 4. 트러블슈팅 및 장애 대처 (`docs/troubleshooting/`)
개발 및 빌드 과정에서 발생한 이슈 및 해결 리포트입니다.

* [MediaPipeTroubleshooting.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/troubleshooting/MediaPipeTroubleshooting.md) - MediaPipe 네이티브 플러그인 로딩 문제 해결
* [ios-black-screen-editor-vs-device.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/troubleshooting/ios-black-screen-editor-vs-device.md) - iOS 기기 블랙 스크린 원인 및 렌더링 검증
* [start-stop-restart-failure-trace.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/troubleshooting/start-stop-restart-failure-trace.md) - 세션 시작/중지 재가동 오류 분석

---

## 📁 5. 제품 및 서비스 명세 (`docs/product/`)
* [exercise-routine-specification.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/product/exercise-routine-specification.md) - 운동 루틴 및 데이터 스펙
* [freemium-and-entitlements.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/product/freemium-and-entitlements.md) - 무상/유상 정책 권한

---

## 📁 6. QA 및 테스트 가이드 (`docs/qa/`)
* [ragUnityTestGuide.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/qa/ragUnityTestGuide.md) - Unity RAG 테스트 실행 및 자동 검증 가이드
* [device-matrix.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/qa/device-matrix.md) - 타겟 단말기 매트릭스
* [device-performance-profiling-harness.md](file:///Users/sindongju/AI-Healthcare-Coach/docs/qa/device-performance-profiling-harness.md) - 기기 성능 측정을 위한 하네스 가이드

---

## 📁 7. 기타 거버넌스 및 운영
* [`docs/governance/`](file:///Users/sindongju/AI-Healthcare-Coach/docs/governance/) - 의료 리뷰 프로토콜 및 규제 가이드라인
* [`docs/operations/`](file:///Users/sindongju/AI-Healthcare-Coach/docs/operations/) - 운영 및 릴리즈 체크리스트
