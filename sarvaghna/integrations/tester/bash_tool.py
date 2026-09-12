import subprocess
import sys
import os
import json
import tempfile
from pathlib import Path

def run_bash(command: str, timeout: int = 90) -> dict:
    """
    Run any shell command and return structured output.
    Claude calls this — keep the return shape consistent.
    """
    try:
        result = subprocess.run(
            command,
            shell=True,
            capture_output=True,
            text=True,
            timeout=timeout,
            env={**os.environ, "PYTHONUNBUFFERED": "1"}
        )
        return {
            "stdout":    result.stdout,
            "stderr":    result.stderr,
            "exit_code": result.returncode,
            "timed_out": False
        }
    except subprocess.TimeoutExpired:
        return {
            "stdout":    "",
            "stderr":    f"Command timed out after {timeout} seconds.",
            "exit_code": -1,
            "timed_out": True
        }


def run_pytest_on_file(filepath: str) -> dict:
    """
    Run pytest with JSON reporting on a single file.
    Returns both the raw result and parsed JSON report if available.
    """
    temp_dir = Path(tempfile.gettempdir())
    report_path = temp_dir / f"pytest_report_{Path(filepath).stem}.json"
    report_path_str = str(report_path).replace("\\", "/")

    command = (
        f'"{sys.executable}" -m pytest "{filepath}" '
        f'--json-report --json-report-file="{report_path_str}" '
        f'--tb=short --no-header -v '
        f'--timeout=30'          # per-test timeout (needs pytest-timeout)
    )

    result = run_bash(command, timeout=90)

    # Try to attach the structured JSON report
    try:
        with open(report_path, "r", encoding="utf-8") as f:
            result["json_report"] = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError, PermissionError):
        # Fallback check in /tmp
        alt_path = Path(f"/tmp/pytest_report_{Path(filepath).stem}.json")
        if alt_path.exists():
            try:
                with open(alt_path, "r", encoding="utf-8") as f:
                    result["json_report"] = json.load(f)
            except Exception:
                result["json_report"] = None
        else:
            result["json_report"] = None

    return result


def install_test_dependencies():
    """One-time setup."""
    packages = [
        "pytest",
        "pytest-playwright",
        "pytest-json-report",
        "pytest-timeout",
        "playwright"
    ]
    subprocess.run(
        [sys.executable, "-m", "pip", "install"] + packages,
        check=True
    )
    subprocess.run(
        [sys.executable, "-m", "playwright", "install", "chromium"],
        check=True
    )
    print("Tester dependencies ready.")


if __name__ == "__main__":
    install_test_dependencies()
