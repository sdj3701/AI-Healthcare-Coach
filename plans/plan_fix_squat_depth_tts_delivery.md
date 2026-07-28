# 스쿼트 깊이 원인별 TTS 전달 누락 수정 계획

## 확인된 원인

- `RealtimePoseRuleEngine`은 깊이 안내 후보를 생성하는 즉시 `HasIssuedShallowDepthFeedbackInCurrentRep`를 `true`로 설정한다.
- 실제 TTS 전달은 그 이후 `FeedbackPrioritizer`의 동일 안내 제한과 전역 간격 제한을 통과해야 한다.
- 다른 자세 후보가 우선 선택되거나 전역 간격·중복 제한에 걸리면 깊이 안내는 TTS로 전달되지 않지만, 반복 잠금은 이미 설정되어 다음 프레임에서 재시도하지 않는다.

## 수정 범위

- 규칙 엔진에서는 깊이 안내 후보만 만들고 반복 잠금을 설정하지 않는다.
- `FeedbackPrioritizer`를 통과하고 `PoseFeedbackJsonReceiver`가 음성 요청을 받아들인 뒤에만 해당 반복의 깊이 안내를 완료 처리한다.
- `PoseFeedbackJsonReceiver.ReceiveFeedback`이 실제 음성 요청 수락 여부를 호출자에게 반환하도록 조정한다.
- 기존 정책은 유지한다.
  - 반복당 깊이 안내 최대 1회
  - 깊이를 교정하거나 Standing으로 돌아오면 오래된 대기 안내 취소
  - 1차/2차 실패 원인별 문구 유지

## 검증

- [x] 전역 음성 간격 또는 다른 우선순위 후보 때문에 깊이 안내가 선택되지 않으면 다음 Bottom 프레임에서 다시 후보가 생성된다.
- [x] 깊이 안내가 TTS에 수락된 뒤에는 같은 반복에서 다시 생성되지 않는다.
- [x] 1차 높이 실패와 2차 깊이 실패 문구가 그대로 TTS에 전달된다.
- [x] 두 단계 통과 후에는 깊이 안내가 생성되지 않는다.
- [x] Unity 컴파일과 Healthcare QA Suite가 통과한다.

## 완료 결과

- `FeedbackPrioritizer`를 후보 선택과 쿨다운 확정의 2단계로 분리했다.
- `PoseFeedbackJsonReceiver`가 새 TTS 요청이 실제로 대기열에 등록된 경우에만 `true`를 반환하도록 했다.
- 깊이 안내 반복 잠금은 위 반환값이 `true`인 경우에만 설정한다.
- Unity `6000.3.18f1` 임시 프로젝트 복사본에서 `Rag.Healthcare.Editor.HealthcareQaSuite.RunBatch`를 실행했고 `AI_HEALTHCARE_QA_PASSED`를 확인했다.
