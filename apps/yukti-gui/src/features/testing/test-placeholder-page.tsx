import { FlaskConical } from "lucide-react";
import { EmptyState } from "@/components/ui/feedback";

// Nav-structure-first placeholder for the Tests section (Web/Mobile/API/
// Database) — the sidebar entry and routes exist now; each form's real
// functionality is a separate, later pass.
export function TestPlaceholderPage({ title }: { title: string }) {
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-h1 text-ink">{title}</h1>
      <EmptyState
        icon={<FlaskConical size={40} />}
        title="Coming soon"
        description={`The ${title} form isn't built yet — this page is a placeholder for the Tests navigation structure.`}
      />
    </div>
  );
}
