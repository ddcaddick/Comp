import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { Activity, CalendarDays, ChevronRight, History, Users } from "lucide-react";
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { addDaysIso, todayIso } from "../lib/dates";
import clubBadge from "../assets/club-badge.png";

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
  // Not date-windowed like the sections below -- an event left InProgress is worth
  // surfacing regardless of when it started, since that's exactly the "someone forgot to
  // finalise this" case an admin most needs to notice.
  const inProgress = events
    .filter((e) => e.status === "InProgress")
    .sort((a, b) => a.eventDate.localeCompare(b.eventDate));
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
      <div className="mb-8 flex items-start justify-between gap-4">
        <div>
          <h1 className="mb-1 text-lg font-semibold">Welcome, {user?.displayName ?? "there"}</h1>
          <p className="text-sm text-muted-foreground">Here's what's happening at the club.</p>
        </div>
        <img src={clubBadge} alt="Club badge" className="h-16 w-16 shrink-0 object-contain" />
      </div>

      {isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {!isLoading && eventsQuery.isError && <p className="text-sm text-destructive">Could not load events.</p>}

      {!isLoading && !eventsQuery.isError && (
        <>
          <div className="mb-8 grid gap-8 md:grid-cols-2">
            <EventSection
              icon={<Activity className="h-4 w-4" />}
              title="In Progress"
              subtitle="Happening right now"
              events={inProgress}
              competitionNameById={competitionNameById}
              emptyText="No events in progress."
              onSelect={goToEvent}
            />
            <CompetitionShootersChart competitions={competitionsQuery.data ?? []} />
          </div>

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
        </>
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
              {/* Fixed column widths, not one joined string, so competition/date/status
                  each line up from row to row regardless of how long any one value is. */}
              <div className="mt-1 flex gap-3 text-xs text-muted-foreground">
                <span className="w-24 shrink-0 truncate">
                  {competitionNameById.get(event.competitionId) ?? "Competition"}
                </span>
                <span className="w-20 shrink-0">{event.eventDate}</span>
                <span className="shrink-0">{event.status}</span>
              </div>
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

// A vivid, saturated palette that reads clearly against the app's dark background --
// distinct from the single muted orange accent used everywhere else, since a chart with
// several same-hue slices would be unreadable.
const CHART_COLORS = ["#ff8a3d", "#22d3ee", "#c084fc", "#4ade80", "#f472b6", "#facc15"];

interface CompetitionRow {
  id: string;
  name: string;
  rosteredShooters: number | string;
}

function CompetitionShootersChart({ competitions }: { competitions: CompetitionRow[] }) {
  const data = competitions
    .map((c) => ({ name: c.name, value: Number(c.rosteredShooters) }))
    .filter((c) => c.value > 0);
  const total = data.reduce((sum, d) => sum + d.value, 0);

  return (
    <div>
      <div className="mb-1 flex items-center gap-2">
        <span className="text-primary">
          <Users className="h-4 w-4" />
        </span>
        <h2 className="text-base font-semibold">Shooters by Competition</h2>
      </div>
      <p className="mb-3 text-xs text-muted-foreground">Rostered across all leagues</p>

      <div className="rounded-lg border border-border bg-background p-4">
        {data.length === 0 ? (
          <p className="py-10 text-center text-sm text-muted-foreground">No shooters rostered yet.</p>
        ) : (
          <div className="flex items-center gap-6">
            <div className="relative h-36 w-36 shrink-0">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie
                    data={data}
                    dataKey="value"
                    nameKey="name"
                    innerRadius="65%"
                    outerRadius="100%"
                    paddingAngle={data.length > 1 ? 3 : 0}
                    stroke="none"
                  >
                    {data.map((entry, index) => (
                      <Cell key={entry.name} fill={CHART_COLORS[index % CHART_COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip
                    contentStyle={{
                      backgroundColor: "#141519",
                      border: "1px solid #2a2c33",
                      borderRadius: 8,
                      fontSize: 12,
                    }}
                    itemStyle={{ color: "#eef1f4" }}
                    labelStyle={{ color: "#eef1f4" }}
                  />
                </PieChart>
              </ResponsiveContainer>
              <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                <span className="text-xl font-bold">{total}</span>
                <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Shooters</span>
              </div>
            </div>
            <div className="flex min-w-0 flex-1 flex-col gap-2">
              {data.map((entry, index) => (
                <div key={entry.name} className="flex items-center justify-between gap-2 text-sm">
                  <span className="flex min-w-0 items-center gap-2">
                    <span
                      className="h-2.5 w-2.5 shrink-0 rounded-full"
                      style={{ backgroundColor: CHART_COLORS[index % CHART_COLORS.length] }}
                    />
                    <span className="truncate">{entry.name}</span>
                  </span>
                  <span className="shrink-0 font-medium">{entry.value}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
