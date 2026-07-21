# Linear 미완료 항목 구현 매트릭스

2026-07-14 조회한 미완료 `LRN-*`/`PBI-*` 91개를 기준으로 한다. `구현`은 코드·설정·실행 가능한 하네스 또는 운영 양식을 의미한다. 사람·실기기 결과가 필요한 행은 외부 증거가 있어야 Linear를 Done으로 바꿀 수 있다.

| ID | 구현 산출물 | 외부 증거 |
| --- | --- | --- |
| LRN-004 | `Learning/NGramModel` smoothing/perplexity | 없음 |
| LRN-005 | 벡터·행렬·softmax | 없음 |
| LRN-006 | embedding/cosine/nearest | 없음 |
| LRN-007 | `TinyNeuralLanguageModel` train step | 없음 |
| LRN-008 | scaled dot-product attention | 없음 |
| LRN-009 | positional encoding/transformer block | 없음 |
| LRN-010 | `ILanguageModelClient`, 대화형 CLI | 실제 모델 adapter 선택 시 필요 |
| LRN-011 | `HealthcareSafetyPolicy` | 정책 검토 |
| LRN-012 | Markdown/TXT loader·overlap chunker | 없음 |
| LRN-013 | TF-IDF vector store/top-k | 없음 |
| LRN-014 | 출처 기반 RAG agent | 없음 |
| LRN-015 | greedy/top-k/sample·reward | 없음 |
| LRN-016 | safety→retrieve→generate→guard 루프 | 없음 |
| PBI-004 | `docs/governance/expert-review-protocol.md` | 전문가 서명 |
| PBI-005 | `docs/governance/regulatory-gate.md` | 국가별 법률 검토 |
| PBI-006 | `OnboardingFlowController` privacy consent | 사용성 확인 |
| PBI-007 | 카메라 권한 coroutine·거부 안내 | iOS/Android 실기기 |
| PBI-008 | 안전 고지 승인·시작 차단 | 문구 검토 |
| PBI-009 | 좌표 로그 opt-in·전체 삭제 | 실기기 파일 검사 |
| PBI-010 | 컨디션 사전 확인·통증 시작 차단 | 사용성 확인 |
| PBI-011 | 선택 모델 다운로드 동의·업데이트 설치기 | 실제 모델 패키지 |
| PBI-012 | `ExerciseCatalog` 스쿼트 카드 모델 | UI 사용성 |
| PBI-014 | Beginner/Standard/Advanced preset | 전문가 threshold 검수 |
| PBI-015 | 시작 전 체크리스트 | 사용성 확인 |
| PBI-016 | ComingSoon 상태·사유 | 없음 |
| PBI-019 | `CameraSetupAdvisor` 거리/조명/각도 | 실기기 카메라 |
| PBI-021 | 20-frame `PoseCalibrationService` | 실기기 2~3초 검증 |
| PBI-022 | calibration 기반 좌표 정규화 | 단말 회전/미러 검증 |
| PBI-023 | `FloorReferenceEstimator` | 다양한 바닥/신발 |
| PBI-026 | `MediaPipeInstallationVerifier` | 지원 단말 native 실행 |
| PBI-029 | `PoseProviderHealthMonitor` 품질 게이트 | 장시간 실기기 |
| PBI-031 | provider health JSONL telemetry | 단말 로그 검토 |
| PBI-040 | 기존 knee symmetry 규칙 + 재사용 evaluator | 전문가 검수 |
| PBI-041 | `rules_v1.json`, `VersionedRuleCatalog` | 콘텐츠 승인 |
| PBI-042 | 기존 `FeedbackPrioritizer` severity/cooldown | 사용성 확인 |
| PBI-043 | 기존 low-visibility early return + health gate | 가림 실기기 |
| PBI-045 | `PoseStatusIndicator` 아이콘/강조 모델 | UI 시각 검수 |
| PBI-047 | 음향·진동·음성·텍스트 설정 | 실기기 진동/오디오 |
| PBI-049 | `SafetyPauseMonitor` 지속 위험 중단 권고 | 전문가 지속시간 검수 |
| PBI-050 | `TextFeedback` 대체 채널 | 접근성 검수 |
| PBI-051 | schema-versioned session metadata | 없음 |
| PBI-052 | GZip 좌표 JSONL 저장 | 저장 공간/복구 시험 |
| PBI-053 | schema-versioned rule event | 없음 |
| PBI-054 | 기존 summary builder + report 연결 | 없음 |
| PBI-055 | retention + 허용 확장자·SHA-256 무결성 | OS sandbox/보관 정책 검토 |
| PBI-056 | 세션별/전체 삭제 API | 실기기 삭제 100% |
| PBI-058 | `WorkoutNetworkGuard`, RemoteApi 차단 | 비행기 모드/네트워크 계측 |
| PBI-059 | `OnDeviceRuntimeVerifier`, model adapter 계약 | Gemma/LiteRT 모델·단말 |
| PBI-060 | deterministic template report fallback | 리포트 이해도 |
| PBI-061 | 제한 prompt template | 안전 평가 |
| PBI-062 | rule_id catalog lookup | 콘텐츠 승인 |
| PBI-063 | `WorkoutReportPresenter` view model/event | 화면 사용성 |
| PBI-064 | 진단·처방·완치 금칙 필터 | red-team 평가 |
| PBI-065 | 통증 중단 안전 문구 후처리 | 문구 검토 |
| PBI-066 | temperature 0.2·seed 42 설정 | 실제 runtime 재현성 |
| PBI-069 | 절차형 `InstructorSquatClip` | 전문가 모션 검수 |
| PBI-070 | `AvatarComparisonCoordinator` 2-panel layout | 기기별 UI |
| PBI-071 | feedback→timeline marker builder | 실제 세션 확인 |
| PBI-072 | Front/Side/ThreeQuarter 시점 전환 | UI 확인 |
| PBI-073 | low-confidence blur/label 모델 | 시각 검수 |
| PBI-074 | 좌표 재구성 리플레이 고지 상수 | 문구 검토 |
| PBI-075 | `SessionHistoryRepository` | 대량 세션 성능 |
| PBI-076 | rule별 개선 추세 analyzer | 실제 다회 세션 |
| PBI-077 | 안정성 점수 공식 | 전문가/사용자 해석 검증 |
| PBI-078 | 로컬 privacy trust survey | 실제 참가자 |
| PBI-079 | 최소 `CoreEventLogger` 스키마 | KPI/개인정보 검토 |
| PBI-080 | 개인식별·좌표 제외 공유 미리보기 | 사용성 확인 |
| PBI-081 | GUID token, 만료, revoke | 서버 공유 채널 선택 시 통합 |
| PBI-082 | coach request 로컬 export | 코치 운영·전송 채널 |
| PBI-083 | lunge profile + reusable lower-body evaluator | 런지 데이터/전문가 검수 |
| PBI-084 | versioned challenge catalog | 전문가 승인 |
| PBI-085 | `DevicePerformanceProfiler` + 세션 화면 벤치 UI(60s/10m)·JSON 저장 하네스 (`docs/qa/device-performance-profiling-harness.md`) | 지원 단말 매트릭스 실측·외부 증거 여전히 필요 |
| PBI-086 | 640×480/10 FPS/LLM-off 저사양 모드 | 저사양 단말 |
| PBI-087 | 지속 FPS 저하·메모리·세션 시간 경고 | 실제 발열 측정 |
| PBI-088 | model memory budget/load/unload/idle policy | 실제 runtime |
| PBI-089 | 분리된 content version manifest | 배포 승인 |
| PBI-090 | 600초 배터리 benchmark | 실기기 배터리 |
| PBI-091 | FPS/latency/drop/low-memory acceptance evaluator | 실기기 결과 |
| PBI-093 | 33-landmark 합성 fixture 4종 | 없음 |
| PBI-094 | `HealthcareQaSuite` 결정론 시나리오 | 수동 시나리오 |
| PBI-095 | `docs/qa/device-matrix.md` | 실제 단말 결과 |
| PBI-096 | `docs/qa/usability-test-script.md` | 실제 참가자 |
| PBI-097 | 전문가 검수 프로토콜/버전 gate | 전문가 서명 |
| PBI-098 | `mvp-release-decision.md` 보류 조건 | 출시 회의 승인 |
| PBI-099 | `store-copy-ko.md` | 스토어/법률 검토 |
| PBI-100 | freemium 경계 문서·불변식 | 사업 승인 |
| PBI-101 | `EntitlementService` | 결제 SDK 선택 시 통합 |
| PBI-102 | SHA-256 staging install/current/previous rollback | 배포 서버·서명 정책 |
| PBI-103 | `closed-beta-cohort.md` | 동의한 20~50명 실제 운영 |
| PBI-104 | 출시 체크리스트 | 승인자·날짜 |
