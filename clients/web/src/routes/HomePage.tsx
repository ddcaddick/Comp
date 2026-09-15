import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { CalendarDays, ChevronRight, History } from "lucide-react";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { addDaysIso, todayIso } from "../lib/dates";

export function HomePage() {
  const { user } = useAuth();
  const navigate = useNavigate();

  const competitionsQuery = useQuery({
    queryKey: ["competitions"],
    queryFn: async () => {
      const { data, error } = await api.GET("/competitions");
      if (error) throw new Error("Failed to load competitions");
      return data;
    },
  });

  const eventsQuery = useQuery({
    queryKey: ["events"],
    queryFn: async () => {
      const { data, error } = await api.GET("/events");
      if (error) throw new Error("Failed to load events");
      return data;
    },
  });

  const competitionNameById = new Map((competitionsQuery.data ?? []).map((c) => [c.id, c.name]));

  const today = todayIso();
  const upcomingEnd = addDaysIso(today, 7);
  const recentStart = addDaysIso(today, -14);

  const events = eventsQuery.data ?? [];
  const upcoming = events
    .filter((e) => e.eventDate >= today && e.eventDate <= upcomingEnd)
    .sort((a, b) => a.eventDate.localeCompare(b.eventDate));
  const recent = events
    .filter((e) => e.eventDate < today && e.eventDate >= recentStart)
    .sort((a, b) => b.eventDate.localeCompare(a.eventDate));

  const isLoading = eventsQuery.isLoading || competitionsQuery.isLoading;

  function goToEvent(competitionId: string) {
    navigate(`/competitions/${competitionId}/events`);
  }

  return (
    <div>
      <h1 className="mb-1 text-lg font-semibold">Welcome, {user?.displayName ?? "there"}</h1>
      <p className="mb-8 text-sm text-muted-foreground">Here's what's happening at the club.</p>

      {isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {!isLoading && eventsQuery.isError && <p className="text-sm text-destructive">Could not load events.</p>}

      {!isLoading && !eventsQuery.isError && (
        <div className="grid gap-8 md:grid-cols-2">
          <EventSection
            icon={<CalendarDays className="h-4 w-4" />}
            title="Upcoming Events"
            subtitle="Next 7 days"
            events={upcoming}
            competitionNameById={competitionNameById}
            emptyText="No events in the next 7 days."
            onSelect={goToEvent}
          />
          <EventSection
            icon={<History className="h-4 w-4" />}
            title="Recent Events"
            subtitle="Last 14 days"
            events={recent}
            competitionNameById={competitionNameById}
            emptyText="No events in the last 14 days."
            onSelect={goToEvent}
          />
        </div>
      )}
    </div>
  );
}

interface EventRow {
  id: string;
  competitionId: string;
  name: string;
  eventDate: string;
  status: string;
}

function EventSection({
  icon,
  title,
  subtitle,
  events,
  competitionNameById,
  emptyText,
  onSelect,
}: {
  icon: React.ReactNode;
  title: string;
  subtitle: string;
  events: EventRow[];
  competitionNameById: Map<string, string>;
  emptyText: string;
  onSelect: (competitionId: string) => void;
}) {
  return (
    <div>
      <div className="mb-1 flex items-center gap-2">
        <span className="text-primary">{icon}</span>
        <h2 className="text-base font-semibold">{title}</h2>
      </div>
      <p className="mb-3 text-xs text-muted-foreground">{subtitle}</p>

      <div className="flex flex-col gap-2">
        {events.map((event) => (
          <button
            key={event.id}
            type="button"
            onClick={() => onSelect(event.competitionId)}
            className="flex items-center justify-between rounded-lg border border-border bg-background p-4 text-left hover:border-primary/50"
          >
            <div>
              <p className="text-sm font-semibold">{event.name}</p>
              <p className="mt-1 text-xs text-muted-foreground">
                {competitionNameById.get(event.competitionId) ?? "Competition"} · {event.eventDate} · {event.status}
              </p>
            </div>
            <ChevronRight className="h-4 w-4 text-muted-foreground" />
          </button>
        ))}
        {events.length === 0 && (
          <p className="rounded-lg border border-dashed border-border p-4 text-center text-sm text-muted-foreground">
            {emptyText}
          </p>
        )}
      </div>
    </div>
  );
}
