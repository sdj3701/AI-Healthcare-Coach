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
