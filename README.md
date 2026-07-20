# AI Healthcare Coach

Unity 6 기반 온디바이스 스쿼트 자세 코칭 프로토타입입니다. 카메라 원본 영상을 저장하거나 업로드하지 않고 MediaPipe 관절 좌표, 규칙 이벤트와 세션 요약으로 실시간 피드백·리포트·3D 리플레이를 제공합니다.

## 주요 모듈

- `Assets/Scripts/RagHealthcare/Product`: 온보딩, 권한, 안전 확인, 운동 카탈로그
- `Camera`, `Pose`, `Rag`: 카메라 품질, 캘리브레이션, pose provider, 규칙/RAG 피드백
- `Reports`, `Replay`, `Analytics`: 안전 제한 리포트, 좌표 기반 리플레이, 세션 추세
- `Privacy`, `Sharing`, `Monetization`: 오프라인 네트워크 가드, 만료 공유, 안전 기능 entitlement 불변식
- `Performance`, `Qa`: 저사양/배터리/메모리 정책과 결정론 QA
- `Learning`: LRN-004~016 표준 C++17 학습·RAG·에이전트 실습

## 검증

```powershell
# Unity 컴파일
& 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe' `
  -batchmode -quit -projectPath "$PWD" -logFile "$PWD\Logs\compile.log"

# 결정론 QA
& 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe' `
  -batchmode -quit -projectPath "$PWD" `
  -executeMethod Rag.Healthcare.Editor.HealthcareQaSuite.RunBatch `
  -logFile "$PWD\Logs\qa.log"

# C++ 학습 모듈
cmake -S Learning -B Learning/build
cmake --build Learning/build --config Release
ctest --test-dir Learning/build -C Release --output-on-failure
```

Linear 미완료 항목과 구현 산출물의 대응은 [docs/linear-implementation-matrix.md](docs/linear-implementation-matrix.md)를 참고하세요. 실기기, 운동 전문가, 법률 검토와 실제 베타 참가자가 필요한 항목은 코드로 결과를 가장하지 않고 실행 프로토콜과 승인 게이트로 분리했습니다.

## AI 작업 절차

기능 개발 요청은 다음 순서로 진행합니다.

1. 코드 작업을 시작하지 말고, **Claude Opus 4.8**을 사용해 기능 구현 계획을 먼저 작성합니다.
2. 사용자가 계획을 명시적으로 수락할 때까지 코드와 프로젝트 파일을 변경하지 않습니다.
3. 사용자가 계획을 수락한 후 **Grok 4.5 High Fast**을 사용해 코드 작업을 시작합니다.
4. 코드 작업과 검증을 완료하면 변경 내용을 자세한 한국어 커밋 메시지로 정리해 GitHub에 커밋하고 원격 저장소로 푸시합니다.


## 최적화 작업 절차
1. 최적화 작업 요청오면은 코드 작업을 시작하지 말고, **Claude Opus 4.8**을 사용해 최적화 문서를 작성합니다.
2. 마지막에 대답을 할 때 어떻게 구현 했는지 작성합니다.
3. 그렇게 구현 했을 때의 장점과 단점은 무엇인지 작성합니다.
4. 다른대안은 없었는지, 다른 대안과 비교한다면에 대해서도 작성합니다.
