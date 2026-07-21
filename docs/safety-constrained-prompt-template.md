# 안전 제한 프롬프트 템플릿 (PBI-061 / AI-102)

온디바이스 운동 리포트 생성용 LLM 프롬프트를 결정론적으로 조립하는 산출물 명세입니다.
구현 클래스: `Rag.Healthcare.Reports.SafetyConstrainedPromptTemplate`.

매트릭스 대응: [linear-implementation-matrix.md](linear-implementation-matrix.md) — PBI-061 구현 산출물 "제한 prompt template", 외부 증거 "안전 평가".

## 프롬프트 구조

고정 순서로 두 블록을 결합합니다.

1. **SYSTEM** — `SafetyDirectives` 상수. 역할·금지 행위·응답 형식을 고정한다.
2. **SESSION** — 화이트리스트 수치만 `CultureInfo.InvariantCulture`로 포맷해 주입한다.

예시 형태:

```text
SYSTEM: You are an offline fitness posture coach. Use only the supplied numeric summary. Do not diagnose, prescribe treatment, mention diseases, or invent facts. Respond in Korean in 3 short sentences.
SESSION: duration=120.0s, feedback=4, warnings=2, critical=1, pose_fps=15.0, visibility=0.85.
```

## 안전 제약 (`SafetyDirectives`)

| 제약 | 의미 |
| --- | --- |
| 제공 수치만 사용 | 공급된 numeric summary 외 사실·가정 금지 |
| 진단·처방 금지 | diagnose / prescribe treatment 금지 |
| 질병·치료 언급 금지 | mention diseases 금지 |
| 날조 금지 | invent facts 금지 |
| 한국어·간결 | Korean, 3 short sentences |

## 화이트리스트 SESSION 필드

| 토큰 | 소스 필드 | 포맷 |
| --- | --- | --- |
| `duration` | `durationSeconds` | `0.0` InvariantCulture |
| `feedback` | `feedbackCount` | 정수 InvariantCulture |
| `warnings` | `warningFeedbackCount` | 정수 InvariantCulture |
| `critical` | `criticalFeedbackCount` | 정수 InvariantCulture |
| `pose_fps` | `averagePoseFps` | `0.0` InvariantCulture |
| `visibility` | `averageVisibility` | `0.00` InvariantCulture |

주입하지 않는 예: `sessionId`, `exercise`, `topFeedbackIds`, 자유 텍스트 피드백 원문, 카메라/기기 문자열.

## 결정론 근거

- 동일 `PoseSessionSummary` → `Build` 두 번 호출 시 문자열 완전 일치.
- 수치 포맷은 로케일 기본값이 아니라 `InvariantCulture`를 사용해 소수점 표기를 고정한다.
- UTC 시각·난수·런타임 상태는 프롬프트에 포함하지 않는다.
- 결정론 QA: `HealthcareQaSuite.VerifySafetyConstrainedPrompt`.

## 안전 평가 체크리스트 (외부 증거)

코드/QA만으로 "안전 평가"를 Done으로 간주하지 않습니다. 사람이 아래를 검토해야 합니다.

- [ ] SYSTEM 지시가 진단·처방·질병·치료·날조를 명확히 금지하는가
- [ ] SESSION에 자유 텍스트·개인식별·좌표 원문이 없는가
- [ ] 의도적 red-team 입력(질병명 유도, 처방 요청)에 대해 프롬프트 제약이 충분한가
- [ ] 한국어 3문장 응답 지시가 제품 톤과 맞는가
- [ ] 후속 금칙어 필터(PBI-064)·안전 문구 후처리(PBI-065)와 역할이 겹치지 않는가

## 후속 이슈 경계

| 이슈 | 범위 | 이 템플릿과의 관계 |
| --- | --- | --- |
| PBI-062 (AI-99) | rule_id 카탈로그 조회·컨텍스트 주입 | 확장 지점만 주석으로 표시. 본 구현 금지 |
| PBI-064 (AI-95) | 출력 금칙어/금지 의도 필터 | `OnDeviceReportService.IsSafe` 영역. 프롬프트 조립과 분리 |
| PBI-065 (AI-103) | 통증 중단 안전 문구 후처리 | `AppendSafetyLanguage` 영역. 프롬프트 조립과 분리 |
| PBI-060 (AI-98) | 저사양 템플릿 리포트 fallback | `BuildTemplateReport` 영역. 변경 없음 |
| PBI-066 | temperature/seed 재현성 | 런타임 생성 설정. 프롬프트 템플릿 범위 밖 |
