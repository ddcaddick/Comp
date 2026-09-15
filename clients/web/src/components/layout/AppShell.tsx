import type { ReactNode } from "react";
import { NavLink } from "react-router";
import { Home, LogOut, Trophy, Users } from "lucide-react";
import { useAuth } from "../../lib/auth";
import { cn } from "../../lib/cn";
import { Button } from "../ui/button";

const NAV_LINKS = [
  { to: "/home", label: "Home", icon: Home },
  { to: "/shooters", label: "Shooters", icon: Users },
  { to: "/competitions", label: "Competitions", icon: Trophy },
];

export function AppShell({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();

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
                    "flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground",
                    isActive && "font-semibold text-primary hover:text-primary",
                  )
                }
              >
                <link.icon className="h-4 w-4" />
                {link.label}
              </NavLink>
            ))}
          </nav>
        </div>
        <div className="flex items-center gap-4">
          {user && <span className="text-sm text-muted-foreground">{user.displayName}</span>}
          <Button variant="outline" size="sm" onClick={logout}>
            <LogOut className="h-4 w-4" />
            Sign out
          </Button>
        </div>
      </header>
      <main className="p-6">{children}</main>
    </div>
  );
}
