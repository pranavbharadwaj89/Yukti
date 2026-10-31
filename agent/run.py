"""
The agent entrypoint the pipeline calls.

    python -m agent.run                 # decide, then act
    python -m agent.run --explain       # decide and print, run nothing
    python -m agent.run --force         # audit every flow regardless

Exit codes, deliberately distinct:
    0  pass, warnings only, or skipped-by-design
    1  a blocking UI regression
    2  the agent or the audit could not run
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

from agent import context, decide

EXIT_OK, EXIT_FAIL, EXIT_ERROR = 0, 1, 2


def log(msg: str = "") -> None:
    print(f"[agent] {msg}" if msg else "", flush=True)


def write_decision_report(repo: Path, ctx, dec, *, verdict: str, audit_exit=None) -> Path:
    """Always leave a report behind, including when nothing was audited.

    A skipped run that writes nothing is indistinguishable from a run that
    never happened, and that ambiguity is how a silently-broken stage survives
    for months.
    """
    out = repo / ".brevitas" / "reports" / ctx.build_id
    out.mkdir(parents=True, exist_ok=True)

    (out / "decision.json").write_text(
        json.dumps(
            {
                "verdict": verdict,
                "action": dec.action.value,
                "reason": dec.reason,
                "skip_reason": dec.skip_reason.value if dec.skip_reason else None,
                "flows": list(dec.flows),
                "gate": dec.gate,
                "notes": list(dec.notes),
                "audit_exit_code": audit_exit,
                "context": {
                    "branch": ctx.branch,
                    "build_id": ctx.build_id,
                    "target_url": ctx.target_url,
                    "diff_base": ctx.diff_base,
                    "changed_file_count": len(ctx.changed_files),
                    "deploy": ctx.deploy_status,
                    "regression": ctx.regression_status,
                    "regression_mode": ctx.regression_mode,
                    "baselines_present": ctx.baselines_present,
                },
                "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            },
            indent=2,
        ),
        encoding="utf-8",
    )

    lines = [
        f"### UI Audit — {verdict}",
        "",
        dec.reason,
        "",
        "| | |",
        "|---|---|",
        f"| Branch | `{ctx.branch}` |",
        f"| Build | `{ctx.build_id}` |",
        f"| Flows | {', '.join(dec.flows) if dec.flows else '—'} |",
        f"| Gating | {'yes' if dec.gate else 'no (report-only)'} |",
    ]
    if ctx.diff_base:
        lines.append(f"| Compared against | `{ctx.diff_base}` ({len(ctx.changed_files)} files) |")
    if dec.banner:
        lines += ["", f"> **{dec.banner}**"]
    if dec.notes:
        lines += ["", "**Notes**", ""] + [f"- {n}" for n in dec.notes]
    if verdict == "SKIPPED":
        lines += ["", "_No audit was performed, so this is not a clean pass._"]

    (out / "decision.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    latest = repo / ".brevitas" / "reports" / "latest"
    try:
        if latest.is_symlink() or latest.exists():
            latest.unlink()
        latest.symlink_to(ctx.build_id)
    except OSError:
        pass

    return out


def find_engine() -> list[str] | None:
    if shutil.which("brevitas"):
        return ["brevitas"]
    local = Path(".venv/bin/brevitas")
    if local.is_file():
        return [str(local)]
    probe = subprocess.run(
        [sys.executable, "-m", "brevitas", "--version"],
        capture_output=True, check=False,
    )
    return [sys.executable, "-m", "brevitas"] if probe.returncode == 0 else None


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="agent.run", description=__doc__)
    ap.add_argument("--explain", action="store_true",
                    help="print the decision and exit without auditing")
    ap.add_argument("--force", action="store_true",
                    help="audit every flow, ignoring the change diff")
    ap.add_argument("--gate", action="store_true",
                    help="let a blocking finding exit 1, overriding the decision")
    args = ap.parse_args(argv)

    repo = Path.cwd()
    if not (repo / ".brevitas").is_dir():
        log("ERROR: no .brevitas/ directory here.")
        log("Run this from the repo root. If the repo has no audit config yet,")
        log("create one with `brevitas init` and commit it.")
        return EXIT_ERROR

    ctx = context.gather(repo, force=args.force)
    dec = decide.decide(
        changed_files=ctx.changed_files,
        ui_paths=ctx.ui_paths,
        flow_triggers=ctx.flow_triggers,
        all_flows=ctx.all_flows,
        deploy_status=ctx.deploy_status,
        regression_status=ctx.regression_status,
        regression_mode=ctx.regression_mode,
        is_default_branch=ctx.is_default_branch,
        baselines_present=ctx.baselines_present,
        force=args.force,
    )

    log("─" * 62)
    log(f"branch     : {ctx.branch}{'  (default)' if ctx.is_default_branch else ''}")
    log(f"build      : {ctx.build_id}")
    log(f"changed    : {len(ctx.changed_files)} file(s)"
        + (f" vs {ctx.diff_base}" if ctx.diff_base else " (diff unavailable)"))
    log(f"deploy     : {ctx.deploy_status}")
    log(f"regression : {ctx.regression_status or 'not reported'}  mode={ctx.regression_mode}")
    log("─" * 62)
    log(f"DECISION   : {dec.action.value.upper()}")
    log(f"             {dec.reason}")
    if dec.should_audit:
        log(f"GATING     : {'ON — a blocking finding will fail this build'
                            if dec.gate else 'off — report-only'}")
    for n in dec.notes:
        log(f"             · {n}")
    log("─" * 62)

    if args.explain:
        return EXIT_OK

    if not dec.should_audit:
        out = write_decision_report(repo, ctx, dec, verdict="SKIPPED")
        log(f"No audit run. Report: {out}")
        print(f"BREVITAS_REPORT={out / 'decision.md'}")
        return EXIT_OK

    engine = find_engine()
    if engine is None:
        log("ERROR: the brevitas engine is not installed.")
        log("Install it with: pip install 'git+<brevitas-repo>@<tag>'")
        out = write_decision_report(repo, ctx, dec, verdict="ERROR")
        print(f"BREVITAS_REPORT={out / 'decision.md'}")
        return EXIT_ERROR

    if not ctx.target_url:
        log("ERROR: BREVITAS_TARGET_URL is not set.")
        log("Set it to the URL of the build that was just deployed.")
        out = write_decision_report(repo, ctx, dec, verdict="ERROR")
        print(f"BREVITAS_REPORT={out / 'decision.md'}")
        return EXIT_ERROR

    cmd = [*engine, "stage",
           "--out", ".brevitas/reports",
           "--format", "html,md,json,sarif,junit"]
    for f in dec.flows:
        cmd += ["--flow", f]

    env = {
        **os.environ,
        "BREVITAS_TARGET_URL": ctx.target_url,
        "BREVITAS_BUILD_ID": ctx.build_id,
        "BREVITAS_UPSTREAM_DEPLOY": ctx.deploy_status,
        "BREVITAS_UPSTREAM_REGRESSION": ctx.regression_status or "",
        "BREVITAS_REGRESSION_MODE": ctx.regression_mode,
        "BREVITAS_REPORT_BANNER": dec.banner or "",
    }

    log(f"Running: {' '.join(cmd)}")
    log()
    try:
        audit_exit = subprocess.run(cmd, env=env, check=False).returncode
    except OSError as exc:
        log(f"ERROR: could not start the engine: {exc}")
        out = write_decision_report(repo, ctx, dec, verdict="ERROR")
        print(f"BREVITAS_REPORT={out / 'decision.md'}")
        return EXIT_ERROR
    log()

    verdict = {0: "PASS", 1: "FAIL", 2: "ERROR", 3: "SKIPPED"}.get(audit_exit, "ERROR")
    out = write_decision_report(repo, ctx, dec, verdict=verdict, audit_exit=audit_exit)

    log("─" * 62)
    log(f"UI AUDIT: {verdict}")
    if verdict == "ERROR":
        log("The audit could not complete. This is not a UI finding.")
    log(f"Report: {out}")
    print(f"BREVITAS_REPORT={out / 'decision.md'}")

    if audit_exit == EXIT_ERROR:
        return EXIT_ERROR
    if audit_exit == EXIT_FAIL:
        if dec.gate or args.gate:
            log("Gating is on: failing the build.")
            return EXIT_FAIL
        log("Report-only: not failing the build.")
        log("Gating turns on for the default branch once baselines are committed.")
        return EXIT_OK
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
