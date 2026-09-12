import json
import uuid
from datetime import datetime
from pathlib import Path

THOUGHTS_FILE = Path("thoughts_log.json")

def load_thoughts() -> list:
    if THOUGHTS_FILE.exists():
        try:
            return json.loads(THOUGHTS_FILE.read_text(encoding="utf-8"))
        except Exception:
            return []
    return []

def save_thought(thought: dict):
    thoughts = load_thoughts()
    thoughts.append(thought)
    THOUGHTS_FILE.write_text(json.dumps(thoughts, indent=2), encoding="utf-8")

def thought_to_ticket(raw_thought: str, base_url: str = "http://localhost:3000") -> dict:
    """
    Turn a plain-English thought into the same shape
    that fetch_ticket() used to return from Jira.
    Everything downstream stays identical.
    """
    # If raw_thought matches an existing ticket ID, return it directly
    for t in load_thoughts():
        if t.get("id") == raw_thought:
            return t

    ticket_id = f"THOUGHT-{uuid.uuid4().hex[:6].upper()}"

    ticket = {
        "id":          ticket_id,
        "summary":     raw_thought[:80],        # first 80 chars as title
        "description": raw_thought,             # full thought as description
        "acceptance":  "",                      # Analyst will infer these
        "type":        "Story",
        "priority":    "Medium",
        "source":      "thought_intake",
        "created_at":  datetime.now().isoformat(),
        "base_url":    base_url,
    }

    save_thought(ticket)
    return ticket

def list_thoughts() -> list:
    return load_thoughts()
