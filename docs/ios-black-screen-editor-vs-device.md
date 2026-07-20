# iOS 빌드 검은 화면: 에디터에서는 되는데 기기에서는 안 되는 이유

작성일: 2026-07-20  
대상 환경: Unity `6000.3.18f1`, iPhone XS Max (iOS 18.7.9), Xcode 16.2  
관련 커밋: `f69b4c0`, `93fb304`, `0508c82`, `16db291`

## 1. 증상 정리

| 구분 | 동작 |
|------|------|
| Unity Editor (Play) | Main 씬 UI·로직이 정상적으로 보임 |
| iOS 실기기 빌드 | Unity 스플래시(로고) 이후 **검은 화면**에서 더 이상 진행되지 않는 것처럼 보임 |
| Xcode CPU / RAM | 앱 프로세스는 살아 있고 리소스 사용은 계속됨 (완전 크래시/프리즈와 다름) |

핵심: 에디터와 기기는 **같은 C# 씬 코드**를 쓰지만, 기기는 **네이티브 플레이어 + 빌드 옵션 + Metal + PlayerConnection** 경로를 타기 때문에 “에디터에서 OK ≠ 기기에서 OK”가 성립한다.

## 2. 한 줄 결론

검은 화면의 주원인은 **게임 로직 버그라기보다 iOS Development / 프로파일링 빌드 설정**이다.  
특히 아래 세 가지가 겹치면 스플래시 이후 화면이 멈춘 것처럼 보인다.

1. **Autoconnect Profiler** — 에디터 연결을 기다리며 시작 hang  
2. **Metal API Validation** — Development 빌드에서만 켜져 렌더가 막힘  
3. **Script Debugging (AllowDebugging)** — 관리 디버거 경로가 시작을 불안정하게 만듦  

에디터 Play 모드에는 위 세 경로가 그대로 적용되지 않거나, 적용돼도 Mac 에디터 렌더 파이프라인을 쓰므로 증상이 재현되지 않는다.

## 3. 에디터와 기기 빌드의 구조적 차이

```text
[Editor Play]
  Editor 프로세스 안에서 씬 실행
  → Game View에 즉시 그림
  → PlayerConnection Autoconnect / iOS Metal Validation 없음
  → MediaPipe도 에디터용 경로(또는 stub)일 수 있음

[iOS Device Build]
  Xcode로 설치한 네이티브 앱
  → Unity splash → IL2CPP 플레이어
  → boot.config 의 PlayerConnection Flags
  → Metal (Apple GPU) + (옵션) Metal API Validation
  → Development 이면 프로파일러/디버거 소켓 광고
```

그래서 “에디터에서 UI가 보인다”는 사실은 **기기 시작 hang / 렌더 정지 여부를 보증하지 않는다.**

## 4. 원인 상세 (우선순위)

### 4.1 Autoconnect Profiler (가장 흔했던 hang)

**무엇이 일어나는가**

- Build Settings / Build Profile에서 **Autoconnect Profiler**가 켜지면 플레이어가 에디터에 먼저 붙으려 한다.
- 기기 로그에 대략 다음이 보인다.
  - `[Flags] 19` (Development + Autoconnect 계열)
  - `Remaining time: …s`
  - `Direct connection timeout`
- 이 동안 스플래시 이후 검은 화면처럼 보이고, 타임아웃·버퍼 오버플로 후 불안정해지거나 멈춘 것처럼 느껴진다.
- 이 기기/OS 조합에서는 PlayerConnection Autoconnect 경로에서 SIGSEGV가 난 기록도 있었다.

**왜 에디터에서는 안 보이인가**

- Editor Play는 이미 에디터 프로세스 안이라 “기기가 Mac 에디터를 찾아 붙는” 대기 루프가 없다.

**조치 (적용됨)**

- `Assets/Editor/IOSDevelopmentBuild.cs`에서  
  `connectProfiler = false`, `BuildOptions.ConnectWithProfiler` 미사용  
- 정상 시 기기 로그: `[Flags] 2`, `Remaining time` 없음, Listen 모드  
- Profiler는 **수동 IP 연결** (예: `192.168.35.25`)만 사용

관련 커밋: `93fb304`

---

### 4.2 Metal API Validation (Development 전용 렌더 정지)

**무엇이 일어나는가**

- `ProjectSettings`의 `metalAPIValidation`은 프로젝트에 원래 켜져 있을 수 있다.
- **이 옵션은 Development 빌드에서만 실제로 활성화**된다.
- Release(일반) 빌드에서는 무시되므로, 예전에 “일반 빌드”로 잘 되던 앱이 프로파일링용 Development로 바뀌는 순간 처음 문제가 난다.
- 증상 패턴:
  - 스플래시 후 검정
  - 프로세스는 살아 있음 (Xcode CPU/RAM 계속 움직임)
  - Metal 초기화 로그는 나올 수 있음

**왜 에디터에서는 안 보이인가**

- Editor Game View는 iOS Metal Validation 경로를 타지 않는다.
- 같은 씬이라도 Mac(Editor) GPU 경로와 iPhone Metal 경로가 다르다.

**조치 (적용됨)**

- `ProjectSettings.asset` → `metalAPIValidation: 0`

관련 커밋: `0508c82`

---

### 4.3 Script Debugging / AllowDebugging

**무엇이 일어나는가**

- `BuildOptions.AllowDebugging` 또는 `EditorUserBuildSettings.allowDebugging = true` 이면  
  `boot.config`에 `player-connection-debug=1` 등이 들어가 관리 디버거 경로가 열린다.
- Unity Profiler(CPU/Memory)에는 **Development만으로 충분**하며, Script Debugging은 필수가 아니다.
- iOS IL2CPP + 실기기에서 시작 직 불안정/지연 요인으로 작용할 수 있다.

**조치 (적용됨)**

- Development 빌드 옵션에서 `AllowDebugging` 제거  
- `allowDebugging = false`, `waitForManagedDebugger = false` 강제  
- 정상 시: `player-connection-debug=0`

관련 커밋: `0508c82`

---

### 4.4 “검은 화면처럼 보이는” UI 상태 (오인 가능)

완전 hang이 아닌데도 검게 보일 수 있다.

- `MobileWorkoutPrototypeView`의 PanelSettings `clearColor`가 거의 검정 (`#0B1119` 근처)
- Main 씬에서 카메라/추적은 기본 **자동 시작 OFF**
  - `playOnStart = 0`
  - `autoStartTracking = 0`
  - `startTrackingOnStart = 0`
- 따라서 정상 기동 직후에도 **카메라 프리뷰 없이 어두운 UI**가 먼저 보인다.
- UI Toolkit이 그려지지 못하면(테마/폰트/예외) 진짜 완전 검정으로 남는다.

조치: `Resources/UI/MobileWorkoutPanelSettings`를 런타임 기본 PanelSettings로 추가하고 기존 `UnityDefaultRuntimeTheme`을 명시적으로 연결했다. 코드 생성 PanelSettings는 자산 로드 실패 시에만 폴백하며, UI 루트·테마 누락과 UI 빌드 성공 여부를 1회 로그로 남기고 clear color도 조금 밝게 조정했다.

에디터에서는 Game View 배경·레이아웃·즉시 리페인트 때문에 “앱이 살아 있다”는 느낌이 훨씬 강하다.

---

### 4.5 링크 Duplicate symbol (검은 화면 원인 아님)

Xcode Issue Navigator에 `unzOpen`, `UCaseMap`, `Thread::Thread` 중복이 많이 보여도, 이는 보통 **링크 경고**이며 스플래시 후 검정과 직접 원인은 아니다.

- Unity `libiPhone-lib.a`와 MediaPipe `libMediaPipeTasksCommon_*_graph.a`가 같은 심볼을 포함
- CocoaPods가 graph lib를 `-force_load` 하던 것이 충돌 방아쇠
- 조치: Podfile `post_install`에서 `-force_load` → `-load_hidden` (`16db291`)

`.pcm: No such file` ModuleCache 오류는 DerivedData/ModuleCache 손상으로, **Clean Build Folder**로 해결한다.

## 5. 원인 → 증상 매핑 체크리스트

기기 콘솔 / Xcode 로그에서 순서대로 확인한다.

| 로그 / 상태 | 의미 | 조치 |
|-------------|------|------|
| `[Flags] 19`, `Remaining time` | Autoconnect 대기 hang | Autoconnect OFF, 메뉴 빌드 사용 |
| `[Flags] 2`, `applicationDidBecomeActive`, Metal 정상인데도 검정 | Metal Validation / Debug 경로 의심 | Validation OFF, AllowDebugging OFF |
| `player-connection-debug=1` | Script Debugging 활성 | Development만 유지, Debugging OFF |
| CPU/RAM 움직임 + 완전 검정 | 프로세스 생존, 렌더/UI 미표시 | 위 설정 + UI/카메라 시작 여부 확인 |
| `duplicate symbol '_unz…'` | MediaPipe↔Unity 링크 경고 | `-load_hidden` 패치, 검은 화면과 별개 |
| `.pcm: No such file` | ModuleCache 손상 | DerivedData / ModuleCache 삭제 |

## 6. 올바른 빌드·프로파일링 방법 (현재 권장)

메뉴:

- `AI Healthcare Coach/Build/iOS Development Build` → `Build/iOS`  
  - Development ON  
  - Autoconnect / Deep Profiling / Script Debugging / wait-for-debugger **OFF**  
  - Profiler는 기기 IP **수동 연결**
- `AI Healthcare Coach/Build/iOS Release Build` → `Build/iOS-Release`  
  - 일반 실행·대조 검증용

서명: Team `VBT88ZWM6D`, Automatic Signing  
Xcode는 반드시 **`Unity-iPhone.xcworkspace`** 로 연다 (`.xcodeproj` 단독 금지)

주의:

- Unity Build Settings UI에서 Autoconnect를 다시 켜고 빌드하면 Flags 19 hang이 **재발**한다.
- Autoconnect / Script Debugging은 `EditorUserBuildSettings`(로컬)에 저장되며 git에 없다.  
  `Assets/Editor/IOSStableBuildSettings.cs`가 에디터 로드 시 및 메뉴  
  `AI Healthcare Coach/Build/Apply Safe iOS Build Settings`로 안전 값을 다시 강제한다.
- `/Users/sindongju/aibuild` 같은 별도 export와 `Build/iOS`가 섞이면 설정이 어긋나기 쉽다. 프로파일링은 `Build/iOS`를 기준으로 한다.

## 7. “에디터 OK / 빌드 검정”을 코드 버그로 단정하면 안 되는 이유

1. 동일 씬이어도 **실행 런타임이 다름** (Editor vs IL2CPP Player)  
2. Development 빌드 전용 네이티브 옵션이 에디터 Play에 없음  
3. 카메라 자동 시작이 꺼져 있어 정상 UI도 어두울 수 있음  
4. MediaPipe 링크 경고는 빌드 실패/검정과 별개로 보일 수 있음  

먼저 **빌드 Flags / boot.config / Metal Validation / Autoconnect**를 확인한 뒤, 그래도 실패할 때만 UI Toolkit·예외 로그를 추적하는 것이 맞다.

## 8. 관련 파일

| 파일 | 역할 |
|------|------|
| `Assets/Editor/IOSDevelopmentBuild.cs` | 안정화된 Development/Release iOS 빌드 |
| `Assets/Editor/IOSStableBuildSettings.cs` | 에디터 로드 시 Autoconnect/디버깅 OFF 강제 |
| `ProjectSettings/ProjectSettings.asset` | `metalAPIValidation`, Team ID, Automatic Signing |
| `Assets/Editor/MediaPipeIOSBuildPostprocessor.cs` | Podfile + `-load_hidden` post_install |
| `Assets/Scenes/Main.unity` | 카메라/추적 자동 시작 OFF |
| `Assets/Scripts/RagHealthcare/UI/MobileWorkoutPrototypeView.cs` | 런타임 UI Toolkit (어두운 clearColor) |
| `Build/iOS/Data/boot.config` | `player-connection-*`, debugger 대기 플래그 확인용 |

## 9. 재발 시 최소 재현 절차

1. `iOS Development Build`로 재export (Unity Build Settings에서 Autoconnect 켜지 말 것)  
2. `pod install` 후 `.xcworkspace` 로 Debug 설치  
3. 기기 로그에서 `[Flags] 2` / `Remaining time` 없음 확인  
4. `boot.config`에서 `player-connection-debug=0`, wait-for-debugger 없음 확인  
5. 화면이 여전히 검으면: UI(“AI 헬스케어 코치” 헤더) 유무와 카메라 Start 전 어두운 배경 오인 여부를 구분  
6. Release 빌드(`Build/iOS-Release`)로 한 번 더 대조 — Release에서만 정상이면 Development 전용 설정 문제 확정  

---

이 문서는 2026-07-20 실기기 프로파일링 환경 구성 과정에서 확인한 사실을 기준으로 한다.  
씬/UI 로직을 먼저 의하기 전에, 위 빌드·연결 설정을 우선 점검할 것.
