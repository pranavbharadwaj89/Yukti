import os
import sys
import json
from pathlib import Path
import anthropic
from bash_tool import run_pytest_on_file

TESTER_SYSTEM_PROMPT = """
You are the Tester agent in a software testing pipeline.

Your job: run test files and produce an honest, structured report.

## What you must NOT do
- Do not fix failing tests
- Do not rewrite or modify any code
- Do not speculate about what might fix a failure
- Do not mark a test as passing if it failed

## What you must do
- Run each test file using the run_tests tool
- Read the output carefully
- Produce a precise JSON report

## Output format
Respond ONLY with valid JSON when all tests are done:

{
  "status": "all_passed" | "some_failed" | "all_failed" | "error",
  "summary": "one plain-English sentence describing the overall result",
  "results": [
    {
      "file": "test_login.py",
      "passed": true | false,
      "tests_run": 4,
      "tests_passed": 3,
      "tests_failed": 1,
      "duration_seconds": 12.4,
      "failures": [
        {
          "test_name": "test_login_with_invalid_password",
          "error_type": "AssertionError | TimeoutError | etc",
          "message": "exact error message — do not paraphrase",
          "likely_cause": "selector changed | element not found | timing | env issue"
        }
      ]
    }
  ],
  "environment_issues": ["anything that looks like infra/setup rather than a real failure"],
  "ready_for_reviewer": true | false
}

## Classifying failures
- Selector not found → likely_cause: "selector changed or element missing"
- Timeout waiting for element → likely_cause: "timing or slow page load"  
- Connection refused → likely_cause: "app not running or wrong base URL"
- AssertionError on text content → likely_cause: "real functional failure"
- Import error → likely_cause: "missing dependency or broken test code"
"""

TESTER_TOOLS = [
    {
        "name": "run_tests",
        "description": (
            "Run a pytest test file and return the full results including "
            "pass/fail status, error messages, and timing. "
            "Always run each file exactly once — do not re-run passing tests."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "filepath": {
                    "type": "string",
                    "description": "Absolute or relative path to the test file, e.g. output_tests/test_login.py"
                }
            },
            "required": ["filepath"]
        }
    }
]


def handle_tool_call(tool_name: str, tool_input: dict) -> str:
    if tool_name != "run_tests":
        return f"Unknown tool: {tool_name}"

    filepath = tool_input["filepath"]
    print(f"    Running: {filepath}")

    result = run_pytest_on_file(filepath)

    # Give Claude a clean, information-dense summary
    output_parts = []

    # High-level status
    passed = result["exit_code"] == 0
    output_parts.append(f"Exit code: {result['exit_code']} ({'PASSED' if passed else 'FAILED'})")

    # Structured report from pytest-json-report (preferred)
    if result.get("json_report"):
        report = result["json_report"]
        summary = report.get("summary", {})
        output_parts.append(
            f"Tests: {summary.get('total', '?')} total, "
            f"{summary.get('passed', 0)} passed, "
            f"{summary.get('failed', 0)} failed, "
            f"{summary.get('error', 0)} errors"
        )
        duration = report.get("duration", 0)
        output_parts.append(f"Duration: {duration:.1f}s" if isinstance(duration, (int, float)) else f"Duration: {duration}")

        # Individual test details
        for test in report.get("tests", []):
            status = test.get("outcome", "unknown")
            name = test.get("nodeid", "?")
            output_parts.append(f"\n[{status.upper()}] {name}")
            if status in ("failed", "error"):
                call = test.get("call", {})
                if call.get("longrepr"):
                    # Trim very long tracebacks — Claude doesn't need the whole stack
                    longrepr = str(call["longrepr"])[-1500:]
                    output_parts.append(f"  Error:\n{longrepr}")

    else:
        # Fallback to raw stdout/stderr
        output_parts.append("\n--- stdout ---")
        output_parts.append(result.get("stdout", "")[-2000:] or "(empty)")
        if result.get("stderr"):
            output_parts.append("\n--- stderr ---")
            output_parts.append(result["stderr"][-1000:])

    if result.get("timed_out"):
        output_parts.append("\nWARNING: Test run timed out.")

    return "\n".join(output_parts)


def classify_failure(error_msg: str, error_type: str = "") -> str:
    lower_msg = (error_msg + " " + error_type).lower()
    if "connection refused" in lower_msg or "err_connection_refused" in lower_msg or "winerror 10061" in lower_msg:
        return "app not running or wrong base URL"
    if "waiting for locator" in lower_msg or "timeout" in lower_msg or "timed out" in lower_msg:
        return "timing or slow page load"
    if "not found" in lower_msg or "cannot find" in lower_msg or "locator" in lower_msg or "selector" in lower_msg:
        return "selector changed or element missing"
    if "importerror" in lower_msg or "modulenotfounderror" in lower_msg:
        return "missing dependency or broken test code"
    if "assertionerror" in lower_msg:
        return "real functional failure"
    return "real functional failure"


def deterministic_fallback_tester(test_files: list[str]) -> dict:
    """Offline heuristic execution when ANTHROPIC_API_KEY is not available."""
    results = []
    env_issues = []
    total_passed = 0
    total_failed = 0

    for filepath in test_files:
        run_res = run_pytest_on_file(filepath)
        report = run_res.get("json_report")
        file_name = Path(filepath).name

        if report:
            summary = report.get("summary", {})
            t_total = summary.get("total", 0)
            t_passed = summary.get("passed", 0)
            t_failed = summary.get("failed", 0) + summary.get("error", 0)
            duration = float(report.get("duration", 0.0))

            failures = []
            for test in report.get("tests", []):
                outcome = test.get("outcome")
                if outcome in ("failed", "error"):
                    call = test.get("call", {})
                    longrepr = str(call.get("longrepr", run_res.get("stderr", "")))
                    cause = classify_failure(longrepr)
                    if "connection refused" in longrepr.lower() or "app not running" in cause:
                        env_issues.append(f"Network connection refused in {file_name}: target application may not be running.")
                    failures.append({
                        "test_name": test.get("nodeid", file_name),
                        "error_type": "AssertionError" if "AssertionError" in longrepr else "ExecutionError",
                        "message": longrepr[:300].strip(),
                        "likely_cause": cause
                    })

            file_passed = (t_failed == 0 and t_passed > 0)
            total_passed += t_passed
            total_failed += t_failed

            results.append({
                "file": file_name,
                "passed": file_passed,
                "tests_run": t_total,
                "tests_passed": t_passed,
                "tests_failed": t_failed,
                "duration_seconds": round(duration, 2),
                "failures": failures
            })
        else:
            # Fallback if json report couldn't be generated
            passed = run_res.get("exit_code") == 0
            stdout = run_res.get("stdout", "")
            stderr = run_res.get("stderr", "")
            failures = []
            if not passed:
                combined_err = (stdout + "\n" + stderr).strip()
                cause = classify_failure(combined_err)
                if "connection refused" in combined_err.lower() or "app not running" in cause:
                    env_issues.append(f"Network connection refused in {file_name}: target application may not be running.")
                failures.append({
                    "test_name": file_name,
                    "error_type": "ExecutionError",
                    "message": (stderr or stdout)[-300:].strip(),
                    "likely_cause": cause
                })
                total_failed += 1
            else:
                total_passed += 1

            results.append({
                "file": file_name,
                "passed": passed,
                "tests_run": 1,
                "tests_passed": 1 if passed else 0,
                "tests_failed": 0 if passed else 1,
                "duration_seconds": 1.0,
                "failures": failures
            })

    if total_failed == 0 and total_passed > 0:
        status = "all_passed"
        summary = f"All {total_passed} tests passed successfully across {len(results)} test file(s)."
    elif total_passed == 0 and total_failed > 0:
        status = "all_failed"
        summary = f"All {total_failed} tests failed across {len(results)} test file(s)."
    elif total_failed > 0 and total_passed > 0:
        status = "some_failed"
        summary = f"{total_passed} tests passed, {total_failed} tests failed across {len(results)} test file(s)."
    else:
        status = "error"
        summary = "No tests were executed or test files were empty."

    return {
        "status": status,
        "summary": summary,
        "results": results,
        "environment_issues": list(set(env_issues)),
        "ready_for_reviewer": True
    }


def run_tester(test_files: list[str]) -> dict:
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        print("[Notice] ANTHROPIC_API_KEY not configured. Executing Tester in deterministic structured mode.")
        return deterministic_fallback_tester(test_files)

    client = anthropic.Anthropic()

    files_list = "\n".join(f"- {f}" for f in test_files)
    user_message = (
        f"Run each of the following test files and report results:\n\n"
        f"{files_list}\n\n"
        f"Run them one at a time. Report all results when done."
    )

    messages = [{"role": "user", "content": user_message}]
    max_iterations = len(test_files) * 2 + 4  # enough for each file + some overhead

    for iteration in range(max_iterations):
        response = client.messages.create(
            model=os.environ.get("ANTHROPIC_MODEL", "claude-sonnet-4-6"),
            max_tokens=3000,
            system=TESTER_SYSTEM_PROMPT,
            tools=TESTER_TOOLS,
            messages=messages
        )

        messages.append({"role": "assistant", "content": response.content})

        # Done — extract final JSON report
        if response.stop_reason == "end_turn":
            for block in response.content:
                if hasattr(block, "text"):
                    try:
                        raw = block.text
                        if raw.startswith("```json"):
                            raw = raw.replace("```json", "").replace("```", "").strip()
                        elif raw.startswith("```"):
                            raw = raw.replace("```", "").strip()
                        return json.loads(raw)
                    except json.JSONDecodeError:
                        pass
            return {
                "status": "error",
                "summary": "Tester finished but returned no valid JSON.",
                "results": [],
                "ready_for_reviewer": False
            }

        # Handle tool calls
        tool_results = []
        for block in response.content:
            if block.type == "tool_use":
                result = handle_tool_call(block.name, block.input)
                tool_results.append({
                    "type":        "tool_result",
                    "tool_use_id": block.id,
                    "content":     result
                })

        if tool_results:
            messages.append({"role": "user", "content": tool_results})

    return {
        "status": "error",
        "summary": "Tester hit max iterations without completing.",
        "results": [],
        "ready_for_reviewer": False
    }
