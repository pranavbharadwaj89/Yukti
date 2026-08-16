import {
  useEffect,
  useRef,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type TextareaHTMLAttributes,
} from "react";
import { useToastStore } from "@/store/toast-store";

// FR-CL-01/02: primitive layer themed entirely via the --yukti-* tokens
// (index.css) — never a hardcoded hex in a className here. Hand-authored
// Tailwind components rather than shadcn/ui's CLI scaffold (network/time
// cost tradeoff, documented in the session summary) but same intent: one
// small owned primitive set every composite/feature builds on.

export function Button({
  variant = "primary",
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "secondary" | "ghost" | "danger" }) {
  const base = "inline-flex items-center justify-center gap-2 rounded-md px-3.5 py-2 text-sm font-medium transition-colors disabled:opacity-50 disabled:pointer-events-none";
  const variants: Record<string, string> = {
    primary: "bg-accent text-accent-ink hover:opacity-90",
    secondary: "bg-surface-2 text-ink border border-border hover:bg-surface",
    ghost: "text-ink-dim hover:text-ink hover:bg-surface-2",
    danger: "bg-danger text-white hover:opacity-90",
  };
  return <button className={`${base} ${variants[variant]} ${className}`} {...props} />;
}

export function Input({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={`w-full rounded-md border border-border bg-surface px-3 py-2 text-sm text-ink placeholder:text-ink-dim focus:border-accent ${className}`}
      {...props}
    />
  );
}

export function Textarea({ className = "", ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      className={`w-full rounded-md border border-border bg-surface px-3 py-2 text-sm text-ink placeholder:text-ink-dim focus:border-accent font-mono ${className}`}
      {...props}
    />
  );
}

export function Card({ className = "", children }: { className?: string; children: ReactNode }) {
  return <div className={`rounded-lg border border-border bg-surface ${className}`}>{children}</div>;
}

const statusStyles: Record<string, string> = {
  Passed: "bg-success-soft text-success",
  Published: "bg-success-soft text-success",
  Failed: "bg-danger-soft text-danger",
  Cancelled: "bg-danger-soft text-danger",
  Running: "bg-info-soft text-info",
  Pending: "bg-info-soft text-info",
  Draft: "bg-surface-2 text-ink-dim",
  Skipped: "bg-warning-soft text-warning",
  Flaky: "bg-warning-soft text-warning",
};

export function StatusPill({ status }: { status: string }) {
  const style = statusStyles[status] ?? "bg-surface-2 text-ink-dim";
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-mono font-medium ${style}`}>{status}</span>
  );
}

export function Dialog({
  open,
  onClose,
  title,
  children,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) return;
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    panelRef.current?.focus();
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      previouslyFocused.current?.focus();
    };
  }, [open, onClose]);

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        ref={panelRef}
        tabIndex={-1}
        className="w-full max-w-md rounded-lg border border-border bg-surface p-5 shadow-xl outline-none"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <h2 className="mb-4 font-mono text-base text-ink">{title}</h2>
        {children}
      </div>
    </div>
  );
}

export function ToastViewport() {
  const toasts = useToastStore((s) => s.toasts);
  const dismiss = useToastStore((s) => s.dismiss);
  const styles: Record<string, string> = {
    success: "border-success/40 bg-success-soft text-success",
    error: "border-danger/40 bg-danger-soft text-danger",
    info: "border-info/40 bg-info-soft text-info",
  };
  return (
    <div
      className="fixed bottom-4 right-4 z-50 flex w-80 flex-col gap-2"
      role="status"
      aria-live="polite"
      aria-atomic="true"
    >
      {toasts.map((t) => (
        <div
          key={t.id}
          role="button"
          tabIndex={0}
          className={`cursor-pointer rounded-md border px-3.5 py-2.5 text-sm shadow-lg ${styles[t.kind]}`}
          onClick={() => dismiss(t.id)}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault();
              dismiss(t.id);
            }
          }}
          aria-label={`Dismiss notification: ${t.message}`}
        >
          <div>{t.message}</div>
          {t.correlationId && <div className="mt-1 font-mono text-xs opacity-70">ref: {t.correlationId}</div>}
        </div>
      ))}
    </div>
  );
}

export function Spinner({ className = "" }: { className?: string }) {
  return (
    <svg className={`animate-spin ${className}`} viewBox="0 0 24 24" fill="none" width="16" height="16">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" />
      <path className="opacity-90" fill="currentColor" d="M4 12a8 8 0 018-8v3a5 5 0 00-5 5H4z" />
    </svg>
  );
}
