import sys
import time
import json
import subprocess
from pathlib import Path
from thought_intake import load_thoughts

PROCESSED_FILE = Path("processed_thoughts.json")

def load_processed() -> set:
    if PROCESSED_FILE.exists():
        try:
            return set(json.loads(PROCESSED_FILE.read_text(encoding="utf-8")))
        except Exception:
            return set()
    return set()

def mark_processed(ticket_id: str):
    processed = load_processed()
    processed.add(ticket_id)
    PROCESSED_FILE.write_text(json.dumps(list(processed), indent=2), encoding="utf-8")

def watch(base_url: str = "http://localhost:3000", poll_seconds: int = 10):
    print(f"Watching for new thoughts every {poll_seconds}s... (Ctrl+C to stop)\n")

    while True:
        processed = load_processed()
        thoughts  = load_thoughts()

        for thought in thoughts:
            tid = thought.get("id")
            if tid and tid not in processed:
                print(f"New thought detected: {thought.get('summary')}")
                mark_processed(tid)
                subprocess.run([
                    sys.executable, "pipeline.py",
                    thought.get("description", thought.get("summary", "")),
                    thought.get("base_url", base_url)
                ])

        time.sleep(poll_seconds)

if __name__ == "__main__":
    base_url = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:3000"
    watch(base_url)
