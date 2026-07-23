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
사용자로서 스쿼트 수행 시 유연성 부족으로 연속 2~3회 자세 실패 판정을 받을 때, 시스템이 내 관절 가동 범위(ROM) 한계를 자동으로 인식하여 안전장치 기준 내에서 목표 깊이를 실시간으로 조정받고 싶다.

## 🔍 상세 구현 요구사항 (Technical Details)
- **대상 이관 경로**: `Assets/Scripts/RagHealthcare/Rag/Runtime/`
- **핵심 모듈 및 연동 기능**:
  1. **SquatPeakDepthTracker**:
     * 하강 후 상승으로 전환되는 시점의 변곡점 최저 각도(Knee Peak Angle)를 링버퍼에 기록.
  2. **AdaptiveRomCalibrator**:
     * 2~3회 연속 뎁스 미달 판정 시 사용자의 최저 각도 평균(예: 125°)을 산출.
     * `RealtimePoseRuleSettings.bottomKneeAngle` 목표 수치를 런타임에 개인 맞춤형으로 동적 업데이트.
  3. **안전장치 (Safety Guardrail Boundary)**:
     * 무릎 각도 135° 초과(너무 얕은 부정 자세) 시 가동 범위 보정 대상에서 제외하고 경고 피드백 유지.
  4. **적응형 안내 UX & TTS**:
     * "사용자님의 관절 가동 범위에 맞춰 목표 깊이가 안전하게 조정되었습니다!" 피드백 노출.
- **참조 계획서**:
  * [plan_adaptive_rom_safety_guardrail.md](file:///Users/sindongju/AI-Healthcare-Coach/plans/plan_adaptive_rom_safety_guardrail.md)

## 🧪 수용 기준 (Acceptance Criteria)
- 2~3회 연속 뎁스 미달 시 사용자의 최저 굴곡 각도를 파악하여 실시간으로 목표 threshold 수치가 안전 완화됨.
- 무릎 각도 135° 이상의 극단적으로 얕은 자세는 안전장치(Guardrail)에 걸려 보정되지 않고 기본 경고 피드백을 출력함.
- 수치 보정 발생 시 사용자 안내 음성/토스트 메시지가 상단에 출력됨.

---
* **원본 ID:** PBI-111
* **모듈:** Pose Engine / Adaptive Safety Guardrail
* **MoSCoW:** Must
* **단계:** MVP W7-8
* **담당 역할:** Unity/C# AI 알고리즘 개발자
"""

variables = {
    "input": {
        "title": "PBI-111 · 스쿼트 관절 가동 범위(ROM) 실시간 동적 보정 및 최소 안전장치(Safety Guardrail) 엔진 구현",
        "description": description,
        "teamId": team_id,
        "stateId": todo_state_id
    }
}

response = requests.post("https://api.linear.app/graphql", headers=headers, json={"query": create_mutation, "variables": variables})
res_data = response.json()
print("Linear Creation Response:", res_data)
