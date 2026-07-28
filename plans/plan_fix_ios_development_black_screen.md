# iOS Development Player 검은 화면 수정 계획

## 확인된 원인

- 2026-07-27 19:50에 현재 설치 앱을 다시 수집한 결과 Player 로그가
  `Build type 'Development'`를 기록했고, 다시
  `[IOSBoot] Splash already finished` 직후 멈췄다.
- 직전 URP RenderGraph Undefined Symbol 재발 대응에서 Unity `6000.3.18f1`
  모든 iOS 빌드에 `BuildOptions.Development`를 강제로 추가한 변경이 검은
  화면을 재발시킨 직접 회귀다.
- iPhone XS Max 실기기 로그에서 검은 화면 빌드는 Unity 엔진과 Metal 초기화 후
  `[IOSBoot] Splash already finished`까지만 진행하고 첫 장면 로드 전에 멈춘다.
- Time Profiler에서는 메인 스레드가 `PlayerLoadFirstScene`의
  `PreloadManager::WaitForAllAsyncOperationsToComplete`에서 대기한다.
- UI Toolkit과 운동 런타임이 없는 `SampleScene`도 Development Player에서는 같은
  위치에서 멈추므로 UI Toolkit은 원인이 아니다.
- 동일한 `OptimizeSize` IL2CPP 설정을 유지하고 Unity Development Player만 끈
  비개발 빌드는 `SampleScene`과 실제 `Main` 장면을 모두 정상 로드했다.
- 실제 `Main` 장면 로그에서
  `MobileWorkoutPrototypeView UI Toolkit workout interface built successfully`와
  `[IOSBoot] AfterSceneLoad scene='Main'`을 확인했다.
- 따라서 직접 원인은 Unity `6000.3.18f1`의 iOS Development Player 초기화 경로이며,
  직전 링크 오류를 해결한 `OptimizeSize` 자체나 UI Toolkit이 아니다.

## 수정 범위

- 기존 `iOS Development Build` 메뉴를 실기기용 안정 빌드로 변경한다.
  - Unity `BuildOptions.Development`는 사용하지 않는다.
  - 일반 Build 및 Build And Run에서 사용자가 Development를 켰더라도 이 Unity
    버전의 iOS 빌드에서는 제거한다.
  - 이전 링크 대응에 필요한 `CleanBuildCache`는 유지한다.
  - Xcode 실행 구성은 `Debug`로 유지해 네이티브 로그와 디버깅 편의는 보존한다.
  - IL2CPP 코드는 링크 오류가 없었던 `OptimizeSize`로 유지한다.
- iOS Release Build도 `OptimizeSize`를 사용하도록 통일해 기존 URP RenderGraph
  Undefined Symbol 재발을 막는다.
- 빌드 로그에 Unity Development Player를 의도적으로 끈 이유를 명확히 남긴다.
- Healthcare QA Suite에 실기기 빌드가 `BuildOptions.Development`를 다시 사용하지
  않고, iOS 코드 생성이 `OptimizeSize`인지 검증하는 회귀 테스트를 추가한다.
- iOS 검은 화면 문제 해결 문서에 실기기 재현 결과와 권장 빌드 경로를 기록한다.
- 수정된 설정으로 공식 `/Users/sindongju/aibuild`를 다시 생성하고 Xcode 빌드,
  iPhone 설치, 실행 로그까지 확인한다.
- 비개발 출력에서 기존 14개 URP RenderGraph 심볼이 다시 누락되면 Unity
  Development Player를 되살리지 않고 비개발 IL2CPP 경로에 한정된 보존/링크
  대책으로 해결한다.

## 검증

- [x] Unity C# 컴파일과 Healthcare QA Suite가 통과한다.
- [x] 생성된 Player 로그의 Build type이 `Release`다.
- [x] Xcode Debug 구성 빌드가 성공한다.
- [x] `Unexpected duplicate tasks`가 발생하지 않는다.
- [x] URP RenderGraph `Undefined symbol`이 발생하지 않는다.
- [x] iPhone XS Max에서 `[IOSBoot] BeforeSceneLoad`와
      `[IOSBoot] AfterSceneLoad scene='Main'`이 기록된다.
- [x] `MobileWorkoutPrototypeView`의 UI Toolkit 생성 성공 로그가 기록된다.
- [x] 앱이 검은 화면에서 멈추지 않고 운동 UI를 표시한다.

## 완료 결과

- 검은 화면 앱은 `Build type 'Development'`로 실행된 뒤 첫 씬 전에 멈췄다.
- 빌드 핸들러가 Unity `6000.3.18f1` iOS 옵션에서 Development,
  ConnectWithProfiler, AllowDebugging을 제거하고 CleanBuildCache만 추가하도록
  수정했다.
- 새 비개발 export를 Xcode Debug로 클린 빌드했으며 exit code 0으로 완료됐다.
- 완성된 UnityFramework에서 기존 14개 URP RenderGraph 미해결 심볼은 0건이다.
- iPhone XS Max에서 `Build type 'Release'`, `BeforeSceneLoad`, UI Toolkit 생성
  성공, `AfterSceneLoad scene='Main'` 순서로 확인했다.
- 검증된 export를 `/Users/sindongju/aibuild`에 배치했고, 교체 전 Development
  export는 `/Users/sindongju/aibuild.before-black-screen-fix-20260727-1959`에
  보존했다.
