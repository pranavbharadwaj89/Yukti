import json
import sys
import time
from pathlib import Path
from datetime import datetime

from jira_client        import fetch_ticket
from analyst_agent      import run_analyst
from coder_agent        import run_coder
from tester_agent       import run_tester
from reviewer_agent     import run_reviewer
from orchestrator_agent import run_orchestrator

OUTPUT_DIR  = Path("output_tests")
STATE_FILE  = Path("pipeline_state.json")
RESULTS_DIR = Path("pipeline_results")

OUTPUT_DIR.mkdir(exist_ok=True)
RESULTS_DIR.mkdir(exist_ok=True)


def safe_print(text: str):
    try:
        print(text)
    except UnicodeEncodeError:
        print(text.encode("ascii", errors="replace").decode("ascii"))


# ── State helpers ────────────────────────────────────────────────────────────

def load_state(ticket_id: str) -> dict:
    """Load existing state or start fresh."""
    if STATE_FILE.exists():
        try:
            state = json.loads(STATE_FILE.read_text(encoding="utf-8"))
            if state.get("ticket_id") == ticket_id:
                safe_print(f"Resuming existing run for {ticket_id} at stage: {state['stage']}")
                return state
        except Exception:
            pass
    return {
        "ticket_id":               ticket_id,
        "stage":                   "start",
        "retry_count":             0,
        "analyst_done":            False,
        "coder_done":              False,
        "tester_done":             False,
        "reviewer_done":           False,
        "reviewer_report":         None,
        "last_retry_instructions": None,
        "errors":                  [],
        "started_at":              datetime.now().isoformat(),
    }

def save_state(state: dict):
    STATE_FILE.write_text(json.dumps(state, indent=2), encoding="utf-8")

def clear_state():
    if STATE_FILE.exists():
        STATE_FILE.unlink()


# ── Stage runners ─────────────────────────────────────────────────────────────

def stage_analyse(state: dict, ticket: dict) -> tuple:
    safe_print("\n── Analyst ─────────────────────────────────────")
    scenarios = run_analyst(ticket)
    n = len(scenarios.get("test_scenarios", []))
    safe_print(f"   {n} scenarios extracted")

    if scenarios.get("ambiguities"):
        safe_print(f"   Ambiguities flagged:")
        for a in scenarios["ambiguities"]:
            safe_print(f"     - {a}")

    # Save for later stages
    path = RESULTS_DIR / f"scenarios_{state['ticket_id']}.json"
    path.write_text(json.dumps(scenarios, indent=2), encoding="utf-8")

    state["analyst_done"]  = True
    state["scenarios_file"] = str(path)
    state["stage"]         = "code"
    return state, scenarios


def stage_code(state: dict, scenarios: dict, retry_instructions: str = None) -> tuple:
    safe_print("\n── Coder ───────────────────────────────────────")

    if retry_instructions:
        safe_print(f"   Retry mode — instructions from Reviewer:")
        safe_print(f"   {retry_instructions}")

    test_files = []
    coder_results = []

    for scenario in scenarios.get("test_scenarios", []):
        safe_print(f"   Coding: {scenario['id']} — {scenario['name']}")

        # On retry, inject the Reviewer's instructions into the scenario
        if retry_instructions:
            scenario = {
                **scenario,
                "retry_instructions": retry_instructions
            }

        result = run_coder(scenario, base_url=state.get("base_url", "http://localhost:3000"))
        coder_results.append(result)

        if result.get("status") == "success" and result.get("filename"):
            test_files.append(str(OUTPUT_DIR / result["filename"]))
            safe_print(f"   [OK] {result['filename']}")
        else:
            safe_print(f"   [FAIL] Failed: {result.get('notes', 'unknown error')}")

    path = RESULTS_DIR / "coder_results.json"
    path.write_text(json.dumps(coder_results, indent=2), encoding="utf-8")

    state["coder_done"]       = True
    state["test_files"]       = test_files
    state["coder_results_file"] = str(path)
    state["stage"]            = "test"
    return state, test_files


def stage_test(state: dict, test_files: list) -> tuple:
    safe_print("\n── Tester ──────────────────────────────────────")

    if not test_files:
        safe_print("   No test files to run — skipping")
        state["tester_done"]   = True
        state["tester_report"] = {
            "status": "error",
            "summary": "No test files produced by Coder.",
            "results": [],
            "ready_for_reviewer": False
        }
        state["stage"] = "review"
        return state, state["tester_report"]

    report = run_tester(test_files)

    passed = sum(r.get("tests_passed", 0) for r in report.get("results", []))
    total  = sum(r.get("tests_run",    0) for r in report.get("results", []))
    safe_print(f"   {passed}/{total} tests passing — {report.get('status')}")

    path = RESULTS_DIR / "tester_report.json"
    path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    state["tester_done"]   = True
    state["tester_report"] = report
    state["stage"]         = "review"
    return state, report


def stage_review(state: dict, ticket: dict, scenarios: dict, tester_report: dict) -> tuple:
    safe_print("\n── Reviewer ────────────────────────────────────")

    report = run_reviewer(ticket, scenarios, tester_report)
    score  = report.get("score", 0)
    decision = report.get("decision", "escalate")

    safe_print(f"   Score: {score}/100  →  {decision.upper()}")
    safe_print(f"   {report.get('summary', '')}")

    path = RESULTS_DIR / "reviewer_report.json"
    path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    state["reviewer_done"]   = True
    state["reviewer_report"] = report
    state["stage"]           = "orchestrate"
    return state, report


# ── Main pipeline ─────────────────────────────────────────────────────────────

def run_pipeline(ticket_id: str, base_url: str = "http://localhost:3000"):

    safe_print(f"\n{'='*52}")
    safe_print(f"  Pipeline starting — {ticket_id}")
    safe_print(f"{'='*52}")

    # ── Load state and ticket ──────────────────────────────
    state = load_state(ticket_id)
    state["base_url"] = base_url
    save_state(state)

    safe_print(f"\nFetching {ticket_id} from Jira...")
    ticket = fetch_ticket(ticket_id)

    # Save ticket for Reviewer
    (RESULTS_DIR / "ticket.json").write_text(json.dumps(ticket, indent=2), encoding="utf-8")
    safe_print(f"   {ticket['summary']}")

    scenarios     = None
    test_files    = []
    tester_report = None

    # Reload any existing results if resuming
    if state.get("scenarios_file") and Path(state["scenarios_file"]).exists():
        scenarios = json.loads(Path(state["scenarios_file"]).read_text(encoding="utf-8"))
    if state.get("tester_report"):
        tester_report = state["tester_report"]
    if state.get("test_files"):
        test_files = state["test_files"]

    # ── Main loop ──────────────────────────────────────────
    max_loops = 8

    for loop in range(max_loops):
        safe_print(f"\n[ Loop {loop+1} | Stage: {state['stage']} | Retries: {state['retry_count']} ]")

        # Ask Orchestrator what to do
        decision = run_orchestrator(state)
        next_stage = decision.get("next_stage")
        safe_print(f"Orchestrator → {next_stage}  ({decision.get('reasoning', '')})")

        # ── Analyse ───────────────────────────────────────
        if next_stage == "analyse":
            state, scenarios = stage_analyse(state, ticket)
            save_state(state)

        # ── Code ──────────────────────────────────────────
        elif next_stage == "code":
            retry_instructions = decision.get("retry_instructions")
            state["last_retry_instructions"] = retry_instructions
            state, test_files = stage_code(state, scenarios, retry_instructions)
            save_state(state)

        # ── Test ──────────────────────────────────────────
        elif next_stage == "test":
            state, tester_report = stage_test(state, test_files)
            save_state(state)

        # ── Review ────────────────────────────────────────
        elif next_stage == "review":
            state, reviewer_report = stage_review(
                state, ticket, scenarios, tester_report
            )
            state["retry_count"] += 1
            save_state(state)

        # ── Done ──────────────────────────────────────────
        elif next_stage == "done":
            summary = decision.get("final_summary", {})
            safe_print(f"\n{'='*52}")
            safe_print(f"  PIPELINE COMPLETE")
            safe_print(f"  Ticket:  {ticket_id}")
            safe_print(f"  Score:   {summary.get('score', '?')}/100")
            safe_print(f"  Tests:   {summary.get('tests_passing', '?')}/{summary.get('tests_written', '?')} passing")
            safe_print(f"  Results: {RESULTS_DIR}/")
            safe_print(f"{'='*52}\n")
            clear_state()
            return True

        # ── Escalate ──────────────────────────────────────
        elif next_stage == "escalate":
            reason = decision.get("escalation_reason", "Unknown reason")
            safe_print(f"\n{'='*52}")
            safe_print(f"  ESCALATED — human review needed")
            safe_print(f"  Ticket: {ticket_id}")
            safe_print(f"  Reason: {reason}")
            if state.get("reviewer_report"):
                score = state["reviewer_report"].get("score", "?")
                safe_print(f"  Score:  {score}/100")
                gaps = state["reviewer_report"].get("gaps", [])
                if gaps:
                    safe_print(f"  Gaps:")
                    for g in gaps[:3]:
                        safe_print(f"    [{g['priority'].upper()}] {g['description']}")
            safe_print(f"  State saved — fix issues and re-run to resume")
            safe_print(f"{'='*52}\n")
            save_state(state)
            return False

        else:
            safe_print(f"Unknown stage: {next_stage} — stopping")
            break

        time.sleep(1)   # brief pause between stages

    safe_print("Hit max loops — something is stuck. Check pipeline_state.json")
    return False


# ── Entry point ───────────────────────────────────────────────────────────────

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python pipeline.py TICKET-ID [base-url]")
        print("       python pipeline.py PROJ-123 https://yourapp.com")
        sys.exit(1)

    ticket_id = sys.argv[1]
    base_url  = sys.argv[2] if len(sys.argv) > 2 else "http://localhost:3000"

    success = run_pipeline(ticket_id, base_url)
    sys.exit(0 if success else 1)
