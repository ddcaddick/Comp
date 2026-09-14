import type { ReactNode } from "react";
import { useAuth } from "../../lib/auth";
import { Button } from "../ui/button";

export function AppShell({ children }: { children: ReactNode }) {
  const { logout } = useAuth();

  return (
    <div className="min-h-screen bg-muted">
      <header className="flex items-center justify-between border-b border-border bg-background px-6 py-3">
        <span className="font-semibold">Comp Admin</span>
        <Button variant="outline" size="sm" onClick={logout}>
          Sign out
        </Button>
      </header>
      <main className="p-6">{children}</main>
    </div>
  );
}
