import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { formatMillis } from "@comp/core";
import { api } from "../lib/api";
import { downloadResultsPdf } from "../lib/resultsPdf";
import { formatResultStatus } from "../lib/resultStatus";
import { Button } from "../components/ui/button";
import { CompetitionTabs } from "../components/layout/CompetitionTabs";
import { ShooterName } from "../lib/shooterName";

export function EventResultsPage() {
  const { competitionId, eventId } = useParams<{ competitionId: string; eventId: string }>();
  const [leagueId, setLeagueId] = useState("");

  const eventsQuery = useQuery({
    queryKey: ["events", competitionId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events", { params: { query: { competitionId } } });
      if (error) throw new Error("Failed to load events");
      return data;
    },
    enabled: !!competitionId,
  });
  const event = eventsQuery.data?.find((e) => e.id === eventId);

  const competitionsQuery = useQuery({
    queryKey: ["competitions"],
    queryFn: async () => {
      const { data, error } = await api.GET("/competitions");
      if (error) throw new Error("Failed to load competitions");
      return data;
    },
  });
  const competition = competitionsQuery.data?.find((c) => c.id === competitionId);

  const leaguesQuery = useQuery({
    queryKey: ["leagues", competitionId],
    queryFn: async () => {
      const { data, error } = await api.GET("/competitions/{id}/leagues", {
        params: { path: { id: competitionId! } },
      });
      if (error) throw new Error("Failed to load leagues");
      return data;
    },
    enabled: !!competitionId,
  });

  const resultsQuery = useQuery({
    queryKey: ["event-results", eventId, leagueId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/results", {
        params: { path: { id: eventId! }, query: leagueId ? { leagueId } : {} },
      });
      if (error) throw new Error("Failed to load results");
      return data;
    },
    enabled: !!eventId,
    // Lets this page be left open on a display during live entry and update on its own as
    // scores come in. Stops once the event is finalised -- a frozen result never changes,
    // so there's nothing left to poll for.
    refetchInterval: (query) => (query.state.data?.isFinal ? false : 5000),
  });

  // Always unfiltered, independent of the on-screen league dropdown above: the PDF shows
  // the overall table and every league's tile side by side, regardless of which single
  // league (if any) is currently selected for the on-screen table.
  const overallResultsQuery = useQuery({
    queryKey: ["event-results", eventId, ""],
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/results", { params: { path: { id: eventId! } } });
      if (error) throw new Error("Failed to load results");
      return data;
    },
    enabled: !!eventId,
  });

  const [downloading, setDownloading] = useState(false);
  async function handleDownloadPdf() {
    if (!overallResultsQuery.data || !event || !competition) return;
    setDownloading(true);
    try {
      await downloadResultsPdf({
        competitionName: competition.name,
        eventName: event.name,
        eventDate: event.eventDate,
        isFinal: overallResultsQuery.data.isFinal,
        participants: overallResultsQuery.data.participants,
      });
    } finally {
      setDownloading(false);
    }
  }

  return (
    <div>
      <div className="mb-1 flex items-center justify-between gap-3">
        <h1 className="text-lg font-semibold">{event ? `Results — ${event.name}` : "Results"}</h1>
        <Button
          variant="outline"
          size="sm"
          disabled={downloading || !overallResultsQuery.data || !event || !competition}
          onClick={handleDownloadPdf}
        >
          {downloading ? "Generating…" : "Download PDF"}
        </Button>
      </div>
      <CompetitionTabs competitionId={competitionId!} />
      <p className="mb-4 text-sm">
        <Link to={`/competitions/${competitionId}/events`} className="text-primary hover:underline">
          ← All events
        </Link>
        {resultsQuery.data && (
          <span className="text-muted-foreground">
            {" "}
            · {resultsQuery.data.isFinal ? "Final" : "Provisional (live)"}
          </span>
        )}
      </p>

      <div className="mb-4 flex flex-col gap-1">
        <label className="text-xs font-medium text-muted-foreground" htmlFor="league-filter">
          League
        </label>
        <select
          id="league-filter"
          value={leagueId}
          onChange={(e) => setLeagueId(e.target.value)}
          className="w-56 rounded-md border border-border bg-input px-3 py-1.5 text-sm"
        >
          <option value="">Overall</option>
          {(leaguesQuery.data ?? []).map((league) => (
            <option key={league.id} value={league.id}>
              {league.name}
            </option>
          ))}
        </select>
      </div>

      {resultsQuery.isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {resultsQuery.isError && <p className="text-sm text-destructive">Could not load results.</p>}

      {!resultsQuery.isLoading && !resultsQuery.isError && (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2 pr-4 font-medium text-muted-foreground">Pos</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Name</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">League</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Time</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Status</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Points</th>
            </tr>
          </thead>
          <tbody>
            {(resultsQuery.data?.participants ?? []).map((p) => (
              <tr key={p.participantId} className="border-b border-border">
                <td className="py-2 pr-4">{(leagueId ? p.leaguePosition : p.overallPosition) ?? "—"}</td>
                <td className="py-2 pr-4">
                  <ShooterName shooter={p} />
                </td>
                <td className="py-2 pr-4">{p.leagueName ?? "—"}</td>
                <td className="py-2 pr-4">
                  {p.eventTimeMs != null ? formatMillis(Number(p.eventTimeMs)) : formatResultStatus(p.status)}
                </td>
                <td className="py-2 pr-4">{formatResultStatus(p.status)}</td>
                <td className="py-2 pr-4">{p.leaguePoints}</td>
              </tr>
            ))}
            {(resultsQuery.data?.participants ?? []).length === 0 && (
              <tr>
                <td colSpan={6} className="py-4 text-center text-muted-foreground">
                  No results yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
