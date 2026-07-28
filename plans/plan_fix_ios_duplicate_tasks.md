# iOS Unexpected Duplicate Tasks 수정 계획

## 확인된 원인

- 최신 Xcode 빌드 로그에서 `GameAssembly` 타깃의 동일한 Run Script가 같은 출력 파일을 두 번 생성하려 하며 `Unexpected duplicate tasks`가 발생했다.
- 생성된 `Unity-iPhone.xcodeproj/project.pbxproj`의 `GameAssembly.buildPhases`에 동일한 UUID `C62A2A42F32E085EF849CF0B`가 연속으로 두 번 등록되어 있다.
- 이번 Xcode 프로젝트는 Unity의 일반 Build/Append 경로로 다시 생성되어, 커스텀 Development Build에만 적용했던 export별 Bee 캐시 보정이 실행되지 않고 공용 캐시 경로도 다시 사용 중이다.

## 수정 범위

- Xcode 프로젝트의 각 `buildPhases` 목록에서 같은 UUID가 중복 등록된 경우 첫 항목만 남기는 멱등성 정리 함수를 추가한다.
- 커스텀 Development Build뿐 아니라 `MediaPipeIOSBuildPostprocessor`에서도 정리 함수를 실행해 일반 Build, Build & Run, Append export 모두 보호한다.
- 모든 iOS export에서 IL2CPP Bee 캐시가 export 내부의 구성별 경로를 사용하도록 보정해 이전 Undefined Symbol 오류의 재발도 막는다.
- 중복 항목 제거, 정상 항목 보존, 두 번 적용 시 무변경을 Healthcare QA Suite에서 검증한다.
- 현재 오류가 있는 `/Users/sindongju/aibuild`를 보존한 뒤 수정된 iOS 프로젝트로 교체한다.

## 검증

- [x] Unity C# 컴파일과 Healthcare QA Suite가 통과한다.
- [x] 생성된 `GameAssembly.buildPhases`에 동일 UUID가 한 번만 존재한다.
- [x] 생성된 Xcode 프로젝트가 export별 Bee 캐시를 사용한다.
- [x] Xcode GUI와 같은 workspace, Debug 구성, 기본 DerivedData에서 빌드가 성공한다.
- [x] `Unexpected duplicate tasks`와 기존 URP RenderGraph Undefined Symbol이 모두 0건이다.
- [x] 수정된 앱이 연결된 iPhone XS Max에 설치되고 실행된다.

## 완료 결과

- Xcode의 각 타깃별 `buildPhases`에서 같은 UUID가 반복되면 첫 항목만 남기는 멱등성 정리 함수를 구현했다.
- 커스텀 Development Build와 일반 iOS `PostProcessBuild` 경로가 모두 동일한 정리 함수를 사용한다.
- 새 `/Users/sindongju/aibuild`의 `GameAssembly` Run Script 참조는 1개이며 Bee 캐시는 `$PROJECT_DIR/Il2CppBuildCache/$CONFIGURATION`을 사용한다.
- 기본 DerivedData, Debug 구성, 연결된 iPhone XS Max 대상으로 `BUILD SUCCEEDED`를 확인했다.
- 최신 Xcode 빌드에서 `Unexpected duplicate tasks`와 `Undefined symbol`은 모두 0건이다.
- 앱 `com.sindongju.aihealthcare`를 iPhone XS Max에 설치하고 실행했다.
- 오류가 있던 export는 `/Users/sindongju/aibuild.before-duplicate-task-fix-20260727`에 보존했다.
