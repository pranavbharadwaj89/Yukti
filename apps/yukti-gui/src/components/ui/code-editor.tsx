import { useState } from "react";
import Editor from "@monaco-editor/react";
import { Check, Copy } from "lucide-react";
import { Select, type SelectOption } from "@/components/ui/form-controls";

// UI_Component_Spec.md Part 3 §3 (Code Editor, Embedded). Wraps the
// already-installed but previously-unused @monaco-editor/react with the
// spec's toolbar (language selector + copy button) restyled onto Yukti
// tokens; Monaco's own theme is mapped to a dark/light pair since it can't
// consume CSS custom properties directly.

const LANGUAGES: SelectOption[] = [
  { value: "json", label: "JSON" },
  { value: "javascript", label: "JavaScript" },
  { value: "python", label: "Python" },
  { value: "sql", label: "SQL" },
];

export function CodeEditor({
  value,
  onChange,
  language = "json",
  onLanguageChange,
  height = 240,
  readOnly = false,
}: {
  value: string;
  onChange?: (value: string) => void;
  language?: string;
  onLanguageChange?: (language: string) => void;
  height?: number;
  readOnly?: boolean;
}) {
  const [copied, setCopied] = useState(false);
  const isDark = document.documentElement.getAttribute("data-theme") !== "light";

  return (
    <div className="overflow-hidden rounded-md border border-border">
      <div className="flex items-center gap-2 border-b border-border bg-surface-2 px-3 py-2">
        {onLanguageChange ? (
          <div className="w-40">
            <Select value={language} options={LANGUAGES} onChange={onLanguageChange} />
          </div>
        ) : (
          <span className="font-mono text-body-sm text-ink-dim">{language}</span>
        )}
        <button
          type="button"
          onClick={() => {
            void navigator.clipboard.writeText(value);
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
          }}
          aria-label="Copy code"
          className="ml-auto flex h-8 w-8 items-center justify-center rounded-md text-ink-dim hover:bg-surface hover:text-ink"
        >
          {copied ? <Check size={16} className="text-success" /> : <Copy size={16} />}
        </button>
      </div>
      <Editor
        height={height}
        language={language}
        value={value}
        onChange={(v) => onChange?.(v ?? "")}
        theme={isDark ? "vs-dark" : "light"}
        options={{
          readOnly,
          minimap: { enabled: false },
          fontSize: 13,
          fontFamily: "var(--font-mono)",
          scrollBeyondLastLine: false,
        }}
      />
    </div>
  );
}
