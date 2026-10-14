import { useId, useRef, useState } from "react";
import { Input } from "@/components/ui/primitives";

// D4-T6: typing `{{vars.` suggests the names earlier steps saveAs'd —
// never a later step's, matching the exact rule Flow.Publish enforces
// server-side (Yukti.Domain/FlowAuthoring/Flow.cs: availableBindings only
// grows as steps are walked in order). Unresolved references use the
// identical wording as that validation's FlowPublishError, so a user sees
// the same reason here and at publish time, not two different stories for
// one rule.
//
// A real ARIA 1.2 combobox (not a styled <ul> bolted on): aria-expanded/
// aria-controls/aria-activedescendant/aria-autocomplete on the input,
// role="listbox"/"option" on the popup — a screen reader announces the
// active suggestion without the input itself ever losing focus.

export interface VariableAutocompleteInputProps {
  value: string;
  onChange: (value: string) => void;
  "aria-label"?: string;
  stepName: string;
  /** Earlier steps' saveAs names only — never the current or later steps'. */
  availableBindings: string[];
  /** Real field paths seen under each binding in the flow's last run, e.g. {"order": ["id", "status"]}. Optional — suggestions degrade gracefully without it. */
  fieldsByBinding?: Record<string, string[]>;
}

const VARS_TOKEN = /\{\{vars\.([A-Za-z0-9_]+)(\.[^}]*)?\}\}/g;

function findActiveToken(value: string, cursor: number): { start: number; partial: string } | null {
  const upToCursor = value.slice(0, cursor);
  const openIdx = upToCursor.lastIndexOf("{{");
  if (openIdx === -1) return null;
  const between = upToCursor.slice(openIdx + 2);
  if (between.includes("}}")) return null; // this {{ was already closed before the cursor
  const match = between.match(/^vars\.([A-Za-z0-9_.]*)$/);
  if (!match) return null;
  return { start: openIdx + 2 + "vars.".length, partial: match[1] };
}

function getCandidates(partial: string, bindings: string[], fieldsByBinding: Record<string, string[]>): string[] {
  const dotIdx = partial.indexOf(".");
  if (dotIdx === -1) {
    return bindings.filter((b) => b.toLowerCase().startsWith(partial.toLowerCase()));
  }
  // The text before the dot may still be a partial binding name (e.g. "ord."
  // typed on the way to "order."), not yet the exact key fieldsByBinding is
  // stored under — resolve it against every binding that could still match.
  const bindingPart = partial.slice(0, dotIdx).toLowerCase();
  const fieldPrefix = partial.slice(dotIdx + 1).toLowerCase();
  const matchedBindings = bindings.filter((b) => b.toLowerCase().startsWith(bindingPart));
  const results: string[] = [];
  for (const binding of matchedBindings) {
    const fields = fieldsByBinding[binding] ?? [];
    for (const f of fields) {
      if (f.toLowerCase().startsWith(fieldPrefix)) results.push(`${binding}.${f}`);
    }
  }
  return results;
}

/** Mirrors Flow.Publish's own unresolved_variable wording exactly. */
export function findUnresolvedVariableErrors(value: string, stepName: string, availableBindings: string[]): string[] {
  const errors: string[] = [];
  const seen = new Set<string>();
  for (const match of value.matchAll(VARS_TOKEN)) {
    const bound = match[1];
    if (!availableBindings.includes(bound) && !seen.has(bound)) {
      seen.add(bound);
      errors.push(`Step '${stepName}' references undeclared variable '${bound}'.`);
    }
  }
  return errors;
}

export function VariableAutocompleteInput({
  value,
  onChange,
  "aria-label": ariaLabel,
  stepName,
  availableBindings,
  fieldsByBinding = {},
}: VariableAutocompleteInputProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [activeIndex, setActiveIndex] = useState(0);
  const [dismissed, setDismissed] = useState(false);
  const listboxId = useId();
  const optionId = (i: number) => `${listboxId}-opt-${i}`;

  const cursor = inputRef.current?.selectionStart ?? value.length;
  const token = findActiveToken(value, cursor);
  const candidates = token ? getCandidates(token.partial, availableBindings, fieldsByBinding) : [];
  const open = !dismissed && !!token && candidates.length > 0;
  const errors = findUnresolvedVariableErrors(value, stepName, availableBindings);

  function accept(candidate: string) {
    if (!token || !inputRef.current) return;
    const el = inputRef.current;
    const alreadyClosed = value.slice(cursor, cursor + 2) === "}}";
    const insertion = alreadyClosed ? candidate : `${candidate}}}`;
    const next = value.slice(0, token.start) + insertion + value.slice(cursor);
    onChange(next);
    setDismissed(true);
    const newCursor = token.start + candidate.length;
    requestAnimationFrame(() => el.setSelectionRange(newCursor, newCursor));
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (!open) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActiveIndex((i) => (i + 1) % candidates.length);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActiveIndex((i) => (i - 1 + candidates.length) % candidates.length);
    } else if (e.key === "Enter" || e.key === "Tab") {
      e.preventDefault();
      accept(candidates[activeIndex]);
    } else if (e.key === "Escape") {
      e.preventDefault();
      setDismissed(true);
    }
  }

  return (
    <div className="relative">
      <Input
        ref={inputRef}
        aria-label={ariaLabel}
        value={value}
        role="combobox"
        aria-expanded={open}
        aria-controls={open ? listboxId : undefined}
        aria-autocomplete="list"
        aria-activedescendant={open ? optionId(activeIndex) : undefined}
        aria-invalid={errors.length > 0}
        className={errors.length > 0 ? "underline decoration-wavy decoration-danger" : undefined}
        onChange={(e) => {
          onChange(e.target.value);
          setDismissed(false);
          setActiveIndex(0);
        }}
        onKeyDown={handleKeyDown}
        onBlur={() => setDismissed(true)}
      />
      {open && (
        <ul
          id={listboxId}
          role="listbox"
          className="absolute z-10 mt-1 max-h-48 w-full overflow-y-auto rounded-md border border-border bg-surface shadow-md"
        >
          {candidates.map((c, i) => (
            <li
              key={c}
              id={optionId(i)}
              role="option"
              aria-selected={i === activeIndex}
              className={`cursor-pointer px-2.5 py-1.5 font-mono text-xs ${i === activeIndex ? "bg-accent text-accent-ink" : "text-ink"}`}
              // onMouseDown (not onClick) fires before the input's onBlur, so
              // the selection a click made is still valid when accept() reads it.
              onMouseDown={(e) => {
                e.preventDefault();
                accept(c);
              }}
            >
              vars.{c}
            </li>
          ))}
        </ul>
      )}
      {errors.length > 0 && (
        <p role="alert" className="mt-1 text-caption text-danger">
          {errors[0]}
        </p>
      )}
    </div>
  );
}
