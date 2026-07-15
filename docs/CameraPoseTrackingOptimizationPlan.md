# 카메라 관절 추적 및 TTS 재생 성능 최적화 Plan

작성일: 2026-07-15

상태: 1차 구현 반영 — 실기기 성능·안정성 검증 대기

대상: Unity iOS 카메라 프리뷰, MediaPipe Pose 추론, 실시간 자세 피드백 및 TTS 재생 경로

> 이 문서는 최적화 방향을 결정하기 위해 작성되었으며, 2026-07-15에 저위험 개선과 iOS 비동기 Pose/TTS 안정화 항목의 1차 코드가 반영되었다. 최종 수치와 출시 판정은 iPhone 실기기 계측 후 확정한다.

구현 반영 범위:

- TTS `active 1 + pending 1`, 우선순위·TTL·중복 병합·세션 generation 적용
- Pose 피드백 콜백과 실제 네이티브 TTS 시작 분리
- iOS TTS의 문장별 shared `AVAudioSession` 재설정 제거 및 시스템 관리 speech session 적용
- MediaPipe `.liveStream + detectAsync` 전환, single-flight와 취소 generation 적용
- 동일 카메라 프레임 재추론 방지, 관절 조회·telemetry·컬렉션 할당 비용 축소
- START/STOP, 카메라 전환, 앱 pause/resume에서 Pose와 TTS 요청 정리

## 0. 결론

현재 버벅임은 카메라 해상도나 Pose FPS 하나만의 문제가 아니라, 다음 경로가 Unity 메인 스레드에서 동기적으로 이어지는 구조가 1순위 원인 후보다.

```text
WebCamTexture
  -> GetPixels32 전체 프레임 읽기
  -> C#에서 Swift P/Invoke 호출
  -> MediaPipe 동기 detect
  -> Swift Dictionary/JSON 생성
  -> C# 문자열 복사/JSON 파싱
  -> 33개 관절 객체 생성
  -> 피드백 분석/로그/UI 갱신
```

TTS가 재생될 때 앱이 사실상 멈추는 현상은 아래 두 번째 동기 경로가 같은 Pose 결과 프레임에 추가되는 문제로 봐야 한다.

```text
Pose 결과의 피드백 선택
  -> PoseFeedbackJsonReceiver
  -> CoachTtsController
  -> iOS TTS P/Invoke
  -> AVAudioSession category 변경
  -> AVAudioSession setActive:YES
  -> 기존 발화 즉시 취소
  -> 한국어 voice 조회/utterance 생성
  -> AVSpeechSynthesizer 재생 시작
```

현재 iOS bridge는 매 문장마다 앱 전체가 공유하는 `AVAudioSession`을 재구성하고, 발화 종료·취소 때 다시 비활성화한다. 새 피드백마다 기존 음성을 즉시 취소하므로 요청이 몰리면 audio route 전환과 cancel/restart가 반복될 수 있다. 이 경로에는 오류와 소요 시간 기록도 없어서 실제 audio-session stall, 첫 voice cold load, 반복 취소, lifecycle race를 현재 로그만으로 구분하기 어렵다.

앱이 30 FPS라면 한 프레임 예산은 약 33.3 ms다. Pose 추론을 8 FPS로 제한해도 한 번의 동기 처리가 33.3 ms를 넘으면 초당 최대 8번의 눈에 띄는 멈춤이 생길 수 있다. 따라서 FPS만 더 낮추는 방법은 임시 완화책일 뿐 근본 해결책이 아니다.

TTS의 `setCategory/setActive`까지 같은 프레임에 겹치면 Pose만 실행했을 때보다 더 긴 frame이 생긴다. 직접적인 `dispatch_sync` 교착이나 Pose/TTS lock의 역순 획득은 정적 코드에서 확인되지 않았으므로, “영구 정지”와 “수백 ms 이상의 main-thread stall이 연속 발생해 정지처럼 보이는 상태”는 실기기 stack capture로 구분해야 한다.

권장 순서는 다음과 같다.

1. 실제 iPhone에서 Pose와 TTS 각 구간의 p50/p95/p99, audio route event, 멈춘 순간의 main-thread stack을 먼저 수집한다.
2. TTS OFF, startup TTS OFF, LogOnly, 현재 native TTS를 비교해 TTS가 만드는 추가 stall을 분리한다.
3. 모든 음성 producer 앞에 bounded priority scheduler를 두고 오래된 안내, 중복 안내, 반복 취소를 차단한다.
4. iOS TTS는 시스템 관리 speech session과 앱 전역 audio-session coordinator를 A/B한 뒤 하나만 선택한다.
5. 새 카메라 프레임만 처리하고, 성능 설정과 처리 주기를 단일화하는 저위험 Pose 개선을 적용한다.
6. Swift MediaPipe를 `.liveStream + detectAsync`로 전환하고 최신 프레임 우선의 bounded pipeline을 만든다.
7. 실측상 필요할 때 JSON bridge, 추론 입력 해상도, native capture를 순서대로 검토한다.
8. Pose와 TTS를 동시에 실행하는 장시간·생명주기 회귀 테스트 후 단계 배포한다.

TTS 재생 시 전체 정지는 출시 차단 수준의 문제로 취급한다. 따라서 TTS 원인 격리와 audio-session/scheduler 안정화는 선택적 JSON·capture 최적화보다 먼저 진행한다. Pose 쪽 핵심은 추론을 Unity 메인 스레드에서 분리하는 것이고, TTS 쪽 핵심은 Pose hot path에서 native 음성 시작을 직접 호출하지 않으며 공유 audio session을 매 문장 재설정하지 않는 것이다.

---

## 1. 어째서 이 기능을 만들어야 했는가

### 1.1 제품 관점의 필요성

카메라 관절 추적은 단순한 화면 효과가 아니라 현재 운동 코칭 흐름의 입력 장치다.

- 별도 센서 없이 휴대폰 카메라 한 대로 사용자의 자세를 추정한다.
- MediaPipe의 관절 좌표로 스켈레톤을 표시하고 전신 인식 상태를 안내한다.
- 관절 각도와 움직임을 이용해 운동 단계, 반복 횟수, 자세 오류를 판정한다.
- 화면을 계속 보지 않아도 안전 경고, 자세 교정, 반복 횟수를 들을 수 있도록 TTS 피드백을 제공한다.
- 실시간 화면 및 음성 피드백과 운동 종료 후 세션 요약·리플레이의 근거를 만든다.
- 원본 영상을 서버로 보내지 않고 온디바이스에서 처리해 오프라인 사용성과 개인정보 보호를 유지한다.

관절 추적이 없다면 현재의 자세 기반 피드백, 반복 판정, 오버레이, 좌표 기반 리플레이가 모두 약해지거나 별도의 웨어러블 센서가 필요하다.

### 1.2 왜 성능 최적화가 필요한가

실시간 코칭에서는 평균 FPS보다 긴 한 프레임과 결과의 오래됨이 더 중요하다.

- 화면이 멈추면 사용자는 카메라나 앱이 고장 났다고 느낀다.
- 늦게 도착한 자세 피드백은 이미 끝난 동작을 지적해 코칭 신뢰도를 떨어뜨린다.
- 메인 스레드가 막히면 START/STOP, 카메라 전환, 백그라운드 전환과 같은 생명주기 작업도 겹쳐 크래시 가능성이 커진다.
- TTS가 시작될 때 화면 전체가 멈추면 안전 안내 채널 자체가 운동 추적을 방해하는 역효과가 난다.
- 오래된 음성 안내나 반복적으로 잘리는 문장은 사용자가 현재 자세와 과거 자세를 혼동하게 만든다.
- 반복적인 이미지 변환, JSON, 객체 할당은 GC, 발열, 배터리 소모를 늘린다.
- 저사양 기기에서만 발생하는 버벅임은 평균 개발 기기 테스트로 놓치기 쉽다.

따라서 목표는 “추론 횟수를 무조건 늘리는 것”이 아니라 다음 세 가지를 동시에 만족하는 것이다.

1. UI는 계속 반응한다.
2. Pose 결과는 오래 쌓이지 않고 가능한 최신 상태를 나타낸다.
3. 자세 판정 정확도와 세션 안정성을 유지한다.

### 1.3 이번 계획의 범위

포함:

- iOS 카메라 프레임 취득부터 MediaPipe 결과가 UI·피드백·로그로 전달되기까지의 hot path
- 피드백 admission부터 iOS `AVSpeechSynthesizer`, `AVAudioSession`, 완료·취소 callback까지의 TTS hot path
- 메인 스레드 점유, 프레임 복사, 네이티브 경계, 할당, 처리 주기, 발열과 생명주기
- TTS queue, 우선순위, TTL, audio-session 소유권, 내부/외부 음악 ducking
- 실기기 성능 계측, 정확도 회귀, 단계 배포와 롤백

제외:

- 자세 규칙 자체의 의미 변경
- UI 디자인 전면 개편
- 원본 영상 저장 또는 서버 업로드 도입
- Android의 별도 네이티브 캡처 재설계
- 클라우드 TTS를 실시간 기본 경로로 도입하는 일
- 측정 없이 특정 해상도나 FPS를 최종값으로 확정하는 일

---

## 2. 현재 기능은 어떻게 구현되어 있는가

### 2.1 현재 실행 경로

현재 모바일 UI 기본값은 카메라 640×480, 요청 카메라 20 FPS, Pose 8 FPS, 앱 렌더링 30 FPS다.

| 단계 | 현재 동작 | 코드 근거 |
| --- | --- | --- |
| 카메라 | Unity `WebCamTexture`를 프리뷰와 추론 입력에 함께 사용 | `MobileWorkoutPrototypeView.cs` |
| 샘플링 | `JointTrackingController` 코루틴이 목표 Pose 간격에 맞춰 provider 호출 | `JointTrackingController.cs` |
| 픽셀 취득 | `GetPixels32(reusableArray)`로 전체 RGBA 프레임을 CPU 배열에 읽음 | `MediaPipePoseTrackingProvider.cs` |
| iOS 경계 | C#이 `IOSMediaPipePoseEstimator`를 통해 Swift C ABI를 동기 호출 | `IOSMediaPipePoseEstimator.cs` |
| MediaPipe | Swift에서 lite 모델, 1 pose, segmentation off, GPU delegate, `.video` 모드의 동기 `detect` 사용 | `AHCMediaPipePoseBridge.swift` |
| 결과 전달 | Swift Dictionary를 JSON으로 직렬화하고 C#이 문자열 복사 후 `JsonUtility`로 파싱 | 같은 파일 및 `IOSMediaPipePoseEstimator.cs` |
| 프레임 모델 | 결과마다 33개 `TrackedJoint`, 배열, `JointTrackingFrame`, GUID 생성 | `MediaPipePoseTrackingProvider.cs` |
| 후속 처리 | 같은 호출 흐름에서 안정화, 특징 추출, 규칙 평가, JSONL 로그, UI overlay 갱신 | `RealtimeFeedbackOrchestrator.cs`, `SessionJsonlLogger.cs`, `MobileWorkoutPrototypeView.cs` |
| TTS admission | 선택된 피드백이 별도 queue 없이 `CoachTtsController.TrySpeak`를 즉시 호출 | `PoseFeedbackJsonReceiver.cs`, `CoachTtsController.cs` |
| iOS 음성 | 같은 호출에서 shared audio session을 재설정하고 현재 음성을 취소한 뒤 새 utterance 재생 | `IosNativeTtsService.cs`, `AhcTtsBridge.mm` |

Unity 코루틴은 백그라운드 스레드가 아니다. 코루틴 안에서 동기 P/Invoke를 호출하면 네이티브 추론이 반환될 때까지 해당 Unity 프레임이 진행되지 않는다.

### 2.2 현재 데이터 비용

640×480 RGBA 한 프레임은 다음 크기다.

```text
640 × 480 × 4 bytes = 1,228,800 bytes ≈ 1.17 MiB
```

Pose 8 FPS에서 `GetPixels32`만 계산해도 초당 약 9.38 MiB가 관리 배열 경로를 통과한다. 이 값에는 이미지 wrapper 생성, MediaPipe 내부 처리, JSON, C# 객체 생성, UI와 로그 비용이 포함되지 않는다.

네이티브 결과 경계에서도 다음 작업이 매 성공 결과마다 이어진다.

- Swift landmark Dictionary/Array 생성
- 필요 여부를 재검토해야 하는 world landmark 포함
- `JSONSerialization`, UTF-8 배열과 문자열 생성
- 네이티브 문자열을 C# `StringBuilder`로 복사
- `StringBuilder.ToString()`과 `JsonUtility.FromJson`
- 33개 관절 class 객체, frame 객체, GUID 문자열 생성

### 2.3 이미 적용된 좋은 선택

현재 구현이 전부 비효율적인 것은 아니다. 다음 선택은 유지할 가치가 있다.

- 무거운 모델 대신 `pose_landmarker_lite.task`를 사용한다.
- iOS에서 GPU delegate를 사용한다.
- 한 프레임에서 한 명만 검출하고 segmentation을 끈다.
- 픽셀 배열과 pinning 일부를 재사용한다.
- Pose 처리율을 카메라 프리뷰보다 낮게 분리했다.
- 네이티브 re-entry를 막고, 최근 수정에서 카메라/estimator를 불필요하게 반복 생성하지 않도록 생명주기를 안정화했다.
- 원본 카메라 영상을 원격 서버로 보내지 않는 온디바이스 구조를 유지한다.

이 선택들은 평균 연산량과 크래시 위험을 줄인다. 다만 동기 추론이 메인 스레드를 막는 구조까지 해결하지는 못한다.

### 2.4 현재 설정의 불일치

성능 관련 값이 여러 위치에 나뉘어 있다.

- 모바일 UI 런타임 기본 Pose 속도: 8 FPS
- `RealtimeFeedbackOrchestrator`의 기대 Pose 속도: 15 FPS
- 기존 acceptance evaluator의 최소 Pose 속도: 10 FPS
- 별도 품질 profile에도 해상도와 FPS 값이 존재

분석 window 크기가 기대 FPS를 기준으로 계산되므로 Pose FPS만 낮추면 분석 시간 범위와 피드백 지연까지 바뀔 수 있다. 성능 profile, 실제 sampling rate, 분석 window, QA 기준은 하나의 설정 원천을 사용하거나 window를 시간 기준으로 계산해야 한다.

### 2.5 현재 운동 화면의 TTS 실행 경로

`Main.unity`는 `CoachTtsController`의 backend를 `Auto`로 두며, iPhone에서는 `IosNativeTtsService`와 `AVSpeechSynthesizer`가 선택된다. `speakOnStart`도 켜져 있어 첫 화면 초기화 중 첫 음성 준비 비용이 발생한다.

```text
JointTrackingController.ReceiveTrackingFrame
  -> RealtimeFeedbackOrchestrator.HandleTrackingFrame
  -> PoseFeedbackJsonReceiver.ReceiveFeedback
  -> CoachTtsController.TrySpeak
  -> IosNativeTtsService.AhcTtsSpeak
  -> AhcTtsBridge.mm
  -> AVAudioSession / AVSpeechSynthesizer
```

현재 동작:

1. Pose 결과 subscriber 안에서 TTS P/Invoke를 즉시 호출한다.
2. iOS main thread에서 호출되면 native block을 같은 frame에서 바로 실행한다.
3. 매 문장마다 shared `AVAudioSession`을 `Playback + SpokenAudio + MixWithOthers + DuckOthers`로 설정한다.
4. 매 문장마다 `setActive:YES`를 호출한다.
5. 현재 발화를 `AVSpeechBoundaryImmediate`로 중단해 내장 utterance queue를 비운다.
6. 매번 `ko-KR` voice를 조회하고 utterance를 만든 뒤 재생한다.
7. 완료 또는 취소 callback에서 shared audio session을 `setActive:NO`로 비활성화한다.

구조상 문제:

- `AVSpeechSynthesizer` 자체는 utterance queue를 제공하지만 현재 bridge는 새 요청마다 먼저 즉시 stop하므로 실질적으로 queue flush 방식이다.
- `PoseFeedbackJsonReceiver`의 cooldown은 같은 ID/text만 막는다. 서로 다른 경고와 매번 ID가 달라지는 `correct_rep_N` 안내는 통과할 수 있다.
- 일반 자세 피드백의 전역 간격은 1.5초지만, 반복 횟수 안내는 그 경로를 우회할 수 있어 같은 Pose frame에서 두 TTS 요청이 생길 가능성이 있다.
- C#에는 `Enqueued/Started/Finished/Canceled/Failed` 상태와 완료 event가 없어서 재생 중 요청을 drop, merge, replace, interrupt하는 정책을 적용할 수 없다.
- `setCategory`, `setActive`, deactivate 오류를 모두 버리며, background thread에서 요청하면 실제 native block이 실행되기 전에 성공을 반환할 수 있다.
- finish/cancel callback과 shutdown이 main queue에 늦게 등록한 deactivate가 새 발화 이후 실행될 수 있지만 utterance/request generation으로 구분하지 않는다.
- synthesizer는 process-global static이지만 C# controller별 owner/refcount가 없어 중복 controller나 scene 전환 시 한 owner가 다른 owner의 engine을 종료할 수 있다.

현재 iPhone 운동 화면 TTS 경로에는 앱 네트워크 요청, 파일 I/O, 음성 길이만큼 기다리는 busy-wait, Unity `AudioClip` 생성이 없다. `TtsAudioDuckingController.CreateTestMusicClip`의 큰 배열과 sine loop는 TTS demo 전용이며 `Main.unity`의 원인으로 보면 안 된다.

Editor backend는 별도 문제다.

- macOS `say` backend는 첫 한국어 voice 검색에서 동기 `ReadToEnd`를 먼저 호출한 뒤 1초 `WaitForExit`를 호출한다. `ReadToEnd` 자체가 process 종료까지 막을 수 있으므로 전체 대기의 1초 상한을 보장하지 않는다.
- macOS/Windows process backend는 요청마다 짧은 `WaitForExit`를 수행한다.

따라서 iPhone 실기기의 audio-session stall과 macOS/Windows Editor의 process wait를 같은 원인으로 합치지 않고 별도 계측해야 한다.

---

## 3. 현재 구현의 장점과 단점

| 관점 | 장점 | 단점 |
| --- | --- | --- |
| 구조 | 입력 한 건이 결과 한 건으로 순서대로 끝나 이해와 디버깅이 쉽다 | 이미지 취득부터 결과 처리까지 한 긴 동기 체인이 된다 |
| 카메라 | Unity 카메라 하나로 프리뷰와 추론을 공유한다 | `GetPixels32` CPU readback과 전체 프레임 복사가 필요하다 |
| MediaPipe | lite + GPU + video tracking으로 평균 연산량을 줄였다 | 동기 `detect`가 호출 스레드를 막고 GPU가 Unity 렌더와 경쟁할 수 있다 |
| 네이티브 경계 | JSON은 C#/Swift 양쪽에서 읽기 쉽고 초기에 연결하기 빠르다 | Dictionary, serialization, 문자열 복사, parse, GC 비용이 반복된다 |
| 처리 순서 | 결과가 같은 프레임에서 즉시 UI와 분석에 반영된다 | UI, 분석, 로그 중 하나가 느려도 전체 메인 스레드가 같이 늦어진다 |
| 안정성 | single-flight와 lock으로 native re-entry를 막기 쉽다 | 비동기 확장 없이 처리량을 올리기 어렵고 긴 호출이 생명주기와 겹친다 |
| 개인정보 | 온디바이스·오프라인 처리가 가능하다 | 단말 CPU/GPU, 메모리, 발열 비용을 직접 감당해야 한다 |

현재 방식은 기능을 빠르게 검증하고 오류를 추적하는 초기 구현으로는 합리적이었다. 그러나 실시간 제품 경로에서는 “구현 단순성”보다 “메인 스레드 비차단, 최신성, 명확한 버퍼 소유권”이 더 중요해지는 단계다.

### 3.1 현재 TTS 구현의 장점과 단점

| 관점 | 장점 | 단점 |
| --- | --- | --- |
| 음성 engine | OS 기본 한국어 음성을 사용해 모델·서버·음성 파일이 필요 없다 | 첫 voice 준비 비용과 OS/기기별 지연·품질 차이가 있다 |
| 개인정보 | 코칭 문장을 외부 서버로 보내지 않는다 | 단말의 audio session과 합성 engine 상태를 직접 관리해야 한다 |
| 구현 | 하나의 persistent synthesizer와 delegate로 기본 재생·완료·취소를 구현했다 | C#까지 완료 상태가 전달되지 않고 실제 시작 실패가 성공처럼 보일 수 있다 |
| 새 피드백 | 새 안내를 즉시 들려주기 위해 기존 안내를 바로 중단한다 | 모든 안내가 queue flush가 되어 반복 취소와 audio route churn이 발생한다 |
| 외부 음악 | `MixWithOthers + DuckOthers`로 다른 session의 음악을 낮출 수 있다 | shared app session을 문장마다 바꾸고 끄므로 Unity audio 소유권과 충돌할 수 있다 |
| 생명주기 | 발화가 끝나면 audio session을 비활성화해 다른 앱에 돌려준다 | 늦은 finish/cancel/shutdown callback이 새 발화의 session까지 끌 수 있다 |

현재 운동 화면은 demo의 `TtsAudioDuckingController`를 사용하지 않는다. 따라서 Unity 내부 음악 ducking과 외부 앱 음악 ducking은 별도 요구사항으로 설계해야 한다.

---

## 4. 병목 진단과 확인해야 할 가설

### 4.1 코드로 확인된 사실

- iOS에서 Swift bridge 경로가 활성화되어 있다.
- `.video` 모드의 `detect(videoFrame:timestampInMilliseconds:)`가 동기 호출된다.
- 이 호출은 Unity 코루틴의 provider 호출 안에서 실행된다.
- 매 Pose 샘플에 `GetPixels32`가 실행된다.
- 결과는 JSON 경계를 지나고, provider 원본 frame/joint 객체가 새로 생성된다.
- 결과 subscriber의 분석, 로그, UI 갱신이 같은 메인 스레드에서 순서대로 실행된다.
- 현재 성능 수집은 평균 FPS와 평균 inference 중심이라 p95/p99 긴 프레임과 단계별 비용을 충분히 보여 주지 못한다.
- TTS 요청은 Pose 결과 처리 호출 안에서 native bridge까지 inline으로 이어진다.
- iOS main thread에서는 `AVAudioSession.setCategory`, `setActive:YES`, 현재 발화 stop, voice/utterance 생성, speak submit이 같은 frame에서 실행된다.
- 현재 bridge는 발화마다 shared audio session을 구성·활성화하고 finish/cancel 때 비활성화한다.
- native audio-session 오류와 각 단계 소요 시간은 기록하지 않는다.
- 애플리케이션 전체 TTS queue, 우선순위, TTL, request generation, C# 완료 event가 없다.
- `Main.unity`의 startup TTS가 켜져 있다.

### 4.2 아직 실기기 측정으로 확정해야 할 내용

- 한 번의 버벅임에서 MediaPipe detect가 차지하는 정확한 비율
- `GetPixels32`, 이미지 wrapper/회전, JSON, C# 객체 생성, 로그, overlay 각각의 p95 비용
- GPU delegate가 iPhone XS Max 등 구형 기기에서 Unity 렌더와 경쟁하는 정도
- 해상도를 낮췄을 때 작은 관절과 원거리 전신 자세의 정확도 변화
- 장시간 사용 시 native/GPU memory, thermal throttling, 배터리 변화
- 회전된 `MPImage` 변환이 기기별로 만드는 추가 복사 비용
- TTS 멈춤에서 `setCategory`, `setActive`, voice cold load, speak submit 중 무엇이 가장 오래 걸리는지
- “완전 정지”가 audio route 변경의 긴 main-thread stall인지, 반복 cancel/activate인지, 별도 native hang인지
- system-managed speech session이 Unity audio, 외부 음악, Bluetooth route에서 수동 session보다 안정적인지
- TTS 재생 중 Pose result gap과 camera→overlay latency가 얼마나 증가하는지
- delayed finish/cancel/deactivate race가 실제 기기에서 audio session stuck을 만드는지

따라서 “동기 메인 스레드 체인”은 구조상 1순위 병목이지만, 하위 단계의 우선순위는 P0 계측 뒤 결정해야 한다.

### 4.3 최적화 원칙

1. 평균 처리량보다 UI 긴 프레임과 결과 age를 먼저 줄인다.
2. 처리할 수 없는 과거 프레임은 쌓지 않고 최신 프레임을 우선한다.
3. Unity API는 Unity 메인 스레드에서만 사용한다.
4. 비동기 입력과 결과의 소유권을 명시한다.
5. 정확도와 안전 피드백 회귀를 성능 향상과 같은 수준으로 검사한다.
6. 큰 재작성은 앞 단계 실측이 필요성을 증명할 때만 진행한다.
7. 음성 요청도 모든 frame을 보존하지 않고 우선순위와 TTL에 따라 최신 유효 안내만 남긴다.
8. shared `AVAudioSession`의 owner는 하나만 두며 TTS plugin이 Unity audio와 경쟁해 임의로 끄지 않게 한다.
9. TTS queue와 MediaPipe frame queue는 목적과 생명주기가 다르므로 절대 공유하지 않는다.

---

## 5. 최적화 목표와 비목표

### 5.1 목표

- Pose 추론이 길어져도 UI, 카메라 프리뷰, 버튼 입력이 직접 멈추지 않는다.
- 대기열이 무한히 늘지 않고 결과가 현재 동작에 가깝다.
- standard 기기에서 최소 10 FPS, low profile에서 최소 8 FPS의 유효 Pose 결과를 목표로 한다.
- 프레임별 JSON/객체 할당과 로그·UI spike를 단계적으로 줄인다.
- START/STOP, 카메라 전환, background/foreground에서 stale callback과 use-after-free가 없다.
- Pose callback은 TTS 의도만 enqueue하고 audio-session 작업을 직접 실행하지 않는다.
- TTS는 active 1개와 bounded pending만 유지하며 오래된 자세 안내를 재생하지 않는다.
- TTS 시작·완료·취소·실패가 request ID와 generation으로 C#까지 관찰된다.
- 발화 전후 Unity audio, 외부 audio, camera/Pose 상태가 정상적으로 복구된다.
- 기존 33관절 기반 규칙과 반복 판정의 의미를 유지한다.
- iOS 15 최소 지원과 온디바이스 개인정보 원칙을 유지한다.

### 5.2 비목표

- Pose FPS를 카메라 또는 UI FPS와 같게 만드는 것
- 모든 프레임을 반드시 처리하는 것
- 측정 전에 AVFoundation 전체 재작성부터 시작하는 것
- 화면을 부드럽게 보이게 하려고 분석 데이터 자체를 과도하게 보간하는 것
- 성능을 위해 안전 관련 자세 오류를 누락시키는 것
- 최종 제품에서 TTS를 완전히 끄는 것으로 정지 문제를 숨기는 것
- 모든 Info/반복 횟수 안내를 무제한 queue에 보존하는 것
- demo 전용 `AudioClip` 생성 코드를 iPhone 운동 화면의 원인으로 단정하는 것

---

## 6. 권장 목표 구조

```text
                         +--> Unity camera preview (20~30 FPS)
Unity new camera frame --+
                         +--> fresh-frame gate
                               -> 1 in-flight + replaceable latest pending 1
                               -> native-owned input buffer
                               -> Swift MediaPipe liveStream / detectAsync
                               -> serial result callback
                               -> thread-safe latest-result slot
                               -> Unity Update poll
                                  +-> pose analysis (8~12 FPS)
                                      -> feedback intent only
                                      -> bounded TTS scheduler
                                  +-> skeleton render (UI FPS, visual interpolation only)
                                  +-> text HUD (about 5 Hz)
                                  +-> bounded background log writer (3~5 Hz)
```

### 6.1 비동기 최신 프레임 우선 처리

Swift Pose Landmarker를 `.liveStream`과 `detectAsync` 기반으로 전환한다. Google의 iOS 가이드와 공식 샘플도 카메라 live stream에서 비동기 submit과 delegate 결과 전달을 사용한다.

권장 규칙:

1. MediaPipe에 submit되어 처리 중인 입력은 최대 1개다.
2. 처리 중 새 프레임이 오면 과거 대기열을 쌓지 않는다.
3. 애플리케이션 pending 슬롯은 최대 1개이며, 새 프레임이 오면 기존 pending을 최신 프레임으로 교체한다. 즉 소유 중인 작업은 `in-flight 1 + pending 1`을 넘지 않는다.
4. 결과 callback은 Unity API를 호출하지 않고 native latest-result 슬롯만 갱신한다.
5. Unity 메인 스레드는 `Update`에서 완료된 최신 결과를 poll한다.
6. MediaPipe와 result age 계산에는 단조 증가하는 `streamTimestampMs`를 사용하고, 로그 상 실제 시각이 필요하면 `captureUnixMs`를 별도 보관한다.
7. session generation이 다른 늦은 결과는 폐기한다.

MediaPipe live stream도 처리 중인 경우 새 입력을 무시해 지연 누적을 막는 동작을 제공한다. 애플리케이션 경계에서도 capacity를 명확히 제한해 SDK 동작이나 버전 변화와 무관하게 backlog를 통제한다.

### 6.2 비동기 버퍼 소유권

현재의 no-copy/pinned 입력은 동기 `detect`가 반환되면 바로 재사용할 수 있기 때문에 안전하다. 비동기 전환 후 같은 배열을 즉시 덮어쓰면 추론 중 데이터가 바뀌거나 해제되는 경쟁 상태가 생길 수 있다.

1차 안전안:

- 2~3개의 native-owned 32BGRA `CVPixelBuffer` 또는 명확한 lease를 가진 고정 입력 슬롯을 만든다.
- 슬롯 상태를 `Free -> Prepared -> Submitted -> Free`로 관리한다.
- submit이 거절되거나 busy/error/exception으로 접수되지 않으면 즉시 반환하고, 접수된 슬롯은 정상·오류 delegate 완료 뒤 반환한다.
- STOP/cancel/close에서 callback이 보장되지 않는 경우에는 새 제출 차단 → native queue drain/barrier → task 종료 확인 순서로 lease를 회수한다.
- watchdog은 hang을 감지하고 estimator를 비정상 상태로 전환하는 용도이며, native 작업이 끝났는지 확인하지 않은 채 timeout만으로 buffer를 재사용해서는 안 된다.
- 빈 슬롯이 없으면 프레임을 버리고 메인 스레드를 기다리게 하지 않는다.
- 채널 순서, 회전, mirror metadata를 frame과 함께 고정하고 좌표 mapping을 검증한다.

초기에는 안전한 한 번의 복사를 허용하고, 실측에서 복사가 다음 병목으로 확인될 때만 zero-copy를 실험하는 편이 안전하다.

### 6.3 결과 전달

P2-B에서는 기존 JSON을 유지해 비동기 구조 변경과 데이터 포맷 변경을 분리할 수 있다. P2-B가 안정화된 뒤 P3에서 고정 C ABI 결과 buffer를 검토한다.

예상 구조:

```text
ResultHeader
  version
  sessionGeneration
  sequence
  streamTimestampMs
  captureUnixMs
  landmarkCount
  errorCode

Landmark[33]
  x, y, z, visibility, presence
```

33개 × 5개 `float`는 약 660 bytes이며 작은 고정 buffer로 전달할 수 있다. 관절 이름은 매번 보내지 않고 C#의 고정 index mapping을 사용한다. world landmarks가 현재 소비자에게 실제로 필요한지 확인한 뒤, 필요하지 않다면 hot path에서 제외하고 필요하다면 별도 versioned 배열로 둔다.

### 6.4 처리 주기 분리

아래 값은 P0 실측 전의 시작점이며 최종 확정값이 아니다.

| 소비자 | 초기 제안 | 이유 |
| --- | ---: | --- |
| 카메라 프리뷰 | 20~30 FPS | 사용자가 화면 구도를 자연스럽게 확인 |
| Pose 추론 | standard 10~12 FPS, low 8 FPS | 최신성·정확도와 발열의 균형 |
| 자세 분석 | 실제 Pose 결과와 동일한 시간 기준 | 빈 프레임을 분석하지 않고 window 시간 왜곡 방지 |
| skeleton 렌더 | 앱 렌더 FPS | 최근 두 결과를 시각적으로만 제한 보간 |
| 숫자/문자 HUD | 약 5 Hz | 불필요한 문자열·layout 갱신 감소 |
| 세션 로그 | 3~5 Hz 또는 이벤트 기반 | 재생 요구를 지키면서 I/O spike 감소 |

시각 보간 결과는 화면에만 사용하고 반복 횟수나 안전 판정에는 사용하지 않는다.

### 6.5 생명주기 상태 머신

```text
Idle -> Starting -> Running -> Stopping -> Idle
                      |  ^
                      v  |
                    Paused

Idle/Starting/Running/Stopping/Paused
  -> submit 차단 + drain/barrier
  -> Disposed
```

필수 규칙:

- 일반 STOP은 새 submit을 막고 결과 적용은 generation으로 즉시 무효화하되, input lease는 delegate 완료 또는 native drain/barrier 뒤에만 반환한다.
- camera switch는 submit 중단 → 결과 generation 무효화 → in-flight drain → buffer 반환 → 카메라 교체 순서로 실행한다.
- dispose는 native serial queue barrier 뒤 정확히 한 번 실행한다.
- background/foreground도 동일한 상태 머신과 generation 규칙을 사용한다.
- timeout이 발생해도 버퍼를 먼저 해제한 뒤 callback이 도착하는 상태를 허용하지 않는다.
- estimator를 매 START/STOP마다 만들고 없애기보다 앱 수명 동안 warm 상태로 재사용하되, 카메라 권한과 OS interruption은 별도로 처리한다.

### 6.6 TTS 비차단 목표 구조

```text
Pose/RAG/rep feedback producer
  -> SpeechRequest 생성
     {requestId, semanticId, source, severity,
      createdMonotonicMs, ttlMs, sessionGeneration, text}
  -> CoachSpeechScheduler
     +-> active 1
     +-> replaceable pending 1
     +-> duplicate/coalesce/TTL/priority
  -> 비차단 native command admission
  -> persistent AVSpeechSynthesizer
  -> 순서 보존 bounded native event queue
     {Enqueued, Started, Finished, Canceled, Failed, Interrupted}
  -> Unity main thread poll
```

Pose result callback의 역할은 `SpeechRequest`를 짧게 enqueue하는 데서 끝나야 한다. `AVAudioSession` 전환, 발화 중단, voice lookup, speech submit을 같은 Pose frame에서 직접 실행하지 않는다.

#### 6.6.1 Admission과 우선순위

| 종류 | active 발화 처리 | pending 처리 | 만료 정책 |
| --- | --- | --- | --- |
| Critical 안전 경고 | 낮은 우선순위 발화만 한 번 선점; immediate/word boundary는 제품 검증 후 결정 | 같은 semantic ID는 최신 하나로 합침 | 짧은 TTL, 만료 전 반드시 admission 결과 기록 |
| Warning 자세 교정 | 기본적으로 현재 문장을 끝내고 다음 재생 | 기존 낮은 우선순위 pending을 교체 | 동작이 끝난 오래된 안내는 폐기 |
| Info/반복 횟수 | 재생 중이면 선점하지 않음 | 최신 횟수 하나로 coalesce하거나 drop | 짧은 TTL |
| startup/안내 | camera/model warm-up과 겹치지 않게 예약 | 사용자 운동 시작 전 한 번만 | 준비 상태가 지나면 drop 가능 |

모든 producer는 같은 scheduler를 거쳐야 한다. receiver의 ID별 cooldown과 orchestrator의 일반 피드백 간격만으로는 반복 횟수, startup, 안전 경고를 함께 조절할 수 없다.

queue 상한은 처음에는 `active 1 + pending 1`로 둔다. 더 큰 queue는 처리량을 늘리지 않고 오래된 자세 안내의 지연만 키울 가능성이 높다.

Critical 선점은 다음 handshake를 지켜야 한다.

1. Critical request를 preempt pending 슬롯에 먼저 보관한다.
2. scheduler 상태를 `StopRequested(activeRequestId)`로 바꾼다.
3. 현재 active 발화에 stop을 정확히 한 번만 요청한다.
4. 해당 active request ID의 `didCancel` 또는 terminal error를 확인한다.
5. Critical의 TTL/generation을 다시 확인한 뒤 새 발화를 submit한다.

cancel 대기 중 추가 요청은 pending에서 coalesce한다. 같은 active request에 stop을 반복 호출하거나 cancel 확인 전에 새 utterance를 submit하면 cancel/restart 폭주가 다시 생길 수 있다. cancel terminal이 제한 시간 안에 오지 않으면 backend를 unhealthy로 격리하고 text/color/haptic 안전 채널을 유지하며, 동시 발화를 강제로 시작하지 않는다.

TTL과 generation은 admission 때만 검사하지 않는다.

- native dispatch 직전
- active terminal 뒤 pending 승격 시
- foreground/session resume 시
- 운동 phase 또는 workout session 변경 시

각 지점에서 다시 검사한다. session/phase가 끝난 뒤 이미 말하고 있는 Info/Warning은 제품 정책에 따라 word boundary에서 한 번 중단하고, Critical은 명시적 supersede 또는 session stop 전까지 임의로 끊지 않는다. 만료·실패한 Critical은 즉시 text/color/haptic fallback으로 표시하고 이유를 telemetry에 남긴다.

#### 6.6.2 Native 상태와 수명

```text
Idle -> Preparing -> Speaking -> Holding -> Idle
  |         |           |          |
  +------> Failed    Stopping   Interrupted
                         |
                      Canceled

Any state
  -> 새 request 차단
  -> callback/event drain
  -> Disposed
```

규칙:

- C#의 “요청 접수”와 실제 `didStart`를 구분한다.
- request/session generation이 다른 `didFinish/didCancel`은 현재 audio-session 상태를 바꾸지 않는다.
- native control과 delegate event는 하나의 serial producer 경로로 모은 뒤, `requestId + state + monotonicSequence`를 가진 순서 보존 bounded SPSC event queue에 넣는다.
- `Started -> Finished/Canceled` 같은 전이는 Unity poll 사이에 덮어쓸 수 없으며 terminal event는 Unity ACK 전까지 소유권을 유지한다.
- event queue는 terminal 전용 예약 공간을 두고, queue 밖에 atomic `backendUnhealthy`와 `overflowSequence`를 둬 queue가 가득 차도 Unity가 실패를 반드시 감지하게 한다.
- event queue overflow 시 먼저 out-of-band health flag를 설정하고 새 음성 admission을 중단한 뒤 voice OFF/LogOnly 안전 fallback과 text/color/haptic을 사용한다. terminal event를 조용히 버려 active 상태를 영구 유지하게 해서는 안 된다.
- Unity는 main thread에서 event를 poll하고 scheduler를 전진시킨다.
- synthesizer와 한국어 voice는 workout 동안 유지한다.
- 중복 controller가 process-global synthesizer를 종료하지 않도록 singleton owner 또는 refcount/generation 계약을 둔다.
- timeout은 hang 진단과 backend 격리에 사용하되 callback을 기다리지 않고 native 객체를 해제하는 근거로 사용하지 않는다.

#### 6.6.3 iOS audio-session 정책

두 정책을 섞지 않고 P0 A/B 뒤 하나를 기본값으로 선택한다.

**선택지 A: system-managed speech session — 1순위 실험**

- synthesizer 생성 시 `usesApplicationAudioSession = false`를 한 번 설정한다.
- audio-session 정책 flag는 active 발화 중 hot-toggle하지 않는다. admission 중단 → active/pending drain → synthesizer 재생성 뒤에만 변경한다.
- TTS bridge의 manual `setCategory/setActive/deactivate`를 제거한다.
- Apple 문서상 시스템이 speech용 별도 session의 activation, interruption, mixing, ducking을 관리한다.
- Unity shared audio session을 TTS plugin이 직접 끄지 않으므로 현재 정지 원인을 가장 작은 변경으로 분리할 수 있다.
- Unity 내부 음악, 외부 음악, AirPods/Bluetooth, silent switch 동작은 실기기 A/B가 필요하다.

**선택지 B: app-wide AudioSessionCoordinator — 정밀 제어가 꼭 필요할 때**

- 앱 전역 owner 하나가 Unity audio, TTS, 향후 STT의 session policy를 관리한다.
- 짧은 코칭 prompt라면 현재 연속 음성용 `.spokenAudio` 대신 `.voicePrompt`를 A/B한다.
- category/mode/options는 매 utterance가 아니라 session 경계 또는 실제 변경 시에만 구성한다.
- active 발화 burst 사이에는 짧은 hold를 두되, Unity audio object가 실행 중일 때 TTS delegate가 shared session을 무조건 deactivate하지 않는다.
- 모든 `NSError`, interruption, route change, media-services lost/reset을 기록하고 recovery state machine을 둔다.
- 이전 Unity audio 설정을 임의로 덮거나 session owner 두 개가 동시에 조작하지 않게 한다.

외부 음악 ducking은 `AVAudioSession`의 역할이고 Unity 내부 음악 ducking은 `AudioMixer/AudioSource`의 역할이다. 둘을 하나의 효과로 가정하지 않는다. 내부 음악이 있다면 native TTS state event에 Unity mixer envelope를 연결한다.

#### 6.6.4 자주 쓰는 문장의 hybrid 후보

system-managed native TTS가 안정화된 뒤에도 첫 발화 또는 고빈도 문장의 지연이 크다면 다음을 조건부 검토한다.

- 고정 안전 경고와 자주 쓰는 자세 문장: preloaded Unity `AudioClip`
- 반고정 문장: `AVSpeechSynthesizer.writeUtterance:toBufferCallback:`로 사전 생성·제한 cache
- 동적 RAG 문장: native `AVSpeechSynthesizer`

이 방식은 prompt 시점의 합성 비용을 줄이지만 앱 크기, PCM memory, 다국어, 음성 톤 일관성, cache invalidation 비용이 생긴다. P0/P2-A 지표가 필요성을 증명할 때만 진행한다.

---

## 7. 단계별 구현 계획

### P0. 기준선과 구간별 계측

목표: 기능을 바꾸기 전에 어디에서 얼마나 멈추는지 수치로 확정한다.

추가할 측정 구간:

1. 새 카메라 프레임 대기와 `GetPixels32`
2. native 입력 복사, 채널/회전 처리, `MPImage` 생성
3. MediaPipe submit 및 실제 inference
4. Swift 결과 변환과 JSON 생성
5. 네이티브→C# 문자열 전달과 JSON parse
6. `JointTrackingFrame` 생성
7. 안정화·특징·규칙·RAG 처리
8. JSONL enqueue/write
9. skeleton repaint와 텍스트 UI 갱신

TTS 요청마다 `requestId`, feedback semantic ID, severity, source, session generation과 monotonic timestamp를 연결하고 다음 구간을 추가한다.

10. feedback 생성 → TTS admission
11. queue 대기, replace/coalesce/drop과 이유
12. C# → native 호출 반환과 native main-queue 대기
13. `AVAudioSession.setCategory` 시간·오류
14. `AVAudioSession.setActive` 시간·오류
15. 기존 발화 stop과 cancel callback
16. voice lookup, utterance 생성, `speakUtterance` submit
17. submit → `didStart`
18. `didStart` → `didFinish/didCancel`
19. audio-session deactivate 시간·오류와 route/interruption event

측정 방법:

- Unity `ProfilerMarker`와 `ProfilerRecorder`
- Development Build + Autoconnect Profiler, Deep Profile off
- Swift와 Objective-C++ `os_signpost`
- Xcode Instruments의 Time Profiler, Allocations, Metal System Trace, Energy
- 멈춤 재현 순간 Xcode Debug Pause, Hangs/System Trace로 모든 thread stack 저장
- Release build의 실제 체감과 10~30분 장시간 측정

수집 지표:

- main-thread CPU frame, GPU frame, 실제 표시 간격의 p50/p95/p99/max
- 33 ms, 50 ms, 100 ms 초과 frame 수
- 위 Pose 9개와 TTS 10개 단계의 p50/p95/p99
- camera timestamp → overlay 표시 end-to-end latency
- latest result age, in-flight 수, pending 수, 의도적 skip과 오류 drop의 분리 집계
- Pose success/fail, 유효 Pose FPS, first-pose time
- managed/native/GPU memory, GC allocation과 pause
- thermal state, 배터리/energy, 앱 background 복귀 상태
- TTS request queue와 native state-event queue의 high-water mark, replace/drop/stale/priority-preemption/overflow 횟수
- idle warm submit → `didStart`, Critical preempt → `didStart`, pending 승격 → `didStart`를 분리한 지연과 finish/cancel/fail 비율
- TTS 전후 500 ms와 발화 중 frame time, Pose result gap, overlay latency 변화
- audio session category/mode/options/route/sample rate의 전후 값
- interruption, route change, media services lost/reset, Unity audio configuration change

A/B 시나리오:

- 카메라만
- 카메라 + Pose
- Pose + overlay
- Pose + 피드백 분석
- Pose + 로그
- Pose + LogOnly TTS
- Pose + 현재 manual audio-session native TTS
- startup TTS ON/OFF
- 현재 native TTS에서 첫 cold 발화/후속 warm 발화
- system-managed speech session
- TTS scheduler ON/OFF
- preloaded `AudioClip` 기준선
- 전체 기능

일반 steady-state 비교는 동일 기기, 거리, 조명, 동작에서 10초 warm-up 후 10분을 3회 수행하고 median과 최악 run을 모두 기록한다. cold 첫 발화는 새 process cold launch 직후 별도 run으로 측정하며 warm-up 결과와 합치지 않는다.

완료 조건:

- 단계별 p95/p99와 long-frame 원인을 같은 timestamp로 비교할 수 있다.
- 정지 재현 시 main thread가 Pose, audio-session, speech, Unity audio 중 어디에서 멈췄는지 stack으로 분류할 수 있다.
- 변경 전 capture를 보관한다.
- standard/low device profile의 기준을 합의한다.

### P1-A. 최우선 TTS 안전장치와 scheduling

목표: audio-session 정책을 크게 바꾸기 전에 발화 폭주, stale 음성, startup 경합을 막고 모든 상태를 관찰 가능하게 만든다.

작업:

- `VoiceEnabled`, `LogOnly`, startup TTS ON/OFF를 runtime feature flag로 분리해 즉시 원인 격리와 안전 fallback이 가능하게 한다.
- `speakOnStart` 실제 안내는 camera/model의 첫 frame과 겹치지 않게 readiness 이후로 미루며 cold/warm 비용을 별도 측정한다.
- 모든 producer가 사용하는 `CoachSpeechScheduler` 하나를 둔다.
- queue를 `active 1 + replaceable pending 1`로 제한한다.
- request에 semantic ID, source, severity, TTL, session generation을 넣는다.
- Critical/Warning/Info/rep count의 admission, coalesce, drop, interrupt 정책을 명시한다.
- Critical 선점은 `pending 저장 → active stop 1회 → 해당 active terminal 확인 → TTL 재검사 → Critical submit` handshake를 사용한다.
- admission, native dispatch 직전, pending 승격, foreground/session resume에서 TTL과 generation을 재검사한다.
- 반복 횟수와 일반 자세 피드백에도 같은 전역 최소 간격을 적용한다.
- native synthesizer와 `ko-KR` voice를 workout 동안 재사용한다.
- `TrySpeak 성공`을 실제 재생 시작으로 표현하지 않고 `Enqueued`와 `Started`를 구분한다.
- 모든 native error와 C# scheduler drop 이유를 telemetry에 남긴다.

임시 안전 fallback:

- TTS가 켜진 조건에서만 정지가 재현되면 최종 수정 전 내부/TestFlight에서는 voice를 끌 수 있다.
- 이는 진단과 crash 회피용이며 최종 해결로 간주하지 않는다.
- Critical 안전 경고의 대체 채널로 text, color, vibration이 항상 즉시 동작하는지 함께 확인한다. TTS drop/fail/timeout이어도 이 채널은 음성 완료를 기다리지 않는다.

완료 조건:

- burst 입력에도 queue 상한을 넘지 않는다.
- Info/횟수 안내가 안전 경고를 선점하지 않는다.
- 같은 Pose frame에서 native TTS 시작이 두 번 호출되지 않는다.
- startup TTS와 camera/model cold start를 독립적으로 재현할 수 있다.

### P1-B. 저위험 Pose hot path 정리

목표: 구조를 크게 바꾸지 않고 중복 처리, 불필요한 할당과 설정 불일치를 제거한다.

작업 후보:

- `WebCamTexture.didUpdateThisFrame` 또는 frame sequence로 새 프레임만 추론한다.
- performance profile을 한 곳에서 관리하고 sampling FPS, 분석 window, QA 기준을 동기화한다.
- 가능하면 분석 window를 frame 개수보다 실제 timestamp 범위로 정의한다.
- 현재 소비자가 사용하지 않는 world landmark와 결과 필드를 hot JSON에서 제외한다.
- skeleton joint lookup을 문자열 선형 검색 대신 고정 MediaPipe index로 바꾼다.
- UI Label은 값이 바뀔 때만 갱신하고 overlay와 text update cadence를 분리한다.
- 로그를 bounded queue에 넣고 별도 writer가 묶어서 기록하며 종료 시 명시적으로 flush한다.
- 480×360/640×480, Pose 8/10/12 FPS를 정확도와 함께 A/B한다.
- iOS export가 사용하는 `MediaPipeTasksVision` 버전을 고정하고 lock/build 정보를 보존해 성능 회귀를 재현 가능하게 한다.

장점:

- 회귀 범위가 작고 병목을 더 선명하게 분리할 수 있다.
- stale/duplicate frame, 문자열/lookup, 로그·UI spike를 줄인다.

한계:

- 동기 native detect는 여전히 메인 스레드를 막는다.

진행 기준:

- 정확도·검출 성공률 회귀가 허용 범위 이내다.
- crash, memory 증가, frame p95 악화가 없다.
- 이 단계만으로 목표를 만족해도 P2-B의 유지보수 가치와 위험을 별도로 평가한다. 긴 동기 frame이 남으면 P2-B로 진행한다.

### P2-A. 핵심 변경: TTS 비차단 및 audio-session 소유권

목표: Pose frame에서 audio-session 전환을 제거하고, TTS start/finish/cancel이 Unity와 공유 audio 상태를 망가뜨리지 않게 한다.

1순위 구현 및 A/B:

- `AVSpeechSynthesizer.usesApplicationAudioSession = false`를 초기화 시 한 번 설정한다.
- 이 variant에서는 bridge의 manual `setCategory/setActive/setActive:NO`를 제거한다.
- C#은 request를 enqueue하고 바로 반환하며 Unity thread에서 speech 완료나 audio-session 전환을 동기 대기하지 않는다.
- system-managed variant에서도 `speakUtterance`와 voice 준비 자체의 main-thread p95를 별도 측정한다.
- speech API의 thread-affinity를 Apple 계약에 맞게 지키면서 native command admission을 serial화한다. 단순 `Task.Run`으로 AVFAudio 호출을 임의 thread에 옮기지 않는다.
- main-thread speech submit 자체가 예산을 넘으면 지원되는 native scheduling 방식 또는 pre-rendered clip/buffer 경로를 검토하며, stall을 다른 Unity frame으로 옮긴 것만으로 완료 처리하지 않는다.
- native delegate는 request ID/generation/monotonic sequence가 있는 state event를 순서 보존 bounded queue에 기록하고 Unity가 ACK하며 poll한다.
- 현재 manual session 경로를 진단용 feature flag fallback으로 유지한다.

system-managed variant가 제품 요구를 만족하지 못할 때만:

- `AppAudioSessionCoordinator` 하나가 Unity audio, TTS, 향후 STT의 shared session을 소유한다.
- `.voicePrompt`, `DuckOthers`, 필요 시 `InterruptSpokenAudioAndMixWithOthers`를 제품 정책에 맞게 A/B한다.
- category는 매 utterance가 아니라 session 설정 변경 시 한 번 적용한다.
- queue가 완전히 빈 뒤 hold를 거쳐 deactivate하되 Unity audio object가 사용 중이면 임의로 끄지 않는다.
- interruption, route change, media-services reset을 상태 머신으로 복구한다.

공통 생명주기:

- late finish/cancel/shutdown은 generation이 일치할 때만 현재 state를 바꾼다.
- preempt stop은 active request당 한 번만 보내고 해당 terminal event 전에는 새 utterance를 submit하지 않는다.
- STOP은 새 request 차단 → pending 폐기 → active cancel 1회 → delegate/event drain → 필요 시 backend dispose 순서다. drain을 Unity thread에서 동기 대기하지 않는다.
- drain timeout이면 해당 generation과 synthesizer를 `Retired/Unhealthy`로 격리하고 재사용하거나 즉시 해제하지 않는다. 안전한 native barrier 또는 process 종료에서만 정리하며, 반복 실패 시 새 synthesizer를 계속 만들지 않고 voice OFF/LogOnly로 유지한다.
- background 진입 시 새 coaching request를 막고 generation을 무효화하며 pending을 모두 폐기한다. active Info/Warning/Critical도 stop을 한 번 요청하고 stale 음성을 foreground에서 다시 재생하지 않는다.
- foreground 복귀 뒤 새 generation을 만들고 audio route와 synthesizer health를 확인한 후 새 feedback부터 재개한다.
- media-services lost/reset은 system-managed/manual 공통으로 admission 차단 → 기존 generation 무효화 → active/pending 정리 → 안전한 drain/barrier → synthesizer 재생성 → health 확인 순서로 복구한다.
- process-global synthesizer owner를 하나로 제한하거나 refcount를 적용한다.
- audio-session 정책을 바꿀 때는 idle/drain 뒤 synthesizer를 재생성하며 발화 중 hot-toggle하지 않는다.
- Unity 내부 music ducking은 native external-audio ducking과 분리해 TTS state event로 AudioMixer를 제어한다.

장점:

- 현재 정지의 유력 원인인 shared audio-session churn을 Pose hot path에서 제거한다.
- 발화 완료·취소·실패를 기준으로 정확한 queue와 ducking을 구현할 수 있다.

단점:

- system-managed session의 외부 음악·Bluetooth 동작은 OS 정책 영향을 받는다.
- app-wide coordinator는 Unity audio와 향후 STT까지 포함해 QA 범위가 커진다.

완료 조건:

- TTS native admission/submit이 main-thread 예산을 만족한다.
- TTS start/finish/cancel 전후 long frame과 Pose result gap이 수용 기준 안에 든다.
- manual/system-managed 어느 variant가 선택됐는지 한 가지 owner 정책으로 고정한다.
- 100회 speak/cancel/lifecycle에서 stale deactivate, stuck session, crash/hang이 없다.

### P2-B. 핵심 변경: Pose async live stream

목표: MediaPipe 추론을 Unity 메인 스레드에서 분리하고 latency backlog를 막는다.

작업:

- Swift `.video + detect`를 `.liveStream + detectAsync + PoseLandmarkerLiveStreamDelegate`로 변경한다.
- native serial queue와 `in-flight 1 + replaceable latest pending 1` backpressure를 만든다.
- native-owned input buffer pool과 명시적인 acquire/release 계약을 만든다.
- submit 거절, busy, error delegate, STOP, task close 각각의 slot 반환 경로와 watchdog을 정의한다.
- callback은 latest-result 슬롯만 갱신하고 Unity는 main thread에서 poll한다.
- timestamp, sequence, session generation, rotation/mirror metadata를 함께 전달한다.
- STOP/switch/background/dispose state machine과 queue barrier를 구현한다.
- 현재 동기 경로를 feature flag fallback으로 유지한다.

장점:

- 긴 inference가 UI frame을 직접 막지 않는다.
- 오래된 frame이 쌓이지 않아 결과 age와 memory가 bounded 된다.
- 현재 33관절 모델과 규칙을 그대로 유지할 수 있다.

단점:

- 입력 buffer 수명과 callback 경쟁 상태가 복잡해진다.
- 일부 카메라 frame은 의도적으로 처리하지 않는다.
- 안전한 초기 구현에서는 native-owned buffer로 한 번 더 복사할 수 있다.

완료 조건:

- main-thread Pose submit p95가 목표 안에 들어온다.
- queue depth가 설계 상한을 넘지 않는다.
- STOP 이후 stale callback이 UI/분석에 적용되지 않는다.
- 정상·거절·오류·STOP 경로에서 input slot 누수와 이중 반환이 없다.
- lifecycle stress에서 crash, hang, callback-after-dispose가 없다.

### P3. 조건부 변경: JSON 브리지 제거

진입 조건:

- P2-B 이후에도 JSON/parse/managed frame 생성이 CPU p95 또는 GC의 유의미한 비중을 차지하거나 GC 목표를 만족하지 못한다.

작업:

- versioned C ABI header와 고정 landmark 배열을 정의한다.
- Swift가 미리 할당한 result slot에 숫자를 직접 기록한다.
- C#은 JSON 없이 reusable 배열 또는 read-only view로 읽는다.
- 매 frame GUID를 증가 sequence로 교체한다.
- 2~3개의 결과 buffer와 lease 규칙을 사용한다.
- JSON은 debug/session 파일 등 hot bridge 밖에서만 필요 시 생성한다.

장점:

- Swift Dictionary/JSON/UTF-8, C# `StringBuilder`/parse, 관절별 문자열 경로를 제거한다.
- GC와 autorelease pressure를 크게 낮출 수 있다.

단점:

- Swift/C# struct alignment, version 호환, endianness와 buffer lifetime을 관리해야 한다.
- JSON보다 사람이 직접 디버깅하기 어렵다.
- subscriber가 과거 frame을 보관한다면 별도 immutable copy 또는 lease가 필요하다.

완료 조건:

- bridge와 provider hot path의 steady-state allocation이 near-zero에 가깝다.
- JSON 경로와 동일 입력에서 landmark 값, timestamp, orientation 결과가 일치한다.
- binary/JSON을 독립적으로 롤백할 수 있다.

### P4. 조건부 변경: 입력 복사와 해상도

진입 조건:

- P2-B 이후 `GetPixels32`, channel/rotation, native buffer copy가 p95 hot path의 큰 비중을 계속 차지한다.

낮은 위험부터 검토:

1. 프리뷰는 640×480을 유지하고 추론 입력만 480×360 또는 320×240으로 낮춘다.
2. GPU downscale + `AsyncGPUReadback` 또는 재사용 `NativeArray`를 A/B한다.
3. iOS 포맷과 회전을 검증한 `CVPixelBuffer` 경로로 wrapper churn을 줄인다.
4. 앞 단계로 목표를 달성하지 못할 때만 AVFoundation `CMSampleBuffer/CVPixelBuffer`를 MediaPipe에 직접 전달하는 전체 캡처 교체를 검토한다.

데이터량 참고:

| 해상도 | RGBA 1프레임 | 640×480 대비 |
| --- | ---: | ---: |
| 640×480 | 약 1.17 MiB | 100% |
| 480×360 | 약 0.66 MiB | 56.25% |
| 320×240 | 약 0.29 MiB | 25% |

주의:

- 전신이 멀리 있거나 발목·손목이 작게 보일 때 정확도가 낮아질 수 있다.
- preview 좌표, inference 좌표, mirror/rotation mapping을 함께 검증해야 한다.
- Unity `WebCamTexture`와 별도 AVFoundation 세션이 동시에 카메라를 소유하지 않도록 한다. native capture를 선택하면 캡처 소유권 전체를 교체해야 한다.

### P5. 적응형 성능 profile

진입 조건:

- 고정 profile이 기기 등급 또는 장시간 발열 조건을 모두 만족하지 못한다.

작업:

- rolling p95 frame time, inference latency, result age, TTS queue delay, speech-start latency, thermal state를 입력으로 사용한다.
- 예: 12 → 10 → 8 FPS와 추론 해상도 profile을 단계적으로 낮춘다.
- 승급과 강등 threshold를 다르게 하고 최소 유지 시간을 둬 profile 진동을 막는다.
- 해상도 변경은 카메라 restart를 유발한다면 세션 경계에서 우선 적용한다.
- CPU/GPU delegate를 최소 지원 기기에서 A/B하고 profile별 선택 가능성을 검토한다.
- thermal/long-frame 강등은 Pose 품질을 먼저 조절하되 Critical TTS를 drop하지 않는다.

장점:

- 최신 기기 성능을 버리지 않으면서 구형 기기와 발열 상황을 보호한다.

단점:

- 재현성과 QA 조합이 늘어난다.
- 너무 잦은 품질 변경은 판정 일관성과 화면 안정성을 해칠 수 있다.

### P6. 회귀 검증과 단계 배포

검증:

- 결정론적 pose/rep QA
- 고정 replay 또는 golden clip의 landmark·phase·rep·feedback 비교
- 실기기 10분 성능 시험과 30분 soak
- START/STOP 200회
- 전면/후면 camera switch 100회
- background/foreground 100회
- portrait/landscape, 회전 중단, 권한 거부/복귀, OS interruption
- 밝은/어두운 환경, 전신/부분 가림, 빠른 동작
- TTS cold/warm 발화, 500 ms burst, Critical/Warning/Info 우선순위
- TTS 중 camera STOP/switch와 background/foreground
- speaker, silent switch, 유선/Bluetooth/AirPods
- Unity 내부 음악, 외부 Music/podcast, Siri/전화/알람 interruption
- system-managed/manual coordinator variant 비교

배포 순서:

1. 개발 기기
2. 내부 QA
3. 제한된 TestFlight cohort
4. standard profile 기본 활성화
5. 지표 확인 후 low/high profile 확대

각 변경은 다음 단위로 분리해 독립 롤백한다.

- 계측
- P1-A TTS scheduler와 안전장치
- P1-B Pose hot path 정리
- P2-A TTS audio-session/state pipeline
- P2-B Pose async pipeline
- P3 binary result
- P4 capture path
- P5 adaptive profile

---

## 8. 권장 구조의 장점과 단점

### 장점

- inference 시간이 길어져도 Unity UI frame과 버튼 반응을 직접 막지 않는다.
- latest-only 정책으로 latency와 memory가 계속 증가하지 않는다.
- 현재 MediaPipe 33관절과 기존 규칙·리플레이 계약을 유지한다.
- 프리뷰, 추론, 분석, UI, 로그의 처리율을 목적에 맞게 분리할 수 있다.
- Pose hot path는 음성 의도만 enqueue하므로 audio route 지연과 분리된다.
- bounded TTS queue와 TTL로 현재 동작과 맞지 않는 오래된 안내를 방지한다.
- Critical/Warning/Info/rep count가 하나의 우선순위 정책을 사용한다.
- speech state event로 실제 시작·종료·취소·실패와 내부 music ducking을 정확히 연결할 수 있다.
- TTS plugin이 Unity shared audio session을 임의로 끄는 위험을 줄인다.
- JSON과 capture 경로는 실측 결과에 따라 후속 단계로 분리하므로 큰 변경의 회귀 원인을 좁힐 수 있다.
- feature flag와 단계별 gate로 실제 기기에서 안전하게 확대할 수 있다.

### 단점

- 비동기 buffer 소유권, callback, STOP/dispose 경쟁 상태가 동기 방식보다 어렵다.
- 처리량보다 최신성을 우선하므로 일부 입력 frame을 의도적으로 버린다.
- 안전한 첫 버전은 native-owned buffer 복사 비용이 남을 수 있다.
- binary ABI까지 적용하면 디버깅과 버전 관리가 복잡해진다.
- TTS scheduler, native event, audio-session state의 generation 계약이 추가된다.
- system-managed speech session은 외부 음악과 route 동작이 OS 정책에 더 의존한다.
- app-wide audio coordinator를 선택하면 Unity audio, TTS, 향후 STT를 함께 검증해야 한다.
- hybrid clip/cache를 쓰면 앱 크기, memory, 현지화와 음성 톤 관리가 늘어난다.
- profile과 기기 조합이 늘어나 QA 비용이 증가한다.

### 완화책

- 입력과 결과에 sequence, timestamp, generation, version을 넣는다.
- queue capacity와 buffer pool 크기를 고정한다.
- background callback에서는 Unity API를 호출하지 않는다.
- TTS request/result와 Pose frame/result에 각각 generation과 독립 queue를 둔다.
- manual/system-managed TTS session, native/clip, 동기/비동기 Pose, JSON/binary, fixed/adaptive를 독립 flag로 둔다.
- 정확도와 lifecycle gate를 통과하지 못하면 해당 단계만 롤백한다.

---

## 9. 다른 대안과 비교

### 9.1 Pose pipeline 대안

| 대안 | 메인 스레드 hitch | 장점 | 단점/위험 | 판단 |
| --- | --- | --- | --- | --- |
| FPS만 4~6으로 낮춤 | 남음 | 가장 빠르고 변경이 작음 | 한 번의 긴 sync detect는 그대로이며 반응과 빠른 동작 정확도가 낮아짐 | 긴급 완화만 |
| 해상도만 낮춤 | 줄지만 남음 | 복사·추론량과 발열 감소 | JSON/GC/동기 구조는 유지되고 원거리 관절 정확도 저하 가능 | P1-B/P4 보조 |
| `.video` 동기 detect를 native background queue로 이동 | 제거 가능 | live stream보다 기존 입력당 결과 계약을 유지하기 쉬움 | backpressure, 취소, buffer 수명을 직접 모두 구현해야 함 | 단기 차선 |
| `.liveStream + detectAsync` latest-only | 동기 inference 원인은 제거, 잔여 경로는 실측 필요 | 카메라용 비동기 처리, 낮은 result age, bounded backlog | 상태 머신과 buffer ownership 난이도 | **1순위 권장** |
| AVFoundation → `CMSampleBuffer/CVPixelBuffer` 직접 입력 | inference·readback 원인은 제거 가능, 후처리는 남음 | `GetPixels32`와 UIImage/CGImage 경로까지 없앨 수 있음 | 프리뷰·권한·회전·Unity texture 공유를 포함한 큰 재작성 | P2-B/P4로 부족할 때 |
| Unity Sentis + MoveNet | native Swift/JSON 제거 | Unity 안에서 runtime 통합 가능 | 일반적으로 17 keypoint 계약이라 현재 33관절 규칙·리플레이·정확도 재검증 필요 | 저사양 실험 후보 |
| Apple Vision Body Pose | 제거 가능 | iOS native stack, 외부 MediaPipe runtime 감소 | 관절 topology와 정확도 특성이 다르고 iOS 전용 | iOS fallback 연구 |
| Homuler MediaPipe Unity plugin | 구현 방식에 따라 다름 | Unity 중심 API와 플랫폼 공통화 가능 | package/native framework/build version 문제를 다시 소유하며 현재 runtime과 병행 시 중복 위험 | 전체 교체일 때만 |
| 서버 Pose 추론 | 단말 hitch 감소 | 단말 연산과 발열 감소 | 네트워크 지연, 비용, 개인정보, 오프라인 불가, 결과 age 변동 | 실시간 기본 경로 비권장 |
| GPU 대신 CPU delegate | 기기별로 다름 | 구형 기기에서 Unity Metal 렌더 경쟁이 줄 가능성 | inference 자체는 느려질 수 있음 | 기기별 A/B 후 profile 후보 |

### 9.2 TTS 대안

| 대안 | 장점 | 단점/위험 | 판단 |
| --- | --- | --- | --- |
| 현재 manual session + 발화 빈도만 감소 | 변경이 가장 작음 | shared session 재구성·deactivate와 main-thread stall, late callback race가 남음 | 긴급 완화만 |
| 현재 bridge + AVSpeechSynthesizer 내장 queue만 사용 | stop/restart 감소, 구현 단순 | priority, TTL, stale 안내, producer 통합이 없음 | 앱 scheduler와 함께만 |
| `usesApplicationAudioSession=false` | 시스템이 speech session의 activation/interruption/mix/duck을 관리하고 Unity shared session 충돌을 줄일 가능성 | 외부 음악, Unity audio, Bluetooth 동작을 앱이 세밀하게 제어하기 어려움 | **1순위 A/B** |
| app-wide `AudioSessionCoordinator` | Unity audio, TTS, 향후 STT 정책을 한 곳에서 정밀 제어 | 상태·route·interruption·owner 관리가 가장 복잡 | system-managed가 요구를 못 맞출 때 |
| 고정 문장 preloaded `AudioClip` | 재생 지연이 일정하고 Unity AudioMixer 연동이 쉬움 | 앱 크기, memory, 다국어, 동적 문장 한계 | 자주 쓰는 안전 문장 후보 |
| `writeUtterance` PCM cache | OS 음성으로 반고정 문장을 미리 생성 가능 | cache miss, PCM bridge, memory/disk, invalidation 복잡 | P2-A 이후 조건부 |
| Clip + native TTS hybrid | 고빈도 문장은 빠르고 동적 RAG 문장도 지원 | backend 두 개와 음성 톤·volume 일관성 관리 | 실측이 필요성을 보이면 장기 권장 |
| Cloud TTS | 음성 품질과 플랫폼 일관성이 좋을 수 있음 | 네트워크 지연, 비용, 개인정보, 오프라인 불가 | 운동 hot path 비권장 |

단순히 TTS 전체 호출을 background queue로 옮기는 것만으로는 shared audio-session 소유권, stale cancel/deactivate, queue 정책이 해결되지 않는다. 반대로 모든 문장을 `AudioClip`으로 바꾸면 동적 코칭의 장점을 잃는다.

### 9.3 최종 선택 이유

`.liveStream + detectAsync + latest-only`는 다음 균형이 가장 좋다.

- 기존 MediaPipe 33관절 정확도 계약을 유지한다.
- UI를 막는 핵심 원인을 직접 제거한다.
- AVFoundation 전체 재작성보다 변경 범위가 작다.
- 서버 의존 없이 온디바이스 개인정보 원칙을 유지한다.
- P1-B, P3, P4를 독립적으로 적용하거나 보류할 수 있다.

TTS는 `bounded CoachSpeechScheduler + system-managed speech session`을 첫 권장 조합으로 실험한다.

- 모든 producer의 우선순위·TTL·중복을 한 곳에서 결정한다.
- 현재 shared audio session churn을 가장 작은 native 변경으로 제거해 원인 가설을 직접 검증한다.
- 실제 시작·완료·취소 event를 도입해 stale callback과 ducking을 제어한다.
- Unity audio/외부 음악 요구를 충족하지 못할 때만 app-wide coordinator로 확장한다.
- native TTS 지연이 계속 큰 고빈도 문장만 hybrid clip/cache 후보로 올린다.

---

## 10. 성능·정확도 수용 기준

아래 값은 P0 baseline 전의 초기 제안이다. 최소 지원 기기와 실제 측정 분포를 확인한 뒤 확정한다.

여기서 CPU frame time은 Unity Profiler의 main-thread `PlayerLoop` 실행 시간에서 v-sync 대기 시간을 제외한 값으로 정의한다. GPU frame time과 실제 화면 표시 간격은 별도 지표로 기록한다. 30 FPS의 33.3 ms는 여유가 없는 경계값이므로 standard profile의 CPU/GPU p95에는 초기 5 ms 수준의 headroom을 둔다.

| 영역 | standard profile 초기 기준 | low profile 초기 기준 |
| --- | ---: | ---: |
| main-thread CPU frame p95 | ≤ 28 ms | ≤ 33.3 ms |
| GPU frame p95 | ≤ 28 ms | ≤ 33.3 ms |
| 실제 표시 간격 p99 | ≤ 50 ms | ≤ 50 ms를 목표로 baseline 뒤 합의 |
| 100 ms 이상 멈춤 | warm-up 후 0회 | warm-up 후 0회 |
| 유효 Pose 결과 | 평균 ≥ 10 FPS | 평균 ≥ 8 FPS |
| inference p95 | ≤ 80 ms | ≤ 120 ms |
| camera → overlay p95 | ≤ 150 ms | ≤ 180 ms |
| 결과 backlog | pending ≤ 1, stale 적용 0 | 동일 |
| main-thread Pose submit p95, P2-B 후 | ≤ 4~6 ms | ≤ 6 ms |

TTS 동시 실행 초기 기준:

| 영역 | 초기 기준 |
| --- | ---: |
| scheduler admission + warm native enqueue 반환 p95 | ≤ 4 ms |
| idle warm native submit → `didStart` p95 | ≤ 300 ms |
| pending 승격 → `didStart` p95 | ≤ 300 ms |
| Critical preempt request → `didStart` p95 | ≤ 500 ms, P0 뒤 확정 |
| queued Warning 전체 대기 | 설정 TTL 이내; 고정 300 ms 기준을 적용하지 않음 |
| TTS start/finish/cancel로 추가된 50 ms 이상 frame | 0회 |
| TTS 연계 100 ms 이상 멈춤 | 0회 |
| 음성 OFF 대비 CPU/GPU frame p95 악화 | ≤ 10% |
| 음성 OFF 대비 유효 Pose FPS 하락 | ≤ 5% 또는 1 FPS 중 더 엄격한 값 |
| 음성 OFF 대비 camera → overlay p95 증가 | ≤ 20 ms |
| 8 FPS profile에서 발화 중 Pose result gap | ≤ 312 ms |
| speech queue | active ≤ 1, pending ≤ 1 |
| native ordered event queue | overflow 0, terminal ACK 누락 0 |
| stale Info/Warning 재생 | 0건 |
| Critical 누락 또는 Info에 의한 우선순위 역전 | 0건 |
| 발화 후 Unity/외부 audio 복구 | 100% |
| speak/cancel 및 lifecycle 반복 | 각 100회 crash/hang/stuck 0건 |

`didStart`는 synthesizer state 기준이며 실제 스피커에서 첫 소리가 난 시점과 완전히 같지 않을 수 있다. 최종 사용자 체감 지연은 화면 timecode와 외부 audio 녹음을 함께 사용해 보정한다. cold 첫 발화는 process cold launch run으로 warm 값과 분리해 측정하고 P0 결과로 별도 기준을 확정한다.

공통 안정성 기준:

- 10분 동안 low-memory event 0회
- thermal critical 0회
- warm-up 뒤 10분 전체 memory 증가 20 MiB 이하
- 10 ms를 넘는 GC pause 0회
- hot bridge allocation은 P3 완료 후 near-zero 목표
- lifecycle 반복에서 crash/hang 0회
- callback-after-dispose와 다른 generation 결과 적용 0회
- stale TTS finish/cancel/shutdown이 새 발화의 audio session을 변경하는 경우 0회
- audio interruption/route change 이후 TTS와 Unity audio가 stuck 상태로 남는 경우 0회

정확도 기준:

- 동일 golden input의 rep count 결과가 기존과 동일
- detection success 하락 2%p 이내
- phase/feedback 이벤트 시간 차이는 합의한 latency tolerance 이내
- safety 관련 fixture에서 새 false negative 0건
- 낮은 해상도 profile은 전신, 원거리, 빠른 스쿼트 조건을 별도로 통과

의도적으로 버린 카메라 frame은 오류 drop과 분리한다. latest-only 구조에서는 전체 카메라 frame 처리율보다 결과 age, 유효 Pose FPS, backlog 상한이 더 중요한 지표다.

---

## 11. 테스트 매트릭스

### 기기

- 현재 보유한 구형 기준 기기: iPhone XS Max
- 제품이 실제로 지원할 가장 오래된 iOS 15 호환 모델을 별도로 확정하고 최소 지원 기기로 추가
- 중간 성능 iPhone
- 최신 고성능 iPhone

### 조건

- front/rear camera
- portrait/landscape 및 화면 회전
- 밝은 실내/어두운 실내
- 전신이 크게 보이는 거리/멀리 있는 거리/부분 가림
- 일반 속도/빠른 운동
- cold start/10분 warm/thermal stress/저전력 모드
- 로그 on/off, overlay on/off, feedback on/off
- 640×480/480×360/320×240
- Pose 8/10/12 FPS
- GPU/CPU delegate A/B가 필요한 최소 지원 기기

TTS 조건:

- voice OFF / LogOnly / 현재 native / system-managed speech session
- startup TTS ON/OFF
- cold 첫 발화 / warm 후속 발화
- 10초 간격 정상 안내
- 500 ms 안에 Info, Warning, Critical이 연속되는 burst
- 짧은 문장 / 최대 길이 문장 / 반복 횟수 coalesce
- TTS 중 camera START/STOP/switch
- TTS 중 background/foreground
- speaker / silent switch / 유선 이어폰 / Bluetooth / AirPods
- Unity 내부 music / 외부 Music / podcast
- Siri / 전화 / 알람 interruption과 route change
- manual coordinator를 유지할 경우 `.spokenAudio`와 `.voicePrompt` A/B

### TTS A/B 매트릭스

| 시나리오 | 분리하려는 비용 |
| --- | --- |
| Pose + voice OFF | Pose 기준선 |
| Pose + LogOnly | feedback 생성, 문자열, scheduler 비용 |
| TTS only, camera/Pose OFF | pure speech/audio-session 비용 |
| camera preview + TTS, Pose OFF | camera와 audio route 상호작용 |
| Pose + 현재 native TTS | 현재 문제 재현 |
| Pose + system-managed speech session | manual shared-session 변경 비용 |
| Pose + scheduler + 현재 session | 반복 cancel/queue churn 비용 |
| Pose + scheduler + system-managed session | 1차 권장 조합 |
| Pose + preloaded clip | runtime speech 합성 비용의 하한 |
| Post P2-B Pose + TTS | 두 비동기 pipeline 통합 결과 |

각 조건은 고정된 운동 입력과 정해진 prompt timestamp를 사용해 최소 3회 반복한다.

### 기록 원칙

- 앱 commit, Unity 버전, Xcode 버전, iOS 버전, MediaPipeTasksVision 버전을 함께 기록한다.
- 같은 기기·조명·거리·동작에서 변경 전후를 비교한다.
- 평균 하나만 쓰지 않고 p50/p95/p99, 최악 run, thermal 전환 시점을 남긴다.
- Development 측정 결과와 Release 체감 결과를 구분한다.
- iPhone native TTS와 macOS/Windows Editor process backend 결과를 섞지 않는다.
- TTS request ID와 Pose/camera timestamp를 같은 monotonic timeline에 기록한다.
- 정지 재현 capture에는 main thread stack, audio category/mode/options/route와 마지막 TTS state를 포함한다.

---

## 12. 중단 및 롤백 기준

다음 중 하나라도 발생하면 해당 phase의 기본 활성화를 중단하고 원인을 해결하거나 독립 롤백한다.

- 새 crash 또는 hang 1건
- callback-after-dispose, buffer double-release, stale generation 적용 1건
- 동일 조건에서 main-thread CPU 또는 GPU frame p95가 baseline보다 10% 이상 악화
- detection success 또는 합의된 정확도 지표가 2%p를 초과해 하락
- golden safety fixture의 새 false negative
- warm-up 뒤 10분 memory가 20 MiB를 초과해 계속 증가하거나 low-memory 발생
- thermal critical 발생 또는 standard profile이 serious 상태에서 자동 하향하지 못함
- orientation/mirror 때문에 좌우 관절이나 overlay가 잘못 매핑됨
- 로그 flush 또는 앱 종료 중 세션 데이터가 허용 범위 이상 유실됨
- TTS start/finish/cancel 전환으로 100 ms 이상 frame이 발생
- TTS ON에서 CPU/GPU frame p95가 voice OFF보다 10% 이상 악화
- TTS ON에서 유효 Pose FPS가 5% 이상 하락하거나 overlay p95가 20 ms 초과 증가
- speech queue가 상한을 넘거나 TTL이 지난 자세 안내가 재생됨
- native state-event queue overflow, terminal event 유실, ACK 누락으로 active가 stuck 됨
- Critical 안내 누락, Info의 Critical 선점, 동일 Critical의 cancel loop 발생
- 발화 종료 뒤 Unity 내부 audio 또는 외부 audio가 복구되지 않음
- interruption/background 뒤 audio session이 stuck 상태로 남음
- controller dispose 뒤 native callback, stale deactivate, shared synthesizer owner 충돌 발생
- TTS cache가 합의한 memory/disk 상한을 초과

롤백 단위:

- async에서 sync로
- binary에서 JSON으로
- adaptive에서 fixed profile로
- inference downscale에서 원래 해상도로
- background logger에서 현재 동기 logger로
- system-managed speech session에서 진단용 manual variant로
- native event scheduler에서 voice OFF/LogOnly 안전 fallback으로
- hybrid clip/cache에서 native-only TTS로

---

## 13. 구현 전에 확정할 결정

P0 이후 다음 항목을 수치로 결정한다.

1. standard/low profile의 실제 기기 목록
2. 기본 Pose 목표가 8, 10, 12 FPS 중 무엇인지
3. 480×360 또는 320×240이 정확도 gate를 통과하는지
4. world landmarks가 현재 runtime 소비자에게 필요한지
5. safe async 1차 입력을 native-owned `CVPixelBuffer`로 할지, lease가 있는 pinned buffer pool로 할지
6. P2-B 뒤 JSON 경로가 P3를 정당화할 만큼 큰 비용인지
7. GPU와 CPU delegate 중 기기 profile별 우세가 있는지
8. 세션 로그가 허용할 수 있는 queue 크기와 종료 flush 시간
9. system-managed speech session이 Unity audio, 외부 음악, Bluetooth 요구를 만족하는지
10. system-managed가 부족할 때 app-wide coordinator의 category/mode/options와 owner가 무엇인지
11. Critical/Warning/Info/rep count별 interrupt, coalesce, TTL과 전역 최소 간격
12. startup 안내를 언제 재생하며 cold speech 비용을 어떻게 숨기지 않고 관리할지
13. 고정 문장 clip 또는 PCM cache가 지연 개선 대비 앱 크기·memory 비용을 정당화하는지
14. Unity 내부 음악 ducking과 외부 음악 ducking의 제품 요구 범위
15. native ordered event queue의 고정 capacity, terminal event 예약 공간, out-of-band health flag와 ACK timeout 정책
16. drain timeout 때 Retired backend를 몇 개까지 격리하고 언제 안전하게 정리할지에 대한 memory 상한

이 결정 전에는 수치 튜닝이나 대규모 캡처 재작성을 최종안으로 고정하지 않는다.

---

## 14. 예상 산출물

- 변경 전/후 실기기 profile capture와 비교표
- 구간별 marker 및 long-frame 진단 로그
- TTS request-to-speech timeline과 audio route/interruption 로그
- 단일 performance profile 설정
- bounded `CoachSpeechScheduler`와 admission 정책 문서
- native TTS state event schema와 request/session generation 계약
- 순서 보존 native event queue의 capacity, overflow fail-safe와 terminal ACK 계약
- system-managed 또는 app-wide audio-session owner 결정 기록
- 필요 시 고정 문장 clip/PCM cache 예산과 manifest
- async latest-only native bridge
- 버퍼 소유권과 lifecycle 상태 전이 문서
- 필요 시 versioned binary result schema
- golden pose/rep/feedback 회귀 fixture
- iOS lifecycle stress 결과
- TestFlight 단계 배포 및 롤백 기록

---

## 15. 관련 문서와 공식 근거

프로젝트 문서:

- `docs/pose-runtime-optimization.md`: 이미 적용된 C# 자세 후처리 할당 최적화와 소유권 규칙
- `docs/TestMediaPipeplan.md`: MediaPipe 실기기 테스트 범위와 기기
- `docs/current-pose-decision-logic.md`: 현재 자세 판정과 반복 계산
- `docs/MediaPipeTroubleshooting.md`: iOS MediaPipe 빌드·런타임 문제 해결
- `docs/TTSCreateplan.md`: 현재 플랫폼별 TTS backend, ducking 요구와 기존 구현 기록
- `docs/FeedbackMediaPipeplan.md`: 피드백 우선순위와 반복 억제의 제품 배경

공식 자료:

- [Google MediaPipe Pose Landmarker iOS 가이드](https://developers.google.com/edge/mediapipe/solutions/vision/pose_landmarker/ios): video/image 동기 처리와 live stream 비동기 delegate, timestamp, busy-frame 처리
- [Google 공식 iOS PoseLandmarkerService 샘플](https://github.com/google-ai-edge/mediapipe-samples/blob/main/examples/pose_landmarker/ios/PoseLandmarker/Services/PoseLandmarkerService.swift): `.liveStream`, `CMSampleBuffer`, `detectAsync` 구현 예
- [Google 공식 CameraViewController 샘플](https://github.com/google-ai-edge/mediapipe-samples/blob/main/examples/pose_landmarker/ios/PoseLandmarker/ViewContoller/CameraViewController.swift): background submit과 main-thread UI 전달 예
- [MediaPipe Swift MPImage API](https://developers.google.com/edge/api/mediapipe/swift/vision/Classes/MPImage): pixel/sample buffer 기반 이미지와 수명 계약
- [Apple TN2445: Handling Frame Drops with AVCaptureVideoDataOutput](https://developer.apple.com/library/archive/technotes/tn2445/_index.html): 실시간 캡처에서 늦은 frame 처리와 queue 누적 방지
- [Apple 성능 signpost 기록](https://developer.apple.com/documentation/os/recording-performance-data): 구간별 성능 계측
- [Apple ProcessInfo thermalState](https://developer.apple.com/documentation/foundation/processinfo/thermalstate-swift.property): thermal 기반 적응형 품질 입력
- [Apple AVSpeechSynthesizer](https://developer.apple.com/documentation/avfaudio/avspeechsynthesizer): utterance queue, stop, delegate와 speech 상태
- [Apple AVSpeechSynthesizerDelegate](https://developer.apple.com/documentation/avfaudio/avspeechsynthesizerdelegate): start, finish, cancel 등 native 상태 event
- [Apple usesApplicationAudioSession](https://developer.apple.com/documentation/avfaudio/avspeechsynthesizer/usesapplicationaudiosession): system-managed speech session의 interruption, mixing, ducking
- [Apple AVAudioSession](https://developer.apple.com/documentation/avfaudio/avaudiosession): shared audio session 구성, activation과 interruption
- [Apple setActive](https://developer.apple.com/documentation/avfaudio/avaudiosession/setactive%28_%3Aoptions%3A%29): 실행 중 audio object의 deactivation과 오류 주의
- [Apple voicePrompt mode](https://developer.apple.com/documentation/avfaudio/avaudiosession/mode-swift.struct/voiceprompt): 짧은 음성 prompt용 mode
- [Apple spokenAudio mode](https://developer.apple.com/documentation/avfaudio/avaudiosession/mode-swift.struct/spokenaudio): 연속 음성 콘텐츠용 mode
- [Apple writeUtterance buffer API](https://developer.apple.com/documentation/avfaudio/avspeechsynthesizer/write%28_%3Atobuffercallback%3A%29): 사전 합성 PCM/cache 대안
- [Apple Audio Session Programming Guide](https://developer.apple.com/library/archive/documentation/Audio/Conceptual/AudioSessionProgrammingGuide/AudioSessionBasics/AudioSessionBasics.html): category, activation, route와 session lifecycle
- [Unity ProfilerRecorder](https://docs.unity3d.com/ja/current/ScriptReference/Unity.Profiling.ProfilerRecorder.html): 런타임 성능 counter 수집
- [Unity on-device profiling](https://docs.unity3d.com/2022.2/Documentation/Manual/profiler-profiling-applications.html): 실기기 Player 연결과 profile 방법

외부 공식 자료는 목표 구조를 선택한 근거이며, 이 프로젝트에서의 실제 개선 폭은 반드시 P0 실기기 측정으로 확인한다.
