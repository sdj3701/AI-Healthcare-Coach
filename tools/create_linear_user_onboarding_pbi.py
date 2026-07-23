import os
import requests

ENV_PATH = "/Users/sindongju/AI-Healthcare-Coach/.env"
api_key = None
if os.path.exists(ENV_PATH):
    with open(ENV_PATH, "r") as f:
        for line in f:
            if line.startswith("LINEAR_API_KEY="):
                api_key = line.strip().split("LINEAR_API_KEY=")[1]

if not api_key:
    print("Error: LINEAR_API_KEY not found in .env")
    exit(1)

headers = {
    "Content-Type": "application/json",
    "Authorization": api_key
}

# 1. Team ID 및 State ID 조회
query_teams_and_states = """
query {
  teams {
    nodes {
      id
      name
      states {
        nodes {
          id
          name
          type
        }
      }
    }
  }
}
"""

res = requests.post("https://api.linear.app/graphql", headers=headers, json={"query": query_teams_and_states})
data = res.json()

team_id = None
todo_state_id = None

if "data" in data and "teams" in data["data"] and len(data["data"]["teams"]["nodes"]) > 0:
    team = data["data"]["teams"]["nodes"][0]
    team_id = team["id"]
    for s in team["states"]["nodes"]:
        if s["type"] in ["unstarted", "backlog"] or s["name"].lower() in ["todo", "backlog", "in progress"]:
            todo_state_id = s["id"]
            if s["name"].lower() == "in progress":
                break

if not team_id or not todo_state_id:
    print(f"Failed to find team or state ID: {data}")
    exit(1)

print(f"Team ID: {team_id}, State ID: {todo_state_id}")

create_mutation = """
mutation IssueCreate($input: IssueCreateInput!) {
  issueCreate(input: $input) {
    success
    issue {
      id
      identifier
      title
      url
    }
  }
}
"""

description = """## 📋 사용자/업무 스토리
사용자로서 앱을 처음 실행했을 때 내 신체 정보, 운동 숙련도, 부상 이력을 등록하여, 내 건강 상태와 부상 부위에 맞는 안전한 관절 가동 범위(ROM) 및 맞춤형 운동 가이드/추천을 제공받고 싶다.

## 🔍 상세 구현 요구사항 (Technical Details)
- **수집 데이터 항목**:
  1. **기본 신체 데이터**:
     * 나이, 성별, 키, 몸무게
     * *(TODO: InBody API / Apple HealthKit / Google Fit 연동 연동 훅 인터페이스 `IHealthDataProvider` 설계)*
  2. **운동 및 건강 프로필 데이터**:
     * **부상 이력**: 어깨, 허리, 무릎, 목 등 과거/현재 통증 및 수술 이력
     * **운동 목적**: 체중 감량, 근력 강화, 자세 교정, 재활 등
     * **운동 장소/기구**: 홈트레이닝(맨몸), 헬스장(바벨/덤벨/머신)
     * **주당 운동 횟수**: 1~2회, 3~4회, 5회 이상
     * **운동 숙련도**: 초보자, 중급자, 상급자
- **주요 모듈 및 시스템 연동**:
  * `OnboardingStatusManager`: 최초 앱 실행 시 온보딩 미완료 유무 판단 및 프로필 작성 화면으로 분기 처리.
  * `UserProfileData`: 사용자 신체/운동 프로필 데이터 모델 클래스 정의 및 암호화 로컬 DB 저장.
  * `PersonalizedRomEvaluator`: 수집된 부상 이력 및 숙련도를 바탕으로 관절 가동 범위(ROM) 안전 임계값(Threshold) 및 운동 추천 알고리즘 연동.
- **참조 계획서**:
  * [plan_user_onboarding_health_profile.md](file:///Users/sindongju/AI-Healthcare-Coach/plans/plan_user_onboarding_health_profile.md)

## 🧪 수용 기준 (Acceptance Criteria)
- 앱 최초 실행 시 온보딩 설문 화면이 팝업되며, 1단계(신체 정보) 및 2단계(운동 이력/부상/숙련도) 정보 입력이 정상 작동함.
- 수집된 데이터가 로컬 DB에 안전하게 저장되며, 다음 진입 시 온보딩 화면이 생략되고 맞춤형 운동 가동 범위 파라미터가 자세 분석 엔진에 바인딩됨.
- 무릎/허리 부상 사용자의 경우 스쿼트 뎁스 안전 보정값이 자동으로 적용됨.

---
* **원본 ID:** PBI-110
* **모듈:** Onboarding / User Profile & Personalization
* **MoSCoW:** Must
* **단계:** MVP W7-8
* **담당 역할:** Unity/C# 개발자 & 데이터 모델러
"""

variables = {
    "input": {
        "title": "PBI-110 · 신규 사용자 최초 실행 시 건강 상태/운동 이력 프로필 수집 및 맞춤형 가동 범위(ROM) 데이터 모델 구축",
        "description": description,
        "teamId": team_id,
        "stateId": todo_state_id
    }
}

response = requests.post("https://api.linear.app/graphql", headers=headers, json={"query": create_mutation, "variables": variables})
res_data = response.json()
print("Linear Creation Response:", res_data)
