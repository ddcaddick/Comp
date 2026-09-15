import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router";
import { api } from "../lib/api";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { CompetitionTabs } from "../components/layout/CompetitionTabs";

// Matches Comp.Domain.Enums.EventStatus — Finalised isn't reachable yet (that's M8).
const STATUS_SEQUENCE = ["Draft", "Setup", "InProgress", "Review"];

export function EventsPage() {
  const { competitionId } = useParams<{ competitionId: string }>();
  const queryClient = useQueryClient();
  const [eventNumber, setEventNumber] = useState("");
  const [name, setName] = useState("");
  const [eventDate, setEventDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  const competitionsQuery = useQuery({
    queryKey: ["competitions"],
    queryFn: async () => {
      const { data, error } = await api.GET("/competitions");
      if (error) throw new Error("Failed to load competitions");
      return data;
    },
  });
  const competition = competitionsQuery.data?.find((c) => c.id === competitionId);

  const eventsQuery = useQuery({
    queryKey: ["events", competitionId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events", { params: { query: { competitionId } } });
      if (error) throw new Error("Failed to load events");
      return data;
    },
    enabled: !!competitionId,
  });

  const createEvent = useMutation({
    mutationFn: async () => {
      const { data, error, response } = await api.POST("/events", {
        body: {
          competitionId: competitionId!,
          eventNumber: Number(eventNumber),
          name,
          eventDate,
          penaltySeconds: null,
          runsPerShooter: null,
          countsForStandings: null,
        },
      });
      if (error || !data) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? `Could not create event (${response.status}).`);
      }
      return data;
    },
    onSuccess: () => {
      setEventNumber("");
      setName("");
      setEventDate("");
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["events", competitionId] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const transitionEvent = useMutation({
    mutationFn: async ({ eventId, to }: { eventId: string; to: string }) => {
      const { error, response } = await api.POST("/events/{id}/transition", {
        params: { path: { id: eventId } },
        body: { to },
      });
      if (error) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? `Could not transition event (${response.status}).`);
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["events", competitionId] }),
    onError: (err: Error) => setError(err.message),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createEvent.mutate();
  }

  return (
    <div>
      <h1 className="mb-1 text-lg font-semibold">{competition ? competition.name : "Events"}</h1>
      <CompetitionTabs competitionId={competitionId!} />

      <form
        onSubmit={handleSubmit}
        className="mb-6 flex flex-wrap items-end gap-3 rounded-lg border border-border bg-background p-4"
      >
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="event-number">
            Event #
          </label>
          <Input
            id="event-number"
            type="number"
            value={eventNumber}
            onChange={(e) => setEventNumber(e.target.value)}
            required
            className="w-20"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="event-name">
            Name
          </label>
          <Input id="event-name" value={name} onChange={(e) => setName(e.target.value)} required className="w-56" />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="event-date">
            Date
          </label>
          <Input
            id="event-date"
            type="date"
            value={eventDate}
            onChange={(e) => setEventDate(e.target.value)}
            required
          />
        </div>
        <Button type="submit" disabled={createEvent.isPending}>
          {createEvent.isPending ? "Creating..." : "New event"}
        </Button>
      </form>

      {error && <p className="mb-4 text-sm text-destructive">{error}</p>}
      {eventsQuery.isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {eventsQuery.isError && <p className="text-sm text-destructive">Could not load events.</p>}

      {!eventsQuery.isLoading && !eventsQuery.isError && (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2 pr-4 font-medium text-muted-foreground">#</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Name</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Date</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground">Status</th>
              <th className="py-2 pr-4 font-medium text-muted-foreground" />
            </tr>
          </thead>
          <tbody>
            {(eventsQuery.data ?? []).map((event) => {
              const currentIndex = STATUS_SEQUENCE.indexOf(event.status);
              const nextStatus = currentIndex >= 0 ? STATUS_SEQUENCE[currentIndex + 1] : undefined;
              return (
                <tr key={event.id} className="border-b border-border">
                  <td className="py-2 pr-4">{event.eventNumber}</td>
                  <td className="py-2 pr-4">{event.name}</td>
                  <td className="py-2 pr-4">{event.eventDate}</td>
                  <td className="py-2 pr-4">{event.status}</td>
                  <td className="py-2 pr-4">
                    {nextStatus && (
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={transitionEvent.isPending}
                        onClick={() => transitionEvent.mutate({ eventId: event.id, to: nextStatus })}
                      >
                        Advance to {nextStatus}
                      </Button>
                    )}
                  </td>
                </tr>
              );
            })}
            {(eventsQuery.data ?? []).length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-center text-muted-foreground">
                  No events yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
