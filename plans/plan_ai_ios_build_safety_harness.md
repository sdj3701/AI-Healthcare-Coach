# 모든 AI 공통 iOS 빌드 안전 규칙과 회귀 방지 하네스 구현 계획

## 1. 목표

- 어떤 AI 도구가 프로젝트를 수정하더라도 iOS 검은 화면과
  `UnityFramework` Undefined symbol 문제가 재발하지 않게 한다.
- AI가 규칙을 읽고 지키는 문서 계층과, 규칙을 무시하거나 실수해도 실제
  빌드를 중단시키는 자동 하네스 계층을 함께 만든다.
- Unity `6000.3.18f1`에서 검증된 다음 안전 계약을 단일 원본으로 관리한다.
  - Unity Development Player OFF
  - Autoconnect Profiler OFF
  - Script Debugging / debugger wait / Deep Profiling OFF
  - Metal API Validation OFF
  - iOS IL2CPP `OptimizeSize`
  - `CleanBuildCache`
  - export-local Bee cache
  - 중복 Xcode build phase 금지
  - CocoaPods workspace와 MediaPipe `-force_load` 정책 유지

## 2. 현재 구조에서 확인한 빈틈

- 공통 지침은 `.agents/AGENTS.md`에만 있고 파일 안의 필수 문서 경로도 실제
  저장 위치와 다르다.
- Cursor에는 일반 개발 절차만 있고 iOS 안전 전용 규칙이 없다.
- 루트 `AGENTS.md`, `CLAUDE.md`, GitHub Copilot 지침이 없어 다른 AI가 같은
  규칙을 자동으로 읽는다는 보장이 없다.
- `IOSDevelopmentBuild`와 `IOSStableBuildSettings`가 안전 옵션을 설정하지만,
  다른 AI가 별도 `BuildPipeline.BuildPlayer` 경로를 추가하거나 기존 보호
  코드를 제거하면 회귀할 수 있다.
- 현재 Healthcare QA는 순수 함수와 설정 변경을 검증하지만, 실제 생성된
  `boot.config`, `project.pbxproj`, Xcode scheme과 빌드 로그를 하나의 안전
  계약으로 검사하지 않는다.
- Unity 버전을 바꾸어도 현재 검증 근거가 유효한지 강제로 재검토시키는
  fail-closed 장치가 없다.

## 3. 전체 구조

```text
AI 개발 도구
  ├─ 루트 AGENTS.md
  ├─ .agents/AGENTS.md
  ├─ CLAUDE.md
  ├─ .cursor/rules/ios-build-safety.mdc
  └─ .github/copilot-instructions.md
          │
          ▼
docs/governance/ai-ios-build-safety-policy.md
          │
          ▼
ProjectSettings/AIHealthcareIOSBuildSafety.json
  ├─ Unity pre-build fail-closed 검사
  ├─ Unity post-export 생성물 검사
  ├─ Healthcare 결정론 QA
  └─ Python 정적/생성물/로그 검사 + GitHub Actions
```

문서는 사람이 읽는 정책이고 JSON은 모든 검사기가 공유하는 기계 판독 가능한
안전 계약으로 사용한다.

## 4. AI 공통 규칙

### 4.1 단일 정책 문서

`docs/governance/ai-ios-build-safety-policy.md`를 추가한다.

반드시 포함할 규칙:

1. iOS 빌드 관련 변경 전 통합 트러블슈팅 문서와 안전 계약을 읽는다.
2. Unity 버전, IL2CPP 코드 생성, BuildOptions, Bee cache, Podfile,
   Xcode build phase를 임의 변경하지 않는다.
3. 심볼 오류를 해결하기 위해 Development Player를 다시 켜지 않는다.
4. MediaPipe duplicate-symbol 노이즈를 줄이기 위해 `-force_load`를 제거하지
   않는다.
5. 기존 export Append로 검증하지 않고 새 clean export를 사용한다.
6. `.xcworkspace`로 빌드한다.
7. 관련 변경은 계획 승인, Unity QA, 안전 하네스, Xcode 링크, 실기기
   breadcrumb 순으로 검증한다.
8. 안전 계약을 바꿀 때는 정책, JSON, Unity QA, 정적 검사, 장애 문서를 한
   작업에서 함께 갱신한다.

### 4.2 AI별 진입 파일

- 루트 `AGENTS.md`
  - Codex 및 AGENTS 규약을 지원하는 AI의 공통 진입점
- `.agents/AGENTS.md`
  - 기존 프로젝트 규칙을 유지하면서 잘못된 문서 경로를 실제 경로로 수정
  - iOS 안전 정책을 필수 읽기 항목으로 추가
- `CLAUDE.md`
  - Claude Code용 필수 규칙
- `.cursor/rules/ios-build-safety.mdc`
  - Cursor에서 `alwaysApply: true`
- `.github/copilot-instructions.md`
  - GitHub Copilot용 필수 규칙

각 파일에는 정책 ID `AHC-IOS-SAFETY-V1`과 단일 정책 경로를 넣는다.
정적 하네스는 모든 진입 파일의 정책 ID와 경로가 일치하는지 검사한다.

## 5. 기계 판독 안전 계약

`ProjectSettings/AIHealthcareIOSBuildSafety.json`을 추가한다.

예정 필드:

```json
{
  "schemaVersion": 1,
  "policyId": "AHC-IOS-SAFETY-V1",
  "validatedUnityVersions": ["6000.3.18f1"],
  "requiredIl2CppCodeGeneration": "OptimizeSize",
  "requireCleanBuildCache": true,
  "forbiddenBuildOptions": [
    "Development",
    "ConnectWithProfiler",
    "AllowDebugging",
    "EnableDeepProfilingSupport",
    "WaitForPlayerConnection"
  ],
  "requiredMetalApiValidation": 0,
  "requiredBeeCachePath": "$PROJECT_DIR/Il2CppBuildCache/$CONFIGURATION",
  "forbiddenBeeCachePath": "$HOME/Library/Unity/cache/bee"
}
```

보고된 RenderGraph 심볼 목록과 Xcode 오류 패턴도 계약에 넣어 빌드 로그
검사에서 사용한다.

## 6. Unity 자동 차단 하네스

### 6.1 신규 파일

`Assets/Editor/IOSBuildSafetyHarness.cs`

구성:

- `IOSBuildSafetyContract`
  - JSON 안전 계약 로드와 스키마 검증
- `IOSBuildSafetyValidator`
  - 프로젝트 설정, BuildOptions, 생성된 Xcode 프로젝트 검증
- `IOSBuildSafetyPreprocessor`
  - 모든 iOS 빌드의 마지막 전처리 단계에서 fail-closed 검사
- `IOSBuildSafetyPostprocessor`
  - MediaPipe 후처리 이후 실제 export 생성물을 검사
- `IOSBuildSafetyMenu`
  - Unity 메뉴와 batch mode 진입점 제공

### 6.2 pre-build 검사

iOS 빌드 시작 전에 다음을 검사하고 하나라도 다르면
`BuildFailedException`으로 중단한다.

- 현재 Unity 버전이 계약의 검증 완료 목록에 있는가
- IL2CPP 코드 생성이 `OptimizeSize`인가
- BuildOptions에 금지 옵션이 없는가
- `CleanBuildCache`가 있는가
- EditorUserBuildSettings의 profiler/debugger 관련 값이 모두 OFF인가
- `ProjectSettings.asset`의 `metalAPIValidation`이 `0`인가

Unity 버전이 바뀌면 자동 허용하지 않는다. 계약과 실기기 검증을 갱신하기
전까지 빌드를 차단해 이전 우회 설정이 다시 들어오는 것을 막는다.

### 6.3 post-export 검사

Unity export 직후 다음을 검사한다.

- `Data/boot.config`
  - debugger wait와 PlayerConnection 자동 연결이 비활성인가
- `Unity-iPhone.xcodeproj/project.pbxproj`
  - Bee cache가 export-local 경로인가
  - Unity 공용 Bee cache가 남아 있지 않은가
  - 각 타깃의 build phase UUID가 중복되지 않았는가
- `Unity-iPhone.xcscheme`
  - 개발용 출력의 LaunchAction은 `Debug`인가
  - ArchiveAction은 기존 Release 구성을 유지하는가
- `Podfile`
  - MediaPipeTasksVision과 `-force_load` 유지 정책이 훼손되지 않았는가
- 검사 결과를 export 안의
  `AIHealthcareIOSBuildSafetyReport.json`에 기록

오류가 있으면 성공한 export처럼 사용할 수 없도록 빌드를 실패 처리한다.

## 7. 외부 정적·생성물·로그 하네스

### 7.1 Python 검사기

`tools/qa/verify_ios_build_safety.py`를 추가한다.

모드:

```text
--project
  AI 지침 파일, 계약 JSON, ProjectSettings, 보호 코드 존재 여부 검사

--export <Xcode export 경로>
  boot.config, pbxproj, scheme, Podfile 검사

--xcode-log <로그 경로>
  보고된 Undefined symbol, Unexpected duplicate tasks,
  MediaPipe graph 등록 오류 검사
```

성공 시:

```text
AI_HEALTHCARE_IOS_SAFETY_PASSED
```

실패 시 비정상 종료 코드와 함께 어떤 계약이 깨졌는지 출력한다.

### 7.2 GitHub Actions

`.github/workflows/ios-safety-contract.yml`을 추가한다.

- pull request와 push에서 `--project` 모드를 실행한다.
- AI 지침, 정책 JSON, PlayerSettings 또는 보호 코드가 불일치하면 CI를
  실패시킨다.
- Unity 라이선스가 없어도 실행 가능한 정적 계약 검사로 구성한다.

## 8. Healthcare QA 확장

`Assets/Editor/RagHealthcare/HealthcareQaSuite.cs`에 다음 회귀 테스트를
추가한다.

- 안전 계약 JSON 로드와 정책 ID 검증
- 검증되지 않은 Unity 버전 거부
- 금지 BuildOptions 각각 거부
- `CleanBuildCache` 누락 거부
- `OptimizeSpeed` 거부
- Metal Validation ON 거부
- shared Bee cache 거부
- 중복 build phase 거부
- unsafe `boot.config` 거부
- 개발 scheme과 ArchiveAction 구분
- MediaPipe `-force_load` 정책 훼손 거부
- 모든 validator가 동일 입력에서 결정적인 결과를 내는지 확인

## 9. 문서 갱신

- `docs/troubleshooting/ios-black-screen-and-xcode-symbol-runbook.md`
  - 자동 하네스 실행법과 오류 메시지 해석 추가
- `docs/qa/ragUnityTestGuide.md`
  - 로컬/배치/생성물/Xcode 로그 검증 명령 추가
- `docs/README.md`
  - AI iOS 안전 정책 연결

## 10. 구현 순서

- [ ] AI 공통 정책과 기계 판독 계약 추가
- [ ] AI별 진입 파일에 `AHC-IOS-SAFETY-V1` 적용
- [ ] Unity pre-build/post-export fail-closed 하네스 구현
- [ ] Python 프로젝트/export/Xcode 로그 검사기 구현
- [ ] GitHub Actions 정적 계약 검사 추가
- [ ] Healthcare 결정론 QA 회귀 테스트 추가
- [ ] 트러블슈팅·QA·문서 인덱스 갱신
- [ ] Unity C# 컴파일 확인
- [ ] Healthcare 결정론 QA 통과
- [ ] Python 프로젝트 모드 통과
- [ ] 현재 Xcode export 검사 통과
- [ ] 의도적으로 잘못된 fixture에서 각 오류가 차단되는지 확인

## 11. 완료 기준

- 지원되는 모든 AI 지침 파일이 동일 정책 ID와 문서를 가리킨다.
- AI가 Development Player, `OptimizeSpeed`, 공용 Bee cache,
  Metal Validation을 다시 켜면 빌드 또는 CI가 실패한다.
- unsafe `boot.config`, 중복 build phase, 잘못된 Xcode scheme과 Podfile이
  export 검사를 통과하지 못한다.
- 보고된 14개 RenderGraph Undefined symbol과
  `Unexpected duplicate tasks`가 Xcode 로그 검사에서 검출된다.
- 정상 프로젝트와 정상 export에서는 모든 하네스가 통과한다.
- Unity 컴파일과 Healthcare 결정론 QA가 통과한다.

## 12. 변경하지 않는 범위

- 운동 자세 판정, TTS, UI Toolkit 런타임 로직은 변경하지 않는다.
- MediaPipe `-force_load` 정책은 제거하지 않는다.
- 현재 검증된 iOS signing Team과 Bundle ID는 변경하지 않는다.
- 사용자의 기존 Xcode export나 백업 폴더를 삭제하지 않는다.
