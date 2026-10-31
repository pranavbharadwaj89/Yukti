"""Tests for the triggering decision.

The failure mode this guards against is silence: if decide() wrongly returns
SKIP, the audit never runs and nothing complains. So the negative cases below
matter at least as much as the positive ones.
"""

from agent.decide import Action, SkipReason, decide, select_flows

UI = ["*.tsx", "*.css", "*/templates/*"]
TRIGGERS = {
    "login": ["src/auth/*", "src/components/Login*"],
    "checkout": ["src/checkout/*", "src/components/Cart*"],
}
FLOWS = ["login", "checkout", "settings"]


def base(**over):
    kw = dict(
        changed_files=["src/auth/login.tsx"],
        ui_paths=UI,
        flow_triggers=TRIGGERS,
        all_flows=FLOWS,
        deploy_status="success",
        regression_status="success",
        regression_mode="skip",
        is_default_branch=True,
        baselines_present=True,
    )
    kw.update(over)
    return decide(**kw)


# ── gate: upstream ────────────────────────────────────────────────────────

def test_failed_deploy_skips():
    d = base(deploy_status="failed")
    assert d.action is Action.SKIP
    assert d.skip_reason is SkipReason.NO_DEPLOY


def test_skipped_deploy_skips():
    assert base(deploy_status="skipped").skip_reason is SkipReason.NO_DEPLOY


def test_garbage_deploy_status_is_not_success():
    # An unrecognised value must never be read as success.
    assert base(deploy_status="TRUE").skip_reason is SkipReason.NO_DEPLOY
    assert base(deploy_status="").skip_reason is SkipReason.NO_DEPLOY


def test_regression_required_and_failed_skips():
    d = base(regression_mode="required", regression_status="failed")
    assert d.skip_reason is SkipReason.REGRESSION_REQUIRED


def test_regression_required_and_missing_skips():
    d = base(regression_mode="required", regression_status=None)
    assert d.skip_reason is SkipReason.REGRESSION_REQUIRED


def test_regression_required_and_passed_runs():
    assert base(regression_mode="required", regression_status="success").should_audit


def test_regression_skip_mode_ignores_regression():
    # Today's pipeline: regression does not exist yet.
    d = base(regression_mode="skip", regression_status=None)
    assert d.should_audit
    assert d.banner is None


def test_regression_optional_and_failed_runs_with_banner():
    d = base(regression_mode="optional", regression_status="failed")
    assert d.should_audit
    assert d.banner and "did not pass" in d.banner


def test_regression_optional_and_passed_has_no_banner():
    assert base(regression_mode="optional", regression_status="success").banner is None


# ── gate: relevance ───────────────────────────────────────────────────────

def test_backend_only_change_skips():
    d = base(changed_files=["src/api/users.py", "README.md"])
    assert d.skip_reason is SkipReason.NO_UI_CHANGES


def test_docs_only_change_skips():
    assert base(changed_files=["docs/guide.md"]).skip_reason is SkipReason.NO_UI_CHANGES


def test_ui_change_runs():
    assert base(changed_files=["src/auth/login.tsx"]).should_audit


def test_mixed_change_runs():
    assert base(changed_files=["src/api/users.py", "src/auth/login.tsx"]).should_audit


def test_empty_diff_audits_everything():
    # An unavailable diff must not be read as "nothing changed".
    d = base(changed_files=[])
    assert d.should_audit
    assert set(d.flows) == set(FLOWS)


def test_force_overrides_every_gate():
    d = base(deploy_status="failed", changed_files=["README.md"], force=True)
    assert d.should_audit
    assert set(d.flows) == set(FLOWS)


def test_no_flows_configured_skips():
    d = base(all_flows=[])
    assert d.skip_reason is SkipReason.NO_FLOWS_CONFIGURED


# ── flow selection ────────────────────────────────────────────────────────

def test_selects_only_the_affected_flow():
    assert base(changed_files=["src/auth/login.tsx"]).flows == ("login",)


def test_selects_multiple_affected_flows():
    d = base(changed_files=["src/auth/login.tsx", "src/checkout/pay.tsx"])
    assert set(d.flows) == {"login", "checkout"}


def test_unmapped_ui_file_audits_everything():
    # Unknown blast radius: audit more, not less.
    d = base(changed_files=["src/components/Mystery.tsx"])
    assert set(d.flows) == set(FLOWS)
    assert any("no flow trigger" in n for n in d.notes)


def test_shared_file_fans_out_to_all_matching_flows():
    triggers = {"login": ["src/shared/*"], "checkout": ["src/shared/*"]}
    flows, _ = select_flows(["src/shared/theme.css"], triggers, FLOWS)
    assert set(flows) == {"login", "checkout"}


def test_no_triggers_configured_audits_everything():
    d = base(flow_triggers={})
    assert set(d.flows) == set(FLOWS)


def test_trigger_naming_a_nonexistent_flow_is_flagged():
    flows, notes = select_flows(
        ["src/ghost/x.tsx"], {"ghost": ["src/ghost/*"]}, FLOWS
    )
    assert flows == ()
    assert any("unknown flow" in n for n in notes)


# ── gating ────────────────────────────────────────────────────────────────

def test_gates_on_default_branch_with_baselines():
    assert base(is_default_branch=True, baselines_present=True).gate is True


def test_no_gate_without_baselines():
    # Without a baseline there is no "before", so nothing is a regression.
    d = base(is_default_branch=True, baselines_present=False)
    assert d.gate is False
    assert any("no committed baselines" in n for n in d.notes)


def test_no_gate_on_feature_branch():
    assert base(is_default_branch=False, baselines_present=True).gate is False


def test_decision_is_immutable():
    import dataclasses
    import pytest
    d = base()
    with pytest.raises(dataclasses.FrozenInstanceError):
        d.gate = True  # type: ignore[misc]
