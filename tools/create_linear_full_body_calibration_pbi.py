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
사용자로서 운동을 시작하기 전 카메라 프레임 내 내 전신이 올바르게 잡혔는지 사전에 측정/확인하여, 운동 중 관절 랜드마크 튐이나 각도 왜곡 없이 안정적으로 실시간 코칭 피드백을 받고 싶다.

## 🔍 상세 구현 요구사항 (Technical Details)
- **대상 이관 경로**: `Assets/Scripts/RagHealthcare/Pose/`
- **핵심 모듈 및 개발 내용**:
  1. **Workout Tracking State Machine 설계**:
     * 상태 분류: `ReadyForCalibration` ➔ `CountingDown` ➔ `InWorkout` ➔ `PausedOutOfFrame`
  2. **전신 감지 캘리브레이션 (Calibration) 검증 로직**:
     * MediaPipe 33개 랜드마크 중 주요 관절(머리, 어깨, 골반, 무릎, 발목)의 `Visibility / Presence Score`가 0.85f 이상인지 확인.
     * 전신 충족 조건이 1.5초간 안정적으로 유지될 경우 `CountingDown`으로 전이.
  3. **실루엣 가이드 & UI 오버레이 연동**:
     * 전신 가이드 영역 표시 및 전신 감지 토스트 메시지 안내 ("카메라 뒤로 물러서주세요" ➔ "전신 감지 완료").
  4. **Out of Frame 예외 처리**:
     * 운동 중 관절 가시성 저하 시 자동 일시정지 및 Ready 가이드 재진입.
- **참조 계획서**:
  * [plan_full_body_calibration.md](file:///Users/sindongju/AI-Healthcare-Coach/plans/plan_full_body_calibration.md)

## 🧪 수용 기준 (Acceptance Criteria)
- 사용자가 카메라 전신 영역에 정상 위치하면 1.5초 후 "전신 감지 완료" 메시지와 함께 3초 카운트다운 진입.
- 사용자가 카메라 가까이에 있거나 관절 일부가 잘린 경우 "카메라 뒤로 물러서주세요" 안내 메시지 노출 및 카운트다운 보류.
- 운동 중 사용자가 화면 밖으로 벗어나면 운동 카운트다운/피드백이 일시정지되고 가이드 화면으로 자동 전환.

---
* **원본 ID:** PBI-109
* **모듈:** Pose / Calibration State Machine
* **MoSCoW:** Must
* **단계:** MVP W7-8
* **담당 역할:** Unity 개발자 / AI 엔지니어
"""

variables = {
    "input": {
        "title": "PBI-109 · 운동 시작 전 전신 측정 및 캘리브레이션 3단계 상태 머신(Ready ➔ Countdown ➔ Workout) 구현",
        "description": description,
        "teamId": team_id,
        "stateId": todo_state_id
    }
}

response = requests.post("https://api.linear.app/graphql", headers=headers, json={"query": create_mutation, "variables": variables})
res_data = response.json()
print("Linear Creation Response:", res_data)
