# 운동 루틴 및 챌린지 생성 기능 상세 기획 및 설계서

본 문서는 **AI Healthcare Coach** 내에서 사용자가 개인화된 운동 목표를 달성하고, 운동 전문가(트레이너)가 제공하는 감수 완료된 콘텐츠를 유료 구독 형태로 제공하기 위한 **운동 루틴 및 챌린지 생성/관리 기능(PBI-084)**의 세부 기획 및 기술 설계서이다.

---

## 1. 개요 및 설계 목표
- **기능명**: 온디바이스 개인 맞춤형 운동 루틴 및 전문가 챌린지 생성 엔진
- **주요 대상 PBI**: 
  - [PBI-084](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L86) (전문가 감수 루틴/챌린지 콘텐츠)
  - [PBI-100](file:///Users/sindongju/AI-Healthcare-Coach/docs/product/freemium-and-entitlements.md#L5) / [PBI-101](file:///Users/sindongju/AI-Healthcare-Coach/docs/product/freemium-and-entitlements.md#L11) (Freemium 기능 경계 및 Entitlement)
  - [PBI-105](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L95) (규제/임상 검토 전 재활 치료 루틴 제외)
  - [PBI-064](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L59) (진단·처방·완치 금칙 필터)
  - [PBI-082](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L75) (코치 과제/피드백 로컬 내보내기)
  - [PBI-058](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L53) (운동 데이터 네트워크 비활성 검증)
- **설계 목적**:
  1. 사용자가 여러 운동 종목(스쿼트, 런지 등)과 세트수, 횟수, 휴식 시간을 묶어 **단일 루틴(Routine) 혹은 다일 챌린지(Challenge)** 형태로 커스텀 생성하고 순차적으로 수행하게 함.
  2. 기획된 모든 콘텐츠와 저장소는 **온디바이스(Local-Only) 및 오프라인 우선** 원칙을 고수하여 개인 프라이버시를 완벽히 격리함.
  3. 의료기기 규제 게이트를 준수하여 **재활·치료·처방 목적의 표현을 완벽히 격리**하고, 일반 피트니스 범위로만 한정하여 동작함.

---

## 2. 시스템 아키텍처 및 데이터 스키마

### 2.1 운동 루틴 데이터 스키마 (`WorkoutRoutine`)
운동 루틴은 JSON 포맷으로 직렬화되어 로컬 샌드박스 영역에 저장된다.

```json
{
  "routineId": "ur_9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "title": "하체 강화 기초 루틴",
  "description": "올바른 무릎 정렬에 집중하며 대퇴사두근과 둔근을 활성화하는 피트니스 루틴입니다.",
  "difficulty": "Beginner",
  "isPredefined": false,
  "isPremium": false,
  "creatorName": "User_Custom",
  "version": 1,
  "safetyGuidelines": "스쿼트 동작 중 무릎에 심한 통증이 느껴지거나 어지러움이 생기면 즉시 중단하십시오.",
  "exercises": [
    {
      "exerciseId": "squat",
      "targetReps": 15,
      "targetSets": 3,
      "restIntervalSeconds": 45,
      "minKneeSymmetryScore": 0.8,
      "instructionOverride": "내려갈 때 무릎이 안쪽으로 모이지 않도록 발끝 방향과 정렬하세요."
    },
    {
      "exerciseId": "lunge",
      "targetReps": 10,
      "targetSets": 2,
      "restIntervalSeconds": 30,
      "minKneeSymmetryScore": 0.75,
      "instructionOverride": "앞 무릎이 발끝보다 너무 앞으로 나가지 않도록 엉덩이를 수직으로 내리세요."
    }
  ],
  "metadata": {
    "createdAt": 1787220493,
    "lastModified": 1787220493,
    "expertApproved": false,
    "expertSignOffDate": null
  }
}
```

### 2.2 클래스 구조 정의 (C# Unity)

```csharp
using System;
using System.Collections.Generic;

namespace RagHealthcare.Product
{
    public enum WorkoutDifficulty
    {
        Beginner,
        Standard,
        Advanced
    }

    [Serializable]
    public class RoutineExerciseItem
    {
        public string exerciseId;            // squat, lunge, pushup, plank 등
        public int targetReps;               // 목표 횟수 (예: 15)
        public int targetSets;               // 목표 세트수 (예: 3)
        public int restIntervalSeconds;      // 세트 간 휴식 시간 (초)
        public float minKneeSymmetryScore;   // 목표 대칭도 점수 (옵션)
        public string instructionOverride;   // 개별 팁 오버라이드
    }

    [Serializable]
    public class WorkoutRoutine
    {
        public string routineId;
        public string title;
        public string description;
        public WorkoutDifficulty difficulty;
        public bool isPredefined;            // 시스템 프리셋 여부
        public bool isPremium;               // 유료 에디션 전용 여부
        public string creatorName;
        public int version;
        public string safetyGuidelines;      // 시작 전 주의 문구
        public List<RoutineExerciseItem> exercises = new List<RoutineExerciseItem>();
        
        // 메타데이터 및 전문가 서명 기록
        public long createdAt;
        public long lastModified;
        public bool expertApproved;
        public string expertSignOffDate;
    }
}
```

---

## 3. 온디바이스 개인정보 보호 및 오프라인 공유 아키텍처

- **로컬 격리 저장**:
  - 생성된 루틴 파일들은 OS 샌드박스의 `Application.persistentDataPath/Routines/` 내에 개별 JSON 파일로 저장된다.
  - 네트워크 API로 루틴 목록을 전송하거나 외부 클라우드로 백업하지 않는다 (**[PBI-058](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L53) 만족**).
- **피어 투 피어(P2P) 코치 과제 연동 (PBI-082)**:
  - 트레이너가 회원을 위해 루틴을 전달하거나, 회원이 자신의 루틴 세션 결과 리포트를 코치에게 전달하고 싶은 경우 **로컬 내보내기/가져오기(Export/Import) 파일 스키마**를 제공한다.
  - **내보내기 포맷**: 암호화되지 않은 순수 `.json` 또는 바이너리 무결성 검증을 마친 `.json.gz` 파일.
  - **공유 방식**: 시스템 공유 시트(iOS Share Sheet / Android Intent)를 트리거하여 기기간 AirDrop, 이메일, 혹은 로컬 메신저 파일 전송을 활용한다.

---

## 4. 안전 가이드라인 및 규제 대응 필터 (Safety & Regulatory Gate)

### 4.1 의학적 표현 및 재활 기능 철저 배제 ([PBI-105](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L95))
본 기능은 일반 스포츠 트레이닝의 자세 교정 목적으로 설계되었으므로 의료 목적으로의 전용을 사전에 원천 차단한다.
- **기능 범위의 엄격한 분리**:
  - 치료(Therapy), 재활(Rehabilitation), 임상 처방(Clinical Prescription) 목적의 루틴은 프리셋 카탈로그에 포함하지 않으며 사용자 정의 시에도 필터링된다.
- **금칙어 필터 탑재 ([PBI-064](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L59) 연동)**:
  - 루틴의 제목(Title), 상세 설명(Description), 개별 가이드라인(InstructionOverride)을 신규 작성하거나 파일로부터 수입(Import)할 때, 텍스트 파싱을 통해 아래 금칙어를 감지하여 저장을 차단한다.
  
| 구분 | 금칙어 카테고리 | 감지 키워드 예시 (대소문자 무관) |
| --- | --- | --- |
| **의학 질환** | 관절염, 디스크, 척추측만증, 인대 파열, 오십견, 관절 수술 | 디스크, 측만증, 인대, 파열, 관절염, 거북목 교정, 수술 후, `disc`, `scoliosis`, `arthritis` |
| **의학 처방** | 치료, 처방, 재활, 임상, 진단, 완치, 물리치료 | 치료, 재활, 처방, 완치, 통증 제거, 물리치료, `rehab`, `therapy`, `cure`, `clinical`, `diagnose` |

### 4.2 전문가 감수 프로토콜의 강제 ([PBI-004](file:///Users/sindongju/AI-Healthcare-Coach/docs/governance/expert-review-protocol.md#L1) / [PBI-097](file:///Users/sindongju/AI-Healthcare-Coach/docs/linear-implementation-matrix.md#L89))
- 시스템이 기본 제공하는 프리셋 챌린지 및 루틴 콘텐츠의 경우, 데이터 구조 내 `expertApproved` 필드가 `true`이고, 승인 날짜와 서명이 포함된 서명 해시가 일치하는 경우에만 활성화된다.
- 임의 편집기나 서명되지 않은 외부 패키지를 로드할 때 시스템 프리셋으로 속이려는 조작을 차단하기 위해 무결성 해시 체크를 수행한다.

---

## 5. 비즈니스 모델 및 Freemium 범위 분리 ([PBI-100](file:///Users/sindongju/AI-Healthcare-Coach/docs/product/freemium-and-entitlements.md#L1))

`EntitlementService`는 다음과 같이 무료 가치와 유료 가치를 엄격하게 구분하여 제어한다.

- **무료 등급 (Free Tier)**:
  - 단일 운동 종류(현재 지원 중인 스쿼트)에 대한 반복 횟수/세트 목표 설정만 허용.
  - 단일 세션을 기록하고 로컬 3D 리플레이 및 기본 분석 리포트를 확인하는 것은 제한 없이 사용 가능.
  - 수동으로 1개씩 단독 실행.
- **유료/구독 등급 (Premium Tier)**:
  - **다중 운동 컴포지션(Multi-exercise Composition)**: 스쿼트 -> 런지 -> 플랭크로 이어지는 연속 서킷 루틴 구성 가능.
  - **전문가 감수 챌린지 카탈로그**: 운동 전문가들이 사전에 설계하고 감수한 7일 챌린지, 하체 정렬 코스 등의 전용 프리셋 잠금 해제.
  - **코치 피드백 내보내기**: 내 운동 데이터를 분석하여 트레이너에게 안전하고 민감 정보가 배제된 형태로 세션 공유.

---

## 6. UI/UX 화면 흐름도 및 설계 (Wireframe)

운동 루틴 기능은 기존 모바일 프로토타입 3단계 흐름(운동 선택 -> 목표 설정 -> 세션)을 확장하여 다음과 같이 동작한다.

### STEP 1. 루틴 라이브러리 및 카탈로그 (Routine Library)
- **구조**:
  - 상단: `시스템 추천 챌린지` (유료 락 아이콘 표시)
  - 하단: `나의 커스텀 루틴 목록` 및 `[+] 새 루틴 만들기` 버튼
- **인터랙션**:
  - 시스템 루틴 선택 시: 감수자 정보(예: "김코치 - 체육학 석사") 및 금칭어 위반 없음 마크 확인 가능.
  - `[+] 새 루틴 만들기` 클릭 시 루틴 편집기로 진입.

### STEP 2. 루틴 크리에이터 / 에디터 (Routine Creator)
- **입력 폼**:
  - **루틴 이름**: 텍스트 필드 (입력 후 포커스 해제 시 금칙어 자동 필터링 동작)
  - **루틴 설명**: 텍스트 필드
  - **난이도 선택**: 쉬움 / 보통 / 어려움 토글
  - **운동 종목 리스트**: 드래그 앤 드롭으로 순서 변경 가능. 종목 선택창에서 종목 추가.
  - **각 종목별 세부 세팅**: 반복 횟수(Reps), 세트 수(Sets), 세트 간 휴식 시간(Seconds) 슬라이더.
- **안전 검증 피드백**:
  - 만약 사용자가 "재활" 등의 단어를 입력하면 붉은색 경고와 함께 *“재활 및 치료 목적의 가이드는 입력할 수 없습니다.”* 라는 안내가 출력되며 저장 버튼이 비활성화됨.

### STEP 3. 루틴 세션 플레이어 (Active Session Player)
루틴 플레이를 시작하면 각 운동 사이에 **휴식 모달 및 자동 전환** 단계가 도입된다.

```text
[운동 1: 스쿼트 시작] (목표 15회 / 1세트)
   ↓ (모든 랩 완료)
[휴식 시간 카운트다운] (남은 시간: 45초) -> TTS: "45초 동안 휴식하세요."
   ↓ (타이머 완료 혹은 건너뛰기 클릭)
[운동 1: 스쿼트 시작] (목표 15회 / 2세트)
   ↓ (모든 세트 완료)
[전환 휴식 시간] -> TTS: "다음 운동은 런지입니다. 자세를 준비하세요."
   ↓ 
[운동 2: 런지 시작]
```

### STEP 4. 루틴 완료 종합 리포트 (Summary & Local LLM Report)
- 모든 운동 종목이 완료되면 종합 리포트 화면으로 전환된다.
- **시각 정보**: 종목별 평균 대칭도 점수(Knee Symmetry Score), 목표 달성율(%), 자세 경고 감지 빈도.
- **온디바이스 LLM 요약 (Gemma/LiteRT)**:
  - 로컬 LLM이 세션의 JSON 로그를 수합하여 요약문을 작성함.
  - *예시*: "금일 하체 루틴의 스쿼트 세트에서 후반부로 갈수록 좌측 무릎의 내측 쏠림이 3회 관찰되었습니다. 체력이 떨어질 때 무릎이 모이지 않도록 발끝 방향으로 펴주는 힘을 유지하는 것을 권장합니다."

---

## 7. 주요 C# API 인터페이스 설계

```csharp
namespace RagHealthcare.Product
{
    /// <summary>
    /// 로컬 스토리지에 운동 루틴을 읽고 쓰는 레포지토리
    /// </summary>
    public interface IRoutineRepository
    {
        List<WorkoutRoutine> GetAllRoutines();
        WorkoutRoutine GetRoutineById(string routineId);
        bool SaveRoutine(WorkoutRoutine routine, out string errorMessage);
        bool DeleteRoutine(string routineId);
    }

    /// <summary>
    /// 루틴 플레이어의 세션 상태 머신 인터페이스
    /// </summary>
    public interface IRoutinePlayer
    {
        void StartRoutine(string routineId);
        void PauseRoutine();
        void SkipToNextSet();
        void TerminateRoutine();
        
        // 현재 실행 중인 상태 정보 제공
        WorkoutRoutine CurrentRoutine { get; }
        RoutineExerciseItem CurrentExercise { get; }
        int CurrentSetIndex { get; }
        float RemainingRestTime { get; }
    }

    /// <summary>
    /// 유료 콘텐츠 사용 자격 및 금지 규칙 위반 여부 검증기
    /// </summary>
    public interface IWorkoutEntitlementValidator
    {
        bool CanCreateMultiExerciseRoutine();
        bool CanAccessPresetRoutine(string routineId);
        bool IsTextSafeFromMedicalClaims(string text, out string violatedKeyword);
    }
}
```

---

## 8. QA 검증 및 인수 테스트 (Verification & QA Test)

루틴 생성 기능의 올바른 작동 및 규제 위험 회피를 검증하기 위해 다음의 테스트 케이스를 QA 스크립트에 탑재한다.

### 8.1 자동화 단위 테스트 시나리오
1. **JSON 직렬화 및 역직렬화 검증**:
   - 다중 운동 구조를 가진 `WorkoutRoutine` 객체가 파일 입출력을 거친 후 원본 데이터를 유실하지 않고 올바르게 복원되는지 확인.
2. **금칙어 필터 정상 차단 검증**:
   - `IsTextSafeFromMedicalClaims("척추 측만증 재활 루틴", out var violatedKeyword)` 호출 시 결과가 `false`를 반환하고 `violatedKeyword`에 `재활` 혹은 `측만증`이 올바르게 검출되는지 확인.
3. **자격 검증 바운더리 테스트**:
   - 무료 권한 상태의 사용자가 여러 운동 종목이 혼합된 루틴 파일 수입(Import)을 시도할 때, `EntitlementService`에서 거부되고 구독 안내 팝업이 활성화되는지 확인.

### 8.2 수동/실기기 사용성 테스트 시나리오
1. **루틴 플레이 및 화면 자동 흐름 검증**:
   - 스쿼트 1세트 완료 후 화면이 실시간 휴식 대기 화면으로 정상 전환되고 카운트다운 타이머가 시작되는지 확인.
   - 휴식 시간 완료 직후 다음 세트의 포즈 트래킹 및 가이드라인 오버레이가 누락 없이 켜지는지 확인.
2. **오프라인 동작 여부 검증**:
   - 기기를 비행기 모드(Airplane Mode)로 전환한 뒤, 기기에 기 저장된 전문가 루틴을 불러와 정상적으로 운동을 마치고 온디바이스 리포트가 생성될 때까지 어떠한 네트워크 에러나 블로킹도 발생하지 않음을 검증.
3. **전문가 승인 무결성 위반 검증**:
   - 시스템 프리셋 파일의 해시 값을 수동으로 변조하여 전문가 서명을 우회하려고 시도할 때, 앱 진입 단계에서 변조된 프리셋을 카탈로그에서 자동 배제하는지 검증.
