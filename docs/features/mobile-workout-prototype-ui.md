# Mobile Workout Prototype UI

이 문서는 `code_artifact.tsx` 와이어프레임을 Unity 프로젝트에 맞게 재구성한 모바일 운동 UI의 현재 구현 내용을 정리한다.

원본 와이어프레임은 React/Tailwind 기반 모바일 프로토타입이었지만, 현재 프로젝트는 Unity 런타임 앱이므로 TSX를 그대로 사용하지 않고 Unity UI Toolkit 기반 UI로 다시 만들었다. 현재 구현은 `UIDocument`, `PanelSettings`, `VisualElement`, `Button`, `TextField`, `Image`를 코드에서 생성하는 방식이다.

## 구현 위치

주요 구현 파일:

- `Assets/Scripts/RagHealthcare/UI/MobileWorkoutPrototypeView.cs`
- `Assets/Editor/RagHealthcare/RagSquatCoachSceneBuilder.cs`

Unity 에셋 메타 파일:

- `Assets/Scripts/RagHealthcare/UI.meta`
- `Assets/Scripts/RagHealthcare/UI/MobileWorkoutPrototypeView.cs.meta`

## 목적

기존 테스트 화면은 기능 검증용 디버그 UI에 가까웠다. 이번 모바일 UI는 실제 앱 흐름에 가까운 형태로 다음 작업을 한 화면 흐름 안에서 확인하기 위한 것이다.

1. 운동 선택
2. 목표 횟수 및 세트 설정
3. 카메라 기반 자세 추적 시작
4. 올바른 자세 카운트 표시
5. Stop 이후 저장 JSON 기반 3D 리플레이 표시

## 자동 생성 방식

`MobileWorkoutPrototypeView`는 다음 조건에서 Play 시 자동 생성된다.

- 씬 안에 `CameraCaptureSource`가 있음
- 씬 안에 `JointTrackingController`가 있음
- 이미 `MobileWorkoutPrototypeView`가 없음

자동 생성은 `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)`로 처리한다.

따라서 기존 `TestRagSysten` 씬을 새로 만들지 않아도 Play를 누르면 모바일 UI가 나타난다.

## 기존 UI와의 관계

모바일 UI가 켜지면 기존 테스트용 UI와 겹치지 않도록 다음 처리를 한다.

- `CameraPreviewDebugView` 비활성화
- 기존 데스크톱용 `Coach Canvas` 비활성화
- `MobileWorkoutPrototypeView`가 `UIDocument`를 생성하거나 기존 `UIDocument`를 재사용
- `PanelSettings`가 없으면 런타임/에디터 미리보기용 PanelSettings 인스턴스를 생성
- 모바일 화면은 UI Toolkit `VisualElement` 트리로 구성

이 처리 덕분에 기존 IMGUI 버튼과 데스크톱 상태 패널이 모바일 UI 위에 겹쳐 보이지 않는다.

## 화면 구조

모바일 UI는 3단계 화면으로 구성된다.

### STEP 1. 운동 선택

목적:

- 사용자가 운동 종류를 선택하는 화면
- 현재 실제 자세 판별 엔진이 지원하는 운동과 준비 중인 운동을 구분

현재 운동 목록:

| 운동 | 카테고리 | 상태 |
| --- | --- | --- |
| 스쿼트 | 하체 | 지원 |
| 런지 | 하체 | 준비 중 |
| 푸시업 | 상체 | 준비 중 |
| 플랭크 | 맨몸 | 준비 중 |

중요:

현재 실제 자세 판별 로직은 스쿼트 기준으로 구현되어 있다. 그래서 UI에서는 스쿼트만 선택 가능하고, 다른 운동은 준비 중 상태로 표시된다.

### STEP 2. 목표 설정

목적:

- 반복 횟수와 세트 수를 입력
- 목표 정확 자세 카운트를 계산
- 기존 자세 카운트 로직에 목표값 연결

입력 항목:

- 반복 횟수
- 세트 수

목표 계산 방식:

```text
목표 정확 자세 카운트 = 반복 횟수 x 세트 수
```

예시:

```text
반복 횟수 15
세트 수 3
목표 정확 자세 카운트 45개
```

적용 위치:

```csharp
RealtimeFeedbackOrchestrator.SetCorrectRepTarget(targetCount)
```

목표값을 적용하면 기존 `RealtimeFeedbackOrchestrator`의 카운트 상태가 리셋되고, 이후 올바른 자세로 완료된 rep만 카운트된다.

### STEP 3. 운동 세션

목적:

- 카메라 화면 표시
- 관절 추적 상태 표시
- 정확한 자세 카운트 표시
- 목표 카운트 표시
- Pose FPS 표시
- 최신 피드백 표시
- Stop 후 3D 리플레이 표시

주요 버튼:

| 버튼 | 동작 |
| --- | --- |
| `START` | 카메라 시작, 관절 추적 시작 |
| `STOP` | 추적 중지, 카메라 중지, 저장 JSON 기반 3D 리플레이 시작 |
| `카메라 전환` | 전면/후면 선호 카메라 전환 후 다시 시작 |
| `목표 수정` | STEP 2 목표 설정 화면으로 이동 |
| `리셋` | 추적/리플레이/타이머 초기화 |
| `운동 선택` | STEP 1 운동 선택 화면으로 이동 |

## START 동작 흐름

`START` 버튼을 누르면 다음 순서로 실행된다.

1. 목표 카운트를 `RealtimeFeedbackOrchestrator`에 적용
2. 진행 중인 리플레이가 있으면 중지
3. `CameraCaptureSource.StartCamera()` 호출
4. `JointTrackingController.StartTracking()` 호출
5. 세션 타이머 시작
6. 카메라 프리뷰를 UI Toolkit `Image`에 표시
7. 자세 상태, 카운트, FPS를 UI Toolkit `Label`에 표시

관련 코드:

```csharp
private void StartWorkout()
{
    ApplyTargetCount();
    replayMode = false;
    replayPlayer?.StopReplay();
    cameraSource?.StartCamera();
    trackingController?.StartTracking();
    ...
}
```

## STOP 동작 흐름

`STOP` 버튼을 누르면 다음 순서로 실행된다.

1. 세션 타이머 정지
2. `JointTrackingController.StopTracking()` 호출
3. `CameraCaptureSource.StopCamera()` 호출
4. `PoseJsonReplayPlayer.PlayLatestSession()` 호출
5. 현재 세션 JSONL 또는 최신 JSONL 파일을 읽음
6. `"type":"frame"` 라인을 `JointTrackingFrame`으로 변환
7. `PoseAvatar3DPreview`가 3D 캐릭터를 움직임
8. 리플레이 `RenderTexture`를 모바일 UI의 프리뷰 영역에 표시

관련 코드:

```csharp
private void StopWorkoutAndReplay()
{
    StopWorkoutOnly();
    cameraSource?.StopCamera();
    replayPlayer?.PlayLatestSession();
    replayMode = true;
    RefreshPreviewTexture();
}
```

## 카메라 프리뷰와 리플레이 표시

운동 세션 화면의 중앙 프리뷰 영역은 UI Toolkit `Image`로 구성된다.

표시 우선순위:

1. 카메라가 실행 중이면 `CameraCaptureSource.PreviewTexture`
2. 카메라가 꺼져 있고 리플레이 프레임이 있으면 `PoseJsonReplayPlayer.PreviewTexture`
3. 둘 다 없으면 안내 placeholder 표시

즉, `START` 중에는 실제 카메라가 보이고, `STOP` 후에는 저장 JSON 기반 3D 리플레이가 같은 영역에 표시된다.

## 카메라 실시간 2D 관절 추적 오버레이 (PBI-108)

카메라가 활성화되어 전신을 추적할 때, 사용자가 관절이 잘 인식되고 있는지 실시간으로 확인할 수 있도록 초록색 스켈레톤 라인과 각 핵심 관절 포인트를 오버레이 렌더링한다.

* **프레임 갱신 연동**: `trackingController.TrackingFrameReceived` 이벤트를 받아 UI Toolkit Image 요소의 `MarkDirtyRepaint()`를 호출함으로써 지연 없이 실시간 렌더링을 갱신한다.
* **이미지 배율 보정 (`GetTextureRect`)**: 카메라의 실제 이미지 비율과 UI Layout 요소의 종횡비가 달라져 발생하는 어긋남을 감쇄하기 위해 Letterbox/Pillarbox 영역을 계산해 2D 드로잉 위치를 영상 위에 정확히 정렬한다.
* **관절 색상 구분**: 왼쪽 관절(청색), 오른쪽 관절(오렌지색), 중앙 관절(백색)로 나누어 직관성을 높인다.
* **자동 비활성화**: STOP 후 3D 리플레이 재생 시점에는 리플레이 화면 자체와 겹치지 않도록 자동으로 오버레이가 비활성화된다.

## 자세 카운트 연결

모바일 UI 자체가 자세를 판별하지는 않는다.

올바른 자세 판별과 카운트 증가는 기존 `RealtimeFeedbackOrchestrator`가 담당한다.

연결되는 값:

- `CorrectRepCount`
- `TargetCorrectRepCount`
- `PhaseState`
- `CurrentRepHasViolation`

화면 표시:

```text
정확한 자세: CorrectRepCount
목표: TargetCorrectRepCount
Phase: Standing / Descent / Bottom / Ascent
상태: 정상 또는 교정 필요
```

카운트 증가 조건:

1. 스쿼트 rep가 시작됨
2. `Descent`, `Bottom`, `Ascent` 구간을 거침
3. 구간 중 자세 오류 후보가 발생하지 않음
4. `ExercisePhaseDetector`의 rep count가 증가함
5. 현재 rep가 오류 없는 rep이면 `CorrectRepCount` 증가

## TTS와의 관계

모바일 UI는 TTS를 직접 호출하지 않는다.

올바른 자세 카운트가 증가할 때의 TTS는 기존 흐름을 그대로 사용한다.

담당 컴포넌트:

- `RealtimeFeedbackOrchestrator`
- `PoseFeedbackJsonReceiver`
- `CoachTtsController`

현재 흐름:

```text
올바른 자세 rep 완료
-> CorrectRepCount 증가
-> PoseFeedbackJsonReceiver.ReceiveFeedback(...)
-> CoachTtsController
-> "정확합니다 {N}개" 형식 TTS
```

## 3D 리플레이와의 관계

모바일 UI의 `STOP` 버튼은 새 리플레이 로직과 연결되어 있다.

사용 컴포넌트:

- `SessionJsonlLogger`
- `PoseJsonReplayPlayer`
- `PoseAvatar3DPreview`
- `PoseAvatar3DRenderer`

리플레이 데이터:

```json
{
  "type": "frame",
  "sessionId": "...",
  "frameId": "...",
  "timestampUnixMilliseconds": 1234567890,
  "joints": [
    {
      "name": "left_knee",
      "x": 0.42,
      "y": 0.61,
      "z": -0.18,
      "visibility": 0.92,
      "confidence": 0.89
    }
  ]
}
```

리플레이는 저장된 `x`, `y`, `z` 값을 단순 3D 좌표로 변환해 Unity primitive 캐릭터를 움직인다.

현재 3D 캐릭터는 외부 모델 파일이 아니라 Unity primitive로 생성된다.

- 관절: `Sphere`
- 뼈대: `Capsule`
- 왼쪽/오른쪽 관절 색상 분리
- 카메라 프리뷰가 꺼진 뒤 같은 영역에 표시

### 오류 관절 빨간색 하이라이트 (PBI-107)

운동수행 오류가 발생한 시점에 3D 아바타의 해당 관절 및 인접 뼈대의 크기와 색상을 변경하여 사용자가 직관적으로 어떤 부분에 잘못된 자세를 수행했는지 피드백을 제공한다.

- **연동 구현**: JSONL 파일의 피드백 정보(`type: "feedback"`)를 읽어 피드백 발생 시점부터 2.5초간 유지한다.
- **시각화 피드백**: 오류가 발생한 관절의 Sphere 스케일을 1.4배 확장하고, 해당 관절 및 연결된 Bone Capsule의 재질을 빨간색(`redMaterial`)으로 렌더링한다.

## 씬 빌더 반영

`RagSquatCoachSceneBuilder`에도 `MobileWorkoutPrototypeView` 생성을 추가했다.

새로 `Rag/RAG/Create TestRagSysten Scene` 메뉴로 씬을 만들 경우에도 모바일 UI가 포함된다.

추가된 연결:

```csharp
var mobileView = runtime.AddComponent<MobileWorkoutPrototypeView>();

SetObject(mobileView, "cameraSource", cameraSource);
SetObject(mobileView, "trackingController", trackingController);
SetObject(mobileView, "feedbackReceiver", feedbackReceiver);
SetObject(mobileView, "feedbackOrchestrator", orchestrator);
SetObject(mobileView, "replayPlayer", replayPlayer);
```

## 테스트 방법

기존 씬에서 테스트:

1. Unity에서 `Assets/Scenes/TestRagSysten.unity` 열기
2. Play 클릭
3. 모바일 폰 형태 UI가 자동으로 뜨는지 확인
4. STEP 1에서 스쿼트 선택 상태 확인
5. 다음 버튼으로 STEP 2 이동
6. 반복 횟수와 세트 수 입력
7. 운동 화면으로 이동
8. `START` 클릭
9. 카메라 화면과 관절 추적이 표시되는지 확인
10. 스쿼트 동작 수행
11. 올바른 자세 카운트가 증가하는지 확인
12. `STOP` 클릭
13. 카메라가 꺼지고 3D 리플레이가 표시되는지 확인

새 씬 생성 후 테스트:

1. Unity 메뉴에서 `Rag/RAG/Create TestRagSysten Scene` 실행
2. 생성된 씬 저장 확인
3. Play 클릭
4. 위 기존 씬 테스트와 동일하게 확인

## 현재 제약

현재 UI는 모바일 앱 흐름을 검증하기 위한 Unity 런타임 프로토타입이다.

제약:

- 실제 자세 판별은 스쿼트만 지원
- 런지, 푸시업, 플랭크는 UI 목록에는 있지만 준비 중
- 디자인은 Unity 기본 UI 컴포넌트 기반이므로 React/Tailwind 원본과 100% 동일하지 않음
- 아이콘 라이브러리 대신 텍스트 중심 버튼을 사용
- 3D 캐릭터는 실제 사람 모델이 아니라 primitive 기반 테스트 캐릭터
- 리플레이는 저장 JSONL에 frame 데이터가 있어야 표시됨
- 카메라 권한이 거부되면 프리뷰가 표시되지 않음

## 문제 확인 포인트

### Play 후 모바일 UI가 안 보일 때

확인할 것:

- 씬에 `CameraCaptureSource`가 있는지
- 씬에 `JointTrackingController`가 있는지
- 콘솔에 컴파일 오류가 없는지
- `Coach Runtime`에 `MobileWorkoutPrototypeView`가 붙어 있는지
- `MobileWorkoutPrototypeView`가 실행되며 `UIDocument`가 생성되었는지

### START 후 카메라가 안 보일 때

확인할 것:

- 카메라 권한 허용 여부
- `CameraCaptureSource.LastError`
- 에디터 또는 실행 환경에서 사용 가능한 웹캠 존재 여부

### STOP 후 리플레이가 안 보일 때

확인할 것:

- START 이후 실제 관절 프레임이 저장되었는지
- `Application.persistentDataPath/RagSessions`에 `.jsonl` 파일이 있는지
- HUD 또는 로그의 `Replay file not found`
- HUD 또는 로그의 `Replay has no frames`
- `PoseJsonReplayPlayer.LoadedFrameCount`가 0보다 큰지

## 다음 개선 후보

1. 스쿼트 외 운동별 자세 판별 로직 추가
2. 운동별 목표 설정을 세트 단위로 분리
3. 모바일 UI를 prefab으로 분리
4. UXML/USS 에셋 분리
5. 실제 3D 캐릭터 모델 적용
6. 리플레이 재생/일시정지/속도 조절 버튼 추가
7. 세션 종료 후 요약 화면 추가
8. 카운트 완료 시 완료 상태 화면 추가
9. 모바일 해상도별 레이아웃 QA
10. 앱 빌드 환경에서 카메라 권한 안내 문구 추가

## 요약

이번 구현으로 기존 디버그 중심 테스트 화면 위에 실제 모바일 앱에 가까운 3단계 UI 흐름이 추가되었다.

현재 사용자는 다음 흐름을 Unity Play 모드에서 바로 확인할 수 있다.

```text
운동 선택
-> 목표 횟수/세트 설정
-> START로 카메라 및 자세 추적 시작
-> 올바른 자세 카운트 확인
-> STOP으로 JSON 기반 3D 리플레이 확인
```

핵심은 UI가 독립적으로 동작하는 장식 화면이 아니라, 기존 카메라, MediaPipe 관절 추적, 자세 판별, TTS 피드백, JSON 저장, 3D 리플레이 흐름에 연결되어 있다는 점이다.
