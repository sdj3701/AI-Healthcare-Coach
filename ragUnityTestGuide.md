# RAG Squat Coach Unity Test Guide

대상: M1 MacBook + Unity Editor  
목표: 카메라로 스쿼트 자세를 추적하고, RAG 기반 실시간 음성 피드백을 출력한다.

## 1. Unity에서 프로젝트 열기

1. Unity Hub에서 이 프로젝트를 연다.
2. Package Manager가 `com.github.homuler.mediapipe`를 resolve할 때까지 기다린다.
3. `Assets/StreamingAssets/MediaPipe/pose_landmarker_lite.task` 파일이 있는지 확인한다.

## 2. MediaPipe define 켜기

Unity 상단 메뉴에서 실행한다.

```text
Rag > RAG > Enable Homuler MediaPipe Define
```

이 작업은 Standalone 대상에 다음 scripting define을 추가한다.

```text
AHC_USE_HOMULER_MEDIAPIPE
```

define을 추가하면 Unity가 스크립트를 다시 컴파일한다. Homuler 패키지가 아직 resolve되지 않은 상태에서 define을 먼저 켜면 compile error가 날 수 있으므로, 패키지 resolve 완료 후 실행한다.

## 3. 테스트 씬 생성

Unity 상단 메뉴에서 실행한다.

```text
Rag > RAG > Create Squat Realtime Coach Scene
```

생성되는 씬:

```text
Assets/Scenes/RagSquatCoach.unity
```

씬에는 다음 컴포넌트가 자동 연결된다.

- `CameraCaptureSource`
- `JointTrackingController`
- `MediaPipePoseTrackingProvider`
- `RealtimeFeedbackOrchestrator`
- `RagRetriever`
- `SessionJsonlLogger`
- `PoseFeedbackJsonReceiver`
- `CoachTtsController`
- camera preview UI
- skeleton overlay
- tracking status UI

## 4. Play Mode 테스트

1. `Assets/Scenes/RagSquatCoach.unity`를 연다.
2. Play 버튼을 누른다.
3. macOS 카메라 권한 요청이 나오면 허용한다.
4. 시작 시 "코칭 시스템이 준비되었습니다." 음성이 나오는지 확인한다.
5. 전신이 화면에 보이도록 한 걸음 뒤로 간다.
6. 스쿼트를 수행한다.

예상 피드백:

```text
왼쪽 무릎을 발끝 방향과 맞춰 주세요.
가슴을 열고 상체를 조금 더 세워 주세요.
체중을 양발 중앙에 고르게 실어 주세요.
카메라에 전신이 보이도록 한 걸음 뒤로 이동해 주세요.
```

## 5. 문제 확인

### 음성이 나오지 않을 때

- `RAG Squat Coach Runtime`의 `CoachTtsController.backend`가 `Auto` 또는 `MacOsSay`인지 확인한다.
- Mac 터미널에서 다음 명령이 동작하는지 확인한다.

```bash
say "무릎을 발끝 방향으로 맞춰 주세요."
```

### status UI에 MediaPipe define 오류가 보일 때

다시 실행한다.

```text
Rag > RAG > Enable Homuler MediaPipe Define
```

### landmark가 잡히지 않을 때

- 카메라 권한을 확인한다.
- 조명을 밝게 한다.
- 전신이 화면에 들어오게 한다.
- `pose_landmarker_lite.task` 파일이 존재하는지 확인한다.
- Homuler MediaPipe 패키지가 Package Manager에서 정상 resolve됐는지 확인한다.

## 6. 로그 위치

세션 로그는 JSONL로 저장된다.

macOS Editor 기준 대략 다음 위치에서 확인할 수 있다.

```text
~/Library/Application Support/<CompanyName>/<ProductName>/RagSessions/
```

로그에는 frame, phase, feedback 이벤트가 줄 단위 JSON으로 저장된다.
