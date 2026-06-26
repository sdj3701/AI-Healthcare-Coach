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
- 내비게이션 안내처럼 TTS 코칭 중에는 배경 음악 볼륨을 낮추고 코칭 음성은 또렷하게 출력
- TTS 종료 후 배경 음악 볼륨을 자연스럽게 원래 값으로 복구

### Not Included Yet

- MediaPipe 관절 데이터 연동
- JSON/XML 자세 피드백 자동 입력
- RAG 응답 생성
- 캐릭터 자세 재현
- Android/iOS 네이티브 TTS
- 클라우드 TTS API
- 음성 선택 UI
- 음성 파일 저장
- 외부 음악 앱(Apple Music, Spotify 등)에 대한 OS 레벨 ducking 최종 구현

## 3. Target Environment

- Unity: Unity 6.3 LTS
- Render Pipeline: URP
- First target: Windows Editor
- Later targets: Android, iOS

초기 구현은 Windows Editor에서 빠르게 검증 가능한 방식으로 진행한다. 이후 모바일 앱 빌드 단계에서 플랫폼별 TTS 백엔드를 추가한다.

## 3.1 Audio Ducking Requirement

운동 코칭 앱은 사용자가 음악을 들으면서 사용할 가능성이 높다. 따라서 TTS가 재생될 때 음악을 완전히 끄면 안 되고, 내비게이션 안내처럼 음악은 낮게 유지하면서 코칭 음성을 우선 들리게 해야 한다.

### Expected Behavior

- 평상시에는 음악이 정상 볼륨으로 재생된다.
- 코칭 TTS가 시작되면 음악 볼륨을 짧은 시간 안에 낮춘다.
- 음악은 완전히 mute 하지 않는다.
- 코칭 음성은 음악보다 명확하게 들리도록 우선 출력한다.
- 코칭 TTS가 끝나면 음악 볼륨을 자연스럽게 원래 값으로 되돌린다.
- 연속 코칭이 들어오면 음악 볼륨을 올렸다 내렸다 반복하지 않고 낮은 상태를 잠시 유지한다.

### Initial Ducking Values

초기 테스트 기준:

```text
Normal Music Volume: 0 dB
Ducked Music Volume: -14 dB
Coach Voice Volume: 0 dB ~ +3 dB
Ducking Fade In: 0.15 sec
Ducking Release: 0.5 sec
Hold After TTS: 0.3 sec
```

값은 실제 iPhone 스피커/이어폰 테스트 후 조정한다. 특히 운동 중에는 주변 소음이 있으므로 코칭 음성이 너무 작으면 안 된다.

### Scope Split

1. Unity 앱 내부 음악 ducking

   - Unity `AudioMixer`로 구현한다.
   - `Music` 그룹 볼륨을 낮추고 `CoachVoice` 그룹은 유지하거나 조금 올린다.
   - Windows Editor에서도 테스트할 수 있다.

2. 외부 음악 앱 ducking

   - 사용자가 Apple Music, Spotify, YouTube Music 등을 켜 둔 상태에서 앱을 실행하는 경우다.
   - Unity `AudioMixer`만으로는 외부 앱 음악 볼륨을 직접 제어할 수 없다.
   - iOS에서는 `AVAudioSession`, Android에서는 `AudioFocus` 정책을 네이티브로 연결해야 한다.
   - 이번 TTS MVP 문서에는 요구사항으로 기록하고, 모바일 네이티브 TTS 단계에서 구현한다.

## 4. User Flow

1. 사용자가 앱 화면을 연다.
2. 텍스트 입력창에 문장을 입력한다.
3. `Speak` 버튼을 누른다.
4. Unity가 입력 텍스트를 TTS 모듈에 전달한다.
5. 음악이 재생 중이면 음악 볼륨을 낮춘다.
6. TTS 모듈이 문장을 음성으로 읽는다.
7. TTS가 끝나면 음악 볼륨을 원래 값으로 복구한다.
8. 필요하면 `Stop` 버튼으로 재생을 중지한다.

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
    Ducking Test Music Toggle
    Music Volume Label
```

초기 UI는 기능 검증이 목적이므로 단순하게 만든다. 나중에 헬스케어 코칭 화면과 연결할 때 UI 스타일을 정리한다.

Audio routing:

```text
AudioMixer
  Master
    Music
    CoachVoice
    Sfx
```

TTS 음성은 `CoachVoice` 그룹으로 보내고, 앱 내부 배경 음악은 `Music` 그룹으로 보낸다. Ducking은 `Music` 그룹 볼륨만 낮추는 방식으로 시작한다.

## 6. Proposed Script Structure

```text
Assets/Scripts/Tts/
  ITtsService.cs
  TtsController.cs
  TtsDemoView.cs
  LogTtsService.cs
  WindowsTtsService.cs
  TtsAudioDuckingController.cs
  TtsPlaybackState.cs
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
- TTS 시작/종료 이벤트를 ducking controller에 전달

### TtsDemoView

Unity UI 버튼과 입력 필드를 연결하는 MonoBehaviour.

Responsibilities:

- Input Field 값 읽기
- Speak Button 클릭 이벤트 연결
- Stop Button 클릭 이벤트 연결
- Status Text 표시
- 테스트용 음악 재생/중지 토글
- 현재 ducking 상태 표시

### TtsAudioDuckingController

TTS 재생 상태에 따라 배경 음악 볼륨을 조절하는 컴포넌트.

Responsibilities:

- TTS 시작 시 `Music` AudioMixer 그룹 볼륨을 낮춤
- TTS 종료 또는 Stop 시 일정 시간 후 원래 볼륨으로 복구
- 연속 TTS가 들어오면 release 타이머를 갱신해 볼륨이 흔들리지 않게 처리
- 음악을 완전히 끄지 않고 지정된 ducked volume까지만 낮춤
- Ducking 상태를 HUD 또는 Status Text에 표시

### TtsPlaybackState

TTS 재생 상태를 UI, queue, ducking controller가 공유하기 위한 상태 모델.

Initial states:

```text
Idle
Preparing
Speaking
Ducking
Restoring
Stopped
Error
```

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

### Step 6. Audio Ducking Create

- `AudioMixer`에 `Music`, `CoachVoice`, `Sfx` 그룹 생성
- `MusicVolumeDb`, `CoachVoiceVolumeDb` 파라미터 노출
- `TtsAudioDuckingController` 작성
- TTS 시작 시 음악 볼륨을 `-14 dB` 근처로 낮춤
- TTS 종료 후 `0.5 sec` 정도에 걸쳐 원래 볼륨으로 복구
- Stop 버튼을 눌렀을 때도 음악 볼륨이 복구되는지 확인
- 음악은 mute하지 않고 낮은 볼륨으로 유지

### Step 7. Manual Test

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
- TTS 재생 중 음악이 완전히 꺼지지 않고 작아지는지
- TTS 음성이 음악보다 명확하게 들리는지
- TTS 종료 후 음악 볼륨이 자연스럽게 원래대로 돌아오는지
- 짧은 코칭 문장이 연속으로 들어와도 음악 볼륨이 심하게 출렁이지 않는지

## 8. Acceptance Criteria

MVP 완료 기준:

- Unity Play Mode에서 텍스트 입력 가능
- Speak 버튼 클릭 시 입력한 문장이 음성으로 출력됨
- Stop 버튼 클릭 시 재생 중지됨
- 빈 입력은 무시되고 에러 없이 처리됨
- TTS 로직이 UI 코드에 직접 묶이지 않고 서비스 구조로 분리됨
- 나중에 자세 피드백 JSON을 같은 TTS 컨트롤러로 전달할 수 있는 구조임
- TTS 시작 시 앱 내부 음악 볼륨이 낮아짐
- TTS 중에도 음악이 완전히 꺼지지 않음
- 코칭 음성이 음악보다 우선적으로 들림
- TTS 종료 또는 Stop 후 음악 볼륨이 원래 값으로 복구됨

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

### Mobile OS Audio Ducking

앱 내부 음악 ducking과 별개로, 사용자가 외부 음악 앱을 켜 둔 상태에서도 코칭 음성이 들려야 한다. 이 기능은 모바일 네이티브 단계에서 별도로 구현한다.

iOS 방향:

- `AVAudioSession` category/options 설정 검토
- 외부 오디오를 완전히 중단하지 않고 ducking 가능한 옵션 사용
- TTS 또는 코칭 음성 재생 중에만 ducking 적용

Android 방향:

- `AudioFocus` 요청 방식 검토
- `AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK` 계열 정책 사용
- 코칭이 끝나면 audio focus 반환

주의:

- 외부 앱 음악 ducking은 OS 정책과 사용자 설정의 영향을 받는다.
- 모든 음악 앱이 동일하게 반응한다고 가정하면 안 된다.
- 초기 MVP에서는 Unity 앱 내부 음악 ducking을 먼저 안정화한다.

## 10. Risks

- Windows TTS 방식은 모바일 빌드에서 그대로 사용할 수 없다.
- Unity Editor에서는 잘 동작해도 Android/iOS에서는 별도 네이티브 연동이 필요하다.
- 한국어 음성 품질은 OS에 설치된 음성 엔진에 따라 달라질 수 있다.
- 실시간 피드백에서는 TTS 지연 시간이 UX에 큰 영향을 준다.
- 음악 ducking release 시간이 너무 짧으면 음악 볼륨이 자주 출렁거릴 수 있다.
- ducked volume이 너무 낮으면 음악이 끊긴 것처럼 느껴지고, 너무 높으면 코칭 음성이 묻힐 수 있다.
- 외부 음악 앱 ducking은 Unity만으로 제어할 수 없어 iOS/Android 네이티브 구현이 필요하다.

## 11. Recommended Next Task

다음 작업은 이 계획에 따라 TTS MVP를 구현하는 것이다.

구현 우선순위:

1. `TtsDemo.unity` 화면 생성
2. `ITtsService`와 `TtsController` 작성
3. Windows Editor용 TTS 백엔드 작성
4. `AudioMixer`와 `TtsAudioDuckingController` 작성
5. 버튼 이벤트 연결
6. Play Mode에서 한국어 문장과 음악 ducking 테스트
