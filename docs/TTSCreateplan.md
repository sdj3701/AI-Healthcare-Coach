# TTS Create Plan

## 1. Goal

Unity 프로젝트에서 사용자가 직접 입력한 텍스트를 버튼 클릭으로 음성 출력하는 TTS MVP를 만든다.

이번 단계의 목표는 MediaPipe, RAG, 자세 분석과 연결하지 않고 TTS 기능만 독립적으로 검증하는 것이다.

## 2. MVP Scope

### Included

- 화면에 텍스트 입력창 제공
- 사용자가 직접 코칭 문장 입력
- `Speak` 버튼 클릭 시 입력 문장을 TTS로 재생
- `Stop` 버튼으로 재생 중인 음성 중지
- 빈 텍스트 입력 시 재생하지 않고 안내 로그 출력
- Windows Unity Editor에서 먼저 동작 검증

### Not Included Yet

- MediaPipe 관절 데이터 연동
- JSON/XML 자세 피드백 자동 입력
- RAG 응답 생성
- 캐릭터 자세 재현
- Android/iOS 네이티브 TTS
- 클라우드 TTS API
- 음성 선택 UI
- 음성 파일 저장

## 3. Target Environment

- Unity: Unity 6.3 LTS
- Render Pipeline: URP
- First target: Windows Editor
- Later targets: Android, iOS

초기 구현은 Windows Editor에서 빠르게 검증 가능한 방식으로 진행한다. 이후 모바일 앱 빌드 단계에서 플랫폼별 TTS 백엔드를 추가한다.

## 4. User Flow

1. 사용자가 앱 화면을 연다.
2. 텍스트 입력창에 문장을 입력한다.
3. `Speak` 버튼을 누른다.
4. Unity가 입력 텍스트를 TTS 모듈에 전달한다.
5. TTS 모듈이 문장을 음성으로 읽는다.
6. 필요하면 `Stop` 버튼으로 재생을 중지한다.

## 5. Proposed Scene UI

Scene name:

```text
Assets/Scenes/TtsDemo.unity
```

UI elements:

```text
Canvas
  TTS Panel
    Title Text
    Text Input Field
    Speak Button
    Stop Button
    Status Text
```

초기 UI는 기능 검증이 목적이므로 단순하게 만든다. 나중에 헬스케어 코칭 화면과 연결할 때 UI 스타일을 정리한다.

## 6. Proposed Script Structure

```text
Assets/Scripts/Tts/
  ITtsService.cs
  TtsController.cs
  TtsDemoView.cs
  LogTtsService.cs
  WindowsTtsService.cs
```

### ITtsService

TTS 기능의 공통 인터페이스.

Responsibilities:

- `Speak(string text)`
- `Stop()`
- `IsSpeaking`

이 인터페이스를 기준으로 구현하면 나중에 Windows, Android, iOS, Cloud TTS를 교체하기 쉽다.

### TtsController

UI와 TTS 백엔드 사이의 중간 계층.

Responsibilities:

- 입력 텍스트 검증
- TTS 서비스 호출
- 상태 메시지 업데이트
- 예외 처리

### TtsDemoView

Unity UI 버튼과 입력 필드를 연결하는 MonoBehaviour.

Responsibilities:

- Input Field 값 읽기
- Speak Button 클릭 이벤트 연결
- Stop Button 클릭 이벤트 연결
- Status Text 표시

### LogTtsService

TTS 백엔드가 준비되지 않았을 때 사용하는 대체 구현.

Responsibilities:

- 실제 음성 출력 대신 Unity Console에 텍스트 출력
- 플랫폼 미지원 상황에서 기능 흐름 테스트

### WindowsTtsService

Windows Editor에서 실제 음성 출력을 담당하는 초기 구현.

Possible approach:

- Windows 내장 Speech API 사용
- Unity Editor 테스트용으로 먼저 구현
- 모바일 빌드용 최종 구현은 별도 백엔드로 분리

## 7. Implementation Steps

### Step 1. Project Check

- Unity 프로젝트가 정상적으로 열리는지 확인
- Unity 버전이 6.3 LTS인지 확인
- URP 프로젝트가 정상 import 되는지 확인

### Step 2. TTS Demo Scene Create

- `TtsDemo.unity` 생성
- Canvas 생성
- Text Input Field 생성
- Speak / Stop 버튼 생성
- Status Text 생성

### Step 3. TTS Interface Create

- `ITtsService` 작성
- `Speak`, `Stop`, `IsSpeaking` 정의
- 플랫폼별 구현이 의존할 공통 계약 확정

### Step 4. First Backend Create

- Windows Editor용 TTS 구현 작성
- 실패 시 `LogTtsService`로 대체 가능하게 구성
- 빈 문자열, 긴 문자열, 한국어 문장 처리 확인

### Step 5. UI Controller Connect

- 입력 필드 텍스트를 `TtsController`로 전달
- Speak 버튼 클릭 시 TTS 실행
- Stop 버튼 클릭 시 재생 중지
- 상태 텍스트 업데이트

### Step 6. Manual Test

Test sentences:

```text
코칭 시스템이 준비되었습니다.
무릎이 안쪽으로 모이고 있어요. 무릎을 발끝 방향으로 맞춰주세요.
허리가 굽어지고 있어요. 가슴을 열고 허리를 곧게 세워주세요.
동작이 너무 빨라요. 천천히 내려가고 안정적으로 올라오세요.
```

확인할 항목:

- 버튼 클릭 시 음성이 나오는지
- 한국어 문장이 깨지지 않는지
- Stop 버튼이 동작하는지
- 빈 입력에서 오류가 나지 않는지
- Play Mode 종료 시 TTS 프로세스가 남지 않는지

## 8. Acceptance Criteria

MVP 완료 기준:

- Unity Play Mode에서 텍스트 입력 가능
- Speak 버튼 클릭 시 입력한 문장이 음성으로 출력됨
- Stop 버튼 클릭 시 재생 중지됨
- 빈 입력은 무시되고 에러 없이 처리됨
- TTS 로직이 UI 코드에 직접 묶이지 않고 서비스 구조로 분리됨
- 나중에 자세 피드백 JSON을 같은 TTS 컨트롤러로 전달할 수 있는 구조임

## 9. Later Extension Plan

### Pose Feedback Integration

나중에 MediaPipe 분석 결과를 아래 형태의 JSON으로 받아 TTS에 연결한다.

```json
{
  "id": "squat_knee_alignment",
  "text": "무릎이 안쪽으로 모이고 있어요. 무릎을 발끝 방향으로 맞춰주세요.",
  "joint": "left_knee",
  "confidence": 0.91,
  "severity": "Warning"
}
```

### Platform TTS Backends

Future services:

- `AndroidTtsService`
- `IosTtsService`
- `CloudTtsService`
- `LocalModelTtsService`

### Coaching Queue

실시간 자세 피드백에서는 같은 문장이 너무 자주 반복되면 사용자 경험이 나빠진다. 이후에는 TTS 큐를 추가한다.

Planned rules:

- 같은 피드백은 일정 시간 동안 반복 금지
- 더 위험한 피드백은 우선순위 높게 재생
- 새 피드백이 들어오면 기존 음성을 끊을지 큐에 넣을지 정책화

## 10. Risks

- Windows TTS 방식은 모바일 빌드에서 그대로 사용할 수 없다.
- Unity Editor에서는 잘 동작해도 Android/iOS에서는 별도 네이티브 연동이 필요하다.
- 한국어 음성 품질은 OS에 설치된 음성 엔진에 따라 달라질 수 있다.
- 실시간 피드백에서는 TTS 지연 시간이 UX에 큰 영향을 준다.

## 11. Recommended Next Task

다음 작업은 이 계획에 따라 TTS MVP를 구현하는 것이다.

구현 우선순위:

1. `TtsDemo.unity` 화면 생성
2. `ITtsService`와 `TtsController` 작성
3. Windows Editor용 TTS 백엔드 작성
4. 버튼 이벤트 연결
5. Play Mode에서 한국어 문장 테스트
