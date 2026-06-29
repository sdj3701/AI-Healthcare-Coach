# Test MediaPipe Plan

## 1. Goal

Unity에서 카메라를 활성화하고 MediaPipe Pose를 연결해 실시간으로 관절 데이터를 받는지 검증한다.

이번 단계의 목표는 스쿼트 규칙 엔진, TTS, RAG, 리포트, 3D 리플레이까지 한 번에 구현하는 것이 아니다. 먼저 카메라 프리뷰와 MediaPipe Pose landmark 수신이 안정적으로 동작하는지 확인한다.

## 2. Source Document Analysis

### Backlog Workbook

분석 파일:

```text
docs/온디바이스_AI_헬스케어_제품_백로그.xlsx
```

MediaPipe 실시간 테스트와 직접 관련 있는 핵심 에픽:

- E04 카메라 세팅/캘리브레이션
- E05 포즈 추정/실시간 파이프라인
- E06 규칙 엔진/반복 카운트
- E07 실시간 피드백/TTS
- E08 데이터/프라이버시/저장
- E13 성능/단말 최적화
- E14 QA/검증/전문가 리뷰

이번 문서에서 우선 반영할 PBI:

- PBI-017: 카메라 프리뷰 위 전신 가이드
- PBI-018: 주요 관절 visibility/presence 기준 충족 시에만 시작 허용
- PBI-019: 발목 누락, 상체 잘림, 조명 부족, 카메라 높이 오류 안내
- PBI-020: 스쿼트 MVP의 1차 촬영 모드 결정
- PBI-021: 2~3초 캘리브레이션
- PBI-024: 카메라 미러링, 회전, 좌우 landmark 매핑
- PBI-025: 실기기 카메라 프레임에서 33개 pose landmark 수신
- PBI-027: LandmarkFrame 공통 모델 정의
- PBI-028: 카메라 프리뷰 위 관절점/관절선 오버레이
- PBI-029: visibility 낮은 프레임 판정 제외
- PBI-030: 카메라 표시 FPS와 포즈 추론 FPS 분리
- PBI-031: FPS, 메모리, 오류 코드 중심의 QA 진단 로그
- PBI-052: 원본 영상 없이 좌표 데이터만 저장

관련 QA:

- T-001: 전신 인식
- T-002: 거리 부족
- T-009: 10분 연속 사용 안정성

관련 규칙/데이터:

- `landmarks`: id, name, x, y, z, visibility
- `fps_sampled`: 포즈 추론 샘플링 FPS
- `events`: t_ms, rep, rule_id, severity
- 원본 영상 저장 금지

### implementation_plan.md

분석 파일:

```text
docs/implementation_plan.md
```

현재 내용은 AI 헬스케어/MediaPipe 구현 계획이 아니라 C++ N-gram 언어 모델과 Kiwi 형태소 분석기 통합 계획이다. 따라서 이번 MediaPipe 실시간 테스트 계획의 직접 구현 근거로는 사용하지 않는다.

다만 다음 원칙은 유지한다:

- 작은 기능 단위로 검증한다.
- 테스트 가능한 산출물을 먼저 만든다.
- 추후 RAG/LLM 기능과 실시간 파이프라인을 분리한다.

## 3. MVP Test Scope

### Included

- Unity에서 카메라 프리뷰 표시
- MediaPipe Pose 연결
- 33개 pose landmark 수신 확인
- landmark 좌표와 visibility 콘솔/화면 표시
- 주요 관절 skeleton 오버레이
- FPS 측정
- visibility 낮을 때 판정 중지 상태 표시
- 원본 영상 파일이 저장되지 않는지 확인

### Not Included Yet

- 스쿼트 반복 카운트
- 스쿼트 자세 오류 규칙 판정
- TTS 피드백 연결
- JSON 세션 저장
- 3D 아바타 리플레이
- RAG/LLM 리포트
- Android 최종 네이티브 최적화
- iOS 최종 배포/TestFlight 설정

## 4. Recommended First Test Target

현재 사용 가능한 테스트 장비는 M1 MacBook Pro와 iPhone XS Max다. Android 실기기 테스트는 당장 어렵기 때문에 iOS 실기기 테스트를 우선한다.

첫 테스트는 다음 순서로 진행한다.

1. Windows Unity Editor 또는 M1 Mac Unity Editor에서 카메라 프리뷰 확인
2. M1 MacBook Pro에서 iOS Build Support, Xcode, CocoaPods, MediaPipe API 준비
3. iPhone XS Max에서 카메라 권한과 MediaPipe Pose landmark 수신 확인
4. iPhone XS Max에서 5~10분 안정성 확인

이유:

- Windows Editor에서는 Unity 화면/오버레이/UI 디버깅이 빠르다.
- iOS 앱의 실제 빌드와 실기기 실행은 Mac + Xcode가 필요하다.
- 실제 성능, 발열, 카메라 권한, iOS 카메라 프레임 처리는 iPhone XS Max에서 확인해야 한다.
- 백로그의 최종 목표는 온디바이스 실시간 처리이므로 Editor 성공만으로 완료 처리하지 않는다.

## 4.1 Current iOS Build Decisions And Questions

### Decision

- Android 테스트는 이번 단계에서 보류한다.
- iOS 테스트 기기는 iPhone XS Max로 한다.
- iOS 빌드 장비는 M1 MacBook Pro로 한다.
- Windows PC는 Unity 코드 작성, 문서 작성, Editor 수준의 화면 검증에 사용한다.
- iOS 실기기 테스트는 Mac에서 Unity iOS Build 또는 Xcode Run으로 진행한다.

### Q1. Windows에서 iPhone 빌드가 가능한가?

결론:

- Windows만으로 iPhone에 설치 가능한 최종 iOS 앱을 로컬 빌드/실행하는 것은 불가능하다.
- Unity는 iOS용 Xcode 프로젝트를 생성하는 단계가 있고, 실제 앱 빌드와 기기 설치는 Xcode가 담당한다.
- Xcode는 macOS에서만 사용할 수 있으므로 로컬 iOS 빌드는 Mac이 필요하다.

근거:

- Unity 공식 문서는 iOS 빌드를 `Unity가 Xcode 프로젝트 생성 -> Xcode가 앱 빌드`의 2단계로 설명한다.
- Unity 공식 문서는 로컬 iOS 빌드에는 macOS가 필요하다고 명시한다.

### Q1-1. M1 MacBook Pro로 빌드 가능한가?

결론:

- 가능하다.
- M1 MacBook Pro에 Unity 6.3 LTS, iOS Build Support, Xcode, Apple Account, 필요한 signing 설정이 있으면 iPhone XS Max에 직접 실행 테스트할 수 있다.

필요한 준비:

- M1 MacBook Pro
- Unity 6.3 LTS
- Unity iOS Build Support module
- Xcode
- Apple Account
- iPhone XS Max USB 연결 또는 Xcode Wi-Fi debugging 설정
- iPhone에서 개발자 모드/신뢰 설정
- Bundle Identifier 예: `com.yourname.aihealthcarecoach`

주의:

- 무료 Apple Account의 Personal Team으로도 개인 기기 테스트는 가능하지만 제한이 있다.
- TestFlight, Ad Hoc, App Store 배포는 Apple Developer Program 유료 계정이 필요할 수 있다.

### Q1-2. iPhone 빌드 실행 테스트는 어떻게 하는가?

권장 방식:

1. M1 MacBook Pro에서 Unity 프로젝트를 연다.
2. Unity Hub에서 iOS Build Support가 설치되어 있는지 확인한다.
3. Unity `Build Profiles` 또는 `Build Settings`에서 iOS로 전환한다.
4. Player Settings에서 Bundle Identifier를 설정한다.
5. `Build` 또는 `Build And Run`으로 Xcode 프로젝트를 생성한다.
6. Xcode에서 `Unity-iPhone.xcodeproj` 또는 workspace를 연다.
7. Signing & Capabilities에서 Team을 선택하고 Automatically manage signing을 켠다.
8. iPhone XS Max를 Mac에 연결하고 Trust/Developer Mode를 허용한다.
9. Xcode 상단 기기 선택에서 iPhone XS Max를 선택한다.
10. Xcode Run 버튼으로 빌드/설치/실행한다.

카카오톡이나 메신저로 앱 파일을 보내 설치하는 방식은 초기 개발 테스트 방식으로 추천하지 않는다.

테스트 배포 방식 구분:

- 혼자 실기기 테스트: Xcode에서 직접 Run
- 팀원/외부 사용자 테스트: TestFlight
- 특정 등록 기기 배포: Ad Hoc 배포
- 스토어 배포: App Store

이번 단계에서는 `Xcode 직접 Run`을 기본으로 한다. TestFlight는 MediaPipe 실시간 테스트가 안정화된 뒤 고려한다.

## 5. Proposed Unity Scene

Scene name:

```text
Assets/Scenes/MediaPipeTest.unity
```

Scene objects:

```text
MediaPipe Test Runtime
  CameraPreviewController
  PoseEstimatorAdapter
  PoseOverlayRenderer
  PoseDebugHud
  PoseQualityGate
```

화면 구성:

```text
Camera Preview
Skeleton Overlay
FPS / Landmark Count / Visibility Status
Start Camera Button
Stop Camera Button
Camera Mode Label
```

## 6. Proposed Module Responsibilities

### CameraPreviewController

역할:

- 카메라 권한 요청
- 카메라 장치 선택
- 프리뷰 Texture 표시
- 전면/후면 또는 웹캠 장치 전환
- 화면 회전/미러링 상태 제공

초기 테스트 기준:

- 카메라 프리뷰가 보인다.
- 프리뷰가 좌우 또는 상하로 뒤집혀 있으면 상태를 로그로 확인할 수 있다.
- Stop 시 카메라 리소스가 해제된다.

### PoseEstimatorAdapter

역할:

- 카메라 프레임을 MediaPipe Pose에 전달
- 결과를 Unity C# 모델로 변환
- timestamp 유지
- 추론 실패 상태 반환

초기 테스트 기준:

- 사람 한 명이 화면에 있을 때 33개 landmark가 들어온다.
- frame timestamp가 증가한다.
- 사람이 화면에서 사라지면 no pose 상태가 된다.

### LandmarkFrame

초기 데이터 모델:

```json
{
  "timestamp_ms": 123456,
  "camera_mode": "front_or_side_test",
  "fps_sampled": 15,
  "landmarks": [
    {
      "id": 0,
      "name": "nose",
      "x": 0.52,
      "y": 0.18,
      "z": -0.12,
      "visibility": 0.98
    }
  ]
}
```

주의:

- x, y는 프리뷰 좌표계와 MediaPipe 좌표계를 구분한다.
- 화면 오버레이용 좌표와 규칙 계산용 좌표를 섞지 않는다.
- 원본 영상 프레임은 저장하지 않는다.

### PoseOverlayRenderer

역할:

- 주요 관절점 표시
- 관절선 표시
- visibility 낮은 관절 흐리게 표시
- 화면 UI와 겹치지 않도록 오버레이 정렬

초기 테스트 기준:

- 코, 어깨, 팔꿈치, 손목, 엉덩이, 무릎, 발목이 대략 올바른 위치에 표시된다.
- 카메라 회전 후에도 관절선이 크게 어긋나지 않는다.

### PoseQualityGate

역할:

- 주요 관절 visibility 기준 평가
- 전신 인식 가능 여부 판단
- 거리 부족/신체 잘림 안내 상태 생성

초기 주요 관절:

- left_shoulder
- right_shoulder
- left_hip
- right_hip
- left_knee
- right_knee
- left_ankle
- right_ankle

초기 테스트 기준:

- 발목이 화면 밖이면 `LOWER_BODY_MISSING`
- 상체가 잘리면 `UPPER_BODY_MISSING`
- 전체 visibility가 낮으면 `LOW_CONFIDENCE`
- 정상 전신이면 `READY`

### PoseDebugHud

역할:

- 카메라 FPS 표시
- 추론 FPS 표시
- landmark count 표시
- 평균 visibility 표시
- 현재 quality gate 상태 표시
- 마지막 오류 메시지 표시

초기 표시 예:

```text
Camera FPS: 30
Pose FPS: 15
Landmarks: 33
Avg Visibility: 0.91
Quality: READY
```

## 7. MediaPipe Integration Options

### Option A. Unity용 MediaPipe 플러그인 사용

장점:

- Unity Editor 테스트가 빠르다.
- C#에서 landmark 결과를 바로 확인하기 쉽다.
- 초기 프로토타입에 적합하다.

단점:

- 플러그인 버전, 플랫폼 빌드 설정, 네이티브 바이너리 호환성 확인이 필요하다.
- iOS 최종 빌드에서 네이티브 바이너리, camera frame format, orientation 이슈가 생길 수 있다.
- Unity 플러그인이 최신 Google MediaPipe Tasks iOS API와 완전히 같은 구조가 아닐 수 있다.

### Option B. iOS Native Bridge 우선 구현

장점:

- 현재 보유 기기인 iPhone XS Max 테스트에 직접 맞다.
- Google MediaPipe Pose Landmarker iOS API의 live stream 구조와 직접 연결할 수 있다.
- iOS 카메라 프레임, orientation, permission, device performance를 실제 환경에서 검증할 수 있다.

단점:

- 초기 디버깅 속도가 느리다.
- Unity Editor에서 실시간 테스트가 제한된다.
- Swift/Objective-C bridge와 Unity C# interop 구조가 필요하다.

### Option C. Editor Stub + iOS Native Bridge

장점:

- Windows/Mac Unity Editor에서는 카메라 프리뷰와 UI/HUD만 빠르게 검증한다.
- 실제 pose inference는 iPhone XS Max에서 MediaPipe iOS API로 검증한다.
- `PoseEstimatorAdapter` 인터페이스를 먼저 고정해 플랫폼별 구현을 바꿔 끼울 수 있다.

단점:

- Editor와 iOS에서 결과가 다를 수 있다.
- iOS 빌드 루프가 필요해 반복 속도가 느리다.

### MediaPipe API Download Requirements

이번 iOS 테스트에는 MediaPipe API와 모델 파일 준비가 필요하다.

필요 항목:

- Google MediaPipe Pose Landmarker iOS task library
- Pose Landmarker model bundle
- iOS live stream mode sample/reference
- Xcode project dependency 설정

초기 모델 선택:

- iPhone XS Max 성능을 고려해 `Pose landmarker lite`부터 테스트한다.
- 안정화 후 `Full` 모델을 비교한다.
- `Heavy` 모델은 초기 테스트 범위에서 제외한다.

검증할 MediaPipe 설정:

- running mode: live stream
- num poses: 1
- min pose detection confidence: 0.5
- min pose presence confidence: 0.5
- min tracking confidence: 0.5
- segmentation mask: off

주의:

- MediaPipe 공식 Pose Landmarker는 image/video/live stream 입력을 지원한다.
- live stream 모드에서는 비동기 결과 callback/delegate 구조를 사용해야 한다.
- Unity C#으로 넘기는 결과는 `LandmarkFrame` schema로 변환한다.
- 원본 카메라 프레임은 저장하지 않는다.

### Recommended Approach

이번 테스트 문서 기준 추천 순서:

1. Windows/Mac Unity Editor에서 카메라 프리뷰, UI, HUD, QualityGate 화면 흐름을 먼저 확인한다.
2. `PoseEstimatorAdapter` 인터페이스를 기준으로 Editor Stub과 iOS Native Bridge를 분리한다.
3. M1 MacBook Pro에서 iOS Build Support와 Xcode 빌드 환경을 준비한다.
4. MediaPipe Pose Landmarker iOS API와 lite model을 다운로드한다.
5. iPhone XS Max에서 live stream pose landmark 수신을 검증한다.

## 8. Implementation Phases

### Phase 0. Environment Check

확인 항목:

- Unity 6.3 LTS 프로젝트가 정상 실행되는지
- 현재 씬 오류가 없는지
- 카메라 장치가 Windows에서 인식되는지
- M1 MacBook Pro에서 Unity 6.3 LTS 설치 여부
- Unity iOS Build Support 설치 여부
- Xcode 설치 여부
- Apple Account 로그인 여부
- iPhone XS Max 연결 및 Trust/Developer Mode 설정 여부
- MediaPipe iOS API와 Pose Landmarker model 다운로드 여부

완료 기준:

- 빈 씬에서 Play Mode 진입 가능
- Console에 기존 컴파일 오류 없음
- Windows/Mac 웹캠 또는 iPhone XS Max 카메라 사용 가능

### Phase 1. Camera Preview Test

작업:

- `MediaPipeTest.unity` 생성
- 카메라 시작/중지 버튼 구성
- 카메라 프리뷰 표시
- 카메라 FPS 표시

테스트:

- 카메라 시작 시 프리뷰가 나온다.
- 카메라 중지 시 프리뷰가 멈추고 리소스가 해제된다.
- Play Mode 종료 후 카메라 LED가 꺼진다.

합격 기준:

- Windows Editor에서 카메라 프리뷰 성공
- Console 오류 없음
- 원본 영상 파일 생성 없음

### Phase 2. MediaPipe Pose Test

작업:

- MediaPipe Pose 연결
- 카메라 프레임을 Pose 추론에 전달
- 33개 landmark 수신
- timestamp와 visibility 표시

테스트:

- 사람이 화면 안에 있으면 landmark count가 33으로 표시된다.
- 사람이 없으면 no pose 상태가 표시된다.
- 움직일 때 landmark 좌표가 실시간 갱신된다.

합격 기준:

- 33개 landmark 수신
- timestamp 증가
- 최소 1분 연속 실행 중 크래시 없음

### Phase 3. Skeleton Overlay Test

작업:

- 카메라 프리뷰 위에 관절점 표시
- 주요 관절선 표시
- visibility 낮은 관절 흐림 처리
- 미러링/회전 보정

테스트:

- 어깨, 엉덩이, 무릎, 발목이 실제 위치와 크게 어긋나지 않는다.
- 전면/후면 카메라 또는 화면 회전 시 좌우가 뒤집히지 않는다.

합격 기준:

- 주요 관절 오버레이가 육안으로 자연스럽다.
- 좌우 관절 라벨이 뒤바뀌지 않는다.

### Phase 4. Pose Quality Gate Test

작업:

- 주요 관절 visibility 기준 정의
- 전신 인식 가능 상태 계산
- 거리 부족/신체 잘림/낮은 신뢰도 상태 표시

테스트:

- 발목을 화면 밖으로 빼면 거리 조정 안내 상태가 나온다.
- 몸이 너무 가까우면 시작 불가 상태가 나온다.
- 전신이 보이면 READY 상태가 된다.

합격 기준:

- T-001 전신 인식 시나리오 통과
- T-002 거리 부족 시나리오 통과
- 낮은 confidence에서 자세 판정을 하지 않는다.

### Phase 5. Sampling And Performance Test

작업:

- 카메라 표시 FPS와 Pose 추론 FPS 분리
- 15fps, 24fps 테스트
- FPS, 메모리, 오류 코드 표시

테스트:

- 카메라 프리뷰는 부드럽게 유지된다.
- Pose 추론 FPS 제한이 적용된다.
- 추론 FPS 변경 시 timestamp 간격이 기록된다.

합격 기준:

- 최소 5분 연속 실행 중 크래시 없음
- 목표 Pose FPS 유지 여부 확인
- 성능 로그가 원본 영상 없이 남는다.

### Phase 6. iOS Device Smoke Test

작업:

- M1 MacBook Pro에서 iOS 빌드
- Xcode 프로젝트 생성
- Signing 설정
- 카메라 권한 요청
- iPhone XS Max에서 프리뷰 확인
- iPhone XS Max에서 MediaPipe landmark 수신 확인

테스트:

- 앱 최초 실행 시 카메라 권한 요청이 나온다.
- 권한 허용 후 프리뷰가 보인다.
- iPhone XS Max에서 33개 landmark가 수신된다.
- 앱 종료 또는 백그라운드 전환 후 카메라 리소스가 해제된다.

합격 기준:

- iPhone XS Max에서 1분 이상 MediaPipe 실시간 테스트 성공
- 앱 종료 후 카메라 리소스 해제
- 크래시 없음

### Phase 7. iOS Stability Test

작업:

- 5분 연속 카메라 프리뷰 + Pose 추론 유지
- FPS, 평균 visibility, dropped frame, error code 확인
- iPhone 발열 체감과 앱 중단 여부 확인

테스트:

- 정면 또는 측면으로 전신이 보이게 선다.
- 5분 동안 가벼운 스쿼트 준비 동작과 정지 상태를 반복한다.
- 앱이 카메라 권한, orientation, background/foreground 전환에서 깨지지 않는지 확인한다.

합격 기준:

- 5분 크래시 없음
- landmark count 33 유지 구간 확인
- no pose와 READY 상태 전환이 자연스럽다.
- 원본 영상 파일 생성 없음

## 9. Manual Test Cases

| Test ID | Area | Scenario | Procedure | Expected Result |
| --- | --- | --- | --- | --- |
| MP-T001 | Camera | 카메라 시작 | Play Mode에서 Start Camera 클릭 | 카메라 프리뷰 표시 |
| MP-T002 | Camera | 카메라 중지 | Stop Camera 클릭 | 프리뷰 중지, 카메라 리소스 해제 |
| MP-T003 | Pose | 사람 감지 | 전신이 화면에 들어오도록 서기 | landmark count 33 |
| MP-T004 | Pose | 사람 없음 | 화면 밖으로 이동 | no pose 상태 |
| MP-T005 | Quality | 발목 누락 | 카메라를 가까이 두어 발목이 잘리게 함 | LOWER_BODY_MISSING |
| MP-T006 | Quality | 상체 누락 | 상체 일부가 화면 밖으로 나가게 함 | UPPER_BODY_MISSING |
| MP-T007 | Quality | 정상 전신 | 어깨/엉덩이/무릎/발목이 모두 보이게 함 | READY |
| MP-T008 | Overlay | 관절선 확인 | 팔/다리를 천천히 움직임 | skeleton이 몸을 따라감 |
| MP-T009 | Performance | 5분 실행 | 5분 동안 프리뷰와 추론 유지 | 크래시 없음, FPS 표시 |
| MP-T010 | Privacy | 영상 미저장 | 테스트 후 프로젝트/앱 저장소 확인 | mp4/image frame 없음 |

## 10. Data To Log

초기 테스트 로그는 원본 영상 없이 다음 값만 남긴다.

```json
{
  "test_id": "MP-T003",
  "timestamp": "2026-06-26T13:00:00+09:00",
  "device": "iPhone XS Max",
  "camera_fps": 30,
  "pose_fps": 15,
  "landmark_count": 33,
  "avg_visibility": 0.91,
  "quality_state": "READY",
  "error_code": null
}
```

저장 금지:

- mp4
- jpg/png 프레임
- 사용자의 원본 얼굴/신체 이미지

허용:

- FPS
- landmark count
- 평균 visibility
- quality state
- error code
- 샘플링된 landmark 좌표

## 11. Acceptance Criteria

MediaPipe 실시간 테스트 완료 기준:

- Unity Play Mode에서 카메라 프리뷰가 표시된다.
- MediaPipe Pose가 33개 landmark를 반환한다.
- landmark timestamp가 실시간으로 갱신된다.
- 주요 관절 skeleton overlay가 화면에 표시된다.
- visibility 낮은 상태에서 자세 판정을 하지 않는다.
- 카메라 프리뷰 FPS와 pose 추론 FPS를 구분해 볼 수 있다.
- 원본 영상 파일이 생성되지 않는다.
- Windows Editor에서 5분 연속 실행 중 크래시가 없다.
- iPhone XS Max에서 1분 smoke test를 통과한다.
- iPhone XS Max에서 5분 안정성 테스트 계획이 분리되어 있다.

## 12. Risks And Mitigation

| Risk | Impact | Mitigation |
| --- | --- | --- |
| MediaPipe Unity 플러그인 호환성 문제 | Editor 또는 iOS 빌드 실패 | Adapter 인터페이스로 격리하고 iOS Native Bridge 대체 가능하게 설계 |
| Windows에서 iOS 최종 빌드 불가 | 빌드 테스트 지연 | M1 MacBook Pro + Xcode를 iOS 빌드 전용 환경으로 사용 |
| iOS signing/provisioning 문제 | iPhone 설치 실패 | Xcode Automatically manage signing, Personal Team 또는 Apple Developer Program 사용 |
| iPhone XS Max 성능 한계 | FPS 저하, 발열 | Pose landmarker lite, 15fps 샘플링, segmentation mask off |
| 카메라 좌표와 landmark 좌표 불일치 | 오버레이 위치 오류 | 프리뷰 좌표계, normalized 좌표계, 규칙 계산 좌표계를 분리 |
| 미러링/회전 문제 | 좌우 무릎/발목 라벨 오류 | 카메라 orientation, mirrored flag를 LandmarkFrame metadata에 포함 |
| visibility 낮은 프레임 | 잘못된 피드백 | QualityGate가 READY가 아닐 때 규칙 평가 중지 |
| 발열/배터리 | 장시간 테스트 실패 | 카메라 FPS와 추론 FPS 분리, 15fps 샘플링부터 시작 |
| 프라이버시 우려 | 사용자 신뢰 저하 | 원본 영상 저장 금지, 좌표/성능 로그만 사용 |

## 13. Recommended Next Task

다음 작업은 코드 구현 전에 MediaPipe 연동 방식을 최종 선택하는 것이다.

결정해야 할 항목:

1. 첫 테스트를 Windows Editor 웹캠으로 할지, M1 Mac Editor 웹캠으로 할지
2. MediaPipe Unity 플러그인을 먼저 검증할지, iOS Native Bridge를 바로 만들지
3. MVP 1차 촬영 모드를 정면으로 할지 측면으로 할지
4. 첫 품질 기준 visibility threshold를 얼마로 둘지
5. 실시간 테스트 로그를 파일로 남길지 Console/HUD만 볼지
6. 무료 Apple Account Personal Team으로 충분한지, Apple Developer Program 가입이 필요한지
7. MediaPipe model을 lite로 고정할지, full 비교까지 할지

권장 결정:

- 1차 테스트: Windows 또는 M1 Mac Unity Editor 웹캠으로 카메라 프리뷰/HUD 확인
- 2차 테스트: M1 MacBook Pro에서 iPhone XS Max로 iOS Build And Run
- MediaPipe 경로: iOS Native Bridge 우선 검토, 막히면 Unity 플러그인 검증
- 모델: Pose landmarker lite
- 구현 구조: `PoseEstimatorAdapter` 인터페이스 기반
- 저장 정책: 원본 영상 저장 없음
- 첫 검증 범위: 33개 landmark, skeleton overlay, visibility gate, FPS HUD

## 14. Reference Links

- Unity iOS build process: https://docs.unity3d.com/6000.2/Documentation/Manual/iphone-BuildProcess.html
- Apple membership and Personal Team testing: https://developer.apple.com/support/compare-memberships/
- Apple TestFlight overview: https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview/
- MediaPipe Pose Landmarker guide: https://developers.google.com/edge/mediapipe/solutions/vision/pose_landmarker
- MediaPipe Pose Landmarker iOS guide: https://developers.google.com/edge/mediapipe/solutions/vision/pose_landmarker/ios

## 15. 2026-06-29 M1 Mac Editor 동작 clarification 및 코드 수정 기록

### 확인된 문제

- M1 MacBook의 Unity Editor에서 카메라 프리뷰 위에 관절 skeleton이 보이지만 실제 몸을 따라가지 않는 현상이 있었다.
- 원인은 실제 MediaPipe 추론이 아니라 `EditorStubPoseEstimator`의 합성 관절 데이터가 기본으로 표시되고 있었기 때문이다.
- 현재 프로젝트의 실제 MediaPipe 추론 백엔드는 `IOSMediaPipePoseEstimator`이며, `UNITY_IOS && !UNITY_EDITOR` 조건에서만 활성화된다.
- 따라서 MacBook M1 Unity Editor에서는 카메라 프리뷰는 가능하지만, 이 코드만으로는 실제 관절 추적이 되지 않는다.
- 이 섹션은 Python Editor 백엔드 추가 전 상태를 기록한 것이다. 현재 상태는 섹션 17을 따른다.

### 코드 변경

- `MediaPipeTestRunner.simulatePoseWhenNativeUnavailable` 기본값을 `false`로 변경했다.
- `Assets/Scenes/MediaPipeTest.unity`에 저장된 `simulatePoseWhenNativeUnavailable`도 `0`으로 변경했다.
- `PoseEstimatorSettings.simulatePoseWhenNativeUnavailable` 기본값을 `false`로 변경했다.
- `EditorStubPoseEstimator`의 백엔드명을 `Editor Simulation (not camera tracking)`으로 바꿔 시뮬레이션임을 명확히 했다.
- 시뮬레이션이 꺼진 상태에서 Editor로 실행하면 `No Native Pose Backend`와 실제 추적 미지원 메시지를 HUD에 표시하도록 했다.

### 현재 사용 방법

M1 MacBook Unity Editor에서 할 수 있는 것:

1. `Assets/Scenes/MediaPipeTest.unity`를 연다.
2. Play Mode에서 `Start Camera`를 누른다.
3. 카메라 권한을 허용하고 프리뷰/FPS/HUD가 정상인지 확인한다.
4. 이 상태에서는 실제 관절 추적이 되지 않는 것이 정상이다.

Editor에서 오버레이 UI만 테스트하고 싶을 때:

1. `MediaPipe Test Runtime` 오브젝트를 선택한다.
2. `MediaPipeTestRunner > Simulate Pose When Native Unavailable`을 켠다.
3. Play Mode에서 가짜 skeleton이 표시되는지 확인한다.
4. 이 skeleton은 카메라 화면을 분석하지 않으며 몸을 추적하지 않는다.

실제 관절 추적을 테스트할 때:

1. M1 MacBook에서 iOS Build Support와 Xcode를 준비한다.
2. Unity Build Profile을 iOS로 전환한다.
3. iPhone XS Max를 연결하고 Xcode에서 실행한다.
4. iOS 실기기 빌드에서 `IOSMediaPipePoseEstimator`가 활성화되어 실제 MediaPipe 추론을 수행한다.

### M1 MacBook Editor에서 실제 추적을 하려면 필요한 추가 작업

MacBook 내장 카메라로 Unity Editor에서 실제 관절 추적까지 하려면 별도 Editor용 MediaPipe 백엔드가 필요하다. 이후 섹션 17에서 Python MediaPipe 워커 백엔드를 추가했다.

가능한 구현 방향:

- MediaPipe Unity 플러그인을 도입해 macOS Apple Silicon 네이티브 바이너리와 C# adapter를 연결한다.
- MediaPipe Tasks Vision macOS 네이티브 플러그인을 직접 만들고 `IPoseEstimator` 구현체를 추가한다.
- 빠른 검증만 필요하면 Python/OpenCV/MediaPipe를 외부 프로세스로 띄우는 프로토타입을 만들 수 있지만, Unity 실시간 앱 구조로는 최종 권장 방식이 아니다.

우선순위:

1. 현재는 M1 Mac Editor에서 카메라 프리뷰/HUD만 검증한다.
2. 실제 관절 추적은 iPhone iOS 실기기 빌드로 검증한다.
3. Mac Editor에서도 실제 추적이 필요하면 섹션 17의 Python MediaPipe Editor 백엔드를 우선 사용하고, 제품화 단계에서는 네이티브 macOS 플러그인 또는 검증된 Unity MediaPipe 플러그인을 검토한다.

## 16. 2026-06-29 계획 대비 구현 보강 기록

### 반영한 계획 항목

이번 수정은 `TestMediaPipeplan`의 MVP 테스트 범위 중 아래 항목을 코드에 더 명확히 반영했다.

- PBI-018: 주요 관절 visibility/presence 기준 충족 시에만 시작 허용
- PBI-019: 발목 누락, 상체 잘림, 조명 부족/낮은 confidence 안내
- PBI-021: 2~3초 캘리브레이션
- PBI-024: 카메라 미러링, 회전, 전/후면 모드 확인
- PBI-029: visibility 낮은 프레임 판정 제외
- PBI-030: 카메라 표시 FPS와 포즈 추론 FPS 분리
- PBI-031: FPS, 메모리, 오류 코드 중심의 QA 진단 로그
- PBI-052: 원본 영상 없이 좌표/성능 로그만 사용

### 코드 변경

- `PoseQualityGate`가 visibility만 보던 구조에서 visibility, presence, 화면 경계 범위를 함께 평가하도록 변경했다.
- `PoseQualityReport`에 `averagePresence`, `trackableRequiredLandmarks`를 추가했다.
- 상체/하체 필수 관절이 낮은 confidence이거나 화면 밖에 가까우면 `UpperBodyMissing`/`LowerBodyMissing`으로 분류한다.
- `MediaPipeTestRunner`에 `requiredPresence`, `landmarkEdgeMargin`, `calibrationSeconds` 설정을 추가했다.
- 포즈가 `READY`가 되더라도 `calibrationSeconds` 동안 유지되어야 HUD 상태가 `Ready`로 바뀌도록 했다.
- `CameraPreviewController`에 현재/선호 카메라 방향 상태와 전/후면 선호 전환 기능을 추가했다.
- 테스트 툴바에 `Switch Camera` 버튼을 추가했다.
- `PoseDebugHud`에 timestamp, source size, camera mode, average presence, visible/trackable required count를 표시하도록 했다.
- `MediaPipeQaLogger`를 추가해 `Application.persistentDataPath/mediapipe_pose_qa.jsonl`에 QA 로그를 JSONL로 저장한다.
- QA 로그는 원본 영상, 이미지 프레임, 얼굴/신체 이미지를 저장하지 않는다.
- iOS Swift 브릿지에서 MediaPipe landmark의 `visibility` 또는 `presence`가 nil일 때 서로 fallback하도록 수정했다.

### M1 MacBook에서 확인할 것

Unity Editor:

1. `Assets/Scenes/MediaPipeTest.unity`를 연다.
2. Play Mode에서 `Start Camera`를 누른다.
3. HUD에 카메라 FPS, 장치명, backend 상태가 표시되는지 확인한다.
4. `Switch Camera` 버튼으로 카메라 선호 방향이 바뀌는지 확인한다.
5. Python MediaPipe 환경이 준비되어 있으면 `Editor Python MediaPipe` 백엔드가 실제 관절 추적을 수행한다.
6. Python 환경이 없거나 실패하면 HUD에 설치 안내 오류가 표시된다.
7. 오버레이 UI만 확인하려면 Inspector에서 `Use Python MediaPipe In Editor`를 끄고 `Simulate Pose When Native Unavailable`을 켠다.

iPhone 실기기:

1. M1 MacBook에서 iOS Build Support와 Xcode를 준비한다.
2. Unity를 iOS Build Profile로 전환하고 빌드한다.
3. Xcode에서 `pod install`이 성공했는지 확인한다.
4. iPhone XS Max에서 실행한다.
5. 사람이 화면에 들어오면 landmark count 33, timestamp 증가, quality state 변화를 확인한다.
6. `Ready` 상태가 2초 캘리브레이션 후 표시되는지 확인한다.
7. 발목이나 상체를 화면 밖으로 빼면 `LowerBodyMissing` 또는 `UpperBodyMissing`이 나오는지 확인한다.
8. 앱 실행 후 QA 로그에 원본 영상 없이 JSONL 상태 로그만 남는지 확인한다.

### 남은 구현 항목

- M1 MacBook Unity Editor의 실제 추론은 Python MediaPipe 워커 백엔드로 우선 지원한다.
- 현재 저장소에는 iOS 실기기용 MediaPipe 브릿지와 Editor Python MediaPipe 백엔드가 있다.
- iOS에서 실제 빌드 후 Swift/Pod 의존성, 카메라 orientation, landmark 좌우 매핑을 실기기로 검증해야 한다.

## 17. 2026-06-29 Unity Editor 실제 MediaPipe 추적 추가 기록

### 목표

M1 MacBook Unity Editor에서도 내장 카메라 프리뷰 위에 실제 MediaPipe Pose landmark가 따라오도록 Editor 전용 백엔드를 추가한다.

### 구현 방식

- Unity C#이 카메라 프레임 RGBA 데이터를 Python 워커 프로세스 stdin으로 전달한다.
- Python 워커는 `mediapipe` Python 패키지의 Pose 추론을 실행한다.
- Python 워커는 33개 landmark와 world landmark를 `LandmarkFrame` JSON으로 stdout에 반환한다.
- 원본 영상 파일, 이미지 프레임, mp4는 저장하지 않는다.
- iOS 실기기 빌드는 기존 `IOSMediaPipePoseEstimator`와 Swift bridge를 그대로 사용한다.

### 코드 변경

- `EditorPythonMediaPipePoseEstimator.cs`를 추가했다.
- `Assets/StreamingAssets/MediaPipe/editor_pose_worker.py`를 추가했다.
- `PoseEstimatorSettings`에 Editor Python 백엔드 설정을 추가했다.
- `PoseEstimatorFactory`가 Unity Editor에서는 `EditorPythonMediaPipePoseEstimator`를 우선 생성하도록 변경했다.
- `MediaPipeTestRunner`에 `Use Python MediaPipe In Editor`, `Editor Python Executable Path`, `Editor Python Worker Relative Path` 설정을 추가했다.
- `Assets/Scenes/MediaPipeTest.unity` 기본값을 Python Editor 백엔드 사용으로 설정했다.

### M1 MacBook 준비

Mac 터미널에서 Python 환경을 준비한다.

```bash
python3 -m venv .venv-mediapipe
source .venv-mediapipe/bin/activate
python -m pip install --upgrade pip
python -m pip install mediapipe numpy
```

Unity에서 설정한다.

1. `Assets/Scenes/MediaPipeTest.unity`를 연다.
2. `MediaPipe Test Runtime` 오브젝트를 선택한다.
3. `MediaPipeTestRunner > Use Python MediaPipe In Editor`가 켜져 있는지 확인한다.
4. venv를 사용했다면 `Editor Python Executable Path`에 `.venv-mediapipe/bin/python`의 절대 경로를 넣는다.
5. Play Mode에서 `Start Camera`를 누른다.
6. HUD의 `Backend`가 `Editor Python MediaPipe`인지 확인한다.
7. 전신이 카메라에 보이도록 물러서서 landmark count가 33으로 바뀌고 skeleton이 몸을 따라오는지 확인한다.

### 실패 시 확인

- `PYTHON_IMPORT_FAILED`: `mediapipe` 또는 `numpy`가 설치되지 않은 Python을 사용 중이다.
- `Editor MediaPipe worker was not found`: `editor_pose_worker.py` 경로가 잘못되었거나 StreamingAssets가 누락되었다.
- `NO_POSE`: 카메라에는 들어오지만 사람이 충분히 보이지 않는다. 전신이 보이도록 거리와 조명을 조정한다.
- `EDITOR_MEDIAPIPE_TIMEOUT`: Python 워커가 너무 느리거나 중단되었다. `targetPoseFps`를 10 이하로 낮춰 확인한다.

### 남은 검증

- 현재 작업 PC는 Windows라 M1 Mac에서 Python MediaPipe 실제 추론은 직접 실행 검증하지 못했다.
- M1 Mac에서 Python 패키지 설치 후 Play Mode로 실기 카메라 추적을 확인해야 한다.
- 성능이 낮으면 `targetPoseFps`를 10~15 사이로 조정한다.

## 18. 2026-06-29 Editor Python MediaPipe 관절 미표시 수정 기록

### 확인한 가능 원인

- Unity `WebCamTexture.GetPixels32()` 픽셀 배열은 일반 이미지 처리 라이브러리가 기대하는 행 방향과 다를 수 있다.
- Python 워커가 세로로 뒤집힌 프레임을 MediaPipe에 전달하면 사람을 찾지 못해 `NO_POSE`가 계속 나올 수 있다.
- 프로젝트 루트에 `.venv-mediapipe`를 만들었어도 Unity Inspector에 Python 경로를 넣지 않으면 시스템 Python을 먼저 사용해 `mediapipe` import가 실패할 수 있다.
- Python MediaPipe의 landmark `presence` 값이 0으로 들어오면 visibility가 정상이어도 QualityGate가 `Ready`로 가지 못할 수 있다.

### 코드 변경

- `EditorPythonMediaPipePoseEstimator`가 프로젝트 루트의 `.venv-mediapipe/bin/python`과 `.venv/bin/python`을 먼저 자동 탐지하도록 했다.
- Python 워커 시작 실패와 timeout 시 Python 실행 파일, exit code, stderr를 HUD 오류 메시지에 포함하도록 했다.
- Python stdout에 JSON이 아닌 로그가 섞여도 JSON 라인을 찾아 읽도록 보강했다.
- `editor_pose_worker.py`에서 원본, 상하반전, 좌우반전, 180도 회전, 90도 회전 후보를 자동으로 시도하도록 했다.
- 감지에 성공한 이미지 방향은 다음 프레임에서 우선 시도해 추적이 안정되도록 했다.
- 성공한 방향의 landmark 좌표를 Unity 프리뷰 좌표계로 다시 변환해 overlay 위치가 맞도록 했다.
- Python landmark의 `presence`가 0이면 `visibility`로 fallback하도록 했다.
- Editor 테스트 기본 `minPoseDetectionConfidence`를 `0.35`로 낮춰 초기 감지 성공률을 올렸다.

### 다시 테스트하는 순서

1. Mac 터미널에서 프로젝트 루트로 이동한다.
2. 아래 명령으로 venv와 패키지를 확인한다.

```bash
source .venv-mediapipe/bin/activate
python -c "import mediapipe, numpy; print('mediapipe ok')"
```

3. Unity를 완전히 재시작한다.
4. `Assets/Scenes/MediaPipeTest.unity`를 연다.
5. `MediaPipe Test Runtime > Editor Python Executable Path`는 비워두거나 `.venv-mediapipe/bin/python` 절대 경로를 넣는다.
6. Play Mode에서 `Start Camera`를 누른다.
7. HUD의 `Backend`가 `Editor Python MediaPipe`인지 확인한다.
8. 전신이 화면에 들어오도록 카메라에서 1.5~2.5m 정도 떨어진다.
9. `Landmarks: 33`이 나오는지 확인한다.

### 여전히 안 나오면 확인할 HUD 값

- `Backend`가 `Editor Python MediaPipe`가 아니면 Python 백엔드가 선택되지 않은 것이다.
- `Error`에 `PYTHON_IMPORT_FAILED`가 나오면 Unity가 사용하는 Python에 `mediapipe numpy`가 설치되지 않은 것이다.
- `Error`에 `NO_POSE`만 나오면 Python은 정상이고, 카메라 거리/조명/프레임 방향 문제다.
- `Source`가 `0x0`이거나 카메라 FPS가 0이면 카메라 프레임이 아직 준비되지 않은 것이다.

### 전신 여부

- 예시 이미지처럼 머리부터 발목/발끝까지 전신이 들어오면 가장 안정적으로 추적된다.
- 상반신만 보여도 일부 landmark는 잡힐 수 있지만, 스쿼트/런지처럼 무릎과 발목이 필요한 동작은 하체가 잘리면 `LowerBodyMissing`이 된다.
- 초록색 skeleton이 아예 안 나오고 `Landmarks: 0`이면 전신 문제만이 아니라 Python 백엔드, 카메라 프레임 방향, 조명, 거리 문제를 먼저 확인한다.
