import { useMemo, type CSSProperties } from "react";
import ReactFlow, { Background, Controls, Handle, Position, type Edge, type Node, type NodeProps } from "reactflow";
import "reactflow/dist/style.css";
import type { FlowStepResponse } from "@/services/types";

// FR-FEAT-04: a real canvas view of the step sequence — steps only carry a
// flat `order` (no branch/graph pointers in FlowStepResponse), and the API
// has no reorder/delete/update-step endpoint yet, so this stays a linear,
// view-and-add surface (nodesDraggable/nodesConnectable are off on purpose)
// rather than pretending to support operations the backend can't perform.

const NODE_WIDTH = 200;
const NODE_GAP = 220;
const NODE_Y = 80;

interface StepNodeData {
  step: FlowStepResponse;
  index: number;
  selected: boolean;
}

function StepNode({ data }: NodeProps<StepNodeData>) {
  const { step, index, selected } = data;
  return (
    <div
      className={`rounded-md border bg-surface-2 px-3 py-2.5 text-left shadow-sm ${
        selected ? "border-accent" : "border-border"
      }`}
      style={{ width: NODE_WIDTH }}
    >
      <Handle type="target" position={Position.Left} className="!bg-border" />
      <div className="flex items-center gap-2">
        <span className="font-mono text-xs text-ink-dim">{index + 1}</span>
        <span className="truncate text-sm text-ink">{step.name}</span>
      </div>
      <div className="mt-1 truncate font-mono text-xs text-ink-dim">
        {step.module}.{step.action}
      </div>
      <Handle type="source" position={Position.Right} className="!bg-border" />
    </div>
  );
}

interface AddNodeData {
  onAdd: () => void;
}

function AddNode({ data }: NodeProps<AddNodeData>) {
  return (
    <button
      type="button"
      onClick={data.onAdd}
      className="flex items-center justify-center rounded-md border border-dashed border-border text-ink-dim hover:border-accent hover:text-accent"
      style={{ width: NODE_WIDTH, height: 60 }}
    >
      <Handle type="target" position={Position.Left} className="!bg-border" />
      <span className="text-lg leading-none">+</span>
      <span className="ml-2 text-xs">Add step</span>
    </button>
  );
}

const nodeTypes = { step: StepNode, add: AddNode };

const edgeStyle: CSSProperties = { stroke: "var(--yukti-border)" };

export function WorkflowCanvas({
  steps,
  canAddStep,
  onAddStep,
  selectedStepId,
  onSelectStep,
}: {
  steps: FlowStepResponse[];
  canAddStep: boolean;
  onAddStep: () => void;
  selectedStepId: string | null;
  onSelectStep: (id: string) => void;
}) {
  const ordered = useMemo(() => [...steps].sort((a, b) => a.order - b.order), [steps]);

  const nodes: Node[] = useMemo(() => {
    const stepNodes: Node[] = ordered.map((step, i) => ({
      id: step.id,
      type: "step",
      position: { x: i * NODE_GAP, y: NODE_Y },
      data: { step, index: i, selected: step.id === selectedStepId } satisfies StepNodeData,
      draggable: false,
    }));
    if (!canAddStep) return stepNodes;
    return [
      ...stepNodes,
      {
        id: "__add__",
        type: "add",
        position: { x: ordered.length * NODE_GAP, y: NODE_Y },
        data: { onAdd: onAddStep } satisfies AddNodeData,
        draggable: false,
        selectable: false,
      },
    ];
  }, [ordered, canAddStep, onAddStep, selectedStepId]);

  const edges: Edge[] = useMemo(() => {
    const chain: Edge[] = ordered.slice(1).map((step, i) => ({
      id: `${ordered[i].id}-${step.id}`,
      source: ordered[i].id,
      target: step.id,
      style: edgeStyle,
    }));
    if (canAddStep && ordered.length > 0) {
      chain.push({
        id: `${ordered[ordered.length - 1].id}-add`,
        source: ordered[ordered.length - 1].id,
        target: "__add__",
        style: edgeStyle,
      });
    }
    return chain;
  }, [ordered, canAddStep]);

  if (ordered.length === 0 && !canAddStep) {
    return <div className="text-sm text-ink-dim">No steps yet.</div>;
  }

  return (
    <div style={{ height: 260 }} className="overflow-hidden rounded-lg border border-border">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodeClick={(_, node) => {
          if (node.type === "step") onSelectStep(node.id);
        }}
        nodesDraggable={false}
        nodesConnectable={false}
        edgesFocusable={false}
        proOptions={{ hideAttribution: true }}
        fitView
        fitViewOptions={{ padding: 0.3, maxZoom: 1 }}
      >
        <Background color="var(--yukti-border)" gap={20} />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
}
