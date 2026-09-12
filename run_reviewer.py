import json
import sys
from pathlib import Path
from reviewer_agent import run_reviewer

def score_color(score: int) -> str:
    if score >= 90: return "[SHIP] ++"
    if score >= 80: return "[SHIP] +"
    if score >= 65: return "[RETRY] ~"
    if score >= 50: return "[RETRY] -"
    return "[ESCALATE] !"

def safe_print(text: str):
    try:
        print(text)
    except UnicodeEncodeError:
        safe = text.encode("ascii", errors="replace").decode("ascii")
        print(safe)

def run(
    ticket_file: str      = "ticket.json",
    scenarios_file: str   = "scenarios.json",
    tester_file: str      = "tester_report.json"
):
    with open(ticket_file, encoding="utf-8")    as f: ticket        = json.load(f)
    with open(scenarios_file, encoding="utf-8") as f: scenarios     = json.load(f)
    with open(tester_file, encoding="utf-8")    as f: tester_report = json.load(f)

    print("Running Reviewer agent...\n")
    report = run_reviewer(ticket, scenarios, tester_report)

    # Save for Orchestrator
    with open("reviewer_report.json", "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    # Human-readable summary
    score    = report["score"]
    decision = report["decision"].upper()
    icon     = score_color(score)

    safe_print(f"{icon}  Score: {score}/100  ->  Decision: {decision}")
    safe_print(f"\n{report['summary']}\n")

    safe_print("Criteria coverage:")
    for c in report.get("criteria_coverage", []):
        status_icon = {"covered": "[PASS]", "partially_covered": "[PARTIAL]",
                       "missing": "[MISSING]", "failed": "[FAIL]"}.get(c["status"], "[?]")
        safe_print(f"  {status_icon}  {c['criterion'][:70]}")
        if c.get("notes"):
            safe_print(f"       {c['notes']}")

    if report.get("deductions"):
        safe_print(f"\nDeductions:")
        for d in report["deductions"]:
            safe_print(f"  -{d['points']:2d}  {d['reason']}")
            safe_print(f"        {d['detail']}")

    if report.get("gaps"):
        safe_print(f"\nGaps ({len(report['gaps'])}):")
        for g in report["gaps"]:
            safe_print(f"  [{g['priority'].upper()}] {g['description']}")
            safe_print(f"         Suggested: {g['suggested_scenario']}")

    if report.get("strengths"):
        safe_print(f"\nStrengths:")
        for s in report["strengths"]:
            safe_print(f"  + {s}")

    if report.get("decision") == "retry" and report.get("retry_instructions"):
        safe_print(f"\nRetry instructions for Coder:")
        safe_print(f"  {report['retry_instructions']}")

    safe_print(f"\nFull report saved to reviewer_report.json")
    return report

if __name__ == "__main__":
    run(
        ticket_file    = sys.argv[1] if len(sys.argv) > 1 else "ticket.json",
        scenarios_file = sys.argv[2] if len(sys.argv) > 2 else "scenarios.json",
        tester_file    = sys.argv[3] if len(sys.argv) > 3 else "tester_report.json"
    )
