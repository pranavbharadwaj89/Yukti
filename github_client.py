import os
import base64
import requests
from pathlib import Path

BASE = "https://api.github.com"

def get_headers() -> dict:
    token = os.environ.get("GITHUB_TOKEN", "")
    headers = {
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28"
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


# ── READ ──────────────────────────────────────────────────────────────────────

def get_pr(owner: str, repo: str, pr_number: int) -> dict:
    """Get PR metadata — title, body, branch, changed files."""
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        print(f"[Notice] GITHUB_TOKEN not set. Returning mock PR #{pr_number} metadata.")
        return {
            "number": pr_number,
            "title": f"Feature: Optimize customer checkout and order confirmation flow (#{pr_number})",
            "body": "Implements payment receipt notifications, retries on transient network errors, and logs OrderPlacedSuccessfully.",
            "head": {"ref": f"feat/checkout-flow-{pr_number}", "sha": "a1b2c3d4e5f67890abcdef1234567890abcdef12"},
            "base": {"ref": "main"}
        }

    r = requests.get(f"{BASE}/repos/{owner}/{repo}/pulls/{pr_number}", headers=get_headers())
    r.raise_for_status()
    return r.json()


def get_pr_files(owner: str, repo: str, pr_number: int) -> list:
    """List files changed in a PR with their diffs."""
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        return [
            {
                "filename": "src/Yukti.Application/Orders/Commands/ConfirmOrderCommand.cs",
                "status": "modified",
                "additions": 42,
                "deletions": 5,
                "patch": "@@ -15,5 +15,42 @@\n+    if (request.PaymentMethod == null) throw new ArgumentNullException();\n+    await _bus.PublishAsync(new OrderConfirmedEvent(order.Id));"
            },
            {
                "filename": "apps/yukti-gui/src/features/checkout/checkout-page.tsx",
                "status": "modified",
                "additions": 28,
                "deletions": 2,
                "patch": "@@ -20,2 +20,28 @@\n+  const handleCheckout = async () => {\n+    await api.post('/api/v1/orders/confirm');\n+  };"
            }
        ]

    r = requests.get(f"{BASE}/repos/{owner}/{repo}/pulls/{pr_number}/files", headers=get_headers())
    r.raise_for_status()
    return r.json()   # list of {filename, status, additions, deletions, patch}


def get_file_content(owner: str, repo: str, filepath: str, ref: str = "main") -> str:
    """Read a file from the repo at a given branch/commit."""
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        # Fallback to local file if available
        local_p = Path(filepath)
        if local_p.exists():
            return local_p.read_text(encoding="utf-8")
        return f"// Content of {filepath} at {ref}"

    r = requests.get(
        f"{BASE}/repos/{owner}/{repo}/contents/{filepath}",
        headers=get_headers(),
        params={"ref": ref}
    )
    r.raise_for_status()
    data = r.json()
    return base64.b64decode(data["content"]).decode("utf-8")


def get_commit_message(owner: str, repo: str, sha: str) -> str:
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        return f"Commit {sha[:7]}: update feature implementation"

    r = requests.get(f"{BASE}/repos/{owner}/{repo}/commits/{sha}", headers=get_headers())
    r.raise_for_status()
    return r.json()["commit"]["message"]


# ── WRITE ─────────────────────────────────────────────────────────────────────

def safe_print(text: str):
    try:
        print(text)
    except UnicodeEncodeError:
        print(text.encode("ascii", errors="replace").decode("ascii"))

def post_pr_comment(owner: str, repo: str, pr_number: int, body: str) -> dict:
    """Post a comment on a PR — markdown supported."""
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        safe_print(f"[Notice] GITHUB_TOKEN not configured. Logging PR comment locally:\n{body[:250]}...\n")
        # Save locally for verification
        Path("pr_comment_preview.md").write_text(body, encoding="utf-8")
        return {"id": 1, "body": body, "status": "mock_posted"}

    r = requests.post(
        f"{BASE}/repos/{owner}/{repo}/issues/{pr_number}/comments",
        headers=get_headers(),
        json={"body": body}
    )
    r.raise_for_status()
    return r.json()


def set_commit_status(owner: str, repo: str, sha: str,
                      state: str, description: str, context: str = "agent-crew/tests") -> dict:
    """
    Set a status check on a commit.
    state: 'pending' | 'success' | 'failure' | 'error'
    """
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        print(f"[Status Check] {context}: [{state.upper()}] {description}")
        return {"state": state, "description": description, "context": context}

    r = requests.post(
        f"{BASE}/repos/{owner}/{repo}/statuses/{sha}",
        headers=get_headers(),
        json={
            "state":       state,
            "description": description[:140],   # GitHub limit
            "context":     context
        }
    )
    r.raise_for_status()
    return r.json()


def create_pr(owner: str, repo: str, title: str, body: str,
              head: str, base: str = "main") -> dict:
    """Open a new PR from head branch into base."""
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        print(f"[Notice] GITHUB_TOKEN not set. Mocking PR '{title}' from {head} -> {base}")
        return {"number": 99, "title": title, "head": {"ref": head}, "base": {"ref": base}}

    r = requests.post(
        f"{BASE}/repos/{owner}/{repo}/pulls",
        headers=get_headers(),
        json={"title": title, "body": body, "head": head, "base": base}
    )
    r.raise_for_status()
    return r.json()
