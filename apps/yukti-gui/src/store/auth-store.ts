import { create } from "zustand";
import type { TokenResponse } from "@/services/types";

// FR-STATE-02: Zustand, scoped to exactly one concern (session identity),
// never a monolithic store. FR-SEC-01: the access token lives only here
// (in-memory, lost on reload by design) — never localStorage. The refresh
// token is the one documented deviation from the (unavailable) Vol 2 §14
// HttpOnly-cookie spec: Yukti.Api returns it as a JSON field, not a
// Set-Cookie header, so there is no cookie to rely on. sessionStorage is
// the least-bad interim store (survives reload, gone on tab close, still
// not readable across tabs/never sent automatically like a cookie would
// be) — flagged, not silent; a real fix needs a backend change.
interface DecodedAccessToken {
  sub: string;
  tenant: string;
  email: string;
  role: string; // "{roleId}:{permissionsBitmaskOrVersion}" per JwtTokenService
  exp: number;
}

interface AuthState {
  accessToken: string | null;
  accessTokenExpiresAt: number | null; // epoch ms
  user: DecodedAccessToken | null;
  setSession: (tokens: TokenResponse) => void;
  clearSession: () => void;
}

const REFRESH_TOKEN_KEY = "yukti.refreshToken";

function decodeAccessToken(token: string): DecodedAccessToken {
  const payload = token.split(".")[1];
  const json = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
  return JSON.parse(json) as DecodedAccessToken;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  accessTokenExpiresAt: null,
  user: null,
  setSession: (tokens) => {
    sessionStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
    set({
      accessToken: tokens.accessToken,
      accessTokenExpiresAt: new Date(tokens.expiresAt).getTime(),
      user: decodeAccessToken(tokens.accessToken),
    });
  },
  clearSession: () => {
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    set({ accessToken: null, accessTokenExpiresAt: null, user: null });
  },
}));

export function getStoredRefreshToken(): string | null {
  return sessionStorage.getItem(REFRESH_TOKEN_KEY);
}
