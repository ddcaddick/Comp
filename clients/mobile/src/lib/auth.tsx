import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api } from "./api";
import { decodeJwtPayload } from "./jwt";
import { clearTokens, loadPersistedTokens, setTokens } from "./tokenStore";

export interface CurrentUser {
  displayName: string;
  email: string;
}

interface AuthContextValue {
  /** True until the persisted tokens have been read from SecureStore once at startup. */
  isLoading: boolean;
  isAuthenticated: boolean;
  /** Decoded from the access token's own claims (display_name/email) -- display only, never
   * trusted for authorization, which the server always re-checks against the bearer token. */
  user: CurrentUser | null;
  login: (
    email: string,
    password: string,
    rememberMe: boolean,
  ) => Promise<{ success: true } | { success: false; error: string }>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function userFromAccessToken(accessToken: string | null): CurrentUser | null {
  if (!accessToken) return null;
  const payload = decodeJwtPayload(accessToken);
  const displayName = payload?.display_name;
  const email = payload?.email;
  if (typeof displayName !== "string" || typeof email !== "string") return null;
  return { displayName, email };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoading, setIsLoading] = useState(true);
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [user, setUser] = useState<CurrentUser | null>(null);

  useEffect(() => {
    let cancelled = false;
    loadPersistedTokens().then(({ accessToken }) => {
      if (cancelled) return;
      setIsAuthenticated(accessToken !== null);
      setUser(userFromAccessToken(accessToken));
      setIsLoading(false);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  async function login(email: string, password: string, rememberMe: boolean) {
    // A network-level failure (no route to the server, connection refused, timeout)
    // rejects this promise instead of resolving with `error` — unlike an HTTP error
    // response, which openapi-fetch reports through `error` below. Without this
    // try/catch that rejection would propagate out of the caller's un-guarded `await
    // login(...)`, skipping its `setSubmitting(false)` and leaving the button stuck
    // on "Verifying" forever with no visible error.
    try {
      const { data, error, response } = await api.POST("/auth/login", { body: { email, password } });
      if (error || !data) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        return { success: false as const, error: detail ?? `Login failed (${response.status}).` };
      }

      await setTokens(data.accessToken, data.refreshToken, rememberMe);
      setIsAuthenticated(true);
      setUser(userFromAccessToken(data.accessToken));
      return { success: true as const };
    } catch {
      return { success: false as const, error: "Could not reach the server. Check your connection and try again." };
    }
  }

  async function logout() {
    await clearTokens();
    setIsAuthenticated(false);
    setUser(null);
  }

  return (
    <AuthContext.Provider value={{ isLoading, isAuthenticated, user, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
