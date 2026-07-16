# AI Healthcare Coach - Agent Rules & Guidelines

이 문서는 AI-Healthcare-Coach 워크스페이스 내에서 작업을 수행할 때 준수해야 하는 에이전트의 작동 원칙, 개발/QA 규칙, 그리고 실시간 자세 분석 최적화 가이드라인을 정의합니다.

---

## 1. DEV 개발 모드 작동 규칙 (Development Rules)
기능 구현 및 개발 작업을 진행할 때, 다음 문서들의 가이드라인과 규칙을 항상 최우선으로 읽고 설계 및 구현에 반영하세요:
* [ragUnityTestGuide.md](file:///Users/sindongju/AI-Healthcare-Coach/ragUnityTestGuide.md)
* [Integrationplan.md](file:///Users/sindongju/AI-Healthcare-Coach/Integrationplan.md)
* [ragSystemplan.md](file:///Users/sindongju/AI-Healthcare-Coach/ragSystemplan.md)

### 선(先) 계획, 후(後) 개발 원칙 (No Code Without plan.md)
* 어떤 상황에서도 코드(`.cs`)부터 먼저 수정하지 마십시오.
* 요구사항을 수령하면 가장 먼저 `plans/` 폴더 내에 `plan_[영문_작업_스네이크_케이스].md` 형태로 작업 계획서를 작성하고 사용자의 승인을 득한 후 작업을 수행해야 합니다.

### 완료 정의 (Definition of Done)
1. 작성한 계획서 파일의 모든 작업 단계 체크박스가 `[x]`로 완료 표시되었는가?
2. 수정된 Unity C# 소스 코드와 계획서가 Git에 정상적으로 커밋 및 푸시되었는가?
3. 수정된 기능에 대해 빌드/테스트 에러가 없음을 자체 검증 완료하였는가?
4. Linear PBI 이슈가 자동으로 업데이트 및 닫기('Done') 상태가 되었는가?

---

## 2. QA 테스트 실행 규칙 (QA Rules)
* **Unity C# 코드 수정 금지**: QA 모드나 테스트 시나리오 검증 시에는 핵심 소스 코드(`.cs`)를 임의로 변경하지 마십시오.
* **스크립트 허용**: QA 자동화, 테스트 스크립트, Linear 연동을 위한 Python 스크립트(`.py`)나 문서(`.md`) 수정만 허용됩니다.
* **버그 보고**: 발견된 버그 및 개선점은 직접 수정하지 말고, 피드백 리포트를 통해 사용자에게 전달하십시오.

---

## 3. 관절 반응 지연 및 성능 최적화 규칙 (Pose Latency & Optimization Rules)
실시간 관절 트래킹의 반응 속도가 한 박자 느리거나 지연이 관찰될 경우, 최적화 작업을 진행할 때 아래 규칙을 따르십시오.

### A. 지연 원인 진단 및 실측 규칙
* **Xcode Instruments 프로필 필수 사용**: 반응 속도 이슈가 보고되면, 추측에 의존하지 말고 Xcode Instruments의 **Time Profiler**를 사용하여 MediaPipe 추론 및 프레임 전달 루프 내의 `detectAsync` 비동기 시간 지연을 실측하고 병목 구간을 구체적으로 식별해야 합니다.

### B. 관절 보정 필터 최적화 규칙
* **필터 알파 상향 조정 (`landmarkSmoothingAlpha`)**:
  * Unity C#의 `landmarkSmoothingAlpha` 누적 지연으로 인해 반응이 밀릴 수 있습니다.
  * 반응속도가 중요한 실기기(특히 iOS) 환경에서는 기본값(예: 0.35f)에서 필터 알파값을 상향 조정(예: 0.5f 이상)하여 누적 지연을 줄이고 반응성(responsiveness)을 보장합니다.
  * *주의*: 알파값을 과도하게 높이면 노이즈(Jitter)가 증가할 수 있으므로, 조명 상태와 노이즈 수준을 교차 검증하며 최적의 값을 찾습니다.

### C. 기기 발열 및 스로틀링(Thermal Throttling) 방지 규칙
* **발열 상태 모니터링 및 부하 저감**:
  * 모바일 기기의 발열 상태(Thermal State)로 인해 CPU/GPU 성능 스로틀링이 걸리면 `detectAsync` 및 프레임 복사 루프에서 큰 지연이 발생할 수 있습니다.
  * 기기 발열을 최소화하기 위해 **카메라 요청 해상도를 필요 스펙에 맞게 제한(Capping)**(예: 640x360 or 640x480 등)하고, CPU-to-GPU 픽셀 복사량 및 메모리 할당(GC Alloc)을 매 프레임 발생하지 않도록 코드를 설계해야 합니다.
