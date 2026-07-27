import { useAuthStore } from "@/store/auth-store";
import { Card } from "@/components/ui/primitives";

// FR-ROUTE-08: identity-access settings. Project-scoped permission
// extension (FR-SEC-04) is [NEW-DOMAIN], blocked on Q-01 — not attempted.
export function SettingsPage() {
  const user = useAuthStore((s) => s.user);
  return (
    <div className="flex flex-col gap-4">
      <h1 className="font-mono text-xl text-ink">Settings</h1>
      <Card className="p-4">
        <h2 className="mb-3 font-mono text-sm text-ink-dim">Profile</h2>
        <dl className="grid grid-cols-[120px_1fr] gap-y-2 text-sm">
          <dt className="text-ink-dim">Email</dt>
          <dd className="font-mono text-ink">{user?.email}</dd>
          <dt className="text-ink-dim">Tenant</dt>
          <dd className="font-mono text-xs text-ink">{user?.tenant}</dd>
          <dt className="text-ink-dim">User ID</dt>
          <dd className="font-mono text-xs text-ink">{user?.sub}</dd>
        </dl>
      </Card>
    </div>
  );
}
