# Feedback MediaPipe Plan

작성일: 2026-06-30

## 1. 목적

MediaPipe 관절 추적이 동작하기 시작했으므로, 다음 단계는 관절 데이터를 이용해 실시간 피드백을 주고 운동이 끝난 뒤 필요한 데이터만 남기는 구조를 설계하는 것이다.

이 문서는 바로 구현하기 전에 다음 내용을 결정하기 위한 기획 문서다.

- 어떤 데이터를 저장할지
- 실시간 피드백은 어떤 기준으로 만들지
- 운동 종료 후 데이터를 바로 삭제할지, 일정 시간 보관할지
- 개인정보와 저장 용량 문제를 어떻게 줄일지
- MVP에서는 어디까지 만들고, 이후 어디까지 확장할지

## 2. 현재 전제

- 카메라는 Unity에서 동작한다.
- MediaPipe 기반 관절 추적이 Unity Editor에서 동작한다.
- 현재 수신 가능한 핵심 데이터는 33개 pose landmark다.
- 서버 없이 로컬에서 추적하는 방향이다.
- 원본 영상 저장은 기본적으로 하지 않는 방향이 적절하다.
- 실시간 피드백은 TTS, 화면 메시지, HUD, 운동 리포트와 연결될 수 있다.

## 2.1 개발 방향 확정

사용자 결정에 따라 개발 방향은 선택지 C로 확정한다.

선택지 C:

- 세션 요약 저장
- 피드백 이벤트 로그 저장
- 전체 landmark 프레임은 운동 종료 시 삭제
- 원본 영상 저장 안 함
- 개발/QA 상세 로그만 옵션으로 하루 보관

이 방향의 핵심은 실시간 피드백에 필요한 데이터와 운동 후 기록에 필요한 데이터를 분리하는 것이다.

- 실시간 피드백:
  - 최근 몇 초의 landmark를 메모리 ring buffer에만 유지한다.
  - 피드백 판단, 반복 카운트, 자세 오류 판정에 사용한다.
- 운동 후 기록:
  - 세션 요약과 피드백 이벤트만 저장한다.
  - 사용자의 운동 이력, 리포트, 자주 발생한 문제 분석에 사용한다.
- 삭제 정책:
  - 전체 landmark buffer는 세션 종료 시 즉시 비운다.
  - QA debug landmark 로그는 옵션으로만 저장하고 24시간 후 자동 삭제한다.

## 3. 저장 데이터 후보

### 3.1 저장하지 않는 것이 좋은 데이터

원본 영상은 기본 저장 대상에서 제외하는 것이 좋다.

이유:

- 얼굴, 집 내부, 주변 사람 등 민감 정보가 포함될 수 있다.
- 저장 용량이 크다.
- 향후 배포 시 개인정보 동의, 삭제 요청, 보안 처리 부담이 커진다.
- 실시간 자세 피드백에는 원본 영상이 필수는 아니다.

단, 개발/QA 단계에서만 사용자가 명시적으로 켠 경우 짧은 테스트 클립을 남기는 옵션은 별도 기능으로 둘 수 있다.

### 3.2 실시간 처리에 필요한 임시 데이터

실시간 피드백에는 모든 과거 프레임이 필요하지 않다. 대부분은 최근 몇 초 데이터만 있으면 된다.

임시 저장 후보:

- 최근 3~10초 landmark ring buffer
- 최근 3~10초 visibility/presence 평균
- 최근 관절 각도
- 최근 반복 상태
- 최근 피드백 발화 기록
- 최근 오류 상태

권장:

- 메모리 ring buffer로만 유지한다.
- 운동 종료 시 기본 삭제한다.
- 디스크에는 저장하지 않는다.

### 3.3 세션 중 저장할 이벤트 데이터

실시간 피드백과 운동 종료 리포트에는 모든 프레임보다 이벤트 데이터가 더 중요하다.

저장 후보:

- 세션 시작/종료 시간
- 운동 종류
- 추적 FPS
- 카메라 방향
- 반복 횟수
- 각 반복의 시작/끝 시간
- 자세 오류 이벤트
- 피드백 메시지 이벤트
- 낮은 visibility 구간
- 사용자가 프레임 밖으로 나간 구간
- 주요 관절 각도 요약

예시:

```json
{
  "t_ms": 18420,
  "type": "feedback",
  "rule_id": "squat_knee_in",
  "severity": "warning",
  "message": "무릎이 안쪽으로 모이고 있습니다.",
  "rep": 3,
  "confidence": 0.82
}
```

권장:

- MVP에서는 JSONL 파일로 저장한다.
- 앱 기능이 커지면 SQLite로 전환한다.
- 이벤트는 작고 분석 가치가 크므로 세션 종료 후에도 보관 가능하다.

### 3.4 세션 종료 후 남길 요약 데이터

운동이 끝난 뒤 사용자에게 보여줄 리포트는 요약 데이터 중심으로 만든다.

저장 후보:

- 운동 날짜
- 총 운동 시간
- 반복 횟수
- 성공/주의/위험 피드백 개수
- 가장 자주 발생한 자세 문제
- 평균 추적 FPS
- 평균 landmark visibility
- 대표 관절 각도 통계
- 세션 점수
- 다음 운동 권장 사항

권장:

- 요약 데이터는 장기 보관 가능하다.
- 원본 영상이나 전체 landmark보다 개인정보 위험이 낮고 용량이 작다.
- 사용자의 변화 추이를 보여주는 데 유용하다.

## 4. 저장 방식 선택지

### 선택지 A: 완전 임시 방식

운동 중에는 메모리만 사용하고, 운동 종료 시 모든 landmark와 이벤트를 삭제한다.

장점:

- 개인정보 부담이 가장 작다.
- 용량 문제가 거의 없다.
- 구현이 단순하다.

단점:

- 운동 종료 리포트가 빈약해진다.
- 나중에 오류 분석이 어렵다.
- 사용자의 장기 개선 추이를 만들기 어렵다.

적합한 경우:

- 실시간 피드백만 목표일 때
- 개인정보 리스크를 최우선으로 줄일 때

### 선택지 B: 세션 요약만 보관

운동 중에는 임시 landmark buffer와 이벤트를 사용하고, 운동 종료 시 요약 데이터만 남긴다.

장점:

- 용량이 작다.
- 개인정보 부담이 낮다.
- 사용자 히스토리와 리포트를 만들 수 있다.

단점:

- 자세 문제를 나중에 상세 재분석하기 어렵다.
- 3D 리플레이를 만들 수 없다.

적합한 경우:

- 피드백 이벤트 근거를 남기지 않는 극단적으로 단순한 MVP에 적합하다.
- 운동 종료 화면이 매우 간단하고, 나중에 피드백 원인을 다시 볼 필요가 없을 때 좋다.

### 선택지 C: 요약 + 이벤트 로그 보관 - 확정

운동 종료 후 요약 데이터와 피드백 이벤트 로그를 남긴다. 전체 landmark 프레임은 삭제한다.

장점:

- 피드백 근거를 확인할 수 있다.
- 리포트 품질이 좋아진다.
- 용량 부담이 낮다.
- 나중에 규칙 엔진 개선에 도움이 된다.

단점:

- 저장/삭제 정책이 필요하다.
- 이벤트 로그에도 건강 정보가 포함될 수 있으므로 관리가 필요하다.

결정:

- 현재 프로젝트의 개발 기준안이다.
- MVP 구현은 이 선택지를 기준으로 진행한다.

### 선택지 D: 요약 + 이벤트 + 저해상도 landmark 샘플 보관

전체 landmark를 전부 저장하지 않고 5~10 FPS로 줄여 저장한다. 원본 영상은 저장하지 않는다.

장점:

- 운동 후 2D/3D 리플레이가 가능하다.
- 자세 분석을 다시 실행할 수 있다.
- 모델/규칙 개선에 유용하다.

단점:

- 용량이 증가한다.
- 개인정보/건강 데이터 관리 부담이 커진다.
- 데이터 스키마와 삭제 정책이 더 중요해진다.

적합한 경우:

- 3D 리플레이나 상세 리포트를 제품 핵심 기능으로 만들 때
- 사용자가 명시적으로 저장을 허용할 때

### 선택지 E: 원본 영상까지 보관

권장하지 않는다.

필요하다면 개발자 QA 모드에서만 제한적으로 사용하고, 사용자 기능으로는 기본 비활성화해야 한다.

## 5. 개발 방향: 선택지 C

현재 단계에서는 선택지 C를 기준으로 개발한다.

확정 구조:

- 운동 중:
  - 최근 5초 landmark ring buffer를 메모리에 유지
  - 자세 규칙 엔진이 이 버퍼를 읽어 실시간 피드백 생성
  - 피드백 이벤트는 세션 이벤트 로그에 기록
- 운동 종료 시:
  - 전체 landmark frame buffer 삭제
  - 세션 요약 저장
  - 피드백 이벤트 로그 저장
  - 원본 영상 저장 안 함
- 개발/QA 옵션:
  - 상세 landmark JSONL 저장을 Inspector 옵션으로 켜고 끌 수 있게 함
  - QA 로그는 기본 1일 보관 후 삭제

선택지 C에서 저장하는 데이터와 삭제하는 데이터:

| 구분 | 저장 여부 | 보관 기간 | 목적 |
| --- | --- | --- | --- |
| 원본 영상 | 저장 안 함 | 없음 | 개인정보/용량 리스크 제거 |
| 실시간 landmark ring buffer | 디스크 저장 안 함 | 운동 중 메모리만 유지 | 실시간 피드백 판단 |
| 전체 landmark 프레임 로그 | 기본 저장 안 함 | 없음 | MVP 범위 제외 |
| QA landmark debug 로그 | 옵션 저장 | 24시간 | 개발/문제 분석 |
| 피드백 이벤트 로그 | 저장 | 세션 데이터와 함께 유지 | 리포트, 문제 추적 |
| 세션 요약 | 저장 | 사용자가 삭제할 때까지 | 운동 기록, 통계 |

## 6. 데이터 보관 기간 정책

### 사용자용 기본 정책

기본 추천:

- 원본 영상: 저장 안 함
- 실시간 landmark buffer: 운동 종료 즉시 삭제
- 전체 landmark 로그: 기본 저장 안 함
- 피드백 이벤트: 세션 요약과 함께 보관
- 세션 요약: 사용자가 삭제하기 전까지 보관

이 방식이면 용량 문제와 개인정보 위험을 줄이면서도 운동 기록 기능은 유지할 수 있다.

### 개발/QA용 정책

개발 중에는 문제 분석을 위해 하루 정도 보관하는 것이 좋다.

추천:

- QA 상세 로그: 24시간 보관
- 오래된 QA 로그는 앱 시작 시 자동 삭제
- 파일명에 날짜/세션 ID 포함
- 사용자가 직접 켠 경우에만 저장

예시:

```text
Application.persistentDataPath/
  pose_sessions/
    summaries/
      20260630_173000_session_summary.json
    events/
      20260630_173000_events.jsonl
    debug/
      20260630_173000_landmarks_debug.jsonl
```

## 7. 실시간 피드백 파이프라인

권장 흐름:

```text
Camera Frame
  -> MediaPipe Pose
  -> LandmarkFrame
  -> Pose Quality Gate
  -> Ring Buffer
  -> Exercise State Machine
  -> Rule Engine
  -> Feedback Event
  -> UI/TTS
  -> Session Summary
```

각 단계 역할:

- `Pose Quality Gate`
  - 전신이 보이는지, visibility가 충분한지 확인한다.
- `Ring Buffer`
  - 최근 몇 초의 landmark와 각도 데이터를 유지한다.
- `Exercise State Machine`
  - 스쿼트의 내려감/최저점/올라옴/완료 상태를 판단한다.
- `Rule Engine`
  - 무릎, 허리, 상체, 깊이, 좌우 균형 등을 평가한다.
- `Feedback Event`
  - 같은 피드백이 너무 자주 나오지 않도록 cooldown을 둔다.
- `UI/TTS`
  - 화면과 음성으로 피드백을 제공한다.
- `Session Summary`
  - 운동 종료 후 보여줄 기록을 만든다.

## 8. MVP에서 먼저 구현할 기능

### Phase 1: 세션 데이터 모델 정의

필요 모델:

- `PoseSession`
- `PoseSessionSummary`
- `PoseFeedbackEvent`
- `PoseFrameSample`
- `PoseStoragePolicy`

우선순위:

1. 세션 시작/종료 시간
2. 운동 종류
3. 반복 횟수
4. 피드백 이벤트
5. 품질 지표

### Phase 2: 메모리 ring buffer

목표:

- 최근 5초 landmark만 메모리에 유지
- 실시간 규칙 엔진이 최근 움직임을 볼 수 있게 함
- 운동 종료 시 자동 삭제

설정 후보:

- `bufferSeconds`: 5
- `sampleFps`: 15
- `maxFrameCount`: 75

### Phase 3: 피드백 이벤트 저장

목표:

- 실시간 피드백이 발생할 때마다 이벤트를 저장
- 같은 rule이 너무 자주 저장되지 않도록 cooldown 적용
- 세션 종료 리포트에서 이벤트를 집계

저장 형식 MVP:

```json
{
  "session_id": "20260630_173000",
  "events": [
    {
      "t_ms": 18420,
      "rep": 3,
      "rule_id": "squat_depth_low",
      "severity": "info",
      "message": "조금 더 앉아 주세요.",
      "confidence": 0.78
    }
  ]
}
```

### Phase 4: 세션 요약 저장

목표:

- 운동 종료 시 요약 JSON 생성
- UI 리포트와 연결 가능하게 함
- 나중에 SQLite로 옮기기 쉬운 구조 유지

요약 예시:

```json
{
  "session_id": "20260630_173000",
  "exercise": "squat",
  "started_at": "2026-06-30T17:30:00+09:00",
  "ended_at": "2026-06-30T17:35:20+09:00",
  "duration_seconds": 320,
  "rep_count": 20,
  "feedback_count": 8,
  "top_issues": [
    "squat_depth_low",
    "knee_valgus"
  ],
  "average_pose_fps": 14.7,
  "average_visibility": 0.82
}
```

### Phase 5: 자동 삭제 정책

목표:

- debug landmark 로그는 24시간 후 삭제
- 요약/이벤트는 유지
- 사용자가 삭제하면 해당 세션 데이터 전체 삭제

MVP에서는 앱 시작 시 아래 폴더를 청소한다.

```text
pose_sessions/debug
```

## 9. 실시간 피드백에서 고려할 점

### 피드백 빈도

너무 자주 말하면 운동을 방해한다.

권장:

- 같은 피드백은 2~5초 cooldown
- 위험도가 높은 피드백은 더 빨리 허용
- 낮은 confidence 피드백은 말하지 않고 화면에만 표시

### 피드백 우선순위

여러 문제가 동시에 발생하면 한 번에 하나만 말하는 것이 좋다.

우선순위 예시:

1. 추적 불가
2. 프레임 밖으로 나감
3. 위험한 자세
4. 반복 카운트에 필요한 핵심 자세
5. 가벼운 개선 팁

### 피드백 확정 조건

한 프레임만 보고 피드백하면 흔들림이 많다.

권장:

- 같은 문제가 3~5프레임 이상 지속될 때 피드백
- 최근 0.3~1초 평균값으로 판단
- visibility 낮은 관절은 판정 제외

### TTS와 화면 피드백 분리

화면에는 자세한 정보를 보여줄 수 있지만, 음성은 짧아야 한다.

예시:

- 화면: `왼쪽 무릎이 안쪽으로 들어옵니다. 발끝 방향과 맞춰 주세요.`
- 음성: `무릎을 발끝 방향으로 맞춰 주세요.`

## 10. 저장 용량 관점

대략적인 용량 감각:

- 33개 landmark를 JSON으로 매 프레임 저장하면 생각보다 커질 수 있다.
- 15 FPS, 10분 운동이면 9,000프레임이다.
- JSON은 필드명이 반복되므로 실제 float 값보다 훨씬 커진다.
- 여러 세션이 쌓이면 빠르게 커질 수 있다.

따라서:

- 전체 landmark JSONL은 기본 저장하지 않는다.
- 저장이 필요하면 FPS를 낮추거나 바이너리/압축/SQLite를 고려한다.
- 세션 요약과 이벤트 로그 중심으로 간다.

## 11. 개인정보와 제품 정책

고려할 점:

- 원본 영상 저장 여부
- 얼굴이 포함되는 데이터인지
- 건강 데이터로 볼 수 있는지
- 사용자 삭제 기능 제공 여부
- 자동 삭제 주기
- 로컬 저장만 할지, 서버 동기화를 할지
- 개발자 QA 로그가 사용자 기기에 남는지

초기 원칙:

- 기본은 로컬 저장
- 원본 영상 저장 안 함
- 상세 landmark 저장은 opt-in
- 삭제 기능 제공
- 자동 삭제 정책 제공

## 12. 확정된 결정과 남은 질문

선택지 C로 개발하기로 했으므로 저장 정책의 큰 방향은 확정됐다.

확정된 결정:

- 전체 landmark 데이터는 운동 종료 후 저장하지 않는다.
- 피드백 이벤트는 세션 요약과 함께 저장한다.
- 세션 요약은 사용자가 삭제할 때까지 보관한다.
- QA 상세 landmark 로그는 옵션으로만 저장하고 24시간 후 삭제한다.
- 원본 영상은 저장하지 않는다.

아래 질문은 선택지 C 안에서 제품 경험을 더 구체화하기 위한 남은 질문이다.

### Q1. 운동 종료 후 사용자에게 어떤 화면을 보여줄까?

선택지:

- A. 반복 횟수와 간단한 피드백만 보여준다.
- B. 반복별 문제 목록까지 보여준다.
- C. 2D/3D 리플레이까지 보여준다.

추천:

- MVP는 B.

### Q2. 전체 landmark 데이터를 운동 후에도 저장해야 할까? - 확정

선택지:

- A. 저장하지 않는다.
- B. 개발/QA 모드에서만 하루 저장한다.
- C. 사용자가 허용하면 저장한다.
- D. 항상 저장한다.

결정:

- MVP는 B.
- 제품 기능으로는 C를 나중에 검토한다.

### Q3. 피드백 이벤트는 얼마나 오래 보관할까? - 확정

선택지:

- A. 운동 종료 후 삭제
- B. 세션 요약과 함께 계속 보관
- C. 30일 후 자동 삭제
- D. 사용자가 설정

결정:

- MVP는 B.

### Q4. 세션 요약은 얼마나 오래 보관할까? - 확정

선택지:

- A. 앱을 끄면 삭제
- B. 사용자가 삭제할 때까지 보관
- C. 30일 보관
- D. 1년 보관

결정:

- MVP는 B.

### Q5. 음성 피드백은 얼마나 자주 나와야 할까?

선택지:

- A. 문제 발생 즉시
- B. 같은 문제가 일정 시간 지속될 때
- C. 반복이 끝날 때만
- D. 운동 종료 후 요약만

추천:

- MVP는 B.

### Q6. 피드백 우선순위는 어떻게 둘까?

선택지:

- A. 먼저 감지된 문제부터 말한다.
- B. 위험도가 높은 문제부터 말한다.
- C. 운동 목표와 관련된 문제부터 말한다.

추천:

- MVP는 B + C 조합.

### Q7. 사용자가 데이터 저장을 직접 선택하게 할까?

선택지:

- A. 설정 없이 기본 정책만 사용
- B. 상세 데이터 저장 옵션 제공
- C. 모든 저장 항목을 세부 설정으로 제공

추천:

- MVP는 A.
- 베타/QA에서는 B.

### Q8. 첫 운동 종목은 무엇으로 고정할까?

선택지:

- A. 스쿼트
- B. 런지
- C. 푸시업
- D. 여러 종목

추천:

- MVP는 스쿼트 단일 종목.

### Q9. 피드백은 어느 언어와 말투로 줄까?

결정 필요:

- 한국어만 우선할지
- 영어도 고려할지
- 명령형으로 말할지, 코치형으로 말할지
- 너무 긴 문장을 피할지

추천:

- MVP는 짧은 한국어 코칭 문장.

### Q10. 저장 파일 포맷은 무엇으로 시작할까?

선택지:

- A. JSON/JSONL
- B. SQLite
- C. ScriptableObject
- D. 바이너리 파일

추천:

- MVP는 JSON/JSONL.
- 세션이 많아지면 SQLite.

## 13. MVP 결정안

선택지 C 기준 MVP 결정:

- 첫 운동 종목: 스쿼트
- 원본 영상: 저장하지 않음
- 실시간 landmark: 최근 5초만 메모리 보관
- 전체 landmark 로그: 기본 저장 안 함
- 개발/QA 상세 로그: 옵션으로 켜고 24시간 후 자동 삭제
- 피드백 이벤트: 세션별 저장
- 세션 요약: 계속 보관
- 저장 포맷: JSON/JSONL
- 피드백 방식: 화면 + 짧은 TTS
- 같은 피드백 cooldown: 3초
- 판정 기준: 최근 0.5초 이상 지속된 문제만 피드백

## 14. 다음 구현 계획

선택지 C 기준 구현 순서:

1. `PoseSessionData` 모델 추가
2. `PoseFrameRingBuffer` 추가
3. `PoseFeedbackEventRecorder` 추가
4. `PoseSessionStorage` 추가
5. `PoseStorageRetentionPolicy` 추가
6. `PoseExerciseFeedbackAnalyzer` 결과를 이벤트 저장으로 연결
7. 운동 종료 시 요약 JSON 생성
8. QA debug landmark 로그 24시간 자동 삭제

### 14.1 구현 책임 분리

`PoseFrameRingBuffer`

- 최근 N초 landmark frame만 메모리에 저장한다.
- 디스크 저장 책임을 갖지 않는다.
- 세션 종료 시 `Clear()`로 즉시 삭제한다.

`PoseFeedbackEventRecorder`

- 실시간 피드백 발생 시 이벤트를 누적한다.
- 같은 rule이 너무 자주 저장되지 않도록 cooldown을 적용한다.
- 운동 종료 시 `PoseSessionStorage`로 전달할 이벤트 목록을 만든다.

`PoseSessionStorage`

- 세션 요약 JSON을 저장한다.
- 피드백 이벤트 JSONL을 저장한다.
- 원본 영상과 전체 landmark frame은 저장하지 않는다.

`PoseStorageRetentionPolicy`

- debug 폴더의 오래된 QA 로그를 삭제한다.
- 기본 삭제 기준은 24시간이다.
- summaries/events 폴더는 자동 삭제하지 않는다.

`PoseSessionSummaryBuilder`

- 세션 종료 시 반복 횟수, 피드백 개수, 주요 문제, 평균 FPS, 평균 visibility를 집계한다.
- 리포트 UI가 바로 사용할 수 있는 요약 모델을 만든다.

### 14.2 우선 구현 순서

1. 세션 시작/종료 API 만들기
2. feedback event 모델과 recorder 만들기
3. 운동 중 이벤트 누적 연결
4. 세션 종료 시 summary/events 파일 저장
5. debug landmark 로그 옵션과 24시간 삭제 정책 추가
6. 세션 요약을 화면에 표시

### 14.3 MVP 완료 기준

- 운동 중 실시간 피드백이 화면 또는 TTS로 나온다.
- 피드백이 발생하면 이벤트 로그에 기록된다.
- 운동 종료 시 세션 요약 JSON이 생성된다.
- 전체 landmark frame은 세션 종료 후 남지 않는다.
- 원본 영상 파일이 생성되지 않는다.
- QA debug 로그는 옵션을 켰을 때만 생성된다.
- 24시간이 지난 debug 로그는 자동 삭제된다.

## 15. 아직 결정하지 않은 것

- 운동 종료 리포트 UI 구성
- 장기 저장 화면 제공 여부
- 3D 리플레이를 MVP에 넣을지 여부
- 제품 기능으로 사용자가 상세 landmark 저장을 허용할 수 있게 할지 여부
- 세션 데이터 암호화 여부
- iOS/Android 배포 시 저장 위치와 백업 제외 설정

## 16. 결론

현재 단계에서는 데이터를 무조건 많이 저장하는 것보다, 실시간 피드백에 필요한 데이터와 운동 후 리포트에 필요한 데이터를 분리하는 것이 좋다.

권장 기본 방향:

- 실시간 판단용 landmark는 메모리에만 잠깐 유지한다.
- 운동 종료 후에는 전체 landmark를 삭제한다.
- 피드백 이벤트와 세션 요약만 저장한다.
- 개발/QA 목적의 상세 로그만 하루 보관한다.

이 방향이면 용량 문제를 줄이면서도 실시간 피드백, 운동 리포트, 향후 개선 분석을 모두 시작할 수 있다.

## 17. 코드 구현 반영 상태

선택지 C 기준으로 1차 코드 구현을 반영했다.

### 추가된 코드

- `Assets/Scripts/MediaPipe/PoseSessionData.cs`
  - 세션 메타데이터, 피드백 이벤트, 세션 요약 모델을 정의한다.
- `Assets/Scripts/MediaPipe/PoseFrameRingBuffer.cs`
  - 최근 N초 landmark frame을 메모리에만 유지한다.
  - 운동 종료 시 `Clear()`로 전체 landmark 참조를 삭제한다.
- `Assets/Scripts/MediaPipe/PoseFeedbackEventRecorder.cs`
  - 실시간 피드백 메시지를 세션 이벤트로 변환해 누적한다.
  - 이벤트에는 rule id, severity, message, joint, confidence, pose fps, camera fps를 저장한다.
- `Assets/Scripts/MediaPipe/PoseSessionSummaryBuilder.cs`
  - 세션 중 평균 FPS, 평균 inference ms, 평균 visibility/presence, 피드백 개수를 집계한다.
  - 세션 종료 시 `PoseSessionSummary`를 생성한다.
- `Assets/Scripts/MediaPipe/PoseSessionStorage.cs`
  - 세션 요약 JSON과 피드백 이벤트 JSONL을 저장한다.
  - 원본 영상과 전체 landmark frame은 저장하지 않는다.
- `Assets/Scripts/MediaPipe/PoseStorageRetentionPolicy.cs`
  - debug 폴더의 오래된 QA 로그를 삭제한다.
  - 기본 정책은 24시간 보관이다.
- `Assets/Scripts/MediaPipe/PoseDebugLandmarkLogger.cs`
  - 개발/QA 옵션이 켜진 경우에만 landmark debug JSONL을 저장한다.

### 수정된 코드

- `Assets/Scripts/MediaPipe/MediaPipeTestRunner.cs`
  - 카메라/pose 시작 시 세션을 시작한다.
  - 프레임마다 summary 통계와 ring buffer를 갱신한다.
  - 피드백 발생 시 이벤트 로그에 기록한다.
  - Stop Camera 또는 종료 시 세션 요약과 이벤트 로그를 저장한다.
  - 저장 후 전체 landmark ring buffer를 비운다.
- `Assets/Scripts/MediaPipe/MediaPipeQaLogger.cs`
  - 절대 경로 저장을 지원하도록 수정했다.
  - 세션 debug 폴더 아래에 QA 로그를 저장할 수 있게 했다.

### 저장 위치

런타임 저장 위치는 Unity의 `Application.persistentDataPath` 아래다.

```text
Application.persistentDataPath/
  pose_sessions/
    summaries/
      {session_id}_summary.json
    events/
      {session_id}_events.jsonl
    debug/
      {session_id}_qa.jsonl
      {session_id}_landmarks_debug.jsonl
```

### 기본 저장 정책

- 원본 영상:
  - 저장하지 않는다.
- 전체 landmark frame:
  - 기본 저장하지 않는다.
  - 메모리 ring buffer에만 최근 5초를 유지한다.
  - 세션 종료 시 삭제한다.
- 피드백 이벤트:
  - 세션별 JSONL로 저장한다.
- 세션 요약:
  - 세션별 JSON으로 저장한다.
- QA/debug 로그:
  - `writeDebugLandmarkLog` 옵션이 켜진 경우에만 landmark debug 로그를 저장한다.
  - debug 폴더는 24시간 보관 후 자동 삭제 대상이다.

### 검증

코드 구현 후 아래 빌드를 확인했다.

```text
dotnet build Assembly-CSharp.csproj
dotnet build Assembly-CSharp-Editor.csproj
```

결과:

- 신규 세션 저장 코드 컴파일 성공
- Editor 어셈블리 컴파일 성공
- 기존 DTO 필드 초기화 경고 외 신규 오류 없음
