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

# Mutation to create issue
create_mutation = """
mutation IssueCreate($input: IssueCreateInput!) {
  issueCreate(input: $input) {
    success
    issue {
      id
      title
    }
  }
}
"""

description = """## 📋 사용자/업무 스토리
사용자로서 3D 아바타 리플레이를 볼 때 내가 어떤 동작과 관절을 잘못 수행했는지 직관적으로 인지하고 싶다.

## 🔍 상세 구현 요구사항 (Technical Details)
- **대상 이관 경로**: Assets/Scripts/RagHealthcare/Pose/Rendering/
- **핵심 클래스**:
  * `PoseJsonReplayPlayer`: JSONL 세션 파일에서 피드백(`type: "feedback"`) 라인을 추가 파싱하고, 피드백 발생 시점부터 2.5초(2500ms) 동안 해당 프레임에 메시지를 매핑
  * `PoseAvatar3DPreview` (`PoseAvatar3DRenderer`): 오류가 발생한 관절의 Sphere 스케일을 1.4배 키우고, 관절 및 연관 뼈대(Bone Segment)를 빨간색 재질(`redMaterial`)로 렌더링
- **참조 설계서 및 계획서**:
  * [implementation_plan.md](file:///Users/sindongju/.gemini/antigravity/brain/253b94d0-3253-4385-9f24-6c88ba71958b/implementation_plan.md)
  * [walkthrough.md](file:///Users/sindongju/.gemini/antigravity/brain/253b94d0-3253-4385-9f24-6c88ba71958b/walkthrough.md)

## 🧪 수용 기준 (Acceptance Criteria)
- 리플레이 파일 내 피드백 이벤트의 타임스탬프와 동기화하여 해당 에러 관절이 빨간색으로 선명하게 강조되어 표시됨.
- 에러 상태가 끝나면(2.5초 경과 후) 관절과 뼈대가 다시 원래의 정상 색상으로 복구됨.

---
* **원본 ID:** PBI-107
* **모듈:** Replay
* **MoSCoW:** Must
* **단계:** MVP W7-8
* **담당 역할:** Unity 개발자
* **의존성 PBI:** PBI-068
* **출처/근거:** §9.2, Table 10
"""

variables = {
    "input": {
        "title": "PBI-107 · 3D 아바타 리플레이 오류 관절 빨간색 하이라이트",
        "description": description,
        "teamId": "58aa69b2-70d1-497b-9a0b-488653e3a488",
        "stateId": "639984dc-8931-4c15-a973-c0fb2c46c1cc" # Done state ID
    }
}

response = requests.post("https://api.linear.app/graphql", headers=headers, json={"query": create_mutation, "variables": variables})
print(response.json())
