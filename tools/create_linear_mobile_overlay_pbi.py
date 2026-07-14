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
사용자로서 카메라가 활성화되어 내 몸을 추적할 때, 내 관절들이 정확한 위치에서 실시간으로 잘 인식되고 있는지 직관적으로 모바일 화면상에서 확인하고 싶다.

## 🔍 상세 구현 요구사항 (Technical Details)
- **대상 이관 경로**: Assets/Scripts/RagHealthcare/UI/
- **핵심 클래스**:
  * `MobileWorkoutPrototypeView`:
    * `trackingController.TrackingFrameReceived` 이벤트를 구독하여 프레임 갱신 시 `previewImage.MarkDirtyRepaint()` 호출.
    * `previewImage.generateVisualContent` 콜백에 custom 2D vector drawing 로직 바인딩.
    * `GetTextureRect` 헬퍼를 통해 카메라의 실제 출력 이미지 배율(Letterbox/Pillarbox 감안)을 계산해 2D 드로잉 위치 보정.
    * `Painter2D`를 통해 녹색 선으로 뼈대(`BoneSegments`)를 연결하고, 청색(좌), 주황(우), 백색(중앙)으로 관절 점을 실시간 시각화.
- **참조 설계서 및 계획서**:
  * [implementation_plan.md](file:///Users/sindongju/.gemini/antigravity/brain/e7e57b4e-bd22-4633-b49d-b201c787da1b/implementation_plan.md)
  * [walkthrough.md](file:///Users/sindongju/.gemini/antigravity/brain/e7e57b4e-bd22-4633-b49d-b201c787da1b/walkthrough.md)

## 🧪 수용 기준 (Acceptance Criteria)
- 모바일 UI에서 START 버튼 클릭 시, 카메라 프리뷰 위에 초록색 뼈대(Bone)와 좌/우/중앙 색상별 관절(Joint) 포인트가 실시간(Pose FPS 기준)으로 오버레이됨.
- 카메라 영상 내 사람의 위치와 오버레이 스켈레톤의 위치가 어긋나지 않고 일치함.
- STOP 클릭 후 리플레이 시점에는 오버레이가 비활성화됨.

---
* **원본 ID:** PBI-108
* **모듈:** UI / Preview
* **MoSCoW:** Must
* **단계:** MVP W7-8
* **담당 역할:** Unity 개발자
* **의존성 PBI:** PBI-068
* **출처/근거:** §9.2, Table 10
"""

variables = {
    "input": {
        "title": "PBI-108 · 모바일 UI Toolkit 프리뷰에 2D 관절 추적 스켈레톤 실시간 시각화 오버레이",
        "description": description,
        "teamId": "58aa69b2-70d1-497b-9a0b-488653e3a488",
        "stateId": "639984dc-8931-4c15-a973-c0fb2c46c1cc" # Done state ID
    }
}

response = requests.post("https://api.linear.app/graphql", headers=headers, json={"query": create_mutation, "variables": variables})
print(response.json())
