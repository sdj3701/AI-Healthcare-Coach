import os
import re
import json
import requests
import pandas as pd

# 환경 변수 및 경로 설정
ENV_PATH = "/Users/sindongju/AI-Healthcare-Coach/.env"
EXCEL_PATH = "/Users/sindongju/AI-Healthcare-Coach/docs/온디바이스_AI_헬스케어_제품_백로그_원본백업_20260630.xlsx"
DOCS_DIR = "/Users/sindongju/AI-Healthcare-Coach/docs"
INTEGRATION_PLAN_PATH = "/Users/sindongju/AI-Healthcare-Coach/Integrationplan.md"

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

# 1. 엑셀 데이터 파싱
print("Parsing Excel...")
xls = pd.ExcelFile(EXCEL_PATH)
df_backlog = xls.parse("01_백로그")
df_epics = xls.parse("02_에픽")
df_qa = xls.parse("04_QA테스트")
df_risks = xls.parse("05_리스크")

# 시트 설명글(iloc[0]) 제외하고 실제 헤더(iloc[1]) 설정
df_backlog.columns = df_backlog.iloc[1]
df_backlog = df_backlog[2:].reset_index(drop=True)

df_epics.columns = df_epics.iloc[1]
df_epics = df_epics[2:].reset_index(drop=True)

df_qa.columns = df_qa.iloc[1]
df_qa = df_qa[2:].reset_index(drop=True)

df_risks.columns = df_risks.iloc[1]
df_risks = df_risks[2:].reset_index(drop=True)

# 에픽 데이터 파싱
epic_dict = {}
for _, row in df_epics.iterrows():
    epic_id = str(row.get("Epic ID", "")).strip()
    epic_name = str(row.get("에픽", "")).strip()
    if epic_id and epic_id != "nan":
        epic_dict[epic_id] = epic_name

# QA 테스트 데이터 파싱
qa_dict = {}
for _, row in df_qa.iterrows():
    pbi_refs = str(row.get("관련 PBI", "")).strip()
    t_id = str(row.get("테스트 ID", "")).strip()
    t_scenario = str(row.get("시나리오", "")).strip()
    t_criteria = str(row.get("합격 기준", "")).strip()
    if pbi_refs and pbi_refs != "nan" and t_id and t_id != "nan":
        for pbi_ref in re.split(r'[/,\s]+', pbi_refs):
            pbi_ref = pbi_ref.strip()
            if pbi_ref:
                if pbi_ref not in qa_dict:
                    qa_dict[pbi_ref] = []
                qa_dict[pbi_ref].append(f"**{t_id} · {t_scenario}** (합격 기준: {t_criteria})")

# 리스크 데이터 파싱
risk_dict = {}
for _, row in df_risks.iterrows():
    pbi_refs = str(row.get("관련 PBI", "")).strip()
    r_id = str(row.get("Risk ID", "")).strip()
    r_name = str(row.get("리스크", "")).strip()
    r_action = str(row.get("대응 방안", "")).strip()
    if pbi_refs and pbi_refs != "nan" and r_id and r_id != "nan":
        for pbi_ref in re.split(r'[/,\s]+', pbi_refs):
            pbi_ref = pbi_ref.strip()
            if pbi_ref:
                if pbi_ref not in risk_dict:
                    risk_dict[pbi_ref] = []
                risk_dict[pbi_ref].append(f"**{r_id} · {r_name}** (대응 방안: {r_action})")

# 2. docs/*.md 및 Integrationplan.md 분석하여 기술 디테일 추출
print("Analyzing MD specification files...")
technical_info = {}

def scan_file_for_pbi(file_path):
    if not os.path.exists(file_path):
        return
    filename = os.path.basename(file_path)
    with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
        pbi_ids = set(re.findall(r'(PBI-\d{3}|LRN-\d{3})', content))
        for pbi in pbi_ids:
            if pbi not in technical_info:
                technical_info[pbi] = []
            technical_info[pbi].append(f"[{filename}](file://{file_path})")

# docs 디렉토리 내의 파일들 스캔
for fname in os.listdir(DOCS_DIR):
    if fname.endswith(".md"):
        scan_file_for_pbi(os.path.join(DOCS_DIR, fname))

# Integrationplan.md 스캔
scan_file_for_pbi(INTEGRATION_PLAN_PATH)

# 3. Linear에서 기존 등록된 이슈 가져오기
print("Fetching existing issues from Linear...")
get_issues_query = """
query {
  issues(first: 250) {
    nodes {
      id
      title
    }
  }
}
"""
result = query_linear(get_issues_query)
linear_issues = result["data"]["issues"]["nodes"]
print(f"Found {len(linear_issues)} issues in Linear.")

linear_map = {}
for issue in linear_issues:
    title = issue["title"]
    match = re.match(r'(PBI-\d{3}|LRN-\d{3})', title)
    if match:
        pbi_code = match.group(1)
        linear_map[pbi_code] = issue["id"]

# 4. 각 백로그에 대한 상세 마크다운 Description 생성 및 업데이트
print("Updating issues in Linear...")
update_mutation = """
mutation IssueUpdate($id: String!, $input: IssueUpdateInput!) {
  issueUpdate(id: $id, input: $input) {
    success
  }
}
"""

success_count = 0
for _, row in df_backlog.iterrows():
    pbi_id = str(row.get("ID", "")).strip()
    if not pbi_id or pbi_id == "nan" or pbi_id not in linear_map:
        continue
    
    linear_uuid = linear_map[pbi_id]
    
    # 엑셀 데이터 추출
    epic_field = str(row.get("에픽", "")).strip()
    epic_code = epic_field.split(" ")[0] if epic_field != "nan" else ""
    epic_name = epic_dict.get(epic_code, epic_field) if epic_code else ""
    
    module = str(row.get("모듈", "")).strip()
    moscow = str(row.get("MoSCoW", "")).strip()
    phase = str(row.get("단계", "")).strip()
    assignee_role = str(row.get("담당", "")).strip()
    story = str(row.get("사용자/업무 스토리", "")).strip()
    desc_detail = str(row.get("상세 설명", "")).strip()
    acceptance_criteria = str(row.get("수용 기준", "")).strip()
    dependencies = str(row.get("의존성", "")).strip()
    source_basis = str(row.get("출처/근거", "")).strip()
    notes = str(row.get("메모", "")).strip()
    
    # NaN 치환
    story = "" if story == "nan" else story
    desc_detail = "" if desc_detail == "nan" else desc_detail
    acceptance_criteria = "" if acceptance_criteria == "nan" else acceptance_criteria
    dependencies = "없음" if dependencies == "nan" or dependencies == "" else dependencies
    source_basis = "" if source_basis == "nan" else source_basis
    notes = "" if notes == "nan" else notes
    module = "" if module == "nan" else module
    moscow = "" if moscow == "nan" else moscow
    phase = "" if phase == "nan" else phase
    assignee_role = "" if assignee_role == "nan" else assignee_role

    # 관련 QA 테스트 정보 결합
    qa_infos = qa_dict.get(pbi_id, [])
    qa_section = ""
    if qa_infos:
        qa_section = "\n### 🧪 연계 QA 테스트 시나리오\n" + "\n".join([f"- {info}" for info in qa_infos])
        
    # 관련 리스크 정보 결합
    risk_infos = risk_dict.get(pbi_id, [])
    risk_section = ""
    if risk_infos:
        risk_section = "\n### ⚠️ 연계 리스크 및 대응 방안\n" + "\n".join([f"- {info}" for info in risk_infos])

    # 참조 설계 문서 결합
    doc_refs = technical_info.get(pbi_id, [])
    doc_section = ""
    if doc_refs:
        doc_section = "\n### 📂 참조 설계서 및 계획서\n" + "\n".join([f"- {ref}" for ref in doc_refs])

    # 최종 description 마크다운 조립
    description = f"""## 📋 사용자/업무 스토리
{story}

## 🔍 상세 구현 요구사항 (Technical Details)
{desc_detail}
{doc_section}
{risk_section}

## 🧪 수용 기준 (Acceptance Criteria)
- {acceptance_criteria}
{qa_section}

---
* **원본 ID:** {pbi_id}
* **모듈:** {module}
* **MoSCoW:** {moscow}
* **단계:** {phase}
* **담당 역할:** {assignee_role}
* **의존성 PBI:** {dependencies}
* **출처/근거:** {source_basis}
"""
    if notes and notes != "":
        description += f"* **메모:** {notes}\n"

    # API 호출하여 Linear 이슈 업데이트
    variables = {
        "id": linear_uuid,
        "input": {
            "description": description
        }
    }
    
    try:
        res = query_linear(update_mutation, variables)
        if res.get("data", {}).get("issueUpdate", {}).get("success"):
            success_count += 1
            print(f"[{success_count}] Successfully updated {pbi_id}")
        else:
            print(f"Failed to update {pbi_id}: {res}")
    except Exception as e:
        print(f"Exception updating {pbi_id}: {e}")

print(f"Update completed. Total {success_count} issues updated.")
