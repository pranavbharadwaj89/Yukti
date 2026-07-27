import { create } from "zustand";

// FR-UX-01: dark default, light + high-contrast variants, no partial
// overrides — the stored value drives :root[data-theme] in index.css.
export type Theme = "dark" | "light" | "high-contrast";

const STORAGE_KEY = "yukti.theme";

interface ThemeState {
  theme: Theme;
  setTheme: (theme: Theme) => void;
}

function applyThemeToDocument(theme: Theme) {
  document.documentElement.setAttribute("data-theme", theme);
}

const initial = (localStorage.getItem(STORAGE_KEY) as Theme | null) ?? "dark";
applyThemeToDocument(initial);

export const useThemeStore = create<ThemeState>((set) => ({
  theme: initial,
  setTheme: (theme) => {
    localStorage.setItem(STORAGE_KEY, theme);
    applyThemeToDocument(theme);
    set({ theme });
  },
}));
