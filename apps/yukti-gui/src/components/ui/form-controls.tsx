import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Check, ChevronDown } from "lucide-react";

// UI_Component_Spec.md Part 2 §3/§4/§5 (Dropdown, Checkbox, Radio),
// restyled onto Yukti's --yukti-* tokens.

export function Checkbox({
  checked,
  indeterminate = false,
  onChange,
  disabled,
  label,
}: {
  checked: boolean;
  indeterminate?: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  label?: ReactNode;
}) {
  const id = useId();
  return (
    <label htmlFor={id} className={`inline-flex items-center gap-2 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}>
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        ref={(el) => {
          if (el) el.indeterminate = indeterminate;
        }}
        onChange={(e) => onChange(e.target.checked)}
        className="peer sr-only"
      />
      <span
        className={`flex h-[18px] w-[18px] flex-shrink-0 items-center justify-center rounded-[3px] border-2 transition-colors peer-focus-visible:shadow-focus ${
          checked || indeterminate ? "border-accent bg-accent" : "border-border bg-surface hover:bg-surface-2"
        }`}
      >
        {checked && !indeterminate && <Check size={12} className="text-accent-ink" strokeWidth={3} />}
        {indeterminate && <span className="h-[2px] w-[10px] bg-accent-ink" />}
      </span>
      {label && <span className="select-none text-body text-ink">{label}</span>}
    </label>
  );
}

export function Radio({
  checked,
  onChange,
  disabled,
  label,
  name,
}: {
  checked: boolean;
  onChange: () => void;
  disabled?: boolean;
  label?: ReactNode;
  name?: string;
}) {
  const id = useId();
  return (
    <label htmlFor={id} className={`inline-flex items-center gap-2 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}>
      <input
        id={id}
        type="radio"
        name={name}
        checked={checked}
        disabled={disabled}
        onChange={onChange}
        className="peer sr-only"
      />
      <span
        className={`flex h-[18px] w-[18px] flex-shrink-0 items-center justify-center rounded-full border-2 transition-colors peer-focus-visible:shadow-focus ${
          checked ? "border-accent" : "border-border bg-surface hover:bg-surface-2"
        }`}
      >
        {checked && <span className="h-2 w-2 rounded-full bg-accent" />}
      </span>
      {label && <span className="select-none text-body text-ink">{label}</span>}
    </label>
  );
}

export interface SelectOption<T extends string = string> {
  value: T;
  label: string;
  disabled?: boolean;
}

export function Select<T extends string = string>({
  id,
  value,
  options,
  onChange,
  placeholder = "Select…",
  disabled,
}: {
  id?: string;
  value: T | "";
  options: SelectOption<T>[];
  onChange: (value: T) => void;
  placeholder?: string;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [highlighted, setHighlighted] = useState(-1);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const idBase = useId();
  const selected = options.find((o) => o.value === value);
  const selectedIndex = options.findIndex((o) => o.value === value);

  useEffect(() => {
    if (!open) return;
    function onDocClick(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, [open]);

  function openList(startAt: number) {
    setOpen(true);
    setHighlighted(startAt >= 0 ? startAt : 0);
  }

  function moveHighlight(delta: number) {
    setHighlighted((prev) => {
      const base = prev < 0 ? (selectedIndex >= 0 ? selectedIndex : 0) : prev;
      let next = base;
      for (let i = 0; i < options.length; i++) {
        next = (next + delta + options.length) % options.length;
        if (!options[next].disabled) break;
      }
      return next;
    });
  }

  function commit(index: number) {
    const opt = options[index];
    if (!opt || opt.disabled) return;
    onChange(opt.value);
    setOpen(false);
    buttonRef.current?.focus();
  }

  function onButtonKeyDown(e: KeyboardEvent<HTMLButtonElement>) {
    if (e.key === "ArrowDown" || e.key === "ArrowUp" || e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      if (!open) openList(selectedIndex);
    }
  }

  function onListKeyDown(e: KeyboardEvent<HTMLUListElement>) {
    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        moveHighlight(1);
        break;
      case "ArrowUp":
        e.preventDefault();
        moveHighlight(-1);
        break;
      case "Home":
        e.preventDefault();
        setHighlighted(options.findIndex((o) => !o.disabled));
        break;
      case "End":
        e.preventDefault();
        for (let i = options.length - 1; i >= 0; i--) {
          if (!options[i].disabled) {
            setHighlighted(i);
            break;
          }
        }
        break;
      case "Enter":
      case " ":
        e.preventDefault();
        commit(highlighted);
        break;
      case "Escape":
        e.preventDefault();
        setOpen(false);
        buttonRef.current?.focus();
        break;
      case "Tab":
        setOpen(false);
        break;
    }
  }

  return (
    <div ref={rootRef} className="relative">
      <button
        ref={buttonRef}
        id={id}
        type="button"
        disabled={disabled}
        onClick={() => (open ? setOpen(false) : openList(selectedIndex))}
        onKeyDown={onButtonKeyDown}
        aria-haspopup="listbox"
        aria-expanded={open}
        className="flex h-9 w-full items-center justify-between rounded-md border border-border bg-surface px-3 text-body text-ink disabled:cursor-not-allowed disabled:opacity-60"
      >
        <span className={selected ? "text-ink" : "text-ink-dim"}>{selected?.label ?? placeholder}</span>
        <ChevronDown size={16} className="text-ink-dim" />
      </button>
      {open && (
        <ul
          role="listbox"
          tabIndex={-1}
          ref={(el) => el?.focus()}
          aria-activedescendant={highlighted >= 0 ? `${idBase}-opt-${highlighted}` : undefined}
          onKeyDown={onListKeyDown}
          className="absolute z-[1000] mt-1 max-h-60 min-w-full overflow-auto rounded-md border border-border bg-surface shadow-lg outline-none"
        >
          {options.map((opt, i) => (
            <li
              key={opt.value}
              id={`${idBase}-opt-${i}`}
              role="option"
              aria-selected={opt.value === value}
              aria-disabled={opt.disabled}
              onMouseEnter={() => setHighlighted(i)}
              onClick={() => commit(i)}
              className={`flex cursor-pointer items-center gap-2 px-3 py-2.5 text-body transition-colors ${
                opt.disabled
                  ? "cursor-not-allowed text-ink-dim"
                  : i === highlighted
                    ? "bg-accent-soft text-accent"
                    : opt.value === value
                      ? "text-accent"
                      : "text-ink hover:bg-surface-2"
              }`}
            >
              {opt.value === value && <Check size={14} />}
              {opt.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
