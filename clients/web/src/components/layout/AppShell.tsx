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
        <div className="flex items-center gap-8">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-md bg-primary text-sm font-extrabold text-primary-foreground">
              SR
            </div>
            <div className="flex flex-col leading-none">
              <span className="text-sm font-extrabold tracking-widest">
                SHOOTER<span className="text-primary">RSG</span>
              </span>
              <span className="mt-1 font-mono text-[10px] uppercase tracking-widest text-muted-foreground">
                Admin
              </span>
            </div>
          </div>
          <nav className="flex items-center gap-5">
            {NAV_LINKS.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                className={({ isActive }) =>
                  cn(
                    "text-sm text-muted-foreground hover:text-foreground",
                    isActive && "font-semibold text-primary hover:text-primary",
                  )
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
