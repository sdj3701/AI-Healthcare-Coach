import os
import re
import requests

ENV_PATH = "/Users/sindongju/AI-Healthcare-Coach/.env"
TEAM_ID = "58aa69b2-70d1-497b-9a0b-488653e3a488"

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

headers = {
    "Content-Type": "application/json",
    "Authorization": api_key
}

def query_linear(query, variables=None):
    payload = {"query": query}
    if variables:
        payload["variables"] = variables
    res = requests.post("https://api.linear.app/graphql", headers=headers, json=payload)
    if res.status_code == 200:
        return res.json()
    else:
        raise Exception(f"Query failed {res.status_code}: {res.text}")

# 1. 기존 라벨 목록 가져오기
print("Fetching existing labels...")
labels_query = """
query {
  issueLabels {
    nodes {
      id
      name
    }
  }
}
"""
labels_res = query_linear(labels_query)
existing_labels = {node["name"]: node["id"] for node in labels_res["data"]["issueLabels"]["nodes"]}

# 2. 필요한 라벨 정의 및 생성
required_labels = {
    "Unity 개발자": "#3A86F0",
    "AI/CV 개발자": "#FF8C00",
    "LLM/Edge 개발자": "#8A2BE2",
    "C++/AI 학습": "#20B2AA",
    "PM/기획": "#2E8B57",
    "QA": "#FF1493",
    "운동 전문가": "#9ACD32",
    "보안/개인정보": "#708090",
    "Legal/Regulatory": "#D2691E",
    "Growth/Business": "#DDA0DD"
}

create_label_mutation = """
mutation IssueLabelCreate($input: IssueLabelCreateInput!) {
  issueLabelCreate(input: $input) {
    success
    issueLabel {
      id
      name
    }
  }
}
"""

label_map = {}
for name, color in required_labels.items():
    # 자소 분리 및 유니코드 매칭 방지를 위해 정확히 일치하거나 포함되는지 확인
    found_id = None
    for ex_name, ex_id in existing_labels.items():
        if name in ex_name or ex_name in name:
            found_id = ex_id
            break
            
    if found_id:
        print(f"Label '{name}' already exists with ID: {found_id}")
        label_map[name] = found_id
    else:
        print(f"Creating label '{name}'...")
        variables = {
            "input": {
                "name": name,
                "color": color,
                "teamId": TEAM_ID
            }
        }
        res = query_linear(create_label_mutation, variables)
        label_data = res.get("data", {}).get("issueLabelCreate", {}).get("issueLabel")
        if label_data:
            label_map[name] = label_data["id"]
            print(f"Successfully created label '{name}' with ID: {label_data['id']}")
        else:
            print(f"Failed to create label '{name}': {res}")

# 3. 이슈 목록 조회 (기존 라벨 포함)
print("\nFetching all issues...")
issues_query = """
query {
  issues(first: 250) {
    nodes {
      id
      title
      description
      labels {
        nodes {
          id
          name
        }
      }
    }
  }
}
"""
issues_res = query_linear(issues_query)
issues = issues_res["data"]["issues"]["nodes"]

update_issue_mutation = """
mutation IssueUpdate($id: String!, $input: IssueUpdateInput!) {
  issueUpdate(id: $id, input: $input) {
    success
  }
}
"""

sync_count = 0
for issue in issues:
    desc = issue["description"] or ""
    match = re.search(r"\*\*담당 역할:\*\*\s*(.+)", desc)
    if not match:
        continue
        
    role = match.group(1).strip()
    # Carriage return 등 클리닝
    role = role.replace("\r", "").strip()
    
    # 매치되는 역할명 찾기
    matched_label_name = None
    for req_name in required_labels.keys():
        if req_name.lower() in role.lower() or role.lower() in req_name.lower():
            matched_label_name = req_name
            break
            
    if not matched_label_name or matched_label_name not in label_map:
        continue
        
    target_label_id = label_map[matched_label_name]
    
    # 이미 해당 라벨이 붙어있는지 확인
    existing_label_ids = [l["id"] for l in issue["labels"]["nodes"]]
    if target_label_id in existing_label_ids:
        continue
        
    # 새 라벨 추가
    new_label_ids = list(set(existing_label_ids + [target_label_id]))
    variables = {
        "id": issue["id"],
        "input": {
            "labelIds": new_label_ids
        }
    }
    
    try:
        res = query_linear(update_issue_mutation, variables)
        if res.get("data", {}).get("issueUpdate", {}).get("success"):
            sync_count += 1
            print(f"[{sync_count}] Attached label '{matched_label_name}' to issue '{issue['title']}'")
        else:
            print(f"Failed to attach label to '{issue['title']}': {res}")
    except Exception as e:
        print(f"Exception updating '{issue['title']}': {e}")

print(f"\nLabel sync complete. Updated {sync_count} issues.")
