# MediaPipe 문제 원인, 오류, 수정 기록

작성일: 2026-06-30

이 문서는 Unity 로컬 관절 추적 전환 과정에서 발생한 문제, 실제 오류 메시지, 원인 분석, 수정 사항, 검증 결과를 나중에 다시 확인하기 쉽도록 목록으로 정리한 기록이다.

## 1. 작업 기준 경로

- 실제 Unity 프로젝트 경로:
  - `D:\AI Healthcare Coach\AI-Healthcare-Coach`
- 앞으로 코드 작업 기준:
  - `D:\AI Healthcare Coach\AI-Healthcare-Coach`
- 이전에 혼동이 있었던 경로:
  - `C:\Users\djthe\OneDrive\문서\Rag`
- 조치:
  - `C:\Users\djthe\OneDrive\문서\Rag`에 있던 런타임/에디터 코드 중 D 프로젝트에 없던 파일을 `D:\AI Healthcare Coach\AI-Healthcare-Coach`로 이관했다.
  - 기존 D 프로젝트 코드와 충돌하지 않도록 `Assets/Scripts/RagHealthcare`와 `Assets/Editor/RagHealthcare` 하위로 분리했다.

## 2. 개발 방향 변경

- 기존 방향:
  - API 또는 서버에 관절 추적 요청을 보내는 구조를 고려했다.
  - 카메라는 Unity에서 동작하지만 실제 관절 추적 엔진이 로컬에 없으면 landmark가 나오지 않는 문제가 있었다.
- 변경된 방향:
  - 서버 없이 Unity 내부에서 관절 추적을 처리하는 완전 로컬 방식으로 전환한다.
  - Unity 안에 MediaPipe Unity Plugin, Sentis/ONNX 모델, MoveNet 같은 추적 엔진을 넣는 구조로 설계한다.
- 설계 원칙:
  - 관절 추적 엔진은 Provider 인터페이스 뒤에 숨긴다.
  - MediaPipe, Sentis MoveNet, Remote API, Null Provider를 교체 가능하게 둔다.
  - 카메라 입력, 관절 추적, 자세 분석, 렌더링, TTS, QA 로그를 모듈별로 분리한다.

## 3. 관절 추적이 안 됐던 주요 원인

- 카메라 동작과 관절 추적 동작은 별개다.
  - `WebCamTexture`가 정상이어도 pose inference 엔진이 없거나 실패하면 landmark는 나오지 않는다.
- Unity Editor에서 보이던 관절이 실제 추론이 아니라 stub/simulation일 수 있었다.
  - 이전 구조에서는 Native MediaPipe가 없을 때 `EditorStubPoseEstimator` 또는 시뮬레이션 데이터가 표시될 수 있었다.
  - 그래서 카메라는 정상인데 실제 사람 관절 추적은 안 되는 상황이 발생했다.
- 서버 API 기반 Provider는 서버가 없으면 동작하지 않는다.
  - `RemoteApiPoseTrackingProvider`는 로컬 추론 엔진이 아니라 API 요청 구조다.
  - 완전 로컬 동작을 위해서는 MediaPipe 또는 Sentis/ONNX Provider가 필요하다.
- Homuler MediaPipe Git 패키지만 추가하면 바이너리/모델이 모두 준비되는 것이 아니다.
  - Git 패키지 안에는 일부 `.meta`만 있고 실제 `.dll`, `.bytes`, 네이티브 바이너리가 빠질 수 있다.
  - 이 경우 컴파일 또는 런타임에서 오류가 난다.

## 4. 발생한 대표 오류

### 4.1 Google.Protobuf IMessage 오류

오류:

```text
Library\PackageCache\com.github.homuler.mediapipe@7e96a7aa4f9a\Runtime\Scripts\Protobuf\Util\RenderData.cs(4673,32): error CS0538: 'Google.Protobuf.IMessage' in explicit interface declaration is not an interface
```

확인 결과:

- 오류가 난 `RenderData.cs`의 해당 코드는 protobuf가 생성한 정상 코드였다.
- 직접 코드 문법 문제가 아니라 `Google.Protobuf.dll` 참조 문제로 판단했다.
- Homuler 패키지 캐시 위치에는 아래 파일이 실제 DLL 없이 `.meta`만 있었다.
  - `Runtime/Plugins/Protobuf/Google.Protobuf.dll.meta`
  - `Runtime/Plugins/Protobuf/System.Runtime.CompilerServices.Unsafe.dll.meta`
- `Mediapipe.Runtime.asmdef`는 `overrideReferences`와 `precompiledReferences`로 아래 DLL을 기대한다.
  - `Google.Protobuf.dll`
  - `System.Buffers.dll`
  - `System.Memory.dll`
  - `System.Runtime.CompilerServices.Unsafe.dll`

원인:

- Homuler Git 패키지에 필요한 Protobuf DLL 본체가 없어 Unity 컴파일러가 올바른 `Google.Protobuf` 어셈블리를 참조하지 못했다.
- 그래서 protobuf 생성 코드의 `pb::IMessage.Descriptor` 같은 명시적 인터페이스 구현부에서 컴파일 오류가 발생했다.

수정:

- D 프로젝트 내부에 명시적으로 Protobuf 관련 DLL을 추가했다.

추가 위치:

```text
Assets/Plugins/Protobuf/
```

추가 파일:

```text
Assets/Plugins/Protobuf/Google.Protobuf.dll
Assets/Plugins/Protobuf/Google.Protobuf.dll.meta
Assets/Plugins/Protobuf/System.Buffers.dll
Assets/Plugins/Protobuf/System.Buffers.dll.meta
Assets/Plugins/Protobuf/System.Memory.dll
Assets/Plugins/Protobuf/System.Memory.dll.meta
Assets/Plugins/Protobuf/System.Runtime.CompilerServices.Unsafe.dll
Assets/Plugins/Protobuf/System.Runtime.CompilerServices.Unsafe.dll.meta
```

검증:

- Unity batchmode 컴파일 성공
- `Mediapipe.Runtime.dll` 생성 확인
- `Assembly-CSharp.dll` 생성 확인
- `Assembly-CSharp-Editor.dll` 생성 확인
- `CS0538` 오류 사라짐
- Unity `.meta` GUID 중복 없음

### 4.2 Homuler 패키지 바이너리/모델 누락 경고

Unity 로그에 남은 경고 예시:

```text
A meta data file (.meta) exists but its asset 'Packages/com.github.homuler.mediapipe/Runtime/Plugins/mediapipe_c.dll' can't be found.
A meta data file (.meta) exists but its asset 'Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe/pose_landmarker_lite.bytes' can't be found.
```

원인:

- Homuler Git 패키지 v0.16.3에는 일부 런타임 바이너리와 모델 파일이 실제 파일 없이 `.meta`만 존재한다.
- Git URL 패키지 추가만으로 Windows 네이티브 플러그인, Android AAR, iOS framework, MediaPipe model `.bytes`가 모두 채워지지 않는다.

현재 영향:

- Protobuf 컴파일 오류는 해결됐다.
- 하지만 실제 Homuler MediaPipe Native 런타임을 사용하려면 추가 바이너리와 모델 파일이 필요할 수 있다.

남은 보강 대상:

- Windows:
  - `mediapipe_c.dll`
- macOS:
  - `libmediapipe_c.dylib`
- Linux:
  - `libmediapipe_c.so`
- Android:
  - `mediapipe_android.aar`
- iOS:
  - `MediaPipeUnity.framework` 실제 바이너리
- Pose model:
  - `pose_landmarker_lite.bytes`
  - `pose_landmarker_full.bytes`
  - `pose_landmarker_heavy.bytes`
  - 또는 프로젝트에서 실제 사용하는 pose model 파일

권장 조치:

- Homuler Git URL 패키지만 믿지 말고 공식 릴리스/배포 절차에서 바이너리 포함 패키지를 확보한다.
- 현재 로컬 Provider가 어떤 런타임을 사용하는지에 따라 필요한 모델만 선별해서 `Assets/StreamingAssets` 또는 패키지 리소스에 배치한다.
- 실제 Native MediaPipe Provider를 켜기 전에 `mediapipe_c.dll` 존재 여부를 먼저 확인한다.

## 5. 적용한 코드/구조 변경

### 5.1 모듈 이관

이관 대상:

```text
C:\Users\djthe\OneDrive\문서\Rag\Assets\Scripts
C:\Users\djthe\OneDrive\문서\Rag\Assets\Editor
```

D 프로젝트 반영 위치:

```text
D:\AI Healthcare Coach\AI-Healthcare-Coach\Assets\Scripts\RagHealthcare
D:\AI Healthcare Coach\AI-Healthcare-Coach\Assets\Editor\RagHealthcare
```

추가된 런타임 모듈:

- `Api`
  - OpenAI STT client
  - Pose tracking API client
- `Camera`
  - Camera capture source
- `Pose`
  - Joint tracking controller
  - Landmark/frame model
  - Feedback message model
  - Pose joint names
- `Pose/Analysis`
  - Pose feedback analyzer
  - Pose geometry
  - Pose rule config
- `Pose/Providers`
  - `IPoseTrackingProvider`
  - `MediaPipePoseTrackingProvider`
  - `SentisMoveNetPoseTrackingProvider`
  - `RemoteApiPoseTrackingProvider`
  - `NullPoseTrackingProvider`
  - Provider backend enum/factory 구조
- `Pose/Rendering`
  - Skeleton renderer
  - Preview overlay binder
  - Pose tracking status view
- `Speech`
  - Speech-to-text test controller
  - Wav encoder
- `Tts`
  - Coach TTS controller
  - TTS service interface
  - Windows PowerShell TTS
  - Log TTS

추가된 에디터 모듈:

- `HealthcareProjectInitializer`
- `SpeechToTextTestSceneBuilder`

에디터 코드 수정:

- `Rag.Healthcare.Camera` 네임스페이스가 `UnityEngine.Camera` 타입명을 가리는 문제가 있어 복사본에서 `UnityEngine.Camera`로 명시했다.

### 5.2 MediaPipe 테스트 기능 강화

수정/추가 파일:

```text
Assets/Scripts/MediaPipe/MediaPipeTestRunner.cs
Assets/Scripts/MediaPipe/MediaPipeQaLogger.cs
Assets/Scripts/MediaPipe/PoseDebugHud.cs
Assets/Scripts/MediaPipe/PoseOverlayRenderer.cs
Assets/Scripts/MediaPipe/PoseExerciseFeedbackAnalyzer.cs
Assets/Scripts/MediaPipe/PoseExerciseFeedbackMessage.cs
```

적용 내용:

- 운동 피드백 분석 로직 추가
- 신뢰도 낮은 관절 표시 처리 개선
- HUD에 추론 시간, 드롭 프레임, 피드백 상태 표시
- QA 로그에 inference/drop/feedback 정보 기록
- TTS 피드백 옵션 연결
- 낮은 confidence landmark는 숨김/희미하게/회색 처리할 수 있도록 표시 로직 개선

### 5.3 패키지 의존성

수정 파일:

```text
Packages/manifest.json
Packages/packages-lock.json
```

추가 의존성:

```text
com.github.homuler.mediapipe
```

주의:

- 이 의존성만으로 모든 바이너리와 모델이 확보되는 것은 아니다.
- 컴파일용 Protobuf DLL은 `Assets/Plugins/Protobuf`에 별도 추가했다.

## 6. 검증 명령

Unity batchmode 컴파일:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe' -batchmode -quit -projectPath 'D:\AI Healthcare Coach\AI-Healthcare-Coach' -logFile 'D:\AI Healthcare Coach\AI-Healthcare-Coach\Temp\UnityCompile2.log'
```

런타임 어셈블리 빌드:

```powershell
dotnet build 'D:\AI Healthcare Coach\AI-Healthcare-Coach\Assembly-CSharp.csproj' --nologo
```

에디터 어셈블리 빌드:

```powershell
dotnet build 'D:\AI Healthcare Coach\AI-Healthcare-Coach\Assembly-CSharp-Editor.csproj' --nologo
```

GUID 중복 확인:

```powershell
$root='D:\AI Healthcare Coach\AI-Healthcare-Coach'
$items=Get-ChildItem -Path (Join-Path $root 'Assets') -Recurse -Filter *.meta | ForEach-Object {
  $line=Select-String -Path $_.FullName -Pattern '^guid: ' -ErrorAction SilentlyContinue | Select-Object -First 1
  if($line){ [pscustomobject]@{Guid=($line.Line -replace '^guid:\s*','');Path=$_.FullName.Substring($root.Length+1)} }
}
$dups=$items | Group-Object Guid | Where-Object {$_.Count -gt 1}
"Duplicate GUID groups: $($dups.Count)"
```

확인된 결과:

- Unity batchmode 종료 코드: 성공
- `Mediapipe.Runtime.dll`: 생성됨
- `Assembly-CSharp.dll`: 생성됨
- `Assembly-CSharp-Editor.dll`: 생성됨
- `dotnet build Assembly-CSharp.csproj`: 성공
- `dotnet build Assembly-CSharp-Editor.csproj`: 성공
- Duplicate GUID groups: `0`

## 7. 앞으로 같은 문제가 나면 확인할 순서

1. 실제 작업 경로가 `D:\AI Healthcare Coach\AI-Healthcare-Coach`인지 확인한다.
2. Unity Console의 첫 번째 컴파일 오류를 확인한다.
3. `Google.Protobuf` 관련 오류면 `Assets/Plugins/Protobuf`에 DLL 4개가 있는지 확인한다.
4. `Library/Bee/artifacts/.../Mediapipe.Runtime.rsp`에 아래 참조가 들어갔는지 확인한다.
   - `Assets/Plugins/Protobuf/Google.Protobuf.dll`
   - `Assets/Plugins/Protobuf/System.Runtime.CompilerServices.Unsafe.dll`
5. Native runtime 오류면 `mediapipe_c.dll` 또는 플랫폼별 native binary 존재 여부를 확인한다.
6. landmark가 0이면 카메라 문제가 아니라 추론 Provider 상태를 먼저 확인한다.
7. Provider가 Remote API이면 서버 연결이 필요하므로 완전 로컬 테스트에는 MediaPipe/Sentis Provider를 선택한다.
8. Unity `.meta` 중복 GUID가 없는지 확인한다.
9. Unity를 한 번 재시작하거나 batchmode를 2회 실행해 Bee DAG 재생성 상태를 정리한다.

## 8. 현재 남은 리스크

- Homuler Git 패키지의 `.meta` 경고는 남아 있다.
  - 이유: 실제 모델/네이티브 바이너리 파일이 패키지에 없기 때문이다.
- Protobuf 컴파일 오류는 해결됐지만 실제 MediaPipe Native Provider 실행은 별도 바이너리 준비가 필요할 수 있다.
- 완전 로컬 제품화를 위해서는 다음 중 하나를 확정해야 한다.
  - Homuler MediaPipe 바이너리 포함 패키지 확보
  - Sentis/ONNX MoveNet 경로로 전환
  - 플랫폼별 Native MediaPipe bridge 직접 관리
- Android/iOS 실기기 검증은 별도 단계로 필요하다.

