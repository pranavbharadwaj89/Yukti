"""
The triggering decision.

This module is where the agent decides whether a UI audit should run, which
flows it should cover, and whether its result is allowed to block anything.

It is deliberately pure: no git calls, no network, no filesystem, no logging.
Everything it needs arrives as arguments and it returns a Decision. That makes
every rule below directly unit-testable without a repo, a browser or a build,
which matters because this is the logic that decides whether the audit runs at
all. A bug here is silent — the audit simply never happens and nothing
complains.

Gathering the inputs is `context.py`. Acting on the output is `run.py`.
"""

from __future__ import annotations

import fnmatch
from dataclasses import dataclass, field
from enum import Enum


class Action(Enum):
    AUDIT = "audit"
    SKIP = "skip"


class SkipReason(Enum):
    """Why no audit ran. Distinct values so a report can be specific — 'skipped'
    on its own is indistinguishable from 'never ran'."""

    NO_DEPLOY = "upstream_deploy_not_successful"
    REGRESSION_REQUIRED = "regression_required_but_not_passed"
    NO_UI_CHANGES = "no_ui_relevant_files_changed"
    NO_FLOWS_CONFIGURED = "no_flows_configured"


@dataclass(frozen=True)
class Decision:
    action: Action
    reason: str
    skip_reason: SkipReason | None = None
    flows: tuple[str, ...] = ()
    gate: bool = False
    banner: str | None = None
    notes: tuple[str, ...] = field(default_factory=tuple)

    @property
    def should_audit(self) -> bool:
        return self.action is Action.AUDIT


def _matches_any(path: str, patterns: list[str]) -> bool:
    return any(fnmatch.fnmatch(path, p) for p in patterns)


def select_flows(
    changed_files: list[str],
    flow_triggers: dict[str, list[str]],
    all_flows: list[str],
) -> tuple[tuple[str, ...], list[str]]:
    """Map changed files onto the flows they could have affected.

    Returns (flows_to_audit, notes).

    A changed file that matches no flow's trigger patterns means the blast
    radius is unknown, so every flow is audited. Auditing too much wastes a
    few minutes; auditing too little means a regression ships. The asymmetry
    is deliberate — when in doubt, audit everything.
    """
    notes: list[str] = []
    if not all_flows:
        return (), notes

    if not flow_triggers:
        notes.append(
            "No flow_triggers configured, so every flow is audited. Add "
            "flow_triggers to .brevitas/config.yml to narrow this."
        )
        return tuple(sorted(all_flows)), notes

    selected: set[str] = set()
    unmapped: list[str] = []

    for path in changed_files:
        hit = False
        for flow, patterns in flow_triggers.items():
            if _matches_any(path, patterns):
                selected.add(flow)
                hit = True
        if not hit:
            unmapped.append(path)

    if unmapped:
        notes.append(
            f"{len(unmapped)} changed file(s) match no flow trigger "
            f"(e.g. {unmapped[0]}), so all flows are audited rather than "
            f"guessing the blast radius."
        )
        return tuple(sorted(all_flows)), notes

    unknown = selected - set(all_flows)
    if unknown:
        notes.append(
            f"flow_triggers reference unknown flow(s): {sorted(unknown)}. "
            f"Check the flow ids in .brevitas/config.yml."
        )
    selected &= set(all_flows)

    return tuple(sorted(selected)), notes


def decide(
    *,
    changed_files: list[str],
    ui_paths: list[str],
    flow_triggers: dict[str, list[str]],
    all_flows: list[str],
    deploy_status: str,
    regression_status: str | None,
    regression_mode: str = "skip",
    is_default_branch: bool = False,
    baselines_present: bool = False,
    force: bool = False,
) -> Decision:
    """Decide whether to audit, what to audit, and whether it may block.

    Order matters: the cheapest and most certain disqualifiers come first, so
    a pipeline with a failed deploy spends no time diffing files.
    """
    notes: list[str] = []

    # ── 1. Is there anything deployed to audit? ──────────────────────────
    if deploy_status != "success" and not force:
        return Decision(
            action=Action.SKIP,
            reason=(
                f"Deploy reported '{deploy_status}', so there is no running "
                f"build to audit. This is not a UI finding."
            ),
            skip_reason=SkipReason.NO_DEPLOY,
        )

    # ── 2. Does policy require regression to have passed? ────────────────
    if regression_mode == "required" and regression_status != "success" and not force:
        return Decision(
            action=Action.SKIP,
            reason=(
                f"Regression is required but reported "
                f"'{regression_status or 'nothing'}'."
            ),
            skip_reason=SkipReason.REGRESSION_REQUIRED,
        )

    banner = None
    if regression_mode == "optional" and regression_status != "success":
        banner = (
            f"Regression did not pass upstream "
            f"({regression_status or 'no result reported'}). UI findings may "
            f"reflect broken functionality rather than UI design."
        )

    # ── 3. Did this change touch the UI at all? ──────────────────────────
    # A backend-only or docs-only merge cannot change the rendered UI, so
    # auditing it burns pipeline minutes to re-confirm the last result.
    if not force:
        if not changed_files:
            notes.append(
                "No changed files were detected, so every flow is audited. "
                "This is normal for a manual or scheduled run."
            )
        else:
            ui_changed = [f for f in changed_files if _matches_any(f, ui_paths)]
            if not ui_changed:
                return Decision(
                    action=Action.SKIP,
                    reason=(
                        f"None of the {len(changed_files)} changed file(s) match "
                        f"ui_paths, so the rendered UI cannot have changed."
                    ),
                    skip_reason=SkipReason.NO_UI_CHANGES,
                )
            notes.append(
                f"{len(ui_changed)} of {len(changed_files)} changed file(s) are "
                f"UI-relevant."
            )

    # ── 4. Which flows? ──────────────────────────────────────────────────
    if force or not changed_files:
        flows = tuple(sorted(all_flows))
    else:
        flows, flow_notes = select_flows(changed_files, flow_triggers, all_flows)
        notes.extend(flow_notes)

    if not flows:
        return Decision(
            action=Action.SKIP,
            reason=(
                "No flows are configured to audit. Add flows to "
                ".brevitas/flows/ before enabling this stage."
            ),
            skip_reason=SkipReason.NO_FLOWS_CONFIGURED,
            notes=tuple(notes),
        )

    # ── 5. May the result block? ─────────────────────────────────────────
    # Gating needs a committed baseline to compare against. Without one there
    # is no "before", so a finding is an observation and not a regression.
    gate = bool(is_default_branch and baselines_present)
    if is_default_branch and not baselines_present:
        notes.append(
            "Not gating: no committed baselines, so nothing to compare "
            "against. Capture baselines and commit them to enable gating."
        )
    elif not is_default_branch:
        notes.append("Not gating: this is not the default branch.")

    return Decision(
        action=Action.AUDIT,
        reason=f"Auditing {len(flows)} flow(s): {', '.join(flows)}.",
        flows=flows,
        gate=gate,
        banner=banner,
        notes=tuple(notes),
    )
