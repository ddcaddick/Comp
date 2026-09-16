import { useState, type FormEvent } from "react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { api } from "../lib/api";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { CompetitionTabs } from "../components/layout/CompetitionTabs";
import { downloadStandingsPdf } from "../lib/standingsPdf";

export function LeaguesPage() {
  const { competitionId } = useParams<{ competitionId: string }>();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [tier, setTier] = useState("");
  const [dropWorstCount, setDropWorstCount] = useState("0");
  const [absencesCountAsZero, setAbsencesCountAsZero] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);

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

  const standingsQueries = useQueries({
    queries: (leaguesQuery.data ?? []).map((league) => ({
      queryKey: ["league-standings", league.id],
      queryFn: async () => {
        const { data, error } = await api.GET("/leagues/{id}/standings", { params: { path: { id: league.id } } });
        if (error) throw new Error("Failed to load standings");
        return data;
      },
    })),
  });
  const allStandingsLoaded =
    standingsQueries.length > 0 && standingsQueries.every((q) => q.isSuccess) && !!competition;

  async function handleDownloadPdf() {
    if (!allStandingsLoaded || !competition) return;
    setDownloading(true);
    try {
      await downloadStandingsPdf({
        competitionName: competition.name,
        leagues: standingsQueries.map((q) => q.data!),
      });
    } finally {
      setDownloading(false);
    }
  }

  const createLeague = useMutation({
    mutationFn: async () => {
      const { data, error, response } = await api.POST("/competitions/{id}/leagues", {
        params: { path: { id: competitionId! } },
        body: {
          name,
          tier: Number(tier),
          pointsForFirst: null,
          pointsDecrement: null,
          dropWorstCount: Number(dropWorstCount),
          absencesCountAsZero,
        },
      });
      if (error || !data) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? `Could not create league (${response.status}).`);
      }
      return data;
    },
    onSuccess: () => {
      setName("");
      setTier("");
      setDropWorstCount("0");
      setAbsencesCountAsZero(true);
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["leagues", competitionId] });
    },
    onError: (err: Error) => setError(err.message),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createLeague.mutate();
  }

  return (
    <div>
      <div className="mb-1 flex items-center justify-between gap-3">
        <h1 className="text-lg font-semibold">{competition ? competition.name : "Leagues"}</h1>
        <Button variant="outline" size="sm" disabled={downloading || !allStandingsLoaded} onClick={handleDownloadPdf}>
          {downloading ? "Generating…" : "Download PDF"}
        </Button>
      </div>
      <CompetitionTabs competitionId={competitionId!} />

      <form
        onSubmit={handleSubmit}
        className="mb-6 flex flex-wrap items-end gap-3 rounded-lg border border-border bg-background p-4"
      >
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="league-name">
            Name
          </label>
          <Input id="league-name" value={name} onChange={(e) => setName(e.target.value)} required className="w-56" />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="league-tier">
            Tier
          </label>
          <Input
            id="league-tier"
            type="number"
            min={1}
            value={tier}
            onChange={(e) => setTier(e.target.value)}
            required
            className="w-20"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="league-drop-worst">
            Drop worst N events
          </label>
          <Input
            id="league-drop-worst"
            type="number"
            min={0}
            value={dropWorstCount}
            onChange={(e) => setDropWorstCount(e.target.value)}
            required
            className="w-20"
          />
        </div>
        <label className="mb-2 flex items-center gap-2 text-sm text-muted-foreground">
          <input
            type="checkbox"
            checked={absencesCountAsZero}
            onChange={(e) => setAbsencesCountAsZero(e.target.checked)}
            className="h-4 w-4 rounded border-border"
          />
          Count absences as zero
        </label>
        <Button type="submit" disabled={createLeague.isPending}>
          {createLeague.isPending ? "Creating..." : "New league"}
        </Button>
      </form>

      {error && <p className="mb-4 text-sm text-destructive">{error}</p>}
      {leaguesQuery.isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {leaguesQuery.isError && <p className="text-sm text-destructive">Could not load leagues.</p>}

      {!leaguesQuery.isLoading && !leaguesQuery.isError && (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2 pr-4 font-medium text-muted-foreground">Tier</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Name</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Points (1st → step)</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Drop worst</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground" />
            </tr>
          </thead>
          <tbody>
            {(leaguesQuery.data ?? []).map((league) => (
              <tr key={league.id} className="border-b border-border">
                <td className="py-2 pr-4">{league.tier}</td>
                <td className="py-2 pr-4">{league.name}</td>
                <td className="py-2 pr-4">
                  {league.pointsForFirst} → −{league.pointsDecrement}
                </td>
                <td className="py-2 pr-4">
                  {league.dropWorstCount}
                  {league.absencesCountAsZero ? "" : " (no absence padding)"}
                </td>
                <td className="py-2 pr-4">
                  <div className="flex items-center gap-3">
                    <Link
                      to={`/competitions/${competitionId}/leagues/${league.id}`}
                      className="text-primary hover:underline"
                    >
                      Roster →
                    </Link>
                    <Link
                      to={`/competitions/${competitionId}/leagues/${league.id}/standings`}
                      className="text-primary hover:underline"
                    >
                      Standings →
                    </Link>
                  </div>
                </td>
              </tr>
            ))}
            {(leaguesQuery.data ?? []).length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-center text-muted-foreground">
                  No leagues yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
