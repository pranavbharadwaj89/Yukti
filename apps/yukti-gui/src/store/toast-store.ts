import { create } from "zustand";

// FR-UX-03: every mutation surfaces a success or failure notification,
// distinct from the route-level error boundary. A tiny in-memory queue —
// no need for a heavier toast library for this scope.
export interface Toast {
  id: string;
  kind: "success" | "error" | "info";
  message: string;
  correlationId?: string;
}

interface ToastState {
  toasts: Toast[];
  push: (toast: Omit<Toast, "id">) => void;
  dismiss: (id: string) => void;
}

export const useToastStore = create<ToastState>((set) => ({
  toasts: [],
  push: (toast) => {
    const id = crypto.randomUUID();
    set((s) => ({ toasts: [...s.toasts, { ...toast, id }] }));
    setTimeout(() => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })), 6000);
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));
