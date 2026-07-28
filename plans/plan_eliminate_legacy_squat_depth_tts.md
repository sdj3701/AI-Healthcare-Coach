# 스쿼트 깊이 기존 TTS 충돌 제거 계획

## 실기기에서 확인된 원인

- 최신 아이폰 세션 로그에서 요청한 1차 실패 문장은 `squat_depth_shallow` 규칙으로 정상 전달됐다.
- 같은 세션에서 별도의 과도한 깊이 규칙인 `squat_depth_deep`가 반복 발생했다.
- `squat_depth_deep` 전용 RAG 지식이 없어 기존 `squat_depth_shallow` 지식 문서가 검색됐고, 그 결과 과도한 깊이 이벤트의 문장이 “조금 더 깊게 내려간 뒤 최저점에서 잠시 유지해 주세요.”로 잘못 교체됐다.

## 수정 범위

- 런타임 RAG 지식에서 “최저점에서 잠시 유지”라는 기존 문장을 제거하고, `squat_depth_shallow`의 기본 문장을 요청한 1차 실패 문장으로 맞춘다.
- `squat_depth_deep` 전용 지식 항목을 추가해 과도한 깊이 규칙이 발생해도 얕은 깊이 안내를 가져오지 않도록 한다.
- `squat_depth_deep` 이벤트는 규칙 엔진이 만든 전용 문장을 우선 사용하게 해 RAG 검색 결과가 잘못 덮어쓰지 못하도록 한다.
- 기존 1차/2차 실패 문장은 정확히 유지한다.
  - 1차: `엉덩이와 무릎 높이가 충분히 가까운 상태가 연속으로 확인되지 않았습니다. 엉덩이를 조금 더 내려 주세요.`
  - 2차: `무릎 각도가 135도보다 크고 골반 하강량도 기준보다 작습니다. 조금 더 깊게 내려 주세요.`

## 검증

- [x] 1차 높이 실패 이벤트와 최종 TTS 문장이 요청 문장과 일치한다.
- [x] 1차 통과·2차 깊이 실패 이벤트와 최종 TTS 문장이 요청 문장과 일치한다.
- [x] 과도한 깊이 이벤트가 발생해도 “최저점에서 잠시 유지” 문장이 선택되지 않는다.
- [x] 런타임 RAG 지식과 버전 규칙 파일에 기존 “최저점에서 잠시 유지” 문장이 남아 있지 않다.
- [x] Unity 컴파일 및 Healthcare QA Suite가 통과한다.
- [x] iOS 빌드를 갱신한 뒤 실기기에서 변경된 StreamingAssets가 포함됐는지 확인한다.

## 완료 결과

- `squat_depth_deep` 이벤트는 전용 템플릿을 우선하도록 변경해 다른 RAG 문장이 덮어쓰지 못하게 했다.
- `squat_depth_shallow`와 `squat_depth_deep` 지식을 분리하고 규칙 카탈로그 버전을 `2026.07.27.3`으로 갱신했다.
- Unity `6000.3.18f1` 임시 프로젝트에서 Healthcare QA Suite를 실행해 `AI_HEALTHCARE_QA_PASSED`를 확인했다.
- 새 iOS Xcode 프로젝트를 `/Users/sindongju/aibuild`에 생성하고 기존 빌드는 `/Users/sindongju/aibuild.before-depth-tts-20260727`에 보존했다.
- Xcode Debug 빌드를 완료하고 iPhone XS Max에 `com.sindongju.aihealthcare` 앱을 설치했다.
