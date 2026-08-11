import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ApiRequestResponse } from "@/services/types";
import { ExplorerTree } from "./explorer-tree";
import { RequestDesigner } from "./request-designer";

// Two-pane API Studio: Explorer (saved Collections/Requests) on the left,
// the Request Designer + Response Viewer on the right. This is the actual
// /tests/api route component now — RequestDesigner alone (Phase 1) is a
// piece of this, not a separate route.
export function ApiStudioPage() {
  const queryClient = useQueryClient();
  const [selectedCollectionId, setSelectedCollectionId] = useState<string | null>(null);
  const [selectedRequest, setSelectedRequest] = useState<ApiRequestResponse | null>(null);

  return (
    <div className="flex gap-4">
      <ExplorerTree
        selectedCollectionId={selectedCollectionId}
        selectedRequestId={selectedRequest?.id ?? null}
        onSelectCollection={(collectionId) => {
          setSelectedCollectionId(collectionId);
          setSelectedRequest(null);
        }}
        onSelectRequest={(collectionId, request) => {
          setSelectedCollectionId(collectionId);
          setSelectedRequest(request);
        }}
      />
      <div className="min-w-0 flex-1">
        <RequestDesigner
          key={selectedRequest?.id ?? selectedCollectionId ?? "blank"}
          collectionId={selectedCollectionId}
          loadedRequest={selectedRequest}
          onSaved={() => void queryClient.invalidateQueries({ queryKey: ["api-collections"] })}
        />
      </div>
    </div>
  );
}
