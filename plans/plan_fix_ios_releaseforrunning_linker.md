# iOS ReleaseForRunning IL2CPP 링크 오류 수정 계획

## 확인된 원인

- 새 iOS Xcode 프로젝트의 IL2CPP 생성 소스에는 보고된 URP RenderGraph 심볼이 존재한다.
- 처음 성공한 `Debug` 빌드는 기존 IL2CPP 아카이브를 재사용한 결과였고, 완전한 클린 빌드에서는 `Debug`와 `ReleaseForRunning` 모두 같은 누락이 재현됐다.
- 기본 `OptimizeSpeed` 코드 생성으로 새로 만든 `libGameAssembly.a`에는 일부 generic 런타임 메타데이터와 문자열 심볼 정의가 빠져 `UnityFramework` 링크가 실패한다.
- Development Build를 `OptimizeSize`로 다시 export하면 범용 generic 구현에 필요한 메타데이터 정의가 아카이브에 포함되고 동일 Xcode 링크가 성공한다.
- 이후 Xcode GUI 빌드에서 Unity 공용 Bee 캐시가 이전 IL2CPP 오브젝트를 재사용하며 같은 심볼 누락이 다시 발생했다.
- GUI가 만든 `libGameAssembly.a`를 직접 검사한 결과, 오류 심볼은 참조(`U`)만 있고 정의(`D`)가 없어 공용 Bee 캐시 재사용이 재발 원인임을 확인했다.

## 수정 범위

- Unity의 iOS Development Build가 Xcode 프로젝트를 생성한 뒤 공유 스킴의 `LaunchAction` 구성을 `Debug`로 고정한다.
- Development Build의 IL2CPP 코드 생성을 generic 범용 구현을 사용하는 `OptimizeSize`로 설정해 누락된 메타데이터 심볼 경로를 제거한다.
- Development Build가 생성한 Xcode IL2CPP 스크립트의 Bee 캐시를 Unity 공용 경로가 아닌 export 내부의 구성별 경로로 격리한다.
- Xcode의 Release/Archive 구성 이름은 유지하되 iOS IL2CPP 코드 생성은
  Development와 동일하게 `OptimizeSize`를 사용한다.
- 이미 생성된 `/Users/sindongju/aibuild`의 공유 스킴도 `Debug` 실행 구성으로 갱신한다.
- CocoaPods 워크스페이스에서 스킴 기본 실행 빌드를 수행해 Undefined Symbol이 사라졌는지 확인한다.
- 성공한 앱을 연결된 iPhone XS Max에 다시 설치한다.

## 검증

- [x] Development Build의 Xcode 공유 스킴 `LaunchAction`이 `Debug`다.
- [x] Xcode Release/Archive 구성 이름은 유지하고 IL2CPP 코드는
      `OptimizeSize`로 생성한다.
- [x] export-local Bee 캐시 변경을 포함한 Unity Healthcare QA Suite가 통과한다.
- [x] Xcode GUI와 동일한 기본 DerivedData 및 스킴 실행 구성으로 클린 링크가 성공한다.
- [x] 최종 `UnityFramework`에 보고된 URP RenderGraph 미해결 심볼이 없다.
- [x] 보고된 URP RenderGraph Undefined Symbol이 0건이다.
- [x] 수정된 앱이 iPhone XS Max에 설치된다.

## 완료 결과

- iOS Development Build는 IL2CPP `OptimizeSize`로 export하고 공유 Xcode 스킴의 실행 구성을 `Debug`로 자동 변경한다.
- iOS Release Build도 `OptimizeSize`를 사용하고 Archive 구성 이름은
  `Release`로 유지한다.
- Unity `6000.3.18f1` 임시 프로젝트에서 Healthcare QA Suite를 실행해 `AI_HEALTHCARE_QA_PASSED`를 확인했다.
- Xcode GUI가 사용하는 기본 DerivedData에서 재현된 실패 아카이브는 심볼 참조만 있고 정의가 없었다.
- Development export가 `BEE_CACHE_DIRECTORY`를 `$PROJECT_DIR/Il2CppBuildCache/$CONFIGURATION`으로 자동 변경하도록 수정했다.
- 새 export의 project-local Bee 캐시를 사용한 기본 DerivedData/Debug/iPhone 빌드가 `BUILD SUCCEEDED`로 완료됐다.
- 새 `libGameAssembly.a`에서 보고된 14개 심볼 정의를 모두 확인했으며 누락은 0개다.
- 수정된 앱(`com.sindongju.aihealthcare`)을 연결된 iPhone XS Max에 설치했다.
- 오류가 있던 이전 Xcode export는 `/Users/sindongju/aibuild.before-il2cpp-fix-20260727`에 보존했다.
- GUI에서 재현된 직전 Xcode export는 `/Users/sindongju/aibuild.before-local-bee-fix-20260727`에 추가 보존했다.

## 2026-07-27 재발 대응

### 재현 결과

- 현재 `/Users/sindongju/aibuild`를 비어 있는 전용 DerivedData로
  `ReleaseForRunning` 빌드했을 때 같은 URP RenderGraph 심볼 14개가
  `UnityFramework` 링크에서 다시 누락됐다.
- 해당 export는 IL2CPP `OptimizeSpeed` 형태의 generic translation unit
  133개를 포함하고 있었다. 따라서 이번 실패는 Xcode DerivedData 재사용만의
  문제가 아니라 export 시점의 IL2CPP 코드 생성 방식까지 포함한 문제다.
- `OptimizeSize`를 명시한 새 Development export는 generic translation unit이
  9개인 범용 generic 코드 경로를 생성했고, 동일한 클린
  `ReleaseForRunning` 빌드에서 `UnityFramework` 링크를 통과했다.
- 검증 빌드는 링크 이후 dSYM 생성 단계에서 테스트용 `/tmp` 공간 부족으로
  중단됐으며, 보고된 Undefined Symbol은 발생하지 않았다.

### 추가 수정 범위

- 커스텀 Development Build뿐 아니라 Release Build, Unity의 일반 Build,
  Build & Run, Append를 포함한 **모든 iOS export 전에**
  `Il2CppCodeGeneration.OptimizeSize`를 강제한다.
- Release export를 `OptimizeSpeed`로 되돌리던 기존 분기를 제거한다.
- 후처리 단계는 생성된 Xcode 프로젝트의 export-local Bee 캐시와 중복 build
  phase 정리를 계속 담당한다.
- Healthcare QA에서 Development/Release 빌드 설정과 일반 iOS 전처리 경로가
  모두 `OptimizeSize`를 유지하는지 검증한다.

### 추가 검증

- [x] iOS 빌드 전처리기가 일반 Build와 커스텀 Build 모두에서
      `OptimizeSize`를 설정한다.
- [x] Unity C# 컴파일과 Healthcare QA Suite가 통과한다.
- [x] 새 iOS export의 클린 `ReleaseForRunning` UnityFramework 링크에
      Undefined Symbol이 없다.
- [x] 전체 Xcode 빌드가 성공한다.

### 추가 완료 결과

- `IOSIl2CppBuildPreprocessor`를 추가해 Unity의 일반 Build, Build & Run,
  Append 및 커스텀 Build 전에 iOS `OptimizeSize`를 강제한다.
- 커스텀 Release Build가 `OptimizeSpeed`로 되돌아가던 분기를 제거했다.
- Unity `6000.3.18f1` 임시 프로젝트에서 Healthcare QA Suite의
  `AI_HEALTHCARE_QA_PASSED`를 확인했다.
- `/Users/sindongju/aibuild`를 범용 generic export로 교체했으며 generic
  translation unit 수는 133개에서 9개로 변경됐다.
- Xcode 기본 DerivedData와 연결된 iPhone XS Max 대상
  `ReleaseForRunning` 전체 빌드가 성공했다.
- 완성된 `UnityFramework`의 undefined symbol 목록에서 보고된 14개 심볼이
  모두 0건임을 확인했다.
- 앱 `com.sindongju.aihealthcare`를 iPhone XS Max에 설치했다.
- 교체 전 export는
  `/Users/sindongju/aibuild.before-global-optimize-size-20260727`에 보존했다.

## 2026-07-27 Build And Run 재발 대응

### 새로 확인된 차이

- 재발한 14개 심볼은 이전과 동일하지만 이번 실행 경로는 검증했던
  `ReleaseForRunning`이 아니라 Unity Build And Run의 Xcode `Debug` 구성이다.
- 새 export에는 `OptimizeSize`가 적용됐고 export-local Bee 캐시도 사용됐다.
  따라서 이전의 `OptimizeSpeed` 또는 공용 캐시 재사용 문제와는 다른 단계다.
- 비개발용 export의 `Il2CppMetadataUsage.c`에는 보고된 URP RenderGraph
  메타데이터/문자열 정의가 없지만 생성 C++에는 참조가 남아 있다.
- 최초에는 Xcode `Debug` 구성 차이로 판단했지만 아래 추가 대조에서
  비개발 export 자체의 누락으로 범위를 좁혔다.

### 추가 조사 결과

- Xcode `Debug`에서 IL2CPP 컴파일만 `Release`로 바꿔도 링크는 실패했다.
- 현재 비개발 export의 `ReleaseForRunning`도 동일한 14개 심볼로 실패했다.
- 현재 ManagedStripped DLL을 별도 임시 경로에서 IL2CPP로 완전히 다시
  변환해도 동일한 메타데이터 정의 누락이 재현되어 Unity/Bee 캐시 문제도
  아니었다.
- 성공했던 출력은 `BuildOptions.Development`로 생성됐고 필요한 메타데이터
  정의가 모두 포함돼 있었다.

### 최종 수정

- Unity `6000.3.18f1`에서만 일반 Build, Build And Run 및 커스텀 iOS 빌드
  옵션에 `Development`와 `CleanBuildCache`를 자동 추가한다.
- Script Debugging, wait-for-debugger, Deep Profiling, profiler autoconnect는
  계속 끈 상태로 유지한다.
- 다른 Unity 버전에서는 사용자가 요청한 빌드 옵션을 변경하지 않는다.
- Xcode 앱 실행 구성은 `Debug`, IL2CPP 코드는 `OptimizeSize`, Bee 캐시는
  export-local 경로를 유지한다.

### 추가 검증

- [x] 현재 `/Users/sindongju/aibuild`를 깨끗한 Development 호환 출력으로 교체한다.
- [x] Healthcare QA Suite가 통과한다.
- [x] Xcode `Debug` 클린 빌드에서 보고된 Undefined Symbol이 0건이다.
- [x] 완성된 Debug `UnityFramework`에서 보고된 미해결 심볼이 0건이다.

### 최종 완료 결과

- Unity 일반 Build And Run을 실제 실행했으며 가드 로그에서
  `Development and Clean Build Cache are ON`을 확인했다.
- 새 `Il2CppMetadataUsage.c`에는 보고된 메서드 메타데이터 4개와 문자열
  10개의 정의가 모두 포함됐다.
- 새 출력의 메타데이터 해시는 이전에 링크가 성공한 깨끗한 Development
  출력과 일치했다.
- 연결된 iPhone 대상 Xcode `Debug` clean build가 exit code 0으로 완료됐다.
- 완성된 Debug `UnityFramework`의 undefined symbol 목록에서 보고된
  14개 심볼은 0건이다.
- 결정론 Healthcare QA 실행 후 `[QA]` 오류는 0건이다.
- 완성된 앱 `com.sindongju.aihealthcare`를 연결된 iPhone XS Max에
  다시 설치했다.
