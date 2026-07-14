# QA Execution Rules
코드가 변경되거나 QA 테스트를 수행할 때, 다음 문서의 규칙을 항상 최우선으로 읽고 반영하세요:

@/ragUnityTestGuide.md
@/Integrationplan.md
@/ragSystemplan.md

## QA Execution Constraints
- **Unity C# 코드 수정 금지**: 프로젝트의 핵심 Unity 소스 코드 파일(예: `.cs` 파일)은 직접 수정하거나 변경하지 마세요.
- **Python 및 스크립트 허용**: QA 자동화, 테스트 스크립트, Linear 연동 등의 도구용 Python 스크립트(`.py`)나 마크다운 문서(`.md`) 등은 필요에 따라 생성하거나 수정할 수 있습니다.
- **QA 전담**: C# 로직 분석 및 검토, QA 테스트 실행, 결과 확인 작업만 수행하세요. C# 코드에 발견된 오류나 개선 사항은 직접 수정하지 말고, 보고서(Artifact)나 메시지를 통해 피드백으로 사용자에게 전달해야 합니다.

## Linear & GitHub Synchronization Rules
- **작업 내용 분석**: 코드 변경, 테스트 결과, 또는 수행한 작업 내용이 있는 경우, 그 내용을 자세히 분석하세요.
- **GitHub 상태 확인**: 작업을 수행한 후, `git status` 또는 `git diff` 등의 git 명령을 실행하여 실제 소스 코드 변경 사항 및 작업 내역을 교차 확인하세요.
- **Linear 자동 업데이트**: GitHub의 작업 내역과 변경 사항을 분석하여 완료된 항목이 있다면, `python tools/sync_linear_with_code.py` 스크립트를 실행하거나 Linear API를 활용하여 관련 PBI 이슈를 자동으로 'Done' 상태로 업데이트하고 세부 사항을 반영하세요.
