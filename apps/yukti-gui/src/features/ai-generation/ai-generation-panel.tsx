import { useId, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { aiApi, ApiError, flowsApi } from "@/services/api-client";
import { Button, Card, Textarea } from "@/components/ui/primitives";
import { useToastStore } from "@/store/toast-store";

/** Mirrors the backend guardrail (AiGuardrailPreCheck.MaxDescriptionLength) so an oversized request never leaves the browser. */
export const MAX_DESCRIPTION_LENGTH = 4000;

/** Token-economy spec §R11: identical text re-submitted within this window reuses the last Draft instead of calling again. */
export const REUSE_WINDOW_MS = 60_000;

type RecentDraft = { flowId: string; at: number };
const recentDraftKey = (text: string) => ["ai-generation", "recent-draft", text] as const;

const EXAMPLES = [
  "Log in on the web, read the order ID, then check the orders API says it's confirmed",
  "Check the orders API returns status confirmed for a new order",
  "Check app.log has no ERROR lines for the order",
];

/**
 * D5-T7: English → Draft flow. Calls the Sarvagna pipeline (POST /flows/generate); a generated
 * flow is always a Draft the user lands on in the normal editor — never published here.
 * An ambiguous request creates nothing and shows the Analyst's own reasons as clarifying
 * buttons. With AI off (D5-T2) the form is replaced by an explanation, never a broken button.
 */
export function AiGenerationPanel() {
  const statusQuery = useQuery({ queryKey: ["ai-status"], queryFn: aiApi.status });

  return (
    <section aria-label="Suggest a flow with AI">
      {statusQuery.data?.enabled === false ? (
        <Card className="p-4">
          <p className="text-sm text-ink-dim" role="note" title="An administrator has turned AI off (Ai:Enabled=false).">
            AI flow suggestions are turned off for this deployment. You can still create flows by hand with “New flow”.
          </p>
        </Card>
      ) : (
        <GenerationForm />
      )}
    </section>
  );
}

function GenerationForm() {
  const [description, setDescription] = useState("");
  const [ambiguities, setAmbiguities] = useState<string[]>([]);
  const [inlineError, setInlineError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const pushToast = useToastStore((s) => s.push);
  const ids = { label: useId(), help: useId(), clarify: useId() };

  const trimmed = description.trim();
  const tooLong = description.length > MAX_DESCRIPTION_LENGTH;

  const openDraft = (flowId: string) => void navigate({ to: "/flows/$flowId", params: { flowId } });

  const generate = useMutation({
    mutationFn: (text: string) => flowsApi.generate(text),
    onSuccess: async (result, text) => {
      setInlineError(null);
      if (result.status === "ambiguous") {
        setAmbiguities(result.ambiguities);
        return;
      }
      setAmbiguities([]);
      // Kept in the query cache, not a component ref: success navigates away and unmounts this
      // panel, so a ref would be gone by the time the user comes back with the same text.
      queryClient.setQueryData<RecentDraft>(recentDraftKey(text), { flowId: result.flowId, at: Date.now() });
      await queryClient.invalidateQueries({ queryKey: ["flows"] });
      pushToast({ kind: "success", message: "Draft flow created by AI — review every step before publishing." });
      openDraft(result.flowId);
    },
    onError: (err) => {
      // RFC 7807 `detail` is already plain English (D5-T7 backend mapping); anything else gets a
      // generic sentence — never a stack trace, never a silent fallback.
      const message =
        err instanceof ApiError ? err.message : "Something went wrong suggesting a flow. Please try again.";
      setInlineError(message);
      pushToast({ kind: "error", message, correlationId: err instanceof ApiError ? err.correlationId : undefined });
    },
  });

  const submit = () => {
    if (generate.isPending || trimmed.length === 0 || tooLong) return;
    const recent = queryClient.getQueryData<RecentDraft>(recentDraftKey(trimmed));
    if (recent && Date.now() - recent.at < REUSE_WINDOW_MS) {
      openDraft(recent.flowId);
      return;
    }
    generate.mutate(trimmed);
  };

  const answerAmbiguity = (reason: string) => {
    setDescription((current) => `${current.trimEnd()}\n(Re: ${reason}) `);
    setAmbiguities([]);
    document.getElementById(`${ids.label}-input`)?.focus();
  };

  return (
    <Card className="flex flex-col gap-3 p-4">
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          submit();
        }}
      >
        <label id={ids.label} htmlFor={`${ids.label}-input`} className="text-h3 text-ink">
          Describe a test and let AI draft the flow
        </label>
        <Textarea
          id={`${ids.label}-input`}
          aria-describedby={ids.help}
          aria-invalid={tooLong}
          rows={3}
          placeholder="e.g. Log in, then check the orders API says the order is confirmed"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
        <p id={ids.help} className={`text-xs ${tooLong ? "text-danger" : "text-ink-dim"}`} aria-live="polite">
          {tooLong
            ? `Too long: ${description.length} of ${MAX_DESCRIPTION_LENGTH} characters. Shorten it to send.`
            : `${description.length} / ${MAX_DESCRIPTION_LENGTH} characters. The result is a Draft — nothing is published.`}
        </p>

        <div className="flex flex-wrap gap-2" role="group" aria-label="Examples">
          {EXAMPLES.map((example) => (
            <Button key={example} type="button" variant="ghost" className="text-xs" onClick={() => setDescription(example)}>
              {example}
            </Button>
          ))}
        </div>

        <div className="flex items-center justify-end gap-2">
          {generate.isPending && (
            <span className="text-xs text-ink-dim" role="status">
              Drafting your flow…
            </span>
          )}
          <Button type="submit" disabled={generate.isPending || trimmed.length === 0 || tooLong}>
            Suggest a flow
          </Button>
        </div>
      </form>

      {inlineError && (
        <p className="text-sm text-danger" role="alert">
          {inlineError}
        </p>
      )}

      {ambiguities.length > 0 && (
        <section aria-labelledby={ids.clarify} className="flex flex-col gap-2">
          <h3 id={ids.clarify} className="text-sm font-medium text-ink">
            The AI needs a clarification before drafting anything. Pick what to clarify:
          </h3>
          <div className="flex flex-col items-start gap-2">
            {ambiguities.map((reason) => (
              <Button key={reason} type="button" variant="secondary" onClick={() => answerAmbiguity(reason)}>
                {reason}
              </Button>
            ))}
          </div>
        </section>
      )}
    </Card>
  );
}
