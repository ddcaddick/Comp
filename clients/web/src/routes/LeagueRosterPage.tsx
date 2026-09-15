import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { api } from "../lib/api";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { CompetitionTabs } from "../components/layout/CompetitionTabs";

const MAX_MEMBERS = 20;

export function LeagueRosterPage() {
  const { competitionId, leagueId } = useParams<{ competitionId: string; leagueId: string }>();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [error, setError] = useState<string | null>(null);

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
  const league = leaguesQuery.data?.find((l) => l.id === leagueId);

  const membersQuery = useQuery({
    queryKey: ["league-members", leagueId],
    queryFn: async () => {
      const { data, error } = await api.GET("/leagues/{id}/members", { params: { path: { id: leagueId! } } });
      if (error) throw new Error("Failed to load roster");
      return data;
    },
    enabled: !!leagueId,
  });
  const memberIds = new Set((membersQuery.data ?? []).map((m) => m.shooterId));

  const searchQuery = useQuery({
    queryKey: ["shooters", search],
    queryFn: async () => {
      const { data, error } = await api.GET("/shooters", { params: { query: { q: search, active: true } } });
      if (error) throw new Error("Failed to search shooters");
      return data ?? [];
    },
    enabled: search.trim().length > 0,
  });

  const setMembers = useMutation({
    mutationFn: async (shooterIds: string[]) => {
      const { data, error, response } = await api.PUT("/leagues/{id}/members", {
        params: { path: { id: leagueId! } },
        body: { shooterIds },
      });
      if (error || !data) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? `Could not update roster (${response.status}).`);
      }
      return data;
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["league-members", leagueId] });
    },
    onError: (err: Error) => setError(err.message),
  });

  function addShooter(shooterId: string) {
    setMembers.mutate([...memberIds, shooterId]);
  }

  function removeShooter(shooterId: string) {
    setMembers.mutate([...memberIds].filter((id) => id !== shooterId));
  }

  const memberCount = membersQuery.data?.length ?? 0;

  return (
    <div>
      <h1 className="mb-1 text-lg font-semibold">{league ? league.name : "Roster"}</h1>
      <CompetitionTabs competitionId={competitionId!} />
      <p className="mb-4 text-sm">
        <Link to={`/competitions/${competitionId}/leagues`} className="text-primary hover:underline">
          ← All leagues
        </Link>
        <span className="text-muted-foreground">
          {" "}
          · {league ? `Tier ${league.tier}` : "League"} · {memberCount}/{MAX_MEMBERS} shooters ·{" "}
        </span>
        <Link to={`/competitions/${competitionId}/leagues/${leagueId}/standings`} className="text-primary hover:underline">
          Standings →
        </Link>
      </p>

      <div className="mb-6 rounded-lg border border-border bg-background p-4">
        <label className="mb-1 block text-xs font-medium text-muted-foreground" htmlFor="shooter-search">
          Add a shooter
        </label>
        <Input
          id="shooter-search"
          placeholder="Search by name or nickname..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="max-w-sm"
        />

        {search.trim().length > 0 && (
          <div className="mt-3 flex flex-col gap-1">
            {searchQuery.isLoading && <p className="text-sm text-muted-foreground">Searching...</p>}
            {(searchQuery.data ?? []).map((shooter) => {
              const alreadyMember = memberIds.has(shooter.id);
              return (
                <div key={shooter.id} className="flex items-center justify-between rounded-md px-2 py-1.5 text-sm">
                  <span>
                    {shooter.firstName} {shooter.lastName}
                  </span>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={alreadyMember || memberCount >= MAX_MEMBERS || setMembers.isPending}
                    onClick={() => addShooter(shooter.id)}
                  >
                    {alreadyMember ? "Already in league" : "Add"}
                  </Button>
                </div>
              );
            })}
            {!searchQuery.isLoading && (searchQuery.data ?? []).length === 0 && (
              <p className="text-sm text-muted-foreground">No matching shooters.</p>
            )}
          </div>
        )}
      </div>

      {error && <p className="mb-4 text-sm text-destructive">{error}</p>}
      {membersQuery.isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {membersQuery.isError && <p className="text-sm text-destructive">Could not load roster.</p>}

      {!membersQuery.isLoading && !membersQuery.isError && (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2 pr-4 font-medium text-muted-foreground">First name</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Last name</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground" />
            </tr>
          </thead>
          <tbody>
            {(membersQuery.data ?? []).map((member) => (
              <tr key={member.shooterId} className="border-b border-border">
                <td className="py-2 pr-4">{member.firstName}</td>
                <td className="py-2 pr-4">{member.lastName}</td>
                <td className="py-2 pr-4">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={setMembers.isPending}
                    onClick={() => removeShooter(member.shooterId)}
                  >
                    Remove
                  </Button>
                </td>
              </tr>
            ))}
            {(membersQuery.data ?? []).length === 0 && (
              <tr>
                <td colSpan={3} className="py-4 text-center text-muted-foreground">
                  No shooters in this league yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
