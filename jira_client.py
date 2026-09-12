import os
import json
import requests
from pathlib import Path
from base64 import b64encode

def fetch_ticket(ticket_id: str) -> dict:
    domain   = os.environ.get("JIRA_DOMAIN")      # e.g. "yourcompany.atlassian.net"
    email    = os.environ.get("JIRA_EMAIL")
    api_key  = os.environ.get("JIRA_API_KEY")     # from id.atlassian.com -> API tokens

    # Check for existing local ticket.json
    local_ticket = Path("ticket.json")
    if local_ticket.exists():
        try:
            data = json.loads(local_ticket.read_text(encoding="utf-8"))
            if data.get("id") == ticket_id:
                return data
        except Exception:
            pass

    if not (domain and email and api_key):
        print(f"[Notice] JIRA credentials not set in environment. Using deterministic mock ticket for {ticket_id}.")
        return {
            "id":          ticket_id,
            "summary":     f"Add automated feature and validation flow for {ticket_id}",
            "description": f"Feature requirement for {ticket_id}: Customer triggers flow and receives 200 OK confirmation.",
            "acceptance":  f"1. Customer executes primary action.\n2. Service returns 200 OK.\n3. Application log contains OrderPlacedSuccessfully.",
            "type":        "Story",
            "priority":    "High"
        }

    credentials = b64encode(f"{email}:{api_key}".encode()).decode()

    response = requests.get(
        f"https://{domain}/rest/api/3/issue/{ticket_id}",
        headers={
            "Authorization": f"Basic {credentials}",
            "Accept": "application/json"
        }
    )
    response.raise_for_status()
    data = response.json()

    # Pull out only what the Analyst needs — keep it lean
    return {
        "id":          data["key"],
        "summary":     data["fields"]["summary"],
        "description": extract_text(data["fields"].get("description")),
        "acceptance":  extract_text(data["fields"].get("customfield_10016")),  # adjust field name for your Jira
        "type":        data["fields"]["issuetype"]["name"],
        "priority":    data["fields"]["priority"]["name"],
    }

def extract_text(field) -> str:
    """Jira uses Atlassian Document Format (ADF) — flatten it to plain text."""
    if field is None:
        return ""
    if isinstance(field, str):
        return field
    # ADF is nested JSON — walk it
    texts = []
    def walk(node):
        if isinstance(node, dict):
            if node.get("type") == "text":
                texts.append(node.get("text", ""))
            for child in node.get("content", []):
                walk(child)
        elif isinstance(node, list):
            for item in node:
                walk(item)
    walk(field)
    return " ".join(texts).strip()
