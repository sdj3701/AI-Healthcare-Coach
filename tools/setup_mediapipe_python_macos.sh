#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
VENV_DIR="${PROJECT_ROOT}/.venv-mediapipe"

if [[ -n "${PYTHON_BIN:-}" ]]; then
  PYTHON_CANDIDATES=("${PYTHON_BIN}")
else
  PYTHON_CANDIDATES=(
    "/opt/homebrew/bin/python3.12"
    "/opt/homebrew/bin/python3.11"
    "/opt/homebrew/bin/python3.10"
    "/opt/homebrew/bin/python3"
    "/usr/local/bin/python3.12"
    "/usr/local/bin/python3.11"
    "/usr/local/bin/python3.10"
    "/usr/local/bin/python3"
    "/usr/bin/python3"
  )
fi

PYTHON=""
for candidate in "${PYTHON_CANDIDATES[@]}"; do
  if [[ -x "${candidate}" ]]; then
    PYTHON="${candidate}"
    break
  fi
done

if [[ -z "${PYTHON}" ]]; then
  echo "Python 3 was not found. Install Python 3, then rerun this script." >&2
  exit 1
fi

echo "Project root: ${PROJECT_ROOT}"
echo "Bootstrap Python: ${PYTHON}"
echo "Venv: ${VENV_DIR}"

"${PYTHON}" -m venv "${VENV_DIR}"
"${VENV_DIR}/bin/python" -m ensurepip --upgrade
"${VENV_DIR}/bin/python" -m pip install --upgrade pip setuptools wheel
"${VENV_DIR}/bin/python" -m pip install mediapipe numpy
"${VENV_DIR}/bin/python" - <<'PY'
import sys
import numpy
import mediapipe

print("Python executable:", sys.executable)
print("numpy:", numpy.__version__, numpy.__file__)
print("mediapipe:", mediapipe.__version__, mediapipe.__file__)
PY

echo
echo "Unity Editor Python Executable Path:"
echo "${VENV_DIR}/bin/python"
