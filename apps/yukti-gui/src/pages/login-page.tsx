import { useState } from "react";
import { useNavigate } from "@tanstack/react-router";
import { authApi, ApiError } from "@/services/api-client";
import { useAuthStore } from "@/store/auth-store";
import { Button, Input } from "@/components/ui/primitives";

// FR-SEC-01: no route guard needed here — this page is reachable whether
// or not a session exists (the guard elsewhere redirects *to* here).
export function LoginPage() {
  const setSession = useAuthStore((s) => s.setSession);
  const navigate = useNavigate();
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      if (mode === "register") {
        await authApi.register(email, password, displayName);
      }
      const tokens = await authApi.login(email, password);
      setSession(tokens);
      void navigate({ to: "/" });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex h-screen w-screen items-center justify-center bg-bg">
      <form onSubmit={handleSubmit} className="w-full max-w-sm rounded-lg border border-border bg-surface p-6">
        <div className="mb-5 font-mono text-lg font-semibold text-accent">YUKTI</div>
        <div className="flex flex-col gap-3">
          {mode === "register" && (
            <Input placeholder="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
          )}
          <Input type="email" placeholder="Email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          <Input type="password" placeholder="Password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          {error && <div className="text-sm text-danger">{error}</div>}
          <Button type="submit" disabled={busy}>
            {mode === "login" ? "Sign in" : "Create account"}
          </Button>
          <button
            type="button"
            className="text-xs text-ink-dim hover:text-ink"
            onClick={() => setMode(mode === "login" ? "register" : "login")}
          >
            {mode === "login" ? "Need an account? Register" : "Have an account? Sign in"}
          </button>
        </div>
      </form>
    </div>
  );
}
