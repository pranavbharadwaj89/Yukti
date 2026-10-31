"""
Gathering the facts the decision needs.

Everything here touches the outside world — git, the environment, the config
file — so nothing here contains a rule. Rules live in decide.py.
"""

from __future__ import annotations

import fnmatch
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

# Paths the agent itself writes. These must never reach the decision: if a
# report folder is committed (by accident or by policy), it would show up as a
# changed file, match no flow trigger, and push the agent into auditing every
# flow forever. The agent's own output must not influence its next decision.
EXCLUDED_FROM_DIFF = [
    ".brevitas/reports/*",
    ".brevitas/reports",
    "*/.brevitas/reports/*",
]

DEFAULT_UI_PATHS = [
    "*.html", "*.htm", "*.css", "*.scss", "*.sass", "*.less",
    "*.js", "*.jsx", "*.ts", "*.tsx", "*.vue", "*.svelte",
    "*.hbs", "*.ejs", "*.pug", "*.twig", "*.erb", "*.blade.php",
    "*/templates/*", "*/components/*", "*/views/*", "*/pages/*",
    "*/public/*", "*/static/*", "*/assets/*",
    ".brevitas/*",
]


@dataclass(frozen=True)
class Context:
    changed_files: list[str]
    ui_paths: list[str]
    flow_triggers: dict[str, list[str]]
    all_flows: list[str]
    deploy_status: str
    regression_status: str | None
    regression_mode: str
    is_default_branch: bool
    baselines_present: bool
    target_url: str
    build_id: str
    branch: str
    diff_base: str | None


def _strip_agent_output(files: list[str]) -> list[str]:
    return [
        f for f in files
        if not any(fnmatch.fnmatch(f, p) for p in EXCLUDED_FROM_DIFF)
    ]


def _git(*args: str) -> str | None:
    try:
        out = subprocess.run(
            ["git", *args], capture_output=True, text=True, timeout=30, check=False
        )
    except (OSError, subprocess.SubprocessError):
        return None
    return out.stdout.strip() if out.returncode == 0 else None


def changed_files_for_event() -> tuple[list[str], str | None]:
    """Files this merge or PR touched, plus the base they were compared against.

    Returns ([], None) when the comparison can't be established — a shallow
    clone, an initial commit, a detached state. The caller treats an unknown
    diff as "audit everything" rather than "audit nothing", because an empty
    list here must never be read as "nothing changed".
    """
    # Pull request: compare against the branch being merged into.
    base_ref = os.environ.get("GITHUB_BASE_REF")
    if base_ref:
        for ref in (f"origin/{base_ref}", base_ref):
            merge_base = _git("merge-base", "HEAD", ref)
            if merge_base:
                files = _git("diff", "--name-only", f"{merge_base}...HEAD")
                if files is not None:
                    return (_strip_agent_output([f for f in files.splitlines() if f]), ref)

    # Push or merge: compare against the commit the branch was at before.
    before = os.environ.get("GITHUB_EVENT_BEFORE") or os.environ.get("CI_COMMIT_BEFORE_SHA")
    if before and set(before) != {"0"}:
        files = _git("diff", "--name-only", f"{before}..HEAD")
        if files is not None:
            return (_strip_agent_output([f for f in files.splitlines() if f]), before[:12])

    # Fall back to the merge's own diff, which covers a squashed PR.
    parents = _git("rev-list", "--parents", "-n", "1", "HEAD")
    if parents and len(parents.split()) >= 3:
        files = _git("diff", "--name-only", "HEAD^1...HEAD")
        if files is not None:
            return (_strip_agent_output([f for f in files.splitlines() if f]), "HEAD^1")

    files = _git("diff", "--name-only", "HEAD~1..HEAD")
    if files is not None:
        return (_strip_agent_output([f for f in files.splitlines() if f]), "HEAD~1")

    return ([], None)


def _load_config(repo: Path) -> dict:
    """Read .brevitas/config.yml. Falls back to a flat parse if PyYAML is
    absent, so a missing dependency degrades rather than crashing the stage."""
    cfg_path = repo / ".brevitas" / "config.yml"
    if not cfg_path.is_file():
        return {}
    try:
        import yaml  # type: ignore
    except ImportError:
        return {}
    try:
        loaded = yaml.safe_load(cfg_path.read_text(encoding="utf-8"))
    except Exception:
        return {}
    return loaded if isinstance(loaded, dict) else {}


def _discover_flows(repo: Path, cfg: dict) -> list[str]:
    flows_dir = repo / cfg.get("flows_dir", ".brevitas/flows")
    if not flows_dir.is_dir():
        return []
    out = []
    for p in sorted(flows_dir.iterdir()):
        if p.suffix in (".yml", ".yaml", ".json") and not p.name.startswith("_"):
            out.append(p.stem)
    return out


def _baselines_present(repo: Path, cfg: dict) -> bool:
    d = repo / cfg.get("baselines_dir", ".brevitas/baselines")
    return d.is_dir() and any(d.rglob("*.json"))


def gather(repo_root: Path | None = None, *, force: bool = False) -> Context:
    repo = repo_root or Path.cwd()
    cfg = _load_config(repo)

    changed, diff_base = ([], None) if force else changed_files_for_event()

    branch = (
        os.environ.get("GITHUB_REF_NAME")
        or os.environ.get("CI_COMMIT_REF_NAME")
        or _git("rev-parse", "--abbrev-ref", "HEAD")
        or "unknown"
    )
    default_branch = (
        os.environ.get("GITHUB_DEFAULT_BRANCH")
        or cfg.get("default_branch")
        or "main"
    )
    # A pull_request event checks out a merge ref, so ref_name is not the branch.
    is_default = branch == default_branch and not os.environ.get("GITHUB_BASE_REF")

    build_id = (
        os.environ.get("BREVITAS_BUILD_ID")
        or _git("rev-parse", "--short", "HEAD")
        or "local"
    )

    pipeline = cfg.get("pipeline") or {}
    upstream = pipeline.get("upstream") or {}

    return Context(
        changed_files=changed,
        ui_paths=cfg.get("ui_paths") or DEFAULT_UI_PATHS,
        flow_triggers=cfg.get("flow_triggers") or {},
        all_flows=_discover_flows(repo, cfg),
        deploy_status=os.environ.get("BREVITAS_UPSTREAM_DEPLOY", "success"),
        regression_status=os.environ.get("BREVITAS_UPSTREAM_REGRESSION") or None,
        regression_mode=os.environ.get(
            "BREVITAS_REGRESSION_MODE", upstream.get("regression", "skip")
        ),
        is_default_branch=is_default,
        baselines_present=_baselines_present(repo, cfg),
        target_url=os.environ.get("BREVITAS_TARGET_URL", ""),
        build_id=build_id,
        branch=branch,
        diff_base=diff_base,
    )
