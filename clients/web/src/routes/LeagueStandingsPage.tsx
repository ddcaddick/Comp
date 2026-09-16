import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { api } from "../lib/api";
import { CompetitionTabs } from "../components/layout/CompetitionTabs";
import { ShooterName } from "../lib/shooterName";

export function LeagueStandingsPage() {
  const { competitionId, leagueId } = useParams<{ competitionId: string; leagueId: string }>();

  const standingsQuery = useQuery({
    queryKey: ["league-standings", leagueId],
    queryFn: async () => {
      const { data, error } = await api.GET("/leagues/{id}/standings", { params: { path: { id: leagueId! } } });
      if (error) throw new Error("Failed to load standings");
      return data;
    },
    enabled: !!leagueId,
  });

  return (
    <div>
      <h1 className="mb-1 text-lg font-semibold">
        {standingsQuery.data ? `Standings — ${standingsQuery.data.leagueName}` : "Standings"}
      </h1>
      <CompetitionTabs competitionId={competitionId!} />
      <p className="mb-4 text-sm">
        <Link to={`/competitions/${competitionId}/leagues/${leagueId}`} className="text-primary hover:underline">
          ← Roster
        </Link>
        {standingsQuery.data && (
          <span className="text-muted-foreground"> · {standingsQuery.data.eventsHeld} events held</span>
        )}
      </p>

      {standingsQuery.isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {standingsQuery.isError && <p className="text-sm text-destructive">Could not load standings.</p>}

      {!standingsQuery.isLoading && !standingsQuery.isError && (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2 pr-4 font-medium text-muted-foreground">Pos</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Name</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Counting</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Missed</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Running total</th>
            </tr>
          </thead>
          <tbody>
            {(standingsQuery.data?.standings ?? []).map((s) => (
              <tr key={s.shooterId} className="border-b border-border">
                <td className="py-2 pr-4">{s.position}</td>
                <td className="py-2 pr-4">
                  <ShooterName shooter={s} />
                  {s.isProvisional && <span className="ml-2 text-xs text-muted-foreground">(provisional)</span>}
                </td>
                <td className="py-2 pr-4">{s.countingTotal}</td>
                <td className="py-2 pr-4">{s.missedEvents}</td>
                <td className="py-2 pr-4">{s.runningTotal}</td>
              </tr>
            ))}
            {(standingsQuery.data?.standings ?? []).length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-center text-muted-foreground">
                  No events have been finalised for this league yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
