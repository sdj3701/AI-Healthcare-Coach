# AI Healthcare Coach Integration Plan

작성일: 2026-07-01  
대상 프로젝트: `D:\AI Healthcare Coach\AI-Healthcare-Coach`  
문서 목적: `docs` 폴더의 계획서, 백로그, 오류 기록, 현재 Unity 코드 구조를 기준으로 각 모듈을 어떻게 하나의 제품 런타임으로 통합할지 설계와 구현 순서를 정리한다.

## 1. 분석한 자료

| 자료 | 통합 계획에 반영한 핵심 내용 |
| --- | --- |
| `docs/module-architecture.md` | 최종 런타임 구조는 `Rag.Healthcare` 네임스페이스의 Camera, Pose, Provider, Analysis, Rendering, Speech, TTS로 분리한다. |
| `docs/TestMediaPipeplan.md` | LocalMediaPipe provider, 33개 landmark 수신, skeleton overlay, visibility gate, FPS/HUD/QA 로그, iOS/M1 Mac 검증 순서를 반영한다. |
| `docs/FeedbackMediaPipeplan.md` | 선택지 C를 확정안으로 사용한다. 세션 요약과 피드백 이벤트만 저장하고, 원본 영상과 전체 landmark frame은 기본 저장하지 않는다. |
| `docs/MediaPipeTroubleshooting.md` | Protobuf 오류, Homuler 바이너리/모델 누락, M1 Mac Python 환경 오류, 경로 혼동을 에러 대응 표에 반영한다. |
| `docs/SpeechTextplan.md` | STT는 MVP에서 push-to-talk, 3-10초 WAV, 백엔드 중계, OpenAI STT 호출 구조로 통합한다. Unity 클라이언트 API 키는 개발 테스트 전용이다. |
| `docs/TTSCreateplan.md` | 최신 TTS 계약은 `TrySpeak`, `Auto` backend, Windows PowerShell, macOS `say`, ducking 처리를 기준으로 삼는다. |
| `docs/온디바이스_AI_헬스케어_제품_백로그.xlsx` | E04-E08, E13, E14를 MVP 통합의 핵심 범위로 보고, 스쿼트 1종, 원본 영상 미저장, 10분 안정성, QA/KPI를 통합 기준으로 삼는다. |
| `docs/implementation_plan.md` | 현재 Unity/MediaPipe 통합과 직접 관련 없는 C++ Kiwi/N-gram 학습 계획이다. 제품 런타임 통합 근거로는 사용하지 않고, 향후 RAG/학습 단계 참고 자료로만 둔다. |

## 2. 현재 상태 요약

현재 프로젝트에는 두 계열의 코드가 공존한다.

| 계열 | 위치 | 상태 | 통합 판단 |
| --- | --- | --- | --- |
| 제품 통합 계열 | `Assets/Scripts/RagHealthcare` | Camera, Pose provider, Remote API, Pose analysis, Rendering, STT test, 기본 TTS가 있음 | 최종 제품 런타임의 기준 네임스페이스로 사용한다. |
| 테스트/기능 검증 계열 | `Assets/Scripts/MediaPipe`, `Assets/Scripts/Tts`, `Assets/Scripts/Speech` | MediaPipe test runner, 세션 저장, QA logger, Python Editor backend, 최신 TTS Auto backend 등이 있음 | 검증된 기능을 `Rag.Healthcare`로 이관하거나 어댑터로 연결한다. |

중요한 불일치:

- `Rag.Healthcare.Tts`는 아직 `WindowsPowerShell`/`LogOnly`만 있고, `AIHealthcareCoach.Tts`의 `Auto`, `MacOsSay`, `TrySpeak`, ducking 안정화가 반영되어 있지 않다.
- `Rag.Healthcare.Pose`에는 provider 구조가 있지만, `AIHealthcareCoach.MediaPipe` 쪽의 세션 저장, ring buffer, QA/debug log, retention policy가 더 많이 구현되어 있다.
- 문서에는 `Assets/Scripts/Api`, `Assets/Scripts/Pose`처럼 적혀 있지만 실제 D 프로젝트 이관 위치는 `Assets/Scripts/RagHealthcare/...`다. 구현 문서와 코드 경로를 `RagHealthcare` 기준으로 맞추는 것이 좋다.
- `C:\Users\djthe\OneDrive\문서\Rag`는 이전 혼동 경로다. 코드 작업 기준은 항상 `D:\AI Healthcare Coach\AI-Healthcare-Coach`다.

## 3. 통합 목표

MVP의 최종 런타임 흐름은 다음과 같다.

```text
CameraCaptureSource
  -> JointTrackingController
  -> PoseTrackingProvider(LocalMediaPipe 우선)
  -> JointTrackingFrame
  -> Pose Quality Gate
  -> PoseFrameRingBuffer
  -> Exercise State Machine
  -> PoseFeedbackAnalyzer / Rule Engine
  -> PoseFeedbackEventRecorder
  -> UI Feedback + Coach TTS
  -> PoseSessionSummaryBuilder
  -> PoseSessionStorage
  -> Report Template / On-device LLM later
```

MVP에서 하지 않을 일:

- 서버 기반 포즈 추정을 기본 경로로 사용하지 않는다. `RemoteApi`는 비교/개발용 fallback으로만 둔다.
- 원본 영상, mp4, jpg/png 프레임을 기본 저장하지 않는다.
- 의료 진단, 치료, 처방 문구를 생성하지 않는다.
- 실시간 LLM 대화를 운동 중 핵심 루프에 넣지 않는다.
- 3D 리플레이와 상세 landmark 장기 저장은 MVP 후속으로 둔다.

## 4. 권장 최종 모듈 구조

최종 구조는 `Rag.Healthcare`를 제품 런타임 기준으로 통일한다.

```text
Assets/Scripts/RagHealthcare/
  Api/
    OpenAISpeechToTextClient.cs
    PoseTrackingApiClient.cs
  Camera/
    CameraCaptureSource.cs
  Pose/
    JointTrackingController.cs
    JointTrackingFrame.cs
    PoseFeedbackMessage.cs
    PoseJointNames.cs
    PoseQualityGate.cs                 # 이관 필요
    PoseSessionData.cs                 # 이관 필요
    PoseFrameRingBuffer.cs             # 이관 필요
    PoseFeedbackEventRecorder.cs       # 이관 필요
    PoseSessionSummaryBuilder.cs       # 이관 필요
    PoseSessionStorage.cs              # 이관 필요
    PoseStorageRetentionPolicy.cs      # 이관 필요
    Providers/
      IPoseTrackingProvider.cs
      PoseTrackingProvider.cs
      MediaPipePoseTrackingProvider.cs
      RemoteApiPoseTrackingProvider.cs
      SentisMoveNetPoseTrackingProvider.cs
      NullPoseTrackingProvider.cs
    Analysis/
      PoseFeedbackAnalyzer.cs
      PoseRuleConfig.cs
      PoseGeometry.cs
      SquatStateMachine.cs             # 신규 권장
    Rendering/
      PoseSkeletonRenderer.cs
      PosePreviewOverlayBinder.cs
      PoseTrackingStatusView.cs
  Speech/
    SpeechToTextController.cs          # 테스트용 이름 정리 권장
    WavEncoder.cs
  Tts/
    ITtsService.cs
    CoachTtsController.cs
    TtsAudioDuckingController.cs       # 이관 필요
    WindowsPowerShellTtsService.cs
    MacOsSayTtsService.cs              # 이관 필요
    LogTtsService.cs
    TtsBackend.cs
    TtsPlaybackState.cs
  Report/
    SessionReportBuilder.cs            # 신규 권장
    SafetyReportTemplate.cs            # 신규 권장
```

## 5. 모듈별 통합 설계

### 5.1 Camera

현재 기준 파일:

- `Assets/Scripts/RagHealthcare/Camera/CameraCaptureSource.cs`

책임:

- 카메라 장치 선택
- `WebCamTexture` 시작/중지
- preview texture 제공
- 필요 시 JPEG 캡처 제공
- raw pixel buffer 제공

통합 방식:

- 제품 씬에서는 모든 pose provider가 직접 카메라를 열지 않고 `CameraCaptureSource.PreviewTexture`만 사용한다.
- `CameraCaptureSource`는 프리뷰 UI와 pose 추론의 단일 입력원이다.
- `TryCaptureJpeg`는 `RemoteApi` 비교 테스트에서만 사용한다.

보강 필요:

- 카메라 권한 거부 상태를 UI 상태로 노출한다.
- 전면/후면, 미러링, 회전, 실제 source width/height를 `JointTrackingFrame` metadata로 넘길 수 있게 한다.
- 모바일에서 앱 background 전환 시 `StopCamera()`가 확실히 호출되도록 lifecycle hook을 둔다.

### 5.2 Pose Provider

현재 기준 파일:

- `Assets/Scripts/RagHealthcare/Pose/Providers/PoseTrackingProvider.cs`
- `Assets/Scripts/RagHealthcare/Pose/Providers/MediaPipePoseTrackingProvider.cs`
- `Assets/Scripts/RagHealthcare/Pose/Providers/RemoteApiPoseTrackingProvider.cs`
- `Assets/Scripts/RagHealthcare/Pose/Providers/SentisMoveNetPoseTrackingProvider.cs`
- `Assets/Scripts/RagHealthcare/Pose/Providers/NullPoseTrackingProvider.cs`

책임:

- 모든 추론 backend를 `JointTrackingFrame`으로 정규화한다.
- `JointTrackingController`는 backend 종류를 몰라도 동일 이벤트를 받을 수 있어야 한다.

통합 기준:

- MVP 기본 backend는 `LocalMediaPipe`.
- `RemoteApi`는 비교와 장애 재현용으로만 유지한다.
- `LocalSentisMoveNet`은 후속 후보로 남긴다.
- `Disabled`는 UI/스토리지/TTS 테스트용으로 유지한다.

MediaPipe provider 게이트:

- `Packages/manifest.json`의 `com.github.homuler.mediapipe` resolve 확인
- Player Settings scripting define에 `AHC_USE_HOMULER_MEDIAPIPE` 추가
- `Assets/StreamingAssets/MediaPipe/pose_landmarker_lite.task` 존재 확인
- 플랫폼별 native binary 존재 확인
- Protobuf DLL 4개 존재 확인

### 5.3 JointTrackingController

현재 기준 파일:

- `Assets/Scripts/RagHealthcare/Pose/JointTrackingController.cs`

책임:

- 카메라 시작
- provider resolve/initialize
- 추론 interval 제어
- busy frame drop 카운트
- `TrackingFrameReceived` 이벤트 발행
- provider 오류를 `TrackingFailed`로 발행
- 분석 결과를 feedback receiver로 전달

통합 방식:

- 제품 씬에서 pose 루프의 중심 오브젝트로 둔다.
- `TrackingFrameReceived`에 렌더러, quality gate, session recorder, report stats collector를 연결한다.
- `TrackingFailed`는 HUD와 QA logger에 연결한다.

보강 필요:

- quality gate가 `READY`가 아닐 때 `PoseFeedbackAnalyzer`를 실행하지 않도록 중간 게이트를 추가한다.
- `TrackingFrameReceived`와 `TrackingFailed` 외에 `QualityStateChanged`, `SessionStarted`, `SessionEnded` 이벤트를 명확히 분리한다.
- 같은 오류 로그가 반복될 때 현재 cooldown은 유지하되, QA 로그에는 code와 count를 남긴다.

### 5.4 JointTrackingFrame 계약

현재 기준 파일:

- `Assets/Scripts/RagHealthcare/Pose/JointTrackingFrame.cs`

현재 필드:

```json
{
  "id": "frame_id",
  "sessionId": "session_id",
  "timestampUnixMilliseconds": 1782748800000,
  "joints": [
    {
      "name": "left_knee",
      "x": 0.42,
      "y": 0.68,
      "z": 0.01,
      "visibility": 0.94,
      "confidence": 0.91
    }
  ],
  "feedback": []
}
```

추가 권장 필드:

```text
providerBackend
providerState
sourceWidth
sourceHeight
cameraMode
isMirrored
rotationDegrees
poseFps
inferenceMs
```

이유:

- 좌표/미러링 문제를 추적하려면 source metadata가 필요하다.
- QA/KPI에서 provider backend, latency, fps를 같이 봐야 한다.
- 리포트와 이벤트 로그가 어떤 rule/provider 기준으로 생성됐는지 재현 가능해야 한다.

### 5.5 Quality Gate

현재 구현은 `AIHealthcareCoach.MediaPipe` 계열에 더 충실하게 존재한다. 제품 계열로 이관이 필요하다.

책임:

- 필수 관절 visibility/presence 검사
- 전신 인식 가능 여부 판단
- `READY`, `LOW_CONFIDENCE`, `UPPER_BODY_MISSING`, `LOWER_BODY_MISSING`, `NO_POSE`, `MODEL_MISSING`, `PLUGIN_MISSING` 같은 상태 분류
- READY가 아니면 규칙 엔진과 TTS 피드백을 막는다.

통합 기준:

- 스쿼트 MVP 필수 관절: 양쪽 어깨, 엉덩이, 무릎, 발목
- 초기 threshold: visibility/presence 0.5
- READY 유지 시간: 2-3초 calibration
- 낮은 confidence에서는 화면 안내만 하고 음성 피드백은 제한한다.

### 5.6 Pose Analysis / Rule Engine

현재 기준 파일:

- `Assets/Scripts/RagHealthcare/Pose/Analysis/PoseFeedbackAnalyzer.cs`
- `Assets/Scripts/RagHealthcare/Pose/Analysis/PoseRuleConfig.cs`
- `Assets/Scripts/RagHealthcare/Pose/Analysis/PoseGeometry.cs`

현재 규칙:

- 무릎 정렬
- 무릎 굽힘 깊이
- 좌우 무릎 각도 차이
- 상체 기울기
- 골반/어깨 수평
- 중심 균형
- 발 visibility

통합 보강:

- `docs/온디바이스_AI_헬스케어_제품_백로그.xlsx`의 `07_규칙_AI_데이터` 시트 rule_id와 코드 rule_id를 맞춘다.
- 현재 `"left_knee_alignment"` 같은 내부 ID를 `squat.knee_valgus.front.v1`처럼 제품 rule_id로 정규화한다.
- 스쿼트 상태 머신을 추가해 한 프레임 오류가 아니라 반복 구간, 최저점, 상승 구간 기준으로 판정한다.
- 같은 오류가 3-5프레임 또는 0.5초 이상 지속될 때만 피드백 이벤트로 확정한다.

### 5.7 Rendering / HUD

현재 기준 파일:

- `Assets/Scripts/RagHealthcare/Pose/Rendering/PoseSkeletonRenderer.cs`
- `Assets/Scripts/RagHealthcare/Pose/Rendering/PosePreviewOverlayBinder.cs`
- `Assets/Scripts/RagHealthcare/Pose/Rendering/PoseTrackingStatusView.cs`

통합 방식:

- skeleton overlay는 `TrackingFrameReceived`만 구독한다.
- preview UI 크기와 overlay 크기는 `PosePreviewOverlayBinder`로 동기화한다.
- HUD에는 camera fps, pose fps, dropped frame, inference ms, quality state, provider state, last error를 표시한다.

보강 필요:

- 미러링은 렌더러만의 문제가 아니다. provider output, 규칙 계산, 화면 overlay의 좌표계가 분리되어야 한다.
- `mirrorX`, `invertY` 값은 scene 설정으로만 흩어두지 말고 frame metadata와 QA 로그에도 남긴다.

### 5.8 TTS

현재 최신 기능 기준:

- `Assets/Scripts/Tts/*`의 `AIHealthcareCoach.Tts` 계열이 더 최신이다.
- `TrySpeak`, `Auto`, `MacOsSay`, ducking 안정화가 있다.

제품 통합 기준:

- 최신 TTS 기능을 `Rag.Healthcare.Tts`로 이관한다.
- `CoachTtsController`는 내부적으로 `TrySpeak`를 사용해야 한다.
- TTS 시작 실패 시 ducking을 시작하지 않는다.
- `PoseFeedbackJsonReceiver`는 confidence, duplicate cooldown, severity priority를 보고 TTS 여부를 결정한다.

권장 backend:

| 플랫폼 | MVP backend | 후속 |
| --- | --- | --- |
| Windows Editor | Windows PowerShell SpeechSynthesizer | Cloud/native voice 선택 |
| macOS Editor | `/usr/bin/say` | native plugin |
| iOS | Log 또는 임시 native bridge 전까지 비활성 | AVSpeechSynthesizer + AVAudioSession ducking |
| Android | Log 또는 임시 native bridge 전까지 비활성 | TextToSpeech + AudioFocus |

TTS 큐 정책:

- 같은 `rule_id`는 3초 cooldown
- high severity는 큐 앞쪽
- 낮은 confidence는 화면 표시만
- 음성 문장은 1초 내외의 짧은 한국어로 제한
- 긴 설명은 리포트 화면으로 보낸다.

### 5.9 Session Storage

현재 구현은 `Assets/Scripts/MediaPipe` 계열에 있다.

제품 계열로 이관할 대상:

- `PoseSessionData.cs`
- `PoseFrameRingBuffer.cs`
- `PoseFeedbackEventRecorder.cs`
- `PoseSessionSummaryBuilder.cs`
- `PoseSessionStorage.cs`
- `PoseStorageRetentionPolicy.cs`
- `PoseDebugLandmarkLogger.cs`

저장 정책:

| 데이터 | MVP 저장 여부 | 보관 기간 | 비고 |
| --- | --- | --- | --- |
| 원본 영상 | 저장 안 함 | 없음 | mp4/jpg/png frame 금지 |
| 실시간 landmark ring buffer | 디스크 저장 안 함 | 운동 중 메모리 | 세션 종료 시 `Clear()` |
| 피드백 이벤트 | 저장 | 사용자가 세션 삭제 전까지 | JSONL |
| 세션 요약 | 저장 | 사용자가 삭제 전까지 | JSON |
| QA 상세 landmark log | 옵션 저장 | 24시간 | 기본 off |

권장 저장 위치:

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

### 5.10 STT

현재 기준 파일:

- `Assets/Scripts/RagHealthcare/Speech/SpeechToTextTestController.cs`
- `Assets/Scripts/RagHealthcare/Api/OpenAISpeechToTextClient.cs`
- `Assets/Scripts/RagHealthcare/Speech/WavEncoder.cs`

통합 기준:

- 현재 클라이언트 API 키 입력 UI는 개발 테스트 전용이다.
- 운영 빌드에서는 OpenAI API key를 Unity에 포함하지 않는다.
- 운영용은 백엔드 `POST /api/stt/transcribe`로만 호출한다.

운영 API 계약:

```text
POST /api/stt/transcribe
Content-Type: multipart/form-data
Input:
  audio: wav/mp3/m4a
  session_id
  language=ko
Output:
  text
  confidence?
  durationMs
  provider
  model
  errorCode?
```

MVP UX:

- push-to-talk
- 3-10초 녹음
- 전송 중 상태 표시
- 실패 시 재시도
- 코치 TTS와 겹치지 않게 녹음 중 TTS 중지 또는 이어폰 권장

### 5.11 Report / RAG / On-device LLM

MVP에서는 리포트 생성에 전체 landmark를 넘기지 않는다.

리포트 입력:

```text
session_summary.json
matched_rules.json
top_errors
feedback_events aggregate
```

리포트 출력 고정 구조:

```text
1. 잘한 점
2. 개선 포인트
3. 다음 세트 팁
4. 안전 안내
```

안전 정책:

- 치료, 진단, 통증 원인 단정, 재활 처방 금지
- `high severity` 또는 통증 보고가 있으면 운동 중단/전문가 상담 템플릿 삽입
- LLM 응답이 실패하면 템플릿 리포트로 fallback
- RAG/LLM은 운동 중 실시간 루프가 아니라 세션 종료 후에만 실행

## 6. 구현 순서

### Phase 0. 기준 정리

목표: 중복 구조와 경로 혼동을 줄인다.

작업:

1. 최종 제품 네임스페이스를 `Rag.Healthcare`로 확정한다.
2. `AIHealthcareCoach.Tts`의 최신 TTS 기능을 `Rag.Healthcare.Tts`로 이관한다.
3. `AIHealthcareCoach.MediaPipe`의 session storage와 QA logging 기능을 `Rag.Healthcare.Pose`로 이관한다.
4. 문서의 경로 표기를 실제 경로인 `Assets/Scripts/RagHealthcare/...`로 맞춘다.
5. `implementation_plan.md`는 Unity 통합 문서가 아니라 학습 계획으로 분류한다.

완료 기준:

- 제품 씬에서 사용하는 MonoBehaviour가 `Rag.Healthcare` 계열로 통일된다.
- 테스트/레거시 씬은 남겨도 최종 runtime dependency가 명확하다.

### Phase 1. Main Runtime Scene 구성

목표: 한 씬에서 카메라, 포즈 추론, overlay, HUD가 연결된다.

씬 권장 구조:

```text
Main Runtime
  CameraCaptureSource
  JointTrackingController
  MediaPipePoseTrackingProvider
  PoseQualityGate
  PoseFeedbackAnalyzer
  PoseFeedbackJsonReceiver
  CoachTtsController
  PoseSessionCoordinator

Canvas
  Camera Preview RawImage
  Pose Overlay RectTransform
  Status HUD
  Start/Stop Button
  End Session Button
```

완료 기준:

- Start 시 camera preview 표시
- provider 초기화 성공 또는 명확한 실패 메시지 표시
- Stop 시 카메라와 provider resource 해제

### Phase 2. LocalMediaPipe 추론 연결

목표: 서버 없이 33개 landmark를 수신한다.

작업:

1. Homuler 패키지 resolve 확인
2. `AHC_USE_HOMULER_MEDIAPIPE` define 추가
3. `pose_landmarker_lite.task` 경로 확인
4. `MediaPipePoseTrackingProvider`에서 33개 landmark mapping 확인
5. `JointTrackingFrame` metadata 보강
6. `PoseSkeletonRenderer` overlay alignment 확인

완료 기준:

- 전신이 화면에 있을 때 landmark count 33
- timestamp 증가
- skeleton이 몸 위치와 대체로 일치
- 원본 영상 파일 생성 없음

### Phase 3. Quality Gate와 규칙 엔진 연결

목표: 준비되지 않은 프레임에서 잘못된 피드백이 나오지 않게 한다.

작업:

1. `PoseQualityGate`를 `Rag.Healthcare.Pose`로 이관
2. READY가 아닐 때 rule analyzer 중지
3. 2-3초 calibration 유지
4. rule_id를 백로그 `07_규칙_AI_데이터` 기준으로 정리
5. 스쿼트 상태 머신 추가

완료 기준:

- 발목 누락 시 `LOWER_BODY_MISSING`
- 상체 누락 시 `UPPER_BODY_MISSING`
- 낮은 confidence에서 TTS 피드백 없음
- 정상 전신 2초 유지 후 READY

### Phase 4. 피드백 UI/TTS 연결

목표: 짧은 실시간 피드백을 화면과 음성으로 전달한다.

작업:

1. `Rag.Healthcare.Tts`에 최신 `TrySpeak` 계약 반영
2. `TtsBackend.Auto`, `MacOsSayTtsService`, ducking controller 이관
3. `PoseFeedbackJsonReceiver`에 severity priority와 queue 정책 추가
4. 같은 rule cooldown 3초 적용
5. 음성 문장은 짧은 한국어로 정리

완료 기준:

- 피드백이 발생하면 화면 메시지와 TTS 중 하나 이상으로 표시
- 같은 피드백이 과도하게 반복되지 않음
- TTS 실패 시 음악 ducking만 발생하지 않음
- Stop/Play Mode 종료 시 TTS 프로세스가 남지 않음

### Phase 5. 세션 저장 연결

목표: 운동 종료 후 요약과 이벤트만 저장한다.

작업:

1. `PoseSessionData` 계열을 `Rag.Healthcare.Pose`로 이관
2. `PoseSessionCoordinator` 신규 작성
3. Start 시 session 생성
4. frame마다 ring buffer와 summary stats 갱신
5. feedback 발생 시 event recorder에 기록
6. End/Stop 시 summary JSON과 events JSONL 저장
7. ring buffer clear
8. debug log retention 24시간 적용

완료 기준:

- `{session_id}_summary.json` 생성
- `{session_id}_events.jsonl` 생성
- 전체 landmark frame은 기본 저장되지 않음
- QA debug landmark log는 option on일 때만 생성
- 24시간 지난 debug log 삭제

### Phase 6. STT 운영 경로 추가

목표: 사용자 발화를 안전하게 텍스트로 바꾼다.

작업:

1. `SpeechToTextTestController`를 개발용으로 표시
2. 운영용 `BackendSpeechToTextClient` 또는 기존 API client wrapper 추가
3. Unity는 백엔드 `POST /api/stt/transcribe`만 호출
4. 녹음 길이, 파일 크기, retry, timeout UI 처리
5. 전사 결과를 session transcript로 연결

완료 기준:

- 운영 빌드에 OpenAI API key 없음
- 3-10초 녹음 후 텍스트 반환
- 실패 시 사용자에게 재시도 가능한 메시지 표시

### Phase 7. 리포트 연결

목표: 세션 종료 후 안전한 요약 리포트를 만든다.

작업:

1. `SessionReportBuilder` 추가
2. session summary와 rule dictionary를 입력으로 사용
3. LLM 실패 시 template report fallback
4. 금지 표현 후처리
5. 리포트 UI 표시

완료 기준:

- 원본 영상이나 전체 landmark 없이 리포트 생성
- rule_id 기반 근거가 유지됨
- 치료/진단/처방 표현 없음

### Phase 8. 모바일/성능 검증

목표: MVP 게이트를 통과한다.

작업:

1. 5분 Play Mode 안정성
2. 10분 실기기 안정성
3. pose fps 10/15/24 비교
4. dropped frame, inference ms, memory, thermal state 기록
5. iOS native TTS/AudioSession 후속 설계
6. Android AudioFocus 후속 설계

완료 기준:

- 10분 크래시 0건
- fps_min 기록
- 발열/메모리 로그 확보
- provider 오류가 앱 크래시로 이어지지 않음

## 7. 에러 대응 설계

| 상황 | 감지 위치 | 사용자 메시지 | 개발자 확인 사항 | 처리 방식 |
| --- | --- | --- | --- | --- |
| 작업 경로 혼동 | 작업 전 체크 | 없음 | `D:\AI Healthcare Coach\AI-Healthcare-Coach`에서 작업 중인지 확인 | C 경로의 이전 파일을 기준으로 수정하지 않는다. |
| 카메라 장치 없음 | `CameraCaptureSource.StartCamera()` | 카메라를 찾을 수 없습니다. | `WebCamTexture.devices` | Start 실패, 앱은 유지 |
| 카메라 권한 거부 | Camera module | 카메라 권한이 필요합니다. | iOS/Android 권한 설정 | 권한 안내 후 retry |
| frame size 0 또는 16 이하 | Camera/Provider | 카메라 프레임 준비 중입니다. | `FrameWidth`, `FrameHeight`, `Source 0x0` | 추론 skip, 오류 반복 로그 제한 |
| MediaPipe 모델 누락 | `MediaPipePoseTrackingProvider.Initialize()` | 포즈 모델 파일이 없습니다. | `Assets/StreamingAssets/MediaPipe/pose_landmarker_lite.task` | provider not ready |
| Homuler define 누락 | `MediaPipePoseTrackingProvider` fallback | MediaPipe 플러그인 설정이 필요합니다. | `AHC_USE_HOMULER_MEDIAPIPE` define, package resolve | fallback 메시지 |
| Protobuf `CS0538 IMessage` | Unity compile | 개발 설정 오류입니다. | `Assets/Plugins/Protobuf`의 DLL 4개 | DLL 복구 후 재컴파일 |
| native binary 누락 | Unity log/runtime | 이 기기에서 포즈 엔진을 시작하지 못했습니다. | `mediapipe_c.dll`, `.dylib`, `.so`, `.aar`, iOS framework | 플랫폼 binary 확보 |
| Python import 실패 | Editor Python backend | Python MediaPipe 환경이 필요합니다. | `.venv-mediapipe`, `numpy`, `mediapipe` | Setup Python 버튼 또는 script 실행 |
| Python timeout | Editor Python backend | 포즈 추론 응답이 지연되고 있습니다. | targetPoseFps, worker stderr | fps 낮춤, worker 재시작 |
| 사람 미검출 | Provider/QualityGate | 전신이 보이도록 뒤로 이동해 주세요. | 조명, 거리, 프레임 방향 | no pose 상태, 피드백 중지 |
| low confidence | QualityGate | 카메라 거리와 조명을 조정해 주세요. | visibility/presence threshold | rule analyzer 중지 |
| 좌우 반전/회전 오류 | Renderer/QA | 없음 또는 카메라 방향 안내 | `mirrorX`, `invertY`, rotation metadata | overlay와 rule 좌표 분리 |
| provider busy | Provider | 없음 | dropped frame count | 새 frame drop, 이전 추론 유지 |
| Remote API endpoint 없음 | Remote provider | 원격 포즈 API 주소가 없습니다. | endpoint URL | dev 비교용만 사용 |
| STT API key 누락 | 개발용 STT test | API 키가 없습니다. | env/input field | 개발용에서만 표시 |
| 운영 빌드에 API key 포함 | build review | 없음 | build symbols, serialized scene | 출시 차단 |
| STT 파일 길이 초과 | Backend/STT client | 녹음이 너무 깁니다. | max seconds, file size | 전송 차단 |
| OpenAI/STT 네트워크 실패 | STT client/backend | 전사에 실패했습니다. 다시 시도해 주세요. | HTTP code, timeout, rate limit | retry/backoff |
| TTS 시작 실패 | TTS service | 음성 재생을 시작하지 못했습니다. | PowerShell stderr, `say -v ?` | ducking 시작 금지 |
| TTS 프로세스 잔류 | TTS service destroy | 없음 | Play Mode 종료 후 process | `Dispose`/`Stop` 강제 |
| ducking만 되고 음성 없음 | TTS controller | 음성 엔진을 확인해 주세요. | `TrySpeak` 결과 | 실패 시 `EndDucking` |
| session 저장 실패 | Storage | 운동 기록 저장에 실패했습니다. | persistentDataPath 권한/용량 | 앱 유지, 오류 로그 |
| 원본 영상 저장 발생 | QA privacy test | 없음 | mp4/jpg/png scan | release gate fail |
| debug log 자동 삭제 실패 | Retention policy | 없음 | file lock/권한 | warning log, 다음 시작 때 재시도 |
| LLM 금지 표현 생성 | Report safety filter | 안전 문구로 대체합니다. | prompt/filter rule | template fallback |

## 8. QA 및 수용 기준

백로그의 QA 시트와 문서를 합쳐 MVP gate를 다음으로 둔다.

| Gate | 기준 |
| --- | --- |
| Pose 기본 | LocalMediaPipe로 33개 landmark 수신 |
| Overlay | 주요 관절 skeleton이 실제 프리뷰와 크게 어긋나지 않음 |
| Quality | 전신/거리 부족/상체 누락/하체 누락 상태 구분 |
| Feedback | 같은 피드백 cooldown, high severity 우선 |
| Storage | 원본 영상 0건, summary/events 생성 |
| STT | 운영 구조에서 API key 클라이언트 미포함 |
| TTS | Windows/macOS Editor에서 Auto backend 동작, 실패 메시지 명확 |
| Performance | 10분 크래시 0건, fps_min/dropped/inference 로그 |
| Privacy | 사용자가 세션 데이터를 삭제할 수 있는 구조 |
| Safety | 치료/진단/처방 표현 없음 |

수동 테스트:

1. 카메라 시작/중지
2. 전신 인식
3. 발목 누락
4. 상체 누락
5. 정상 스쿼트 10회
6. 무릎 안쪽/전방 오류
7. TTS 피드백 cooldown
8. 운동 종료 summary/events 저장
9. 저장소에 mp4/jpg/png가 없는지 확인
10. 10분 안정성

자동/반자동 테스트 권장:

- `PoseGeometry` 각도 계산 단위 테스트
- `PoseQualityGate` synthetic frame 테스트
- `PoseFeedbackAnalyzer` rule_id별 테스트
- `PoseFeedbackEventRecorder` cooldown 테스트
- `PoseSessionSummaryBuilder` 집계 테스트
- 금지 표현 safety filter 테스트

## 9. 구현 시 주의할 결정 사항

### 9.1 `Rag.Healthcare`로 통일

새 기능은 `Rag.Healthcare`에 작성한다. 기존 `AIHealthcareCoach.*`에 검증된 코드가 있으면 복사/이관하되, 최종 제품 씬이 두 네임스페이스를 섞어 참조하지 않게 한다.

### 9.2 테스트 씬과 제품 씬 분리

`MediaPipeTest.unity`, `TtsDemo.unity`, `SpeechToTextTest.unity`는 유지할 수 있다. 다만 최종 사용자 플로우는 `Main.unity` 또는 별도 제품 씬에서 `Rag.Healthcare` 컴포넌트만 쓰도록 한다.

### 9.3 원본 영상 저장 금지

Remote API 테스트나 debug 목적의 JPEG 캡처 기능이 있어도, MVP 제품 플로우에서는 저장하지 않는다. QA는 세션 종료 후 앱 저장소에서 영상/이미지 파일이 없는지 확인해야 한다.

### 9.4 STT 보안

현재 테스트 컨트롤러의 API key input은 편하지만 운영 빌드에는 위험하다. 출시 빌드에서는 해당 UI/컴포넌트를 제외하거나 backend-only client로 교체한다.

### 9.5 MediaPipe 패키지 리스크

Homuler Git package는 `.meta`만 있고 실제 native binary/model이 없을 수 있다. Unity compile 성공과 실제 추론 성공은 별개다. 통합 체크리스트에 platform binary 확인을 넣는다.

### 9.6 피드백 정확도

한 프레임의 좌표로 바로 말하지 않는다. QualityGate, 최근 0.5초 지속 조건, cooldown, severity priority를 통과한 메시지만 TTS로 보낸다.

### 9.7 의료/헬스케어 표현

MVP는 피트니스 자세 코칭이다. 통증, 부상, 재활, 치료 표현이 들어오면 앱은 운동 중단과 전문가 상담을 권장하고, 원인을 단정하지 않는다.

## 10. 우선 작업 체크리스트

1. `AIHealthcareCoach.Tts` 최신 구현을 `Rag.Healthcare.Tts`로 이관
2. `AIHealthcareCoach.MediaPipe` 세션 저장 계열을 `Rag.Healthcare.Pose`로 이관
3. `PoseQualityGate`를 제품 pose loop에 연결
4. `JointTrackingController`가 READY frame만 analyzer에 넘기도록 수정
5. rule_id를 백로그 `07_규칙_AI_데이터` 기준으로 정리
6. `PoseSessionCoordinator` 작성
7. `Main.unity` 제품 씬 배선
8. MediaPipe model/define/native binary 체크 스크립트 또는 Editor validator 작성
9. STT 개발용/운영용 경로 분리
10. MVP gate 테스트 실행

## 11. 최종 권장 통합 방향

가장 현실적인 통합 전략은 기능을 새로 다시 만들기보다, 이미 검증된 테스트 모듈을 제품 네임스페이스로 선별 이관하는 것이다.

우선순위는 다음이다.

1. `Rag.Healthcare`를 최종 제품 런타임 기준으로 확정한다.
2. 카메라와 provider 구조는 현재 `Rag.Healthcare` 구현을 유지한다.
3. `AIHealthcareCoach.MediaPipe`의 세션 저장/QA/quality 기능을 이관한다.
4. `AIHealthcareCoach.Tts`의 최신 TTS/ducking 기능을 이관한다.
5. STT는 개발용 직접 OpenAI 호출을 유지하되, 운영용 backend 중계 계약을 별도로 만든다.
6. 리포트와 RAG는 세션 요약과 rule 이벤트를 입력으로 받는 종료 후 기능으로 붙인다.

이 순서가 좋은 이유는 현재 가장 위험한 부분이 카메라-추론-좌표-피드백의 실시간 루프이기 때문이다. 이 루프가 안정화된 뒤 STT, 리포트, RAG, 3D 리플레이를 붙이면 문제 원인을 모듈별로 분리해서 추적할 수 있다.
