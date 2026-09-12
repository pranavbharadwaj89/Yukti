import base64
import json
import time
import uuid
from datetime import datetime, timezone
from flask import Flask, request, jsonify, make_response

app = Flask(__name__)

# In-memory storage for stateful mock API
DATA = {
    "projects": [
        {"id": "proj-1", "name": "Yukti Core Platform", "description": "Core test automation platform and execution engine", "createdAt": "2026-09-10T10:00:00Z"},
        {"id": "proj-2", "name": "E-Commerce Checkout Suite", "description": "End-to-end regression tests for customer payments & checkout", "createdAt": "2026-09-11T12:30:00Z"},
        {"id": "proj-3", "name": "Mobile Banking App", "description": "iOS & Android biometrics and transfer flows", "createdAt": "2026-09-12T08:15:00Z"}
    ],
    "flows": [
        {
            "flowId": {"value": "flow-001"},
            "id": "flow-001",
            "familyId": "fam-001",
            "version": 1,
            "name": "User Registration & Onboarding Flow",
            "description": "Verifies account creation, email verification token, and initial dashboard landing",
            "status": 1, # Published
            "projectId": {"value": "proj-1"},
            "steps": [
                {"id": "step-1", "name": "Open registration portal", "module": "web", "action": "navigate", "order": 1, "params": {"url": "https://app.yukti.io/register"}},
                {"id": "step-2", "name": "Fill registration form", "module": "web", "action": "fillForm", "order": 2, "params": {"selector": "#signup-form", "email": "test@yukti.io"}},
                {"id": "step-3", "name": "Assert welcome banner", "module": "web", "action": "assertVisible", "order": 3, "params": {"selector": ".welcome-banner"}}
            ]
        },
        {
            "flowId": {"value": "flow-002"},
            "id": "flow-002",
            "familyId": "fam-002",
            "version": 2,
            "name": "Payment Gateway Integration Test",
            "description": "Simulates Stripe 3DS payment auth and webhook notification settlement",
            "status": 1, # Published
            "projectId": {"value": "proj-2"},
            "steps": [
                {"id": "step-10", "name": "Initialize checkout session", "module": "api", "action": "sendRequest", "order": 1, "params": {"method": "POST", "url": "/api/v1/checkout"}},
                {"id": "step-11", "name": "Validate HMAC signature", "module": "api", "action": "assertStatus", "order": 2, "params": {"expectedStatus": 200}}
            ]
        },
        {
            "flowId": {"value": "flow-003"},
            "id": "flow-003",
            "familyId": "fam-003",
            "version": 1,
            "name": "Audit Log Anomaly Scanner",
            "description": "Scans production auth audit logs for brute force patterns",
            "status": 0, # Draft
            "projectId": {"value": "proj-1"},
            "steps": [
                {"id": "step-20", "name": "Scan security audit stream", "module": "logs", "action": "detectAnomalies", "order": 1, "params": {"threshold": 2.5}}
            ]
        }
    ],
    "runs": {},
    "triggers": [
        {"id": "trig-1", "flowId": "flow-001", "kind": "Cron", "cronExpression": "0 2 * * *", "isEnabled": True, "lastFiredAt": "2026-09-12T02:00:00Z"},
        {"id": "trig-2", "flowId": "flow-002", "kind": "Webhook", "webhookSecret": "whsec_live_9a8b7c6d5e", "isEnabled": True, "lastFiredAt": "2026-09-12T14:22:18Z"}
    ],
    "audit": [
        {"id": "aud-1", "occurredAt": "2026-09-12T16:25:00Z", "actor": "pranav@yukti.io", "action": "FlowPublished", "resourceType": "Flow", "resourceId": "flow-001"},
        {"id": "aud-2", "occurredAt": "2026-09-12T16:20:00Z", "actor": "sarvaghna-agent", "action": "TestRunCompleted", "resourceType": "FlowRun", "resourceId": "run-882"},
        {"id": "aud-3", "occurredAt": "2026-09-12T15:45:00Z", "actor": "pranav@yukti.io", "action": "ProjectCreated", "resourceType": "Project", "resourceId": "proj-3"}
    ],
    "environments": {
        "proj-1": [
            {"id": "env-1", "projectId": "proj-1", "name": "Local Dev", "variables": {"BASE_URL": "http://localhost:5000", "DEBUG": "true"}},
            {"id": "env-2", "projectId": "proj-1", "name": "Staging", "variables": {"BASE_URL": "https://staging.yukti.internal", "DEBUG": "false"}}
        ]
    },
    "apiCollections": [
        {
            "id": "col-1",
            "name": "Auth & User Management",
            "description": "Endpoints for authentication, JWT issuance, and RBAC",
            "projectId": "proj-1",
            "requests": [
                {"id": "req-1", "name": "Login User", "method": "POST", "url": "/api/v1/auth/login", "headers": {"Content-Type": "application/json"}},
                {"id": "req-2", "name": "Refresh Token", "method": "POST", "url": "/api/v1/auth/refresh", "headers": {"Content-Type": "application/json"}}
            ]
        }
    ]
}

MODULES = [
    {
        "kind": "web",
        "displayName": "Web Browser (Playwright)",
        "trust": "Trusted",
        "contractVersion": "1.0.0",
        "actions": [
            {"actionName": "navigate", "description": "Navigate to URL", "parameters": [{"name": "url", "type": "string", "required": True}]},
            {"actionName": "click", "description": "Click element selector", "parameters": [{"name": "selector", "type": "string", "required": True}]},
            {"actionName": "fillForm", "description": "Fill input text", "parameters": [{"name": "selector", "type": "string", "required": True}, {"name": "value", "type": "string", "required": True}]},
            {"actionName": "assertVisible", "description": "Assert element visibility", "parameters": [{"name": "selector", "type": "string", "required": True}]}
        ]
    },
    {
        "kind": "api",
        "displayName": "REST & GraphQL API",
        "trust": "Trusted",
        "contractVersion": "1.0.0",
        "actions": [
            {"actionName": "sendRequest", "description": "HTTP Request", "parameters": [{"name": "url", "type": "string", "required": True}, {"name": "method", "type": "string", "required": True}]},
            {"actionName": "assertStatus", "description": "Assert HTTP Status code", "parameters": [{"name": "expectedStatus", "type": "number", "required": True}]}
        ]
    },
    {
        "kind": "mobile",
        "displayName": "Mobile App (Appium)",
        "trust": "Trusted",
        "contractVersion": "1.0.0",
        "actions": [
            {"actionName": "launchApp", "description": "Launch target mobile application", "parameters": [{"name": "appId", "type": "string", "required": True}]},
            {"actionName": "tapElement", "description": "Tap UI selector", "parameters": [{"name": "selector", "type": "string", "required": True}]}
        ]
    },
    {
        "kind": "ui",
        "displayName": "Desktop UI (FlaUI / WinAppDriver)",
        "trust": "Trusted",
        "contractVersion": "1.0.0",
        "actions": [
            {"actionName": "launchExecutable", "description": "Start native executable", "parameters": [{"name": "executablePath", "type": "string", "required": True}]},
            {"actionName": "clickControl", "description": "Click automation element", "parameters": [{"name": "automationId", "type": "string", "required": True}]}
        ]
    },
    {
        "kind": "logs",
        "displayName": "Log & Audit Analytics",
        "trust": "Trusted",
        "contractVersion": "1.0.0",
        "actions": [
            {"actionName": "checkRules", "description": "Scan logs matching pattern", "parameters": [{"name": "pattern", "type": "string", "required": True}]},
            {"actionName": "detectAnomalies", "description": "Statistical outlier detector", "parameters": [{"name": "threshold", "type": "number", "required": True}]}
        ]
    }
]

def make_jwt(email="admin@yukti.io", role="admin:9999"):
    header = base64.urlsafe_b64encode(json.dumps({"alg": "HS256", "typ": "JWT"}).encode()).decode().rstrip("=")
    payload = base64.urlsafe_b64encode(json.dumps({
        "sub": "usr_01J7YUKTI8899AA",
        "tenant": "tenant-default",
        "email": email,
        "role": role,
        "exp": int(time.time()) + 86400 * 30
    }).encode()).decode().rstrip("=")
    sig = base64.urlsafe_b64encode(b"mock_signature_bytes_for_local_dev").decode().rstrip("=")
    return f"{header}.{payload}.{sig}"

@app.after_request
def add_cors(response):
    response.headers["Access-Control-Allow-Origin"] = "http://localhost:5173"
    response.headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, Idempotency-Key"
    response.headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS"
    return response

@app.before_request
def handle_options():
    if request.method == "OPTIONS":
        return "", 204

# ── AUTH ──────────────────────────────────────────────────────────────────────
@app.route("/api/v1/auth/login", methods=["POST"])
def auth_login():
    data = request.get_json(silent=True) or {}
    email = data.get("email", "admin@yukti.io")
    token = make_jwt(email=email)
    return jsonify({
        "accessToken": token,
        "refreshToken": "ref_" + uuid.uuid4().hex,
        "expiresAt": "2030-01-01T00:00:00Z"
    })

@app.route("/api/v1/auth/register", methods=["POST"])
def auth_register():
    return jsonify({"userId": "usr_" + uuid.uuid4().hex[:12]})

@app.route("/api/v1/auth/refresh", methods=["POST"])
def auth_refresh():
    token = make_jwt()
    return jsonify({
        "accessToken": token,
        "refreshToken": "ref_" + uuid.uuid4().hex,
        "expiresAt": "2030-01-01T00:00:00Z"
    })

# ── MODULES ───────────────────────────────────────────────────────────────────
@app.route("/api/v1/modules", methods=["GET"])
def get_modules():
    return jsonify(MODULES)

# ── TRENDS ────────────────────────────────────────────────────────────────────
@app.route("/api/v1/trends", methods=["GET"])
def get_trends():
    return jsonify({
        "tenantId": "tenant-default",
        "totalRunsLast24h": 48,
        "passRateLast24h": 0.96,
        "flakeRateLast24h": 0.02,
        "lastUpdatedAt": datetime.now(timezone.utc).isoformat()
    })

# ── FLOWS ─────────────────────────────────────────────────────────────────────
@app.route("/api/v1/flows", methods=["GET"])
def get_flows():
    summaries = []
    for f in DATA["flows"]:
        summaries.append({
            "flowId": f["flowId"],
            "name": f["name"],
            "status": f["status"],
            "version": f["version"],
            "projectId": f.get("projectId")
        })
    return jsonify(summaries)

@app.route("/api/v1/flows/<flow_id>", methods=["GET"])
def get_flow(flow_id):
    for f in DATA["flows"]:
        if f["id"] == flow_id or f["flowId"]["value"] == flow_id:
            return jsonify(f)
    return jsonify({"status": 404, "title": "Not Found", "detail": f"Flow {flow_id} not found"}), 404

@app.route("/api/v1/flows", methods=["POST"])
def create_flow():
    body = request.get_json(silent=True) or {}
    new_id = f"flow-{uuid.uuid4().hex[:6]}"
    new_flow = {
        "flowId": {"value": new_id},
        "id": new_id,
        "familyId": f"fam-{new_id}",
        "version": 1,
        "name": body.get("name", "Untitled Flow"),
        "description": body.get("description", ""),
        "status": 0, # Draft
        "projectId": {"value": body.get("projectId")} if body.get("projectId") else None,
        "steps": []
    }
    DATA["flows"].append(new_flow)
    return jsonify({"flowId": new_id})

@app.route("/api/v1/flows/<flow_id>/steps", methods=["POST"])
def add_step(flow_id):
    body = request.get_json(silent=True) or {}
    for f in DATA["flows"]:
        if f["id"] == flow_id or f["flowId"]["value"] == flow_id:
            step = {
                "id": f"step-{uuid.uuid4().hex[:6]}",
                "name": body.get("name", "Step"),
                "module": body.get("module", "web"),
                "action": body.get("action", "navigate"),
                "order": len(f["steps"]) + 1,
                "params": body.get("params", {}),
                "saveAs": body.get("saveAs"),
                "when": body.get("when")
            }
            f["steps"].append(step)
            return jsonify(step)
    return jsonify({"status": 404, "title": "Not Found", "detail": "Flow not found"}), 404

@app.route("/api/v1/flows/<flow_id>/publish", methods=["POST"])
def publish_flow(flow_id):
    for f in DATA["flows"]:
        if f["id"] == flow_id or f["flowId"]["value"] == flow_id:
            f["status"] = 1
            return jsonify({"succeeded": True, "errors": []})
    return jsonify({"succeeded": False, "errors": ["Flow not found"]}), 404

@app.route("/api/v1/flows/<flow_id>/runs", methods=["POST"])
def trigger_run(flow_id):
    run_id = f"run-{uuid.uuid4().hex[:8]}"
    flow = next((f for f in DATA["flows"] if f["id"] == flow_id or f["flowId"]["value"] == flow_id), None)
    step_results = []
    if flow:
        for s in flow.get("steps", []):
            step_results.append({
                "id": f"sres-{uuid.uuid4().hex[:6]}",
                "stepName": s["name"],
                "module": s["module"],
                "action": s["action"],
                "status": "Succeeded",
                "durationMs": 142,
                "message": "Step executed cleanly"
            })
    run = {
        "id": run_id,
        "flowId": flow_id,
        "status": "Succeeded",
        "startedAt": datetime.now(timezone.utc).isoformat(),
        "finishedAt": datetime.now(timezone.utc).isoformat(),
        "durationMs": 420,
        "stepResults": step_results
    }
    DATA["runs"][run_id] = run
    return jsonify(run)

@app.route("/api/v1/runs/<run_id>", methods=["GET"])
def get_run(run_id):
    run = DATA["runs"].get(run_id)
    if not run:
        run = {
            "id": run_id,
            "flowId": "flow-001",
            "status": "Succeeded",
            "startedAt": datetime.now(timezone.utc).isoformat(),
            "finishedAt": datetime.now(timezone.utc).isoformat(),
            "durationMs": 350,
            "stepResults": [
                {"id": "sr-1", "stepName": "Execution Test", "module": "web", "action": "navigate", "status": "Succeeded", "durationMs": 350, "message": "All asserts passed"}
            ]
        }
    return jsonify(run)

# ── PROJECTS ──────────────────────────────────────────────────────────────────
@app.route("/api/v1/projects", methods=["GET"])
def get_projects():
    return jsonify(DATA["projects"])

@app.route("/api/v1/projects", methods=["POST"])
def create_project():
    body = request.get_json(silent=True) or {}
    new_id = f"proj-{uuid.uuid4().hex[:6]}"
    p = {
        "id": new_id,
        "name": body.get("name", "New Project"),
        "description": body.get("description", ""),
        "createdAt": datetime.now(timezone.utc).isoformat()
    }
    DATA["projects"].append(p)
    return jsonify({"projectId": new_id})

# ── ENVIRONMENTS ──────────────────────────────────────────────────────────────
@app.route("/api/v1/projects/<project_id>/environments", methods=["GET"])
def get_environments(project_id):
    envs = DATA["environments"].get(project_id, [
        {"id": f"env-{project_id}-default", "projectId": project_id, "name": "Default Environment", "variables": {"ENV": "local"}}
    ])
    return jsonify(envs)

# ── TRIGGERS ──────────────────────────────────────────────────────────────────
@app.route("/api/v1/triggers", methods=["GET"])
def get_triggers():
    return jsonify(DATA["triggers"])

# ── AUDIT ─────────────────────────────────────────────────────────────────────
@app.route("/api/v1/audit-entries", methods=["GET"])
def get_audit():
    return jsonify(DATA["audit"])

@app.route("/api/v1/audit-entries/<audit_id>", methods=["GET"])
def get_audit_detail(audit_id):
    item = next((a for a in DATA["audit"] if a["id"] == audit_id), None)
    if not item:
        item = {"id": audit_id, "occurredAt": datetime.now(timezone.utc).isoformat(), "actor": "admin", "action": "Viewed", "resourceType": "System", "resourceId": audit_id}
    return jsonify(item)

# ── REPORTS ───────────────────────────────────────────────────────────────────
@app.route("/api/v1/flow-reports", methods=["GET"])
def get_flow_reports():
    reports = []
    for f in DATA["flows"]:
        reports.append({
            "flowId": f["id"],
            "flowName": f["name"],
            "totalRuns": 12,
            "passedRuns": 11,
            "failedRuns": 1,
            "lastRunStatus": "Succeeded",
            "lastRunAt": "2026-09-12T15:30:00Z"
        })
    return jsonify(reports)

@app.route("/api/v1/flow-reports/<flow_id>/runs", methods=["GET"])
def get_flow_report_runs(flow_id):
    return jsonify([
        {"runId": f"run-{flow_id}-1", "status": "Succeeded", "durationMs": 412, "executedAt": "2026-09-12T15:30:00Z"},
        {"runId": f"run-{flow_id}-2", "status": "Succeeded", "durationMs": 398, "executedAt": "2026-09-12T12:00:00Z"}
    ])

# ── API COLLECTIONS ───────────────────────────────────────────────────────────
@app.route("/api/v1/api-collections", methods=["GET"])
def get_api_collections():
    return jsonify(DATA["apiCollections"])

# ── SIGNALR NEGOTIATE ─────────────────────────────────────────────────────────
@app.route("/hubs/run-progress/negotiate", methods=["POST", "GET"])
def signalr_negotiate():
    return jsonify({
        "negotiateVersion": 1,
        "connectionId": f"conn_{uuid.uuid4().hex[:16]}",
        "availableTransports": []
    })

if __name__ == "__main__":
    print("Yukti Local Development API Server listening on http://127.0.0.1:5000...")
    app.run(host="0.0.0.0", port=5000, debug=False)
