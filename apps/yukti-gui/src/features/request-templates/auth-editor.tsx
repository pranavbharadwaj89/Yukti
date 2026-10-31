import { Input } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";

// DISC-20: edits the same `auth` shape ApiModule.BuildAuth (API-T4/T5)
// already understands — a RequestTemplate's `auth` is exactly an
// ApiRequest step's own `auth` param, never re-validated here. No shared
// editor exists yet for this shape (api-studio's request-designer.tsx
// doesn't edit auth directly either), so this is this feature's own, kept
// local rather than extracted to a shared component nothing else uses yet.

export type AuthType = "none" | "basic" | "bearer" | "apiKey" | "oauth2ClientCredentials";
export type ApiKeyLocation = "header" | "query" | "cookie";

export interface AuthDraft {
  type: AuthType;
  username: string;
  password: string;
  token: string;
  apiKeyName: string;
  apiKeyValue: string;
  apiKeyIn: ApiKeyLocation;
  tokenUrl: string;
  clientId: string;
  clientSecret: string;
  scopes: string; // comma-separated in the UI, an array on the wire
}

export function emptyAuthDraft(): AuthDraft {
  return {
    type: "none",
    username: "",
    password: "",
    token: "",
    apiKeyName: "",
    apiKeyValue: "",
    apiKeyIn: "header",
    tokenUrl: "",
    clientId: "",
    clientSecret: "",
    scopes: "",
  };
}

export function authDraftFromWire(auth: unknown): AuthDraft {
  const draft = emptyAuthDraft();
  if (!auth || typeof auth !== "object") return draft;
  const a = auth as Record<string, unknown>;
  const type = a.type as AuthType | undefined;
  if (!type) return draft;
  draft.type = type;
  if (typeof a.username === "string") draft.username = a.username;
  if (typeof a.password === "string") draft.password = a.password;
  if (typeof a.token === "string") draft.token = a.token;
  if (typeof a.name === "string") draft.apiKeyName = a.name;
  if (typeof a.value === "string") draft.apiKeyValue = a.value;
  if (a.in === "header" || a.in === "query" || a.in === "cookie") draft.apiKeyIn = a.in;
  if (typeof a.tokenUrl === "string") draft.tokenUrl = a.tokenUrl;
  if (typeof a.clientId === "string") draft.clientId = a.clientId;
  if (typeof a.clientSecret === "string") draft.clientSecret = a.clientSecret;
  if (Array.isArray(a.scopes)) draft.scopes = a.scopes.filter((s) => typeof s === "string").join(", ");
  return draft;
}

export function authDraftToWire(draft: AuthDraft): unknown {
  switch (draft.type) {
    case "none":
      return null;
    case "basic":
      return { type: "basic", username: draft.username, password: draft.password };
    case "bearer":
      return { type: "bearer", token: draft.token };
    case "apiKey":
      return { type: "apiKey", name: draft.apiKeyName, value: draft.apiKeyValue, in: draft.apiKeyIn };
    case "oauth2ClientCredentials": {
      const scopes = draft.scopes
        .split(",")
        .map((s) => s.trim())
        .filter(Boolean);
      return {
        type: "oauth2ClientCredentials",
        tokenUrl: draft.tokenUrl,
        clientId: draft.clientId,
        clientSecret: draft.clientSecret,
        ...(scopes.length > 0 ? { scopes } : {}),
      };
    }
  }
}

const AUTH_TYPE_OPTIONS: { value: AuthType; label: string }[] = [
  { value: "none", label: "No auth" },
  { value: "basic", label: "Basic" },
  { value: "bearer", label: "Bearer token" },
  { value: "apiKey", label: "API key" },
  { value: "oauth2ClientCredentials", label: "OAuth 2.0 (client credentials)" },
];

const API_KEY_LOCATION_OPTIONS: { value: ApiKeyLocation; label: string }[] = [
  { value: "header", label: "Header" },
  { value: "query", label: "Query param" },
  { value: "cookie", label: "Cookie" },
];

export function AuthEditor({ draft, onChange }: { draft: AuthDraft; onChange: (draft: AuthDraft) => void }) {
  function patch(partial: Partial<AuthDraft>) {
    onChange({ ...draft, ...partial });
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <label className="text-body-sm text-ink-dim">Auth type</label>
        <Select
          ariaLabel="Auth type"
          value={draft.type}
          options={AUTH_TYPE_OPTIONS}
          onChange={(type) => patch({ type })}
          testId="auth-type"
        />
      </div>

      {draft.type === "basic" && (
        <div className="grid grid-cols-2 gap-2">
          <Input placeholder="Username" value={draft.username} onChange={(e) => patch({ username: e.target.value })} />
          <Input placeholder="Password" type="password" value={draft.password} onChange={(e) => patch({ password: e.target.value })} />
        </div>
      )}

      {draft.type === "bearer" && (
        <Input placeholder="Token" value={draft.token} onChange={(e) => patch({ token: e.target.value })} />
      )}

      {draft.type === "apiKey" && (
        <div className="grid grid-cols-3 gap-2">
          <Input placeholder="Name" value={draft.apiKeyName} onChange={(e) => patch({ apiKeyName: e.target.value })} />
          <Input placeholder="Value" value={draft.apiKeyValue} onChange={(e) => patch({ apiKeyValue: e.target.value })} />
          <Select
            ariaLabel="API key location"
            value={draft.apiKeyIn}
            options={API_KEY_LOCATION_OPTIONS}
            onChange={(apiKeyIn) => patch({ apiKeyIn })}
            testId="auth-apikey-in"
          />
        </div>
      )}

      {draft.type === "oauth2ClientCredentials" && (
        <div className="flex flex-col gap-2">
          <Input placeholder="Token URL" value={draft.tokenUrl} onChange={(e) => patch({ tokenUrl: e.target.value })} />
          <div className="grid grid-cols-2 gap-2">
            <Input placeholder="Client ID" value={draft.clientId} onChange={(e) => patch({ clientId: e.target.value })} />
            <Input placeholder="Client secret" type="password" value={draft.clientSecret} onChange={(e) => patch({ clientSecret: e.target.value })} />
          </div>
          <Input placeholder="Scopes (comma-separated, optional)" value={draft.scopes} onChange={(e) => patch({ scopes: e.target.value })} />
        </div>
      )}
    </div>
  );
}
