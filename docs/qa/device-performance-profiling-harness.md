# 기기 성능 프로파일링 하네스 (PBI-085 / AI-123)

`DevicePerformanceProfiler`로 세션 중 pose FPS·추론 지연·관리 메모리 peak를 샘플링하고, 종료 시 JSON 리포트를 저장한다.  
앱 내 디버그 버튼으로 60초 smoke / 10분 acceptance 벤치를 실행할 수 있다.

## 사용 방법

1. **빌드**
   - 프로파일링용: `AI Healthcare Coach/Build/iOS Development Build` → `Build/iOS` (Autoconnect Profiler OFF, 수동 IP 연결).
   - 일반 실행·대조: `AI Healthcare Coach/Build/iOS Release Build` → `Build/iOS-Release`.
   - iOS Development hang·검정 화면 주의사항은 [ios-black-screen-editor-vs-device.md](../ios-black-screen-editor-vs-device.md) §6 을 따른다.
2. **앱에서 세션 화면까지 이동**한 뒤 START로 추적을 켠다(권장). 벤치는 START와 독립이지만, pose 지표는 추적이 켜져 있어야 의미가 있다.
3. 세션 화면 하단 디버그 행에서 **60초** 또는 **10분**을 누른다. 진행 중에는 경과 시간·pose FPS·inference ms·memory MB가 표시된다.
4. 자동 종료를 기다리거나 **중지**로 수동 종료한다. 완료 시 `Saved <파일명> · PASS|FAIL|SMOKE` 상태가 표시된다.
5. 기기에서 JSON을 회수한다.

## 저장 경로

- 디렉터리: `Application.persistentDataPath/performance/`
- 파일명: `perf_bench_{benchKind}_{yyyyMMdd_HHmmss}_{deviceModelSanitized}.json`
- 예: iOS Documents 하위 `performance/`, Android는 앱 persistent data 하위 동일 폴더.

## 60초 vs 10분 acceptance

| benchKind | 길이 | acceptance.applicable | 의미 |
| --- | --- | --- | --- |
| `60s` | 60초 | `false` | smoke 전용. 메트릭은 저장되나 10분 합격 판정은 적용하지 않음(`SMOKE`). |
| `10m` | 600초 | `true` | `PerformanceAcceptanceEvaluator` 실행. PASS/FAIL. |

10분 합격 기준(요약): 약 10분 완료, 평균 pose FPS ≥ 10, 평균 inference ≤ 100 ms, 초당 drop 과다 없음, low-memory 신호 없음.

## JSON schema (v1)

주요 필드: `schemaVersion`, `pbi` (`PBI-085`), `benchKind`, `deviceModel`, `operatingSystem`, `unityVersion`, `result` (`PerformanceBenchmarkResult`), `acceptance` (`applicable`, `passed`, `reasons[]`), `savedAtUtc`, `savedPath`.

## 씬 / UI 배선

- `Main` 씬 `Coach Runtime`에 `DevicePerformanceProfiler` 부착.
- `MobileWorkoutPrototypeView`에 `performanceProfiler` 참조, `showPerformanceBenchControls`로 디버그 행 표시 여부 제어(기본 ON).
- 플레이 모드에서 프로파일러가 없으면 동일 GameObject에 `AddComponent`로 생성한다.

## Out of scope

- LowSpec 자동 전환
- Xcode Instruments / Unity Profiler CPU·GPU 상세 수집 자동화
- 단말 매트릭스 실측 결과 기입(외부 증거 별도 필요; `device-matrix.md` 참고)
- 프로덕션 UX로의 승격(현재는 세션 화면 compact debug 행)
