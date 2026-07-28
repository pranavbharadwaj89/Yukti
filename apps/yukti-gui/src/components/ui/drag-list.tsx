import { useState, type ReactNode } from "react";
import { GripVertical } from "lucide-react";

// UI_Component_Spec.md Part 3 §6 (Drag & Drop Container). Native HTML5 DnD
// (no extra dependency) — generic reorderable list. Not wired to flow steps:
// flowsApi has no reorder endpoint yet, so wiring this to real step order
// would silently drop on refresh. Left as a standalone, reusable primitive
// for the next feature that has a backend to persist reordering against.

export interface DragListProps<T> {
  items: T[];
  itemKey: (item: T) => string;
  onReorder: (items: T[]) => void;
  renderItem: (item: T) => ReactNode;
}

export function DragList<T>({ items, itemKey, onReorder, renderItem }: DragListProps<T>) {
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [overIndex, setOverIndex] = useState<number | null>(null);

  function handleDrop() {
    if (dragIndex === null || overIndex === null || dragIndex === overIndex) {
      setDragIndex(null);
      setOverIndex(null);
      return;
    }
    const next = [...items];
    const [moved] = next.splice(dragIndex, 1);
    next.splice(overIndex, 0, moved);
    onReorder(next);
    setDragIndex(null);
    setOverIndex(null);
  }

  return (
    <ul className="flex flex-col">
      {items.map((item, i) => (
        <li
          key={itemKey(item)}
          draggable
          onDragStart={() => setDragIndex(i)}
          onDragOver={(e) => {
            e.preventDefault();
            setOverIndex(i);
          }}
          onDrop={handleDrop}
          onDragEnd={() => {
            setDragIndex(null);
            setOverIndex(null);
          }}
          className={`mb-2 flex cursor-move items-center gap-3 rounded-md border border-border bg-surface px-4 py-3 transition-all ${
            dragIndex === i ? "scale-[0.98] opacity-70 shadow-lg" : ""
          } ${overIndex === i && dragIndex !== i ? "border-accent bg-accent-soft" : ""}`}
        >
          <GripVertical size={16} className="flex-shrink-0 text-ink-dim" />
          <div className="flex-1">{renderItem(item)}</div>
        </li>
      ))}
    </ul>
  );
}
