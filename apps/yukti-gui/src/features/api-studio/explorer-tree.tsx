import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Folder, FolderOpen, Plus, Trash2 } from "lucide-react";
import { apiCollectionsApi, ApiError } from "@/services/api-client";
import type { ApiRequestResponse } from "@/services/types";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/primitives";
import { useToastStore } from "@/store/toast-store";
import { useProjectStore } from "@/store/project-store";

const METHOD_VARIANT: Record<string, BadgeVariant> = {
  GET: "success",
  POST: "primary",
  PUT: "warning",
  PATCH: "warning",
  DELETE: "danger",
};

// Collection > Request tree, backed by the api-collections CRUD endpoints
// (no execution here — Send always happens in RequestDesigner via the
// existing Flow-based path, this is purely the saved-request browser).
export function ExplorerTree({
  selectedCollectionId,
  selectedRequestId,
  onSelectCollection,
  onSelectRequest,
}: {
  selectedCollectionId: string | null;
  selectedRequestId: string | null;
  onSelectCollection: (collectionId: string) => void;
  onSelectRequest: (collectionId: string, request: ApiRequestResponse) => void;
}) {
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const selectedProjectId = useProjectStore((s) => s.selectedProjectId);

  const collectionsQuery = useQuery({ queryKey: ["api-collections"], queryFn: apiCollectionsApi.list });

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ["api-collections"] });
  }

  function reportError(fallback: string) {
    return (err: unknown) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : err instanceof Error ? err.message : fallback,
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      });
  }

  const createMutation = useMutation({
    mutationFn: () =>
      apiCollectionsApi.create(`Collection ${new Date().toLocaleTimeString()}`, undefined, selectedProjectId ?? undefined),
    onSuccess: invalidate,
    onError: reportError("Failed to create collection."),
  });

  const deleteMutation = useMutation({
    mutationFn: (collectionId: string) => apiCollectionsApi.delete(collectionId),
    onSuccess: invalidate,
    onError: reportError("Failed to delete collection."),
  });

  function toggle(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const collections = selectedProjectId
    ? (collectionsQuery.data ?? []).filter((c) => c.projectId === selectedProjectId)
    : (collectionsQuery.data ?? []);

  return (
    <div className="flex w-72 flex-shrink-0 flex-col gap-2 border-r border-border pr-3">
      <div className="flex items-center justify-between">
        <h2 className="text-body-sm font-medium text-ink-dim">Collections</h2>
        <button
          type="button"
          onClick={() => createMutation.mutate()}
          aria-label="New collection"
          className="flex h-7 w-7 items-center justify-center rounded-md text-ink-dim hover:bg-surface-2 hover:text-ink"
        >
          <Plus size={16} />
        </button>
      </div>

      {collectionsQuery.isLoading && <Spinner />}

      <ul className="flex flex-col gap-0.5">
        {collections.map((c) => {
          const isExpanded = expanded.has(c.id);
          return (
            <li key={c.id}>
              <div
                className={`group flex cursor-pointer items-center gap-1.5 rounded-md px-2 py-1.5 text-body-sm ${
                  selectedCollectionId === c.id && !selectedRequestId ? "bg-accent-soft text-accent" : "text-ink hover:bg-surface-2"
                }`}
                onClick={() => {
                  toggle(c.id);
                  onSelectCollection(c.id);
                }}
              >
                {isExpanded ? <FolderOpen size={14} /> : <Folder size={14} />}
                <span className="flex-1 truncate">{c.name}</span>
                <button
                  type="button"
                  aria-label="Delete collection"
                  onClick={(e) => {
                    e.stopPropagation();
                    deleteMutation.mutate(c.id);
                  }}
                  className="text-ink-dim opacity-0 hover:text-danger group-hover:opacity-100"
                >
                  <Trash2 size={12} />
                </button>
              </div>
              {isExpanded && (
                <ul className="ml-4 flex flex-col gap-0.5 border-l border-border pl-2">
                  {c.requests.map((r) => (
                    <li
                      key={r.id}
                      onClick={() => onSelectRequest(c.id, r)}
                      className={`flex cursor-pointer items-center gap-1.5 rounded-md px-2 py-1 text-body-sm ${
                        selectedRequestId === r.id ? "bg-accent-soft text-accent" : "text-ink-dim hover:bg-surface-2 hover:text-ink"
                      }`}
                    >
                      <Badge variant={METHOD_VARIANT[r.method] ?? "gray"} className="text-[10px]">
                        {r.method}
                      </Badge>
                      <span className="truncate">{r.name}</span>
                    </li>
                  ))}
                  {c.requests.length === 0 && <li className="px-2 py-1 text-caption text-ink-dim">No requests yet.</li>}
                </ul>
              )}
            </li>
          );
        })}
        {collections.length === 0 && !collectionsQuery.isLoading && (
          <li className="px-2 py-1 text-body-sm text-ink-dim">No collections yet. Click + to create one.</li>
        )}
      </ul>
    </div>
  );
}
