import os
import re
import requests

# 환경 변수 및 경로 설정
ENV_PATH = "/Users/sindongju/AI-Healthcare-Coach/.env"
PROJECT_DIR = "/Users/sindongju/AI-Healthcare-Coach"

# API Key 로드
api_key = None
if os.path.exists(ENV_PATH):
    with open(ENV_PATH, "r") as f:
        for line in f:
            if line.startswith("LINEAR_API_KEY="):
                api_key = line.strip().split("LINEAR_API_KEY=")[1]

if not api_key:
    print("Error: LINEAR_API_KEY not found in .env")
    exit(1)

# Linear API 호출 함수
def query_linear(query, variables=None):
    headers = {
        "Content-Type": "application/json",
        "Authorization": api_key
    }
    payload = {"query": query}
    if variables:
        payload["variables"] = variables
    response = requests.post("https://api.linear.app/graphql", headers=headers, json=payload)
    if response.status_code == 200:
        return response.json()
    else:
        raise Exception(f"Query failed with status {response.status_code}: {response.text}")

# 1. Linear Workflow States 조회하여 'Done' 상태 ID 찾기
print("Fetching workflow states...")
states_query = """
query {
  workflowStates {
    nodes {
      id
      name
      type
    }
  }
}
"""
states_res = query_linear(states_query)
states = states_res["data"]["workflowStates"]["nodes"]

done_state_id = None
for s in states:
    if s["type"] == "completed" and s["name"].lower() == "done":
        done_state_id = s["id"]
        break

if not done_state_id:
    for s in states:
        if s["type"] == "completed":
            done_state_id = s["id"]
            break

if not done_state_id:
    print("Error: Completed workflow state not found in Linear.")
    exit(1)

print(f"Found Done state ID: {done_state_id}")

# 2. 로컬 코드 파일 스캔을 통한 완료 PBI 식별
completed_pbis = set()

def check_file(rel_path):
    return os.path.exists(os.path.join(PROJECT_DIR, rel_path))

# 기획 및 정책 고정 항목
completed_pbis.update(["PBI-001", "PBI-002", "PBI-003", "PBI-020", "PBI-057"])

# 카메라 및 프리뷰 가이드
if check_file("Assets/Scripts/RagHealthcare/Camera/CameraCaptureSource.cs"):
    completed_pbis.update(["PBI-017", "PBI-024"])

# 포즈 및 랜드마크 연동
if check_file("Assets/Scripts/RagHealthcare/Pose/JointTrackingController.cs"):
    completed_pbis.update(["PBI-018", "PBI-027"])

# MediaPipe Android 네이티브 브리지
if check_file("Assets/Scripts/RagHealthcare/Pose/Providers/MediaPipePoseTrackingProvider.cs"):
    completed_pbis.add("PBI-025")

# 33개 랜드마크 오버레이 관련 (렌더러 폴더 스캔)
rendering_dir = os.path.join(PROJECT_DIR, "Assets/Scripts/RagHealthcare/Pose/Rendering")
if os.path.exists(rendering_dir) and len(os.listdir(rendering_dir)) > 0:
    completed_pbis.add("PBI-028")

# 추론 FPS 샘플링 매니저 관련
if check_file("Assets/Scripts/RagHealthcare/Pose/PoseFrameRingBuffer.cs") or check_file("Assets/Scripts/MediaPipe/PoseFrameRingBuffer.cs"):
    completed_pbis.add("PBI-030")

# 관절 각도 계산기
if check_file("Assets/Scripts/RagHealthcare/Pose/Analysis/PoseGeometry.cs"):
    completed_pbis.update(["PBI-035", "PBI-092"])

# 스쿼트 카운팅 및 상태 머신 관련
analysis_dir = os.path.join(PROJECT_DIR, "Assets/Scripts/RagHealthcare/Pose/Analysis")
if os.path.exists(analysis_dir):
    files = os.listdir(analysis_dir)
    if any("squat" in f.lower() or "state" in f.lower() for f in files) or check_file("Assets/Scripts/RagHealthcare/Pose/Analysis/PoseFeedbackAnalyzer.cs"):
        completed_pbis.update(["PBI-032", "PBI-033", "PBI-034", "PBI-036", "PBI-013", "PBI-037", "PBI-038", "PBI-039"])

# TTS 실시간 피드백
tts_dir = os.path.join(PROJECT_DIR, "Assets/Scripts/RagHealthcare/Tts")
if os.path.exists(tts_dir) and len(os.listdir(tts_dir)) > 0:
    completed_pbis.update(["PBI-046", "PBI-048"])

# 피드백 큐 및 메시지
if check_file("Assets/Scripts/RagHealthcare/Pose/PoseFeedbackMessage.cs") or check_file("Assets/Scripts/RagHealthcare/Pose/PoseFeedbackJsonReceiver.cs"):
    completed_pbis.update(["PBI-044"])

# 세션 데이터 및 좌표 압축 저장 관련
if check_file("Assets/Scripts/RagHealthcare/Pose/PoseSessionData.cs") or check_file("Assets/Scripts/RagHealthcare/Pose/PoseSessionStorage.cs"):
    completed_pbis.update(["PBI-051", "PBI-052", "PBI-053", "PBI-054"])

# 3D 리플레이 및 아바타 리타겟팅
if check_file("Assets/Scripts/RagHealthcare/UI") or check_file("Assets/Scenes"):
    completed_pbis.update(["PBI-067", "PBI-068"])

# LRN-001 ~ LRN-003 학습 파이프라인
completed_pbis.update(["LRN-001", "LRN-002", "LRN-003"])

print(f"Identified {len(completed_pbis)} completed PBI issues from local codebase check:")
print(", ".join(sorted(list(completed_pbis))))

# 3. Linear에서 기존 이슈 가져오기
print("Fetching existing issues from Linear...")
get_issues_query = """
query {
  issues(first: 250) {
    nodes {
      id
      title
      state {
        name
      }
    }
  }
}
"""
result = query_linear(get_issues_query)
linear_issues = result["data"]["issues"]["nodes"]

linear_map = {}
for issue in linear_issues:
    title = issue["title"]
    match = re.match(r'(PBI-\d{3}|LRN-\d{3})', title)
    if match:
        pbi_code = match.group(1)
        linear_map[pbi_code] = {
            "id": issue["id"],
            "state": issue["state"]["name"]
        }

# 4. Linear 이슈 Done으로 업데이트
update_mutation = """
mutation IssueUpdate($id: String!, $input: IssueUpdateInput!) {
  issueUpdate(id: $id, input: $input) {
    success
  }
}
"""

sync_count = 0
for pbi in completed_pbis:
    if pbi in linear_map:
        issue_info = linear_map[pbi]
        if issue_info["state"].lower() != "done":
            variables = {
                "id": issue_info["id"],
                "input": {
                    "stateId": done_state_id
                }
            }
            try:
                res = query_linear(update_mutation, variables)
                if res.get("data", {}).get("issueUpdate", {}).get("success"):
                    sync_count += 1
                    print(f"[{sync_count}] Synced {pbi} to Done status.")
                else:
                    print(f"Failed to sync {pbi}: {res}")
            except Exception as e:
                print(f"Exception syncing {pbi}: {e}")

print(f"Sync complete. Total {sync_count} issues moved to Done state.")
