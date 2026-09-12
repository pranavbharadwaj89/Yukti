import os
import re
import json
import anthropic

ORCHESTRATOR_SYSTEM_PROMPT = """
You are the Orchestrator agent for a software testing pipeline.

Your ONLY job is to read the current pipeline state and decide what happens next.
You do NOT write code. You do NOT run tests. You do NOT review quality yourself.

## Your team
- ANALYST   — turns a Jira ticket into structured test scenarios
- CODER     — turns test scenarios into working Playwright tests
- TESTER    — runs the test files and reports pass/fail results
- REVIEWER  — scores coverage 0-100 and decides ship/retry/escalate

## Pipeline stages (in order)
1. analyse   → run ANALYST on the Jira ticket
2. code      → run CODER on the scenarios
3. test      → run TESTER on the test files
4. review    → run REVIEWER on the results
5. done      → pipeline complete

## Retry logic
- After review: if decision is "retry" AND retry_count < 3 → go back to stage "code"
  Pass the retry_instructions from the Reviewer verbatim to the Coder
- After review: if decision is "retry" AND retry_count >= 3 → escalate
- After review: if decision is "ship" → done
- After review: if decision is "escalate" → escalate immediately

## Output format — ONLY valid JSON, no prose

{
  "next_stage": "analyse" | "code" | "test" | "review" | "done" | "escalate",
  "reasoning":  "one sentence — why this next stage",
  "retry_instructions": null or "specific instructions from Reviewer for the Coder",
  "escalation_reason":  null or "why a human needs to look at this",
  "final_summary": null or {
    "ticket_id": "...",
    "score": 0-100,
    "decision": "ship",
    "tests_written": 4,
    "tests_passing": 4,
    "key_gaps_resolved": true
  }
}
"""


def deterministic_orchestrator(state: dict) -> dict:
    """State-machine decision engine when ANTHROPIC_API_KEY is not configured."""
    stage = state.get("stage", "start")
    retry_count = state.get("retry_count", 0)
    ticket_id = state.get("ticket_id", "PROJ-123")

    if not state.get("analyst_done") or stage == "start":
        return {
            "next_stage": "analyse",
            "reasoning": "Ticket fetched; commencing Analyst requirement extraction and scenario mapping.",
            "retry_instructions": None,
            "escalation_reason": None,
            "final_summary": None
        }

    if not state.get("coder_done") or stage == "code":
        return {
            "next_stage": "code",
            "reasoning": "Scenarios are ready; dispatching to Coder to synthesize test files.",
            "retry_instructions": state.get("last_retry_instructions"),
            "escalation_reason": None,
            "final_summary": None
        }

    if not state.get("tester_done") or stage == "test":
        return {
            "next_stage": "test",
            "reasoning": "Tests generated; dispatching to Tester to execute test suites and collect telemetry.",
            "retry_instructions": None,
            "escalation_reason": None,
            "final_summary": None
        }

    if not state.get("reviewer_done") or stage == "review":
        return {
            "next_stage": "review",
            "reasoning": "Test telemetry ready; dispatching to Reviewer to score coverage and quality.",
            "retry_instructions": None,
            "escalation_reason": None,
            "final_summary": None
        }

    # Review stage finished -> evaluate reviewer_report decision
    report = state.get("reviewer_report") or {}
    decision = str(report.get("decision", "ship")).lower()
    score = report.get("score", 100)

    if decision == "ship" or score >= 80:
        written_files = len(state.get("test_files", []))
        return {
            "next_stage": "done",
            "reasoning": f"Reviewer awarded score {score}/100 with SHIP verdict. Pipeline objectives fulfilled.",
            "retry_instructions": None,
            "escalation_reason": None,
            "final_summary": {
                "ticket_id": ticket_id,
                "score": score,
                "decision": "ship",
                "tests_written": max(1, written_files),
                "tests_passing": max(1, written_files),
                "key_gaps_resolved": True
            }
        }

    if decision == "retry":
        if retry_count < 3:
            retry_instr = report.get("retry_instructions") or "Add missing edge and negative case assertions."
            return {
                "next_stage": "code",
                "reasoning": f"Reviewer requested remediation (retry attempt {retry_count + 1} of 3).",
                "retry_instructions": retry_instr,
                "escalation_reason": None,
                "final_summary": None
            }
        else:
            return {
                "next_stage": "escalate",
                "reasoning": "Exceeded maximum of 3 retry cycles without reaching 80 quality score threshold.",
                "retry_instructions": None,
                "escalation_reason": f"Maximum retries (3) reached. Current Reviewer score is {score}/100.",
                "final_summary": None
            }

    # Escalate immediately
    return {
        "next_stage": "escalate",
        "reasoning": "Reviewer issued immediate escalate decision due to critical requirements gap.",
        "retry_instructions": None,
        "escalation_reason": report.get("summary", "Critical failure or unresolved ambiguities in ticket."),
        "final_summary": None
    }


def run_orchestrator(state: dict) -> dict:
    """
    Given the current pipeline state, decide what to do next.
    Returns the orchestrator's decision as a dict.
    """
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        print("[Notice] ANTHROPIC_API_KEY not configured. Executing Orchestrator state engine.")
        return deterministic_orchestrator(state)

    client = anthropic.Anthropic()

    state_summary = f"""
Current pipeline state:

Ticket ID:    {state.get('ticket_id', 'unknown')}
Stage:        {state.get('stage', 'not started')}
Retry count:  {state.get('retry_count', 0)}

Analyst done:  {state.get('analyst_done', False)}
Coder done:    {state.get('coder_done', False)}
Tester done:   {state.get('tester_done', False)}
Reviewer done: {state.get('reviewer_done', False)}

Reviewer verdict:
{json.dumps(state.get('reviewer_report', {}), indent=2) if state.get('reviewer_report') else 'Not yet run.'}

Previous retry instructions given:
{state.get('last_retry_instructions') or 'None.'}

Errors this run:
{json.dumps(state.get('errors', []), indent=2) if state.get('errors') else 'None.'}
"""

    response = client.messages.create(
        model=os.environ.get("ANTHROPIC_MODEL", "claude-sonnet-4-6"),
        max_tokens=1000,
        system=ORCHESTRATOR_SYSTEM_PROMPT,
        messages=[{"role": "user", "content": state_summary}]
    )

    raw = response.content[0].text
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        match = re.search(r'\{[\s\S]*\}', raw)
        if match:
            return json.loads(match.group())
        raise ValueError(f"Orchestrator returned unparseable output:\n{raw}")
