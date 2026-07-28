import { useState, type ReactNode } from "react";
import { AlertTriangle, CheckCircle2, Info, XCircle } from "lucide-react";
import { Button, Dialog } from "@/components/ui/primitives";

// UI_Component_Spec.md Part 3 (Tooltip, Progress) + Part 4 (Alert, Empty
// States, Confirmation Dialogs, Skeleton Loader), restyled onto Yukti
// tokens. Toast (popup) already exists in primitives.tsx — Alert here is
// the inline-in-page variant the spec calls out separately.

export function Tooltip({ label, children }: { label: string; children: ReactNode }) {
  const [visible, setVisible] = useState(false);
  return (
    <span
      className="relative inline-flex"
      onMouseEnter={() => setVisible(true)}
      onMouseLeave={() => setVisible(false)}
      onFocus={() => setVisible(true)}
      onBlur={() => setVisible(false)}
    >
      {children}
      {visible && (
        <span
          role="tooltip"
          className="pointer-events-none absolute bottom-full left-1/2 z-[1000] mb-2 -translate-x-1/2 whitespace-nowrap rounded-md bg-ink px-2.5 py-1.5 text-caption text-bg shadow-lg"
        >
          {label}
        </span>
      )}
    </span>
  );
}

export type AlertKind = "success" | "error" | "warning" | "info";

const alertStyles: Record<AlertKind, { border: string; bg: string; text: string; Icon: typeof Info }> = {
  success: { border: "border-l-success", bg: "bg-success-soft", text: "text-success", Icon: CheckCircle2 },
  error: { border: "border-l-danger", bg: "bg-danger-soft", text: "text-danger", Icon: XCircle },
  warning: { border: "border-l-warning", bg: "bg-warning-soft", text: "text-warning", Icon: AlertTriangle },
  info: { border: "border-l-info", bg: "bg-info-soft", text: "text-info", Icon: Info },
};

export function Alert({
  kind,
  title,
  children,
  onClose,
}: {
  kind: AlertKind;
  title?: string;
  children: ReactNode;
  onClose?: () => void;
}) {
  const { border, bg, text, Icon } = alertStyles[kind];
  return (
    <div className={`flex items-start gap-3 rounded-md border-l-4 px-4 py-3 ${border} ${bg}`}>
      <Icon size={20} className={`mt-0.5 flex-shrink-0 ${text}`} />
      <div className="flex-1 text-body text-ink">
        {title && <div className="mb-0.5 font-medium">{title}</div>}
        {children}
      </div>
      {onClose && (
        <button type="button" onClick={onClose} aria-label="Dismiss" className="opacity-60 hover:opacity-100">
          <XCircle size={18} />
        </button>
      )}
    </div>
  );
}

export function ProgressBar({ value, label }: { value?: number; label?: string }) {
  const determinate = typeof value === "number";
  return (
    <div className="flex items-center gap-3">
      <div className="h-1 flex-1 overflow-hidden rounded-full bg-surface-2">
        {determinate ? (
          <div
            className="h-full rounded-full bg-accent transition-[width] duration-300 ease-out"
            style={{ width: `${Math.min(100, Math.max(0, value))}%` }}
          />
        ) : (
          <div className="h-full w-1/3 animate-[progress-indeterminate_1s_linear_infinite] rounded-full bg-accent" />
        )}
      </div>
      {label && <span className="text-body-sm text-ink-dim">{label}</span>}
    </div>
  );
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`animate-pulse rounded-md bg-surface-2 ${className}`} />;
}

export function SkeletonText({ lines = 3 }: { lines?: number }) {
  return (
    <div className="flex flex-col gap-2">
      {Array.from({ length: lines }, (_, i) => (
        <Skeleton key={i} className={`h-4 ${i === lines - 1 ? "w-2/3" : "w-full"}`} />
      ))}
    </div>
  );
}

export function EmptyState({
  icon,
  title,
  description,
  action,
}: {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex min-h-[280px] flex-col items-center justify-center gap-1 px-6 py-12 text-center">
      {icon && <div className="mb-3 text-ink-dim/50">{icon}</div>}
      <div className="text-h4 text-ink">{title}</div>
      {description && <p className="max-w-[300px] text-body text-ink-dim">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

export function ConfirmDialog({
  open,
  onCancel,
  onConfirm,
  title = "Are you sure?",
  message = "This action cannot be undone.",
  confirmLabel = "Confirm",
  danger = true,
}: {
  open: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  title?: string;
  message?: ReactNode;
  confirmLabel?: string;
  danger?: boolean;
}) {
  return (
    <Dialog open={open} onClose={onCancel} title={title}>
      <p className="mb-5 text-body text-ink-dim">{message}</p>
      <div className="flex justify-end gap-3">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button variant={danger ? "danger" : "primary"} onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </Dialog>
  );
}
