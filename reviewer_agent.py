import os
import re
import json
import anthropic

REVIEWER_SYSTEM_PROMPT = """
You are the Reviewer agent in a software testing pipeline.

Your job: evaluate how well the test suite covers the original requirements.
You produce an honest, precise quality score that the Orchestrator uses to
decide whether to ship, retry, or escalate.

## What you receive
- The original Jira ticket (acceptance criteria and description)
- The test scenarios the Analyst extracted
- The test results the Tester produced

## Scoring rubric (0–100)

Start at 100. Deduct points as follows:

COVERAGE GAPS (how much of the requirement is actually tested):
  -15  each acceptance criterion with NO corresponding test
  -8   each acceptance criterion with a test that exists but FAILED
  -5   each acceptance criterion covered only by a happy-path test (no negative/edge)

TEST QUALITY (how trustworthy the tests are):
  -10  more than 30% of tests failed due to environment issues (not real failures)
  -8   any test with no assertions (only navigation, no expect() calls)
  -5   no negative test cases at all across the whole suite
  -5   no edge case tests at all across the whole suite

COMPLETENESS:
  -10  Analyst flagged ambiguities that were never resolved and affect coverage
  -5   out-of-scope items were tested anyway (wasted coverage)

Floor: score cannot go below 0.

## Decision thresholds
- 90–100 → SHIP: excellent coverage, minor gaps acceptable
- 80–89  → SHIP: good coverage, note gaps for follow-up
- 65–79  → RETRY: meaningful gaps, Coder should fill them
- 50–64  → RETRY: significant coverage problems, re-analyse may be needed
- below 50 → ESCALATE: fundamental issues, human review needed

## Output format
Respond ONLY with valid JSON:

{
  "score": 0-100,
  "decision": "ship" | "retry" | "escalate",
  "summary": "2-3 sentence plain-English verdict a PM could read",
  "criteria_coverage": [
    {
      "criterion": "exact text of the acceptance criterion",
      "status": "covered" | "partially_covered" | "missing" | "failed",
      "covered_by": ["test_name_1", "test_name_2"],
      "notes": "any detail worth noting"
    }
  ],
  "deductions": [
    {
      "reason": "exact reason from rubric",
      "points": 15,
      "detail": "which criterion or test this applies to"
    }
  ],
  "gaps": [
    {
      "priority": "high" | "medium" | "low",
      "description": "what is missing",
      "suggested_scenario": "what test should be written to fill this gap"
    }
  ],
  "strengths": ["what the test suite does well — be specific"],
  "retry_instructions": null or "precise instructions for the Coder on what to add"
}

## Rules
- Be honest. A 60 that is accurate is better than an 85 that is not.
- Map each acceptance criterion to tests by reading test names and descriptions carefully
- A test that exists but fails does NOT count as coverage
- Do not penalise for failures caused by environment issues (app not running etc.)
- retry_instructions must be specific enough for the Coder to act on immediately
"""


def deterministic_fallback_reviewer(ticket: dict, scenarios: dict, tester_report: dict) -> dict:
    """Deterministic rubric scoring when ANTHROPIC_API_KEY is not configured."""
    score = 100
    deductions = []
    gaps = []
    strengths = []
    criteria_coverage = []

    test_scenarios = scenarios.get("test_scenarios", [])
    tester_results = tester_report.get("results", [])
    env_issues = tester_report.get("environment_issues", [])

    # Extract criteria
    raw_criteria = ticket.get("acceptance") or ticket.get("description", "Primary feature execution")
    criterion_lines = [c.strip("-* 1234567890.\t") for c in str(raw_criteria).split("\n") if c.strip()]
    if not criterion_lines:
        criterion_lines = [ticket.get("summary", "Complete primary user story")]

    scenario_types = [s.get("type", "").lower() for s in test_scenarios]
    has_negative = any("negative" in t for t in scenario_types)
    has_edge = any("edge" in t or "boundary" in t for t in scenario_types)

    # Check overall test pass status
    all_tests_passed = tester_report.get("status") == "all_passed"

    for crit in criterion_lines[:5]:
        matching_scenarios = [
            s.get("name", s.get("id"))
            for s in test_scenarios
            if any(w.lower() in (s.get("name", "") + " " + s.get("description", "")).lower() for w in crit.split()[:3])
        ]
        if not matching_scenarios and test_scenarios:
            matching_scenarios = [test_scenarios[0].get("name", test_scenarios[0].get("id"))]

        if not matching_scenarios:
            status = "missing"
            score -= 15
            deductions.append({
                "reason": "each acceptance criterion with NO corresponding test",
                "points": 15,
                "detail": f"Criterion '{crit[:50]}' has no matching test scenario."
            })
            gaps.append({
                "priority": "high",
                "description": f"Missing test for: {crit}",
                "suggested_scenario": f"Write end-to-end scenario verifying {crit}"
            })
        elif not all_tests_passed:
            status = "failed"
            score -= 8
            deductions.append({
                "reason": "each acceptance criterion with a test that exists but FAILED",
                "points": 8,
                "detail": f"Criterion '{crit[:50]}' covered by tests that encountered failures."
            })
        elif not has_negative and not has_edge:
            status = "partially_covered"
            score -= 5
            deductions.append({
                "reason": "each acceptance criterion covered only by a happy-path test (no negative/edge)",
                "points": 5,
                "detail": f"Criterion '{crit[:50]}' lacks negative or boundary coverage."
            })
        else:
            status = "covered"

        criteria_coverage.append({
            "criterion": crit,
            "status": status,
            "covered_by": matching_scenarios or ["None"],
            "notes": "Verified against test execution report."
        })

    # Test Quality Deductions
    if not has_negative:
        score -= 5
        deductions.append({
            "reason": "no negative test cases at all across the whole suite",
            "points": 5,
            "detail": "Test scenarios contain no negative/error-handling validation."
        })

    if not has_edge:
        score -= 5
        deductions.append({
            "reason": "no edge case tests at all across the whole suite",
            "points": 5,
            "detail": "Test scenarios contain no timeout or latency edge cases."
        })

    if env_issues:
        score -= 10
        deductions.append({
            "reason": "more than 30% of tests failed due to environment issues",
            "points": 10,
            "detail": f"Environment issues detected: {', '.join(env_issues[:2])}"
        })

    # Completeness Deductions
    ambiguities = scenarios.get("ambiguities", [])
    if ambiguities:
        score -= 10
        deductions.append({
            "reason": "Analyst flagged ambiguities that were never resolved and affect coverage",
            "points": 10,
            "detail": f"{len(ambiguities)} ambiguity marker(s) left unclarified."
        })

    # Floor score at 0
    score = max(0, min(100, score))

    if score >= 80:
        decision = "ship"
        summary = f"High-confidence test suite with {score}/100 quality score. Core requirements and scenarios verified cleanly."
        retry_instructions = None
        strengths.append("Strong alignment between acceptance criteria and test scenario assertions.")
        if has_negative:
            strengths.append("Includes negative test paths for invalid user input.")
    elif score >= 50:
        decision = "retry"
        summary = f"Acceptable baseline ({score}/100) but meaningful gaps or failure modes were identified. Coder should generate missing coverage."
        retry_instructions = "Generate additional negative and edge-case tests covering flagged acceptance criteria."
    else:
        decision = "escalate"
        summary = f"Critical quality deficit ({score}/100). Test suite failed to cover essential requirements or suffered blocking failures."
        retry_instructions = "Human developer and QA lead review required before proceeding."

    return {
        "score": score,
        "decision": decision,
        "summary": summary,
        "criteria_coverage": criteria_coverage,
        "deductions": deductions,
        "gaps": gaps,
        "strengths": strengths or ["Automated test suite cleanly generated and parsed."],
        "retry_instructions": retry_instructions
    }


def run_reviewer(
    ticket: dict,
    scenarios: dict,
    tester_report: dict
) -> dict:
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        print("[Notice] ANTHROPIC_API_KEY not configured. Executing Reviewer in deterministic rubric mode.")
        return deterministic_fallback_reviewer(ticket, scenarios, tester_report)

    client = anthropic.Anthropic()

    # Build a clean, structured input — the better Claude can read this,
    # the more accurate the scoring will be
    user_message = f"""
Please review this test suite and score its quality.

## Original Jira ticket
Ticket ID: {ticket.get('id', 'PROJ-UNKNOWN')}
Summary: {ticket.get('summary', '')}

Acceptance criteria:
{ticket.get('acceptance') or 'Not explicitly stated — infer from description.'}

Full description:
{ticket.get('description', '')}

---

## Test scenarios (from Analyst)
{json.dumps(scenarios.get('test_scenarios', []), indent=2)}

Analyst flagged these ambiguities:
{json.dumps(scenarios.get('ambiguities', []), indent=2)}

---

## Test results (from Tester)
Overall status: {tester_report.get('status')}
Summary: {tester_report.get('summary')}

Detailed results:
{json.dumps(tester_report.get('results', []), indent=2)}

Environment issues noted:
{json.dumps(tester_report.get('environment_issues', []), indent=2)}
"""

    response = client.messages.create(
        model=os.environ.get("ANTHROPIC_MODEL", "claude-sonnet-4-6"),
        max_tokens=3000,
        system=REVIEWER_SYSTEM_PROMPT,
        messages=[{"role": "user", "content": user_message}]
    )

    raw = response.content[0].text

    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        match = re.search(r'\{[\s\S]*\}', raw)
        if match:
            return json.loads(match.group())
        raise ValueError(f"Reviewer returned unparseable output:\n{raw}")
