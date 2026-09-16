import { createContext, useContext, useState, type ReactNode } from "react";
import { api, clearTokens, getTokens, setTokens } from "./api";
import { decodeJwtPayload } from "./jwt";

export interface CurrentUser {
  displayName: string;
  email: string;
  role: string | null;
}

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

interface AuthContextValue {
  isAuthenticated: boolean;
  /** Decoded from the access token's own claims (display_name/email) -- display only, never
   * trusted for authorization, which the server always re-checks against the bearer token. */
  user: CurrentUser | null;
  login: (email: string, password: string) => Promise<{ success: true } | { success: false; error: string }>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function userFromAccessToken(accessToken: string | null): CurrentUser | null {
  if (!accessToken) return null;
  const payload = decodeJwtPayload(accessToken);
  const displayName = payload?.display_name;
  const email = payload?.email;
  if (typeof displayName !== "string" || typeof email !== "string") return null;
  const role = payload?.[ROLE_CLAIM];
  return { displayName, email, role: typeof role === "string" ? role : null };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState(() => getTokens().accessToken !== null);
  const [user, setUser] = useState<CurrentUser | null>(() => userFromAccessToken(getTokens().accessToken));

  async function login(email: string, password: string) {
    const { data, error, response } = await api.POST("/auth/login", { body: { email, password } });
    if (error || !data) {
      const detail = (error as { detail?: string | null } | undefined)?.detail;
      return { success: false as const, error: detail ?? `Login failed (${response.status}).` };
    }

    setTokens(data.accessToken, data.refreshToken);
    setIsAuthenticated(true);
    setUser(userFromAccessToken(data.accessToken));
    return { success: true as const };
  }

  function logout() {
    clearTokens();
    setIsAuthenticated(false);
    setUser(null);
  }

  return <AuthContext.Provider value={{ isAuthenticated, user, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
