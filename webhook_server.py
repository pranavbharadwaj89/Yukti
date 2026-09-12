import hashlib
import hmac
import json
import os
import threading
from flask import Flask, request, jsonify

from github_client   import set_commit_status, post_pr_comment
from github_analyst  import analyse_pr
from coder_agent     import run_coder
from tester_agent    import run_tester
from reviewer_agent  import run_reviewer

app = Flask(__name__)

WEBHOOK_SECRET = os.environ.get("GITHUB_WEBHOOK_SECRET", "local-webhook-secret")
OWNER          = os.environ.get("GITHUB_OWNER", "pranavbharadwaj89")
REPO           = os.environ.get("GITHUB_REPO", "Yukti")


def verify_signature(payload: bytes, signature: str) -> bool:
    """Confirm the webhook actually came from GitHub."""
    secret = os.environ.get("GITHUB_WEBHOOK_SECRET")
    if not secret:
        # Development mode without webhook secret configured
        return True
    if not signature:
        return False

    expected = "sha256=" + hmac.new(
        secret.encode(),
        payload,
        hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(expected, signature)


def run_pipeline_for_pr(pr_number: int, sha: str):
    """Full pipeline — runs in a background thread so webhook returns fast."""
    try:
        # 1. Tell GitHub we're working on it
        set_commit_status(OWNER, REPO, sha, "pending",
                         "Agent crew is analysing your PR...")

        # 2. Analyst reads the PR
        print(f"[PR #{pr_number}] Analyst reading diff...")
        scenarios = analyse_pr(OWNER, REPO, pr_number)
        n = len(scenarios.get("test_scenarios", []))
        print(f"[PR #{pr_number}] {n} scenarios extracted")

        # 3. Coder writes tests
        print(f"[PR #{pr_number}] Coder writing tests...")
        test_files  = []
        coder_results = []
        for scenario in scenarios.get("test_scenarios", []):
            result = run_coder(scenario)
            coder_results.append(result)
            if result.get("status") == "success" and result.get("filename"):
                test_files.append(f"output_tests/{result['filename']}")

        # 4. Tester runs them
        print(f"[PR #{pr_number}] Tester running {len(test_files)} files...")
        tester_report = run_tester(test_files) if test_files else {
            "status": "error", "summary": "No test files produced.", "results": []
        }

        # 5. Reviewer scores
        print(f"[PR #{pr_number}] Reviewer scoring...")
        ticket = {
            "id":          f"PR-{pr_number}",
            "summary":     scenarios.get("summary", ""),
            "description": scenarios.get("summary", ""),
            "acceptance":  ""
        }
        review = run_reviewer(ticket, scenarios, tester_report)
        score    = review.get("score", 0)
        decision = review.get("decision", "escalate")

        # 6. Post results back to the PR
        comment = format_pr_comment(scenarios, tester_report, review)
        post_pr_comment(OWNER, REPO, pr_number, comment)

        # 7. Set final commit status
        if decision == "ship":
            set_commit_status(OWNER, REPO, sha, "success",
                            f"Agent crew: {score}/100 — coverage looks good")
        else:
            set_commit_status(OWNER, REPO, sha, "failure",
                            f"Agent crew: {score}/100 — gaps found, see PR comment")

        print(f"[PR #{pr_number}] Done — score {score}/100 ({decision})")

    except Exception as e:
        print(f"[PR #{pr_number}] Pipeline error: {e}")
        set_commit_status(OWNER, REPO, sha, "error", f"Agent crew error: {str(e)[:100]}")


def format_pr_comment(scenarios: dict, tester_report: dict, review: dict) -> str:
    """Format a clean markdown comment for the PR."""
    score    = review.get("score", 0)
    decision = review.get("decision", "?")
    summary  = review.get("summary", "")

    icon = "✅" if decision == "ship" else "⚠️" if decision == "retry" else "🚨"

    lines = [
        f"## {icon} Agent crew test report",
        f"",
        f"**Coverage score: {score}/100** — `{decision.upper()}`",
        f"",
        f"{summary}",
        f"",
        f"### Scenarios tested ({len(scenarios.get('test_scenarios', []))})",
    ]

    for s in scenarios.get("test_scenarios", []):
        lines.append(f"- `{s.get('id')}` {s.get('name')} _{s.get('type')}_")

    lines += ["", "### Test results"]
    for r in tester_report.get("results", []):
        passed = r.get("tests_passed", 0)
        total  = r.get("tests_run", 0)
        status = "✅" if r.get("passed") else "❌"
        lines.append(f"- {status} `{r.get('file')}` — {passed}/{total} passing")
        for f in r.get("failures", []):
            lines.append(f"  - ❌ `{f.get('test_name')}`: {f.get('message', '')[:80]}")

    if review.get("gaps"):
        lines += ["", "### Gaps to address"]
        for g in review["gaps"]:
            pri = g.get("priority", "medium").upper()
            lines.append(f"- **[{pri}]** {g.get('description')}")
            lines.append(f"  → _{g.get('suggested_scenario')}_")

    lines += ["", "---", "_Generated by your agent crew — Analyst → Coder → Tester → Reviewer_"]
    return "\n".join(lines)


@app.route("/webhook", methods=["POST"])
def webhook():
    # Verify it's really GitHub
    sig = request.headers.get("X-Hub-Signature-256", "")
    if not verify_signature(request.data, sig):
        return jsonify({"error": "invalid signature"}), 401

    event   = request.headers.get("X-GitHub-Event", "")
    payload = request.get_json(silent=True) or {}

    # PR opened or new commits pushed to a PR
    if event == "pull_request":
        action = payload.get("action")
        if action in ("opened", "synchronize", "reopened"):
            pr_number = payload.get("pull_request", {}).get("number", 1)
            sha       = payload.get("pull_request", {}).get("head", {}).get("sha", "head-sha")
            print(f"PR #{pr_number} {action} — triggering pipeline")
            # Run in background so we return 200 to GitHub immediately
            thread = threading.Thread(
                target=run_pipeline_for_pr,
                args=(pr_number, sha),
                daemon=True
            )
            thread.start()

    # Direct push to main (optional — run on every commit)
    elif event == "push":
        branch = payload.get("ref", "").replace("refs/heads/", "")
        if branch == "main":
            sha = payload.get("after", "head-sha")
            print(f"Push to main ({sha[:7]}) — agent crew notified")

    return jsonify({"status": "received"}), 200


if __name__ == "__main__":
    port = int(os.environ.get("PORT", 5001))
    print(f"Agent Crew GitHub Webhook Server listening on port {port}...")
    app.run(port=port, debug=False)
