import { createContext, useContext, useState, type ReactNode } from "react";
import { api, clearTokens, getTokens, setTokens } from "./api";

interface AuthContextValue {
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<{ success: true } | { success: false; error: string }>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState(() => getTokens().accessToken !== null);

  async function login(email: string, password: string) {
    const { data, error, response } = await api.POST("/auth/login", { body: { email, password } });
    if (error || !data) {
      const detail = (error as { detail?: string | null } | undefined)?.detail;
      return { success: false as const, error: detail ?? `Login failed (${response.status}).` };
    }

    setTokens(data.accessToken, data.refreshToken);
    setIsAuthenticated(true);
    return { success: true as const };
  }

  function logout() {
    clearTokens();
    setIsAuthenticated(false);
  }

  return <AuthContext.Provider value={{ isAuthenticated, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
