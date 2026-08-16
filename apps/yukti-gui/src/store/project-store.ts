import { create } from "zustand";

// Selection-only state — the actual Project/TestEnvironment lists are
// fetched via react-query (projectsApi/environmentsApi), not duplicated
// here. Persisted to localStorage (not sessionStorage like auth-store's
// refresh token) so the selection survives across tabs/reloads — unlike a
// session token, which project/environment you're working in isn't
// sensitive and there's no reason a reload should lose it.
interface ProjectState {
  selectedProjectId: string | null;
  selectedEnvironmentId: string | null;
  selectProject: (projectId: string | null) => void;
  selectEnvironment: (environmentId: string | null) => void;
}

const PROJECT_KEY = "yukti.selectedProjectId";
const ENVIRONMENT_KEY = "yukti.selectedEnvironmentId";

export const useProjectStore = create<ProjectState>((set) => ({
  selectedProjectId: localStorage.getItem(PROJECT_KEY),
  selectedEnvironmentId: localStorage.getItem(ENVIRONMENT_KEY),
  selectProject: (projectId) => {
    if (projectId) localStorage.setItem(PROJECT_KEY, projectId);
    else localStorage.removeItem(PROJECT_KEY);
    // Switching projects invalidates whatever environment was selected —
    // an environment belongs to exactly one project.
    localStorage.removeItem(ENVIRONMENT_KEY);
    set({ selectedProjectId: projectId, selectedEnvironmentId: null });
  },
  selectEnvironment: (environmentId) => {
    if (environmentId) localStorage.setItem(ENVIRONMENT_KEY, environmentId);
    else localStorage.removeItem(ENVIRONMENT_KEY);
    set({ selectedEnvironmentId: environmentId });
  },
}));
