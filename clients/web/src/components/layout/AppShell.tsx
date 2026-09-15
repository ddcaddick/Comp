import type { ReactNode } from "react";
import { NavLink } from "react-router";
import { useAuth } from "../../lib/auth";
import { cn } from "../../lib/cn";
import { Button } from "../ui/button";

const NAV_LINKS = [
  { to: "/shooters", label: "Shooters" },
  { to: "/competitions", label: "Competitions" },
];

export function AppShell({ children }: { children: ReactNode }) {
  const { logout } = useAuth();

  return (
    <div className="min-h-screen bg-muted">
      <header className="flex items-center justify-between border-b border-border bg-background px-6 py-3">
        <div className="flex items-center gap-6">
          <span className="font-semibold">Comp Admin</span>
          <nav className="flex items-center gap-4">
            {NAV_LINKS.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                className={({ isActive }) =>
                  cn("text-sm text-muted-foreground hover:text-foreground", isActive && "font-medium text-foreground")
                }
              >
                {link.label}
              </NavLink>
            ))}
          </nav>
        </div>
        <Button variant="outline" size="sm" onClick={logout}>
          Sign out
        </Button>
      </header>
      <main className="p-6">{children}</main>
    </div>
  );
}
