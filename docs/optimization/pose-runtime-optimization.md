# 자세 인식 런타임 최적화 설계 및 검증 가이드

작성일: 2026-07-14  
대상: Unity 6000.3.18f1, `RealtimeFeedbackOrchestrator` 기반 실시간 자세 분석 경로

## 1. 목적

이 문서는 카메라에서 자세 랜드마크가 들어온 뒤 피드백이 생성되기까지의 런타임 경로를 최적화한 이유와 구현 방식을 설명한다.

이번 작업의 핵심 목표는 다음과 같다.

- 매 자세 프레임마다 반복되던 managed heap 할당을 워밍업 시점으로 이동한다.
- 중앙값 계산과 이동 거리 비교에서 불필요한 배열, 정렬, 제곱근 계산을 제거한다.
- 객체 재사용 때문에 이전 프레임 데이터가 덮어써지는 문제를 방지한다.
- 인식 정확도, 이상치 제거, EMA 평활화, rep 성공/실패 판정 결과는 유지한다.
- Windows, Android, iOS의 조건부 컴파일과 결정론적 QA를 모두 통과한다.

이번 변경은 MediaPipe 추론 자체를 빠르게 만드는 작업이 아니다. 추론 결과를 Unity C#에서 안정화하고 특징을 계산하며 규칙을 평가하고 JSONL로 기록하는 후처리 경로를 대상으로 한다.

## 2. 최적화 결과 요약

프레임 처리 경로는 다음처럼 바뀌었다.

```text
MediaPipe 원본 프레임
  -> 재사용 가능한 안정화 프레임/관절 슬롯
  -> 재사용 가능한 현재 feature
  -> 값을 복사해 보관하는 고정 window 슬롯
  -> 호출자가 소유하는 재사용 통계 객체
  -> 워밍업 후 재사용되는 feedback event/evidence
  -> stackalloc 숫자 포맷을 사용하는 JSONL 기록
```

동일한 관절 수와 규칙 조합이 반복되는 정상 실행 상태에서는 다음 객체를 매 프레임 새로 만들지 않는다.

- 안정화 결과 `JointTrackingFrame`
- 안정화 결과 `TrackedJoint[]`
- 안정화 결과의 개별 `TrackedJoint`
- 중앙값 계산용 `float[]`
- 현재 `PoseFeatureFrame`
- 윈도에 보관하는 `PoseFeatureFrame`
- `PoseWindowStats`
- 통계 순회용 iterator
- 이미 풀에 준비된 `FeedbackEvent`와 `Evidence` dictionary
- JSON에 숫자를 쓰기 위한 개별 float 문자열

단, MediaPipe provider가 생성하는 원본 프레임/관절, JSONL 한 줄을 `StreamWriter`로 넘기기 위한 최종 문자열, 실제 피드백이 선택됐을 때 생성되는 메시지 등은 이번 범위에 남아 있다.

## 3. 최적화 전 병목

기본 자세 분석 속도인 15 FPS와 33개 MediaPipe 관절을 기준으로 코드상 발생 가능한 할당을 정리하면 다음과 같다. 이 수치는 Profiler 측정값이 아니라 변경 전 코드 구조에서 계산한 객체 생성 횟수다.

| 구간 | 변경 전 동작 | 15 FPS 기준 코드상 생성량 |
| --- | --- | ---: |
| 랜드마크 안정화 출력 | 프레임 1개, 관절 배열 1개, 관절 객체 최대 33개를 매 프레임 생성 | 최대 525개 객체/초 |
| 3프레임 중앙값 | 관절마다 x/y/z용 `float[]` 3개를 생성하고 정렬 | 최대 1,485개 배열/초 |
| feature 추출 | `PoseFeatureFrame`을 매 프레임 생성 | 15개 객체/초 |
| window 통계 | `PoseWindowStats`와 `yield` iterator를 매 프레임 생성 | 약 30개 객체/초 |
| 규칙 이벤트 | 후보마다 event와 dictionary를 생성 | 자세 오류 수에 따라 변동 |
| JSON float 포맷 | 관절당 x/y/z/visibility/confidence 문자열 5개 생성 | 최대 2,475개 문자열/초 |

안정화 구간만 합치면 최대 2,010개 객체/초였고, JSON 숫자 문자열까지 포함하면 식별된 반복 할당은 약 4,500개/초 수준이었다. 실제 생성량은 누락 관절 수, confidence, 로그 설정, 발생 규칙에 따라 달라진다.

짧은 객체는 개별 생성 비용보다 누적된 GC가 더 큰 문제다. 모바일에서는 GC가 실행되는 프레임에 UI, 카메라 프리뷰, TTS 시작 타이밍이 같이 밀리면서 사용자가 자세가 떨리거나 피드백이 늦는 것처럼 느낄 수 있다.

## 4. 상세 구현

### 4.1 랜드마크 안정화 프레임과 관절 재사용

대상 파일: `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseLandmarkStabilizer.cs`

`PoseLandmarkStabilizer`는 출력 프레임 하나와 관절 배열 하나를 내부에 보관한다. 관절 수가 같으면 다음 프레임에도 같은 컨테이너를 사용하고 값만 덮어쓴다. 관절 수가 바뀌는 경우에만 배열을 다시 만든다.

관절 슬롯은 입력이 null인 경우 출력도 null로 유지한다. 이후 같은 인덱스에 관절이 다시 들어오면 별도로 보관한 관절 풀에서 해당 객체를 되살려 사용한다.

이 방식을 선택한 이유:

- orchestrator는 안정화 결과를 로그와 feature 추출에 같은 프레임 안에서 동기적으로 사용한다.
- `SessionJsonlLogger.LogFrame()`은 호출 중 JSON 문자열을 완성하므로 프레임 객체를 나중에 보관하지 않는다.
- provider의 원본 프레임을 직접 수정하지 않으므로 카메라 렌더링이나 다른 subscriber와 데이터 소유권이 충돌하지 않는다.

반환된 안정화 프레임의 수명 계약은 중요하다.

> `Stabilize()`가 반환한 프레임과 관절은 다음 `Stabilize()` 호출 전까지만 유효한 현재 프레임 view다. 장기 보관이 필요하면 값을 복사해야 한다.

### 4.2 중앙값용 배열과 정렬 제거

기존 구현은 관절별 최근 좌표를 `List<Vector3>`에 보관하고, 매 호출마다 x/y/z 배열을 만들어 `Array.Sort()`로 중앙값을 구했다.

현재 구현은 `JointState` 안에 다음 고정 슬롯을 둔다.

```text
Sample0, Sample1, Sample2
SampleCount
NextSampleIndex
```

샘플이 3개일 때 각 축의 중앙값은 다음 식으로 구한다.

```text
median(a, b, c) = a + b + c - min(a, b, c) - max(a, b, c)
```

샘플이 1개면 해당 값을, 2개면 평균을 사용한다. 따라서 중앙값 계산 중 임시 배열, 리스트 이동, 정렬이 발생하지 않는다.

장점:

- 중앙값 계산의 managed allocation이 없다.
- 고정된 세 값만 비교하므로 계산량과 실행 시간이 일정하다.
- 기존 3프레임 median + EMA 동작을 유지한다.

단점:

- 필터 창을 5프레임 이상으로 바꾸려면 슬롯 구조와 중앙값 구현을 함께 수정해야 한다.
- 범용 컬렉션보다 코드가 길고 유지보수 시 샘플 개수를 명확히 관리해야 한다.

### 4.3 거리 비교의 제곱근 제거

이상치 검사는 2D 관절 이동 거리가 `maximumNormalizedJointJump`를 넘는지 확인한다.

변경 전에는 `Vector2.Distance()`를 사용해 제곱근까지 계산했다. 현재는 아래와 같이 제곱 거리끼리 비교한다.

```text
dx² + dy² > maximumJump²
```

두 식의 판정 결과는 같지만 후자는 `sqrt`가 필요 없다. `Vector2`는 struct라 heap 할당은 아니었지만, 프레임당 최대 33회 반복되는 계산 비용을 줄일 수 있다.

### 4.4 Reset 시 워밍업 상태 유지

세션 초기화 시 관절 상태 dictionary 전체를 버리지 않고 각 `JointState`의 값만 초기화한다. 따라서 같은 관절 이름을 사용하는 다음 세션에서 state를 다시 생성하지 않는다.

장점:

- 목표 반복 수 변경이나 세션 재시작 뒤 재할당이 감소한다.

단점:

- 한 번 관찰한 관절 이름의 state는 stabilizer 수명 동안 남는다.
- 현재 MediaPipe의 고정 33개 이름에서는 메모리 영향이 매우 작지만, 임의의 동적 관절 이름을 계속 넣는 provider를 연결한다면 dictionary 크기 제한이 필요하다.

### 4.5 feature view와 window 소유권 분리

대상 파일:

- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseFeatureExtractor.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseFeatureFrame.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseWindowBuffer.cs`

`PoseFeatureExtractor`는 하나의 `workingFeature`를 재사용한다. 속도 계산에 필요한 이전 프레임은 객체 참조 대신 timestamp, hip center y, average knee angle의 scalar 값으로 보관한다.

현재 feature도 다음 `Extract()` 호출에서 덮어써지므로 분석 window가 이 참조를 그대로 저장하면 모든 슬롯이 같은 프레임을 가리키게 된다. 이를 막기 위해 `PoseWindowBuffer`가 생성될 때 capacity만큼 슬롯을 미리 만들고, `Add()`에서 `CopyFrom()`으로 값을 복사한다.

소유권은 다음처럼 구분된다.

| 객체 | 소유자 | 유효 기간 |
| --- | --- | --- |
| 현재 feature view | `PoseFeatureExtractor` | 다음 `Extract()` 전까지 |
| window feature slot | `PoseWindowBuffer` | 해당 ring slot이 다시 사용될 때까지 |
| 이전 속도 기준 | `PoseFeatureExtractor`의 scalar 필드 | `Reset()` 전까지 |

장점:

- feature 추출과 window 추가에서 프레임당 객체 생성이 없다.
- window는 mutable view를 직접 보관하지 않아 과거 통계가 안전하다.

단점:

- `PoseFeatureFrame`에 필드를 추가하면 `Reset()`과 `CopyFrom()`도 같이 수정해야 한다.
- extractor 결과를 외부 시스템이 비동기로 보관하려면 직접 복사해야 한다.

### 4.6 통계 결과와 window 순회 재사용

대상 파일:

- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseWindowStats.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/PoseWindowBuffer.cs`
- `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimeFeedbackOrchestrator.cs`

orchestrator가 `reusableStats` 하나를 소유하고 다음 overload로 계산한다.

```csharp
PoseWindowStats.Calculate(windowBuffer, ruleSettings, reusableStats)
```

계산 시작 시 모든 누적값을 `Reset()`하고 같은 객체에 다시 채운다. 기존 2개 인자 overload는 테스트나 외부 호출의 호환성을 위해 남겨 두었으며, 이 overload는 새 객체를 생성한다.

또한 통계 계산은 `RecentFrames()`의 `yield` 열거 대신 `GetChronological(index)`를 사용하는 for loop로 변경했다. ring buffer의 오래된 프레임부터 최신 프레임까지의 순서는 유지하면서 iterator 객체 생성을 피한다.

### 4.7 feedback event 풀링

대상 파일: `Assets/Scripts/RagHealthcare/Rag/Runtime/RealtimePoseRuleEngine.cs`

규칙 엔진은 지금까지 관찰한 최대 후보 수만큼 `FeedbackEvent`를 풀에 보관한다. 다음 평가에서는 `usedEventCount`를 0으로 되돌리고 기존 event를 앞에서부터 다시 채운다. `Evidence` dictionary도 `Clear()`한 뒤 현재 evidence 한 항목을 기록한다.

이벤트 재사용이 안전한 이유:

- `FeedbackPrioritizer`, `RepQualityAccumulator`, `RagRetriever`, `FeedbackComposer`, `SessionJsonlLogger`는 현재 orchestrator 호출 안에서 이벤트를 동기적으로 소비한다.
- 다음 `Evaluate()`까지 이벤트를 비동기 queue에 보관하는 현재 코드는 없다.

왼쪽/오른쪽 무릎 메시지는 문자열 보간 대신 미리 정의된 두 literal 중 하나를 선택한다. 규칙이 발생할 때마다 id와 한국어 문장을 새로 합성하지 않는다.

장점:

- 같은 규칙 조합이 반복될 때 event와 dictionary 생성이 없다.
- 규칙 후보 수가 일시적으로 늘어나도 이후에는 확보된 최대 크기를 재사용한다.

단점:

- 반환된 event list와 event는 다음 `Evaluate()` 전까지만 유효하다.
- 향후 이벤트를 비동기 전송하거나 지연 처리한다면 queue에 넣기 전에 immutable DTO로 복사해야 한다.
- 풀은 관찰한 최대 후보 수를 high-water mark로 유지한다.

### 4.8 JSONL 숫자 포맷 문자열 제거

대상 파일: `Assets/Scripts/RagHealthcare/Rag/Logging/SessionJsonlLogger.cs`

기존 `Float()`는 관절 값 하나마다 `value.ToString()` 결과 문자열을 만들었다. 33개 관절에 5개 숫자를 기록하면 프레임당 최대 165개 짧은 문자열이 생긴다.

현재 `AppendFloat()`는 stack에 48자의 임시 `Span<char>`를 만들고 `TryFormat()`으로 invariant 숫자를 직접 포맷한 뒤 `StringBuilder`에 붙인다.

```text
float -> stackalloc Span<char> -> shared StringBuilder
```

정상적인 좌표, confidence, 각도 범위는 48자 안에 들어간다. 포맷에 실패하는 비정상적으로 긴 값은 기존 `ToString()` fallback을 사용해 로그 유실을 막는다.

장점:

- 관절 숫자마다 생성되던 짧은 문자열을 제거한다.
- 소수점 구분자는 기기 locale과 무관하게 `.`을 유지한다.
- 기존 JSONL 스키마와 `0.######` 정밀도를 유지한다.

단점:

- stack 공간을 호출 중 잠깐 사용한다.
- JSONL 한 줄을 writer에 넘기기 위한 `builder.ToString()` 결과 문자열 한 개는 여전히 프레임마다 생성된다.

## 5. 의도적으로 남겨 둔 할당과 이유

### 5.1 MediaPipe provider 원본 프레임

provider가 만드는 원본 `JointTrackingFrame`, `TrackedJoint[]`, 개별 관절 객체는 이번 작업에서 재사용하지 않았다.

`JointTrackingController.TrackingFrameReceived`에는 orchestrator 외 subscriber가 있을 수 있고, `LatestFrame`이나 렌더러가 원본을 현재 프레임 이후에도 참조할 수 있다. producer 단계에서 객체를 재사용하면 한 subscriber의 과거 프레임이 다른 subscriber 처리 중 바뀌는 소유권 문제가 생길 수 있다.

이 부분을 최적화하려면 다음 중 하나를 먼저 도입해야 한다.

- 명시적인 frame lease/reference counting
- immutable native buffer와 read-only view
- subscriber별 copy 정책
- Unity Collections의 `NativeArray` 기반 전달 계약

### 5.2 JSONL 최종 line 문자열

현재 로그는 replay 기능의 입력이므로 프레임 로그를 끄지 않았다. `StringBuilder.ToString()`에서 최종 line 문자열 한 개는 남는다.

이를 더 줄이는 선택지는 다음과 같다.

- `StringBuilder` chunk를 지원하는 writer로 직접 기록
- UTF-8 byte buffer에 직접 JSON 작성
- 메인 스레드에서는 구조화된 ring buffer만 채우고 background writer가 일괄 기록

background 기록은 종료 직전 flush, 앱 강제 종료 시 데이터 손실, thread synchronization을 함께 설계해야 하므로 이번 변경에는 포함하지 않았다.

### 5.3 실제 피드백 메시지

피드백이 선택될 때 생성되는 `PoseFeedbackMessage`, RAG 검색 결과, placeholder가 적용된 최종 문장은 사용자에게 전달하거나 TTS queue에 들어가므로 현재 프레임보다 긴 수명을 가질 수 있다. 이 객체는 안전하게 풀링하려면 queue 완료 시점을 알아야 하므로 유지했다.

## 6. 정확도와 동작 보존

최적화는 다음 판정 로직을 바꾸지 않는다.

- 최근 3프레임 관절 중앙값
- EMA 기반 평활화
- normalized joint jump 이상치 보류
- 낮은 confidence의 짧은 grace
- 무릎 각도와 속도 계산
- 하강에서 상승으로 반전될 때 Bottom 인식
- 분석 window의 최소 무릎 각도를 사용한 깊이 판정
- warning/critical의 시간 누적 기반 rep 품질 판정

객체 생성 방식은 달라졌지만 입력값이 같을 때 feature와 규칙 결과는 같아야 한다.

## 7. 자동 QA

`Assets/Editor/RagHealthcare/HealthcareQaSuite.cs`에 다음 회귀 검사를 포함한다.

| 검사 | 보장하는 내용 |
| --- | --- |
| landmark jitter | 작은 흔들림이 median + EMA로 줄어드는지 확인 |
| single outlier | 한 프레임의 큰 관절 점프가 보류되는지 확인 |
| frame reuse | 워밍업 후 stabilizer가 같은 frame/array를 재사용하는지 확인 |
| window ownership | source feature를 수정해도 window의 과거 값이 바뀌지 않는지 확인 |
| stats reuse | 호출자가 전달한 통계 객체가 그대로 반환되는지 확인 |
| event pool reuse | 두 번째 규칙 평가에서 event가 재사용되는지 확인 |
| phase reversal | 멈추지 않고 전환한 스쿼트도 Bottom/rep로 인식되는지 확인 |
| minimum depth | standing 프레임 때문에 충분한 깊이가 얕다고 오판되지 않는지 확인 |
| temporal quality | 단일 warning과 persistent warning을 구분하는지 확인 |

2026-07-14 검증 결과:

- Windows Editor/Win64 스크립트 컴파일: 통과
- Android 스크립트 컴파일: 통과
- iOS 스크립트 컴파일: 통과
- 각 타깃 결정론적 QA: `AI_HEALTHCARE_QA_PASSED`
- `git diff --check`: 통과

Unity batch 실행 예시:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'D:\AI Healthcare Coach\AI-Healthcare-Coach' `
  -buildTarget Android `
  -executeMethod Rag.Healthcare.Editor.HealthcareQaSuite.RunBatch `
  -logFile 'Temp\pose-optimization-android.log'
```

Android 또는 iOS 검증 뒤 개발 환경을 Windows로 사용할 경우 `-buildTarget Win64`로 한 번 더 실행해 active build target을 복원한다.

## 8. Unity Profiler 실측 방법

코드상 객체 재사용 검사는 할당 구조가 다시 생기는 것을 잡아 주지만, 최종 성능 평가는 실제 기기 Profiler로 해야 한다.

### 8.1 권장 측정 조건

1. Development Build와 Autoconnect Profiler를 켠다.
2. Deep Profile은 끈다. Deep Profile 자체 오버헤드가 프레임 시간을 왜곡한다.
3. 카메라, MediaPipe, 자세 분석, JSONL 로그, UI, TTS를 실제 제품 설정으로 모두 켠다.
4. 앱 시작 뒤 최소 10초간 워밍업한다.
5. 서 있기 1분, 정상 스쿼트 2분, 의도적 오류 2분, 총 5분 이상 측정한다.
6. CPU Usage의 Timeline과 Hierarchy에서 `GC Alloc`, `GC.Collect`, main thread 시간을 확인한다.
7. 같은 기기와 조명에서 변경 전/후 capture를 저장해 비교한다.

### 8.2 구간별 확인 포인트

| 구간 | 기대 상태 |
| --- | --- |
| `PoseLandmarkStabilizer.Stabilize` | 관절 수가 고정된 워밍업 후 GC Alloc 0 B |
| `PoseFeatureExtractor.Extract` | 워밍업 후 GC Alloc 0 B |
| `PoseWindowBuffer.Add` | 생성된 capacity 안에서 GC Alloc 0 B |
| `PoseWindowStats.Calculate` 3인자 overload | GC Alloc 0 B |
| `RealtimePoseRuleEngine.Evaluate` | 기존 최대 후보 수 이내에서 GC Alloc 0 B |
| `SessionJsonlLogger.LogFrame` | float별 문자열은 없어야 하며 최종 line 문자열 할당은 남음 |

Profiler에서 allocation call stack을 보려면 필요한 짧은 구간에만 Call Stacks의 GC.Alloc 기록을 켠다. 전체 장시간 capture에 call stack을 켜면 측정 오버헤드와 파일 크기가 크게 늘어난다.

### 8.3 권장 초기 수용 기준

아래 값은 실측 완료 전의 초기 제품 기준이며 기기 등급에 맞게 조정해야 한다.

- 자세 후처리 핵심 5개 구간의 steady-state GC Alloc: 각각 `0 B/frame`
- 자세 추론을 제외한 후처리 main-thread p95: `4 ms 이하`
- 목표 pose 처리율: `15 FPS 이상`
- 10분 연속 운동 중 GC로 인한 main-thread pause: `10 ms 초과 없음`
- 정상 스쿼트 회귀 데이터의 correct rep 결과: 최적화 전과 동일
- 단일 랜드마크 outlier fixture: 실패 rep로 확정되지 않음

전체 프레임의 GC Alloc은 provider 원본 객체와 JSONL 최종 문자열이 남아 있으므로 0 B가 목표가 아니다. 컴포넌트별로 분리해서 판단해야 한다.

## 9. 모바일 기기 테스트 매트릭스

최소 다음 조합에서 같은 시나리오를 실행한다.

| 플랫폼 | 등급 | 확인 항목 |
| --- | --- | --- |
| Android | 저사양/4 GB RAM | GC pause, 발열, 10분 지속 FPS |
| Android | 중간 사양 | 기본 acceptance 기준 |
| Android | 고사양 | 정확도 회귀와 최고 처리율 |
| iOS | 지원 범위의 구형 기기 | thermal throttling 이후 FPS |
| iOS | 최신 기기 | 기본 acceptance 기준 |

각 기기에서 카메라 해상도, MediaPipe model complexity, target FPS, 로그 활성화 여부를 기록한다. 설정이 다르면 capture끼리 직접 비교하면 안 된다.

## 10. 튜닝 가이드

### 현재 관절 추적 품질 기본값 (2026-07-22)

- 추론 입력: `enableInferenceDownscale=false` → 촬영과 동일 `640×480` full webcam
- Pose FPS: `12` (`mobilePoseFps` / Editor `targetPoseFps` / `expectedPoseFps`)
- MediaPipe confidence: detection / presence / tracking 모두 `0.40`
- 안정화: `maximumNormalizedJointJump=0.12`, iOS `landmarkSmoothingAlpha=0.55`(비-iOS `0.35`)

### 흔들림은 줄었지만 반응이 느린 경우

- `landmarkSmoothingAlpha`를 조금 높인다.
- 한 번에 `0.05` 이하로 조정하고 정상/빠른 스쿼트를 같이 확인한다.
- 필터 창 3개는 현재 고정 구현이므로 창 크기를 바꾸려면 stabilizer 코드를 함께 수정한다.

### 실제 빠른 움직임이 outlier로 보류되는 경우

- `maximumNormalizedJointJump`를 소폭 높인다.
- 카메라 가까이에서 관절의 화면 이동량이 커지는 조건을 함께 테스트한다.
- threshold를 너무 높이면 실제 추정 오류가 각도 급변으로 전달될 수 있다.

### GC가 여전히 큰 경우

Profiler call stack으로 먼저 소유 구간을 구분한다.

1. provider 원본 frame/joint 생성인지 확인한다.
2. `SessionJsonlLogger`의 최종 line 문자열 크기를 확인한다.
3. 피드백/TTS가 발생한 프레임인지 확인한다.
4. UI의 문자열 갱신이나 layout rebuild인지 확인한다.
5. RAG 검색 결과 list와 placeholder 문장 생성인지 확인한다.

프레임 로그가 원인이라도 replay 요구사항이 있으면 단순히 `logFrames=false`로 끄기보다 byte buffer/background writer 설계를 먼저 검토한다.

## 11. 유지보수 규칙

다음 변경 시 반드시 같이 확인해야 한다.

- `PoseFeatureFrame` 필드 추가: `Reset()`과 `CopyFrom()` 갱신
- stabilizer 반환값을 비동기 보관: 별도 deep copy 또는 lease 도입
- rule event를 queue에 저장: immutable event copy 도입
- window capacity 런타임 변경: 새 buffer 생성에 따른 일회성 allocation 허용 여부 확인
- 관절 수나 이름이 동적으로 증가: state dictionary와 joint pool 상한 검토
- JSON 숫자 포맷 변경: replay parser와 locale 독립성 확인

객체 재사용 코드는 성능을 얻는 대신 수명 계약이 중요하다. 새 subscriber나 비동기 처리를 추가할 때는 “이 참조가 다음 프레임에도 동일한 값이어야 하는가?”를 먼저 확인해야 한다.

## 12. 롤백 기준

다음 문제가 발생하면 해당 최적화를 부분 롤백하거나 소유권 구조를 다시 설계한다.

- 이전 프레임을 보관하는 기능에서 값이 최신 프레임으로 바뀜
- 비동기 logger/TTS/RAG가 재사용 event를 늦게 참조함
- `PoseFeatureFrame` 새 필드가 window copy에서 누락됨
- Android/iOS AOT 환경에서 숫자 포맷 결과가 replay parser와 달라짐
- Profiler에서 최적화 코드의 복잡성 대비 유의미한 GC/CPU 개선이 확인되지 않음

롤백은 전체 변경을 한 번에 되돌리기보다 stabilizer, feature/window, stats, event pool, logger 포맷 단위로 분리하는 편이 안전하다.

## 13. 관련 문서

- `docs/current-pose-decision-logic.md`: 현재 자세 판정, 안정화, rep 품질 기준
- `docs/MediaPipeTroubleshooting.md`: MediaPipe 설치 및 런타임 문제 해결
- `docs/TestMediaPipeplan.md`: MediaPipe 테스트 계획
- `docs/module-architecture.md`: 전체 모듈 구조

