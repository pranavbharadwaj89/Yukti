import json
import sys
from pathlib import Path
from tester_agent import run_tester

OUTPUT_DIR = Path("output_tests")

def run(coder_results_file: str = "coder_results.json"):
    # Load what the Coder produced
    if not Path(coder_results_file).exists():
        print(f"Coder results file '{coder_results_file}' not found.")
        return

    with open(coder_results_file, "r", encoding="utf-8") as f:
        coder_results = json.load(f)

    # Collect test files that were successfully coded
    test_files = [
        str(OUTPUT_DIR / r["filename"])
        for r in coder_results
        if r.get("status") == "success" and r.get("filename")
    ]

    if not test_files:
        print("No passing test files from Coder — nothing to test.")
        return

    print(f"Running Tester agent on {len(test_files)} file(s):\n")
    for f in test_files:
        print(f"  {f}")
    print()

    report = run_tester(test_files)

    # Save for the Reviewer
    with open("tester_report.json", "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    # Print summary
    print(f"\nStatus: {report.get('status')}")
    print(f"Summary: {report.get('summary')}")
    for result in report.get("results", []):
        icon = "✓" if result.get("passed") else "✗"
        try:
            print(f"  {icon} {result.get('file')} — "
                  f"{result.get('tests_passed', 0)}/{result.get('tests_run', 0)} passed")
        except UnicodeEncodeError:
            safe_icon = "[PASS]" if result.get("passed") else "[FAIL]"
            print(f"  {safe_icon} {result.get('file')} - "
                  f"{result.get('tests_passed', 0)}/{result.get('tests_run', 0)} passed")
        for failure in result.get("failures", []):
            print(f"      FAIL: {failure.get('test_name')}")
            print(f"            {failure.get('message', '')[:100]}")

    print(f"\nReport saved to tester_report.json")
    print(f"Ready for Reviewer: {report.get('ready_for_reviewer')}")

if __name__ == "__main__":
    results_file = sys.argv[1] if len(sys.argv) > 1 else "coder_results.json"
    run(results_file)
