import { NavLink } from "react-router";
import { cn } from "../../lib/cn";

export function CompetitionTabs({ competitionId }: { competitionId: string }) {
  const tabs = [
    { to: `/competitions/${competitionId}/events`, label: "Events" },
    { to: `/competitions/${competitionId}/leagues`, label: "Leagues" },
  ];

  return (
    <nav className="mb-4 flex gap-4">
      {tabs.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          end={false}
          className={({ isActive }) =>
            cn("text-sm text-muted-foreground hover:text-foreground", isActive && "font-semibold text-primary")
          }
        >
          {tab.label}
        </NavLink>
      ))}
    </nav>
  );
}
