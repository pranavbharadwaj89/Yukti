import type { ReactNode } from "react";

// UI_Component_Spec.md Part 2 §7 (Badge), colors mapped onto Yukti's
// existing semantic tokens instead of the spec's literal hexes.
export type BadgeVariant = "primary" | "success" | "danger" | "warning" | "gray";

const variants: Record<BadgeVariant, string> = {
  primary: "bg-accent-soft text-accent",
  success: "bg-success-soft text-success",
  danger: "bg-danger-soft text-danger",
  warning: "bg-warning-soft text-warning",
  gray: "bg-surface-2 text-ink-dim",
};

export function Badge({
  variant = "gray",
  icon,
  onDismiss,
  className = "",
  children,
}: {
  variant?: BadgeVariant;
  icon?: ReactNode;
  onDismiss?: () => void;
  className?: string;
  children: ReactNode;
}) {
  return (
    <span
      className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-1 text-body-sm font-medium ${variants[variant]} ${className}`}
    >
      {icon}
      {children}
      {onDismiss && (
        <button
          type="button"
          onClick={onDismiss}
          aria-label="Remove"
          className="ml-0.5 opacity-70 hover:opacity-100"
        >
          ×
        </button>
      )}
    </span>
  );
}
