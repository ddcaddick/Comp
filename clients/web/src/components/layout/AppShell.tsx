import { useState, type ReactNode } from "react";
import { NavLink } from "react-router";
import { Home, LogOut, Menu, Trophy, Users, X } from "lucide-react";
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
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <div className="min-h-screen bg-muted">
      <header className="border-b border-border bg-background px-4 py-3 sm:px-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-primary text-sm font-extrabold text-primary-foreground">
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

          <nav className="hidden items-center gap-5 md:flex">
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

          <div className="hidden items-center gap-4 md:flex">
            {user && <span className="text-sm text-muted-foreground">{user.displayName}</span>}
            <Button variant="outline" size="sm" onClick={logout}>
              <LogOut className="h-4 w-4" />
              Sign out
            </Button>
          </div>

          <button
            type="button"
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:text-foreground md:hidden"
            onClick={() => setMenuOpen((open) => !open)}
            aria-label={menuOpen ? "Close menu" : "Open menu"}
          >
            {menuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
          </button>
        </div>

        {menuOpen && (
          <div className="mt-3 flex flex-col gap-3 border-t border-border pt-3 md:hidden">
            <nav className="flex flex-col gap-3">
              {NAV_LINKS.map((link) => (
                <NavLink
                  key={link.to}
                  to={link.to}
                  onClick={() => setMenuOpen(false)}
                  className={({ isActive }) =>
                    cn(
                      "flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground",
                      isActive && "font-semibold text-primary hover:text-primary",
                    )
                  }
                >
                  <link.icon className="h-4 w-4" />
                  {link.label}
                </NavLink>
              ))}
            </nav>
            <div className="flex items-center justify-between border-t border-border pt-3">
              {user && <span className="text-sm text-muted-foreground">{user.displayName}</span>}
              <Button variant="outline" size="sm" onClick={logout}>
                <LogOut className="h-4 w-4" />
                Sign out
              </Button>
            </div>
          </div>
        )}
      </header>
      <main className="p-4 sm:p-6">{children}</main>
    </div>
  );
}
