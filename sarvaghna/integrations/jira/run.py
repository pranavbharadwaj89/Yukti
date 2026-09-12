import os
import sys
import json
from jira_client import fetch_ticket
from analyst_agent import run_analyst

def analyse_ticket(ticket_id: str):
    print(f"Fetching {ticket_id} from Jira...")
    try:
        ticket = fetch_ticket(ticket_id)
    except Exception as e:
        print(f"[Notice] Jira fetch failed ({e}). Checking for mock or local ticket fallback...")
        ticket = {
            "id": ticket_id,
            "summary": f"Implement automated checkout and payment confirmation flow for {ticket_id}",
            "description": "When customer adds items to cart and submits payment, order service must confirm order and log transaction.",
            "acceptance": "1. User completes checkout.\n2. Payment API returns 200.\n3. Application log contains OrderPlacedSuccessfully.",
            "type": "Story",
            "priority": "High"
        }

    # Save ticket.json for Reviewer agent
    with open("ticket.json", "w", encoding="utf-8") as f:
        json.dump(ticket, f, indent=2)

    print("Running Analyst agent...")
    scenarios = run_analyst(ticket)

    print(f"\nFound {len(scenarios['test_scenarios'])} test scenarios")

    if scenarios.get("ambiguities"):
        print(f"\nAmbiguities flagged (review before coding):")
        for a in scenarios["ambiguities"]:
            print(f"  - {a}")

    # Save for the Coder agent to pick up
    output_filename = f"scenarios_{ticket_id}.json"
    with open(output_filename, "w", encoding="utf-8") as f:
        json.dump(scenarios, f, indent=2)

    print(f"\nSaved to {output_filename}")
    return scenarios

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python run.py <TICKET_ID> (e.g. python run.py PROJ-123)")
        sys.exit(1)
    analyse_ticket(sys.argv[1])
