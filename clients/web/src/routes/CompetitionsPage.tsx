import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { api } from "../lib/api";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";

export function CompetitionsPage() {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [year, setYear] = useState(new Date().getFullYear().toString());
  const [startsOn, setStartsOn] = useState("");
  const [endsOn, setEndsOn] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data, isLoading, isError } = useQuery({
    queryKey: ["competitions"],
    queryFn: async () => {
      const { data, error } = await api.GET("/competitions");
      if (error) throw new Error("Failed to load competitions");
      return data;
    },
  });

  const createCompetition = useMutation({
    mutationFn: async () => {
      const { data, error, response } = await api.POST("/competitions", {
        body: { name, year: Number(year), startsOn, endsOn },
      });
      if (error || !data) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? `Could not create competition (${response.status}).`);
      }
      return data;
    },
    onSuccess: () => {
      setName("");
      setStartsOn("");
      setEndsOn("");
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["competitions"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createCompetition.mutate();
  }

  return (
    <div>
      <h1 className="mb-4 text-lg font-semibold">Competitions</h1>

      <form
        onSubmit={handleSubmit}
        className="mb-6 flex flex-wrap items-end gap-3 rounded-lg border border-border bg-background p-4"
      >
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="comp-name">
            Name
          </label>
          <Input id="comp-name" value={name} onChange={(e) => setName(e.target.value)} required className="w-56" />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="comp-year">
            Year
          </label>
          <Input
            id="comp-year"
            type="number"
            value={year}
            onChange={(e) => setYear(e.target.value)}
            required
            className="w-24"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="comp-starts">
            Starts on
          </label>
          <Input
            id="comp-starts"
            type="date"
            value={startsOn}
            onChange={(e) => setStartsOn(e.target.value)}
            required
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="comp-ends">
            Ends on
          </label>
          <Input id="comp-ends" type="date" value={endsOn} onChange={(e) => setEndsOn(e.target.value)} required />
        </div>
        <Button type="submit" disabled={createCompetition.isPending}>
          {createCompetition.isPending ? "Creating..." : "New competition"}
        </Button>
      </form>

      {error && <p className="mb-4 text-sm text-destructive">{error}</p>}
      {isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {isError && <p className="text-sm text-destructive">Could not load competitions.</p>}

      {!isLoading && !isError && (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2 pr-4 font-medium text-muted-foreground">Name</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Year</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Dates</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Status</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground" />
            </tr>
          </thead>
          <tbody>
            {(data ?? []).map((competition) => (
              <tr key={competition.id} className="border-b border-border">
                <td className="py-2 pr-4">{competition.name}</td>
                <td className="py-2 pr-4">{competition.year}</td>
                <td className="py-2 pr-4">
                  {competition.startsOn} – {competition.endsOn}
                </td>
                <td className="py-2 pr-4">{competition.status}</td>
                <td className="py-2 pr-4">
                  <div className="flex gap-3">
                    <Link to={`/competitions/${competition.id}/events`} className="text-primary hover:underline">
                      Events →
                    </Link>
                    <Link to={`/competitions/${competition.id}/leagues`} className="text-primary hover:underline">
                      Leagues →
                    </Link>
                  </div>
                </td>
              </tr>
            ))}
            {(data ?? []).length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-center text-muted-foreground">
                  No competitions yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
