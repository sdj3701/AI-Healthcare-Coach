# 기능 테스트 시나리오

대상: PBI-093, PBI-094

자동 결정론 검사는 Unity Editor 메뉴 `AI Healthcare > Run Deterministic QA Suite` 또는 배치 메서드 `Rag.Healthcare.Editor.HealthcareQaSuite.RunBatch`로 실행한다.

| ID | 시나리오 | 합격 기준 |
| --- | --- | --- |
| QA-POSE-01 | 정상 전신 합성 좌표 | 33 landmarks, 캘리브레이션 성공, 바닥선 유효 |
| QA-POSE-02 | 낮은 confidence | 자세 판정 중지, 리플레이 흐림/참고 라벨 |
| QA-CAMERA-01 | 어두운 프레임 | 조명 개선 안내 표시 |
| QA-PRIVACY-01 | 오프라인 운동 중 RemoteApi 선택 | 네트워크 provider 거부 |
| QA-REPLAY-01 | 절차형 강사 스쿼트 | 렌더 가능한 관절 프레임 생성 |
| QA-PAYWALL-01 | 구독 없음 | 안전 피드백·삭제·기본 운동 사용 가능 |
| QA-PERF-01 | 정상 10분 결과 fixture | 성능 게이트 통과 |
| QA-MP-01 | MediaPipe 설치 | 패키지와 pose model 검증 통과 |

실기기 수동 시나리오는 카메라 허용/거부, 앱 백그라운드 복귀, 10분 세션, 데이터 삭제 후 파일 부재, 비행기 모드 운동 완료를 포함한다. 테스트 기록에는 앱 버전, 콘텐츠 버전, 기기, OS, 결과, 증거 경로와 결함 ID를 남긴다.
