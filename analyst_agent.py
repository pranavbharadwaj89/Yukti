import os
import json
import anthropic

ANALYST_SYSTEM_PROMPT = """
You are the Analyst agent in a software testing pipeline.

Your job: read a Jira ticket and extract everything needed to write automated tests.

## Output format
Respond ONLY with valid JSON. No prose, no markdown fences.

{
  "ticket_id": "string",
  "summary": "one sentence summary of what this feature does",
  "test_scenarios": [
    {
      "id": "TS-001",
      "name": "short name for this scenario",
      "type": "happy_path" | "edge_case" | "negative" | "boundary",
      "description": "what needs to happen",
      "preconditions": ["list of things that must be true first"],
      "steps": ["step 1", "step 2"],
      "expected_result": "what success looks like",
      "priority": "high" | "medium" | "low"
    }
  ],
  "ambiguities": ["anything unclear that the Orchestrator should flag"],
  "out_of_scope": ["things explicitly NOT to test"]
}

## Rules
- Extract every acceptance criterion as at least one scenario
- Always include at least one negative test (what happens when it breaks)
- Always include at least one edge case
- If the ticket is vague, still output what you can — put gaps in "ambiguities"
- Never invent requirements that aren't in the ticket
"""

def run_analyst(ticket: dict) -> dict:
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        # Fallback offline deterministic parser if no API key is provided
        return {
            "ticket_id": ticket.get("id", "PROJ-000"),
            "summary": ticket.get("summary", "Feature execution"),
            "test_scenarios": [
                {
                    "id": "TS-001",
                    "name": f"Happy path: {ticket.get('summary', 'Execute primary flow')}",
                    "type": "happy_path",
                    "description": ticket.get("description", "Execute primary workflow successfully"),
                    "preconditions": ["User is authenticated with valid credentials"],
                    "steps": ["Navigate to target feature", "Trigger action", "Verify success status"],
                    "expected_result": "System returns 200 OK and updates UI state",
                    "priority": "high"
                },
                {
                    "id": "TS-002",
                    "name": "Negative: Invalid inputs and validation error",
                    "type": "negative",
                    "description": "Verify system rejects invalid parameters gracefully",
                    "preconditions": ["User is on feature form"],
                    "steps": ["Submit form with empty required fields"],
                    "expected_result": "System returns 400 Bad Request with RFC 7807 problem details",
                    "priority": "medium"
                },
                {
                    "id": "TS-003",
                    "name": "Edge case: Timeout and network failure recovery",
                    "type": "edge_case",
                    "description": "Verify retry and graceful timeout behavior under degraded network",
                    "preconditions": ["Network latency threshold simulated"],
                    "steps": ["Trigger asynchronous flow run", "Simulate transient 503 error"],
                    "expected_result": "Retry policy executes and succeeds on subsequent attempt",
                    "priority": "low"
                }
            ],
            "ambiguities": [
                "Acceptance criteria not explicitly enumerated in ticket fields; inferred from description."
            ] if not ticket.get("acceptance") else [],
            "out_of_scope": ["Load testing beyond 10,000 req/s", "Third-party gateway downtime"]
        }

    client = anthropic.Anthropic()  # reads ANTHROPIC_API_KEY from env

    # Format the ticket clearly for the agent
    ticket_text = f"""
Ticket ID: {ticket['id']}
Type: {ticket['type']}
Priority: {ticket['priority']}
Summary: {ticket['summary']}

Description:
{ticket['description']}

Acceptance Criteria:
{ticket['acceptance'] or 'Not specified — infer from description.'}
"""

    response = client.messages.create(
        model=os.environ.get("ANTHROPIC_MODEL", "claude-sonnet-4-6"),
        max_tokens=2000,
        system=ANALYST_SYSTEM_PROMPT,
        messages=[
            {"role": "user", "content": f"Analyse this Jira ticket:\n\n{ticket_text}"}
        ]
    )

    raw = response.content[0].text
    # Handle optional markdown code block wrapping
    if raw.startswith("```json"):
        raw = raw.replace("```json", "").replace("```", "").strip()
    elif raw.startswith("```"):
        raw = raw.replace("```", "").strip()

    return json.loads(raw)
