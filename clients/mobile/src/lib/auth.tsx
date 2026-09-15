import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api } from "./api";
import { clearTokens, loadPersistedTokens, setTokens } from "./tokenStore";

interface AuthContextValue {
  /** True until the persisted tokens have been read from SecureStore once at startup. */
  isLoading: boolean;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<{ success: true } | { success: false; error: string }>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoading, setIsLoading] = useState(true);
  const [isAuthenticated, setIsAuthenticated] = useState(false);

  useEffect(() => {
    let cancelled = false;
    loadPersistedTokens().then(({ accessToken }) => {
      if (cancelled) return;
      setIsAuthenticated(accessToken !== null);
      setIsLoading(false);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  async function login(email: string, password: string) {
    const { data, error, response } = await api.POST("/auth/login", { body: { email, password } });
    if (error || !data) {
      const detail = (error as { detail?: string | null } | undefined)?.detail;
      return { success: false as const, error: detail ?? `Login failed (${response.status}).` };
    }

    await setTokens(data.accessToken, data.refreshToken);
    setIsAuthenticated(true);
    return { success: true as const };
  }

  async function logout() {
    await clearTokens();
    setIsAuthenticated(false);
  }

  return (
    <AuthContext.Provider value={{ isLoading, isAuthenticated, login, logout }}>{children}</AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
