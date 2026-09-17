import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { api } from "@/lib/api";
import { preferredName } from "@/lib/shooterName";
import { colors, fonts } from "@/lib/theme";

function errorDetail(error: unknown, fallback: string): string {
  const detail = (error as { detail?: string | null } | undefined)?.detail;
  return detail ?? fallback;
}

function formatArrivalTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

export default function SquadListScreen() {
  const { eventId, name } = useLocalSearchParams<{ eventId: string; name?: string }>();
  const router = useRouter();
  const queryClient = useQueryClient();
  const insets = useSafeAreaInsets();
  const [error, setError] = useState<string | null>(null);

  const squadsQuery = useQuery({
    queryKey: ["squads", eventId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/squads", { params: { path: { id: eventId } } });
      if (error) throw error;
      return data ?? [];
    },
  });

  const participantsQuery = useQuery({
    queryKey: ["participants", eventId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/participants", { params: { path: { id: eventId } } });
      if (error) throw error;
      return data ?? [];
    },
  });

  // Unfiltered (overall), same as the web results page's own overallResultsQuery -- only
  // used here for each participant's NotRun/Dnf/Ranked status, not to rank anyone.
  const resultsQuery = useQuery({
    queryKey: ["event-results", eventId, ""],
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/results", { params: { path: { id: eventId } } });
      if (error) throw error;
      return data ?? null;
    },
  });

  const createSquad = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/events/{id}/squads", {
        params: { path: { id: eventId } },
        body: { squadNumber: null, name: null },
      });
      if (error) throw new Error(errorDetail(error, "Could not add a squad."));
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["squads", eventId] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const assignToSquad = useMutation({
    mutationFn: async ({ participantId, squadId }: { participantId: string; squadId: string }) => {
      const { error } = await api.PATCH("/events/{id}/participants/{participantId}", {
        params: { path: { id: eventId, participantId } },
        body: { squadId, positionInSquad: null },
      });
      if (error) throw new Error(errorDetail(error, "Could not assign this shooter to a squad."));
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["participants", eventId] });
      queryClient.invalidateQueries({ queryKey: ["squads", eventId] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const completeSquad = useMutation({
    mutationFn: async (squadId: string) => {
      const { error } = await api.POST("/events/{id}/squads/{squadId}/complete", {
        params: { path: { id: eventId, squadId } },
      });
      if (error) throw new Error(errorDetail(error, "Could not complete this squad."));
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["squads", eventId] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const reopenSquad = useMutation({
    mutationFn: async (squadId: string) => {
      const { error } = await api.POST("/events/{id}/squads/{squadId}/reopen", {
        params: { path: { id: eventId, squadId } },
      });
      if (error) throw new Error(errorDetail(error, "Could not amend this squad."));
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["squads", eventId] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const participants = participantsQuery.data ?? [];
  // Earliest sign-on first -- shooters are meant to be allocated to squads in the order
  // they turned up, so the top of this list is who's been waiting longest.
  const unassigned = participants
    .filter((p) => !p.squadId)
    .sort((a, b) => new Date(a.addedAt).getTime() - new Date(b.addedAt).getTime());
  const squads = squadsQuery.data ?? [];
  const openSquads = squads.filter((s) => s.status !== "Allocated");

  const participantCountBySquad = new Map<string, number>();
  for (const p of participants) {
    if (p.squadId) {
      participantCountBySquad.set(p.squadId, (participantCountBySquad.get(p.squadId) ?? 0) + 1);
    }
  }

  // NotRun means literally zero runs recorded yet (see Comp.Scoring's DNF/NotRun split) --
  // everything else (Ranked or Dnf) means at least one run, valid or not, has been entered.
  const resultParticipants = resultsQuery.data?.participants ?? [];
  const shotCount = resultParticipants.filter((p) => p.status !== "NotRun").length;
  const toShootCount = resultParticipants.filter((p) => p.status === "NotRun").length;

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ headerShown: true, title: name ?? "Squads" }} />

      <View style={styles.statsBar}>
        <View style={styles.statItem}>
          <Text style={styles.statValue}>{shotCount}</Text>
          <Text style={styles.statLabel}>Shot</Text>
        </View>
        <View style={styles.statDivider} />
        <View style={styles.statItem}>
          <Text style={styles.statValue}>{toShootCount}</Text>
          <Text style={styles.statLabel}>To Shoot</Text>
        </View>
        <View style={styles.statDivider} />
        <View style={styles.statItem}>
          <Text style={styles.statValue}>{unassigned.length}</Text>
          <Text style={styles.statLabel}>To Assign</Text>
        </View>
      </View>

      <View style={styles.actionRow}>
        <Pressable
          style={styles.actionButton}
          onPress={() => router.push({ pathname: "/events/[eventId]/add-participant", params: { eventId } })}
        >
          <Text style={styles.actionButtonText}>+ Shooter</Text>
        </Pressable>
        <Pressable
          style={styles.actionButton}
          disabled={createSquad.isPending}
          onPress={() => createSquad.mutate()}
        >
          <Text style={styles.actionButtonText}>{createSquad.isPending ? "Adding..." : "+ Squad"}</Text>
        </Pressable>
      </View>

      {error && <Text style={styles.error}>{error}</Text>}
      {squadsQuery.isLoading && <ActivityIndicator color={colors.accent} style={styles.spinner} />}
      {squadsQuery.isError && <Text style={styles.error}>Could not load squads.</Text>}

      <FlatList
        style={styles.listContainer}
        data={squads}
        keyExtractor={(item) => item.id}
        contentContainerStyle={[styles.list, { paddingBottom: 24 + insets.bottom }]}
        ListHeaderComponent={
          unassigned.length > 0 ? (
            <View style={styles.unassignedSection}>
              <Text style={styles.sectionLabel}>Unassigned</Text>
              {unassigned.map((participant) => (
                <View key={participant.id} style={styles.unassignedRow}>
                  <View style={styles.unassignedHeader}>
                    <Text style={styles.unassignedName}>{preferredName(participant)}</Text>
                    <Text style={styles.unassignedTime}>{formatArrivalTime(participant.addedAt)}</Text>
                  </View>
                  <View style={styles.chipRow}>
                    {openSquads.map((squad) => (
                      <Pressable
                        key={squad.id}
                        style={styles.chip}
                        disabled={assignToSquad.isPending}
                        onPress={() => assignToSquad.mutate({ participantId: participant.id, squadId: squad.id })}
                      >
                        <Text style={styles.chipText}>{squad.name ?? `Squad ${squad.squadNumber}`}</Text>
                      </Pressable>
                    ))}
                    {openSquads.length === 0 && <Text style={styles.chipHint}>Add a squad first</Text>}
                  </View>
                </View>
              ))}
            </View>
          ) : null
        }
        renderItem={({ item }) => {
          const isAllocated = item.status === "Allocated";
          const count = participantCountBySquad.get(item.id) ?? 0;
          return (
            <View style={[styles.row, isAllocated && styles.rowAllocated]}>
              <Pressable
                style={styles.rowMain}
                onPress={() =>
                  router.push({
                    pathname: "/events/[eventId]/squads/[squadId]",
                    params: { eventId, squadId: item.id },
                  })
                }
              >
                <Text style={styles.rowTitle}>{item.name ?? `Squad ${item.squadNumber}`}</Text>
                <Text style={styles.rowSubtitle}>
                  {count} shooter{count === 1 ? "" : "s"} · {isAllocated ? "Allocated" : "Pending"}
                </Text>
              </Pressable>
              {isAllocated ? (
                <Pressable
                  style={styles.amendButton}
                  disabled={reopenSquad.isPending}
                  onPress={() => reopenSquad.mutate(item.id)}
                >
                  <Text style={styles.amendButtonText}>Amend</Text>
                </Pressable>
              ) : (
                <Pressable
                  style={styles.completeButton}
                  disabled={completeSquad.isPending}
                  onPress={() => completeSquad.mutate(item.id)}
                >
                  <Text style={styles.completeButtonText}>Complete</Text>
                </Pressable>
              )}
            </View>
          );
        }}
        ListEmptyComponent={
          !squadsQuery.isLoading ? <Text style={styles.empty}>No squads yet for this event.</Text> : null
        }
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingHorizontal: 20, paddingTop: 16 },
  statsBar: {
    flexDirection: "row",
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 10,
    paddingVertical: 10,
    marginBottom: 12,
  },
  statItem: { flex: 1, alignItems: "center" },
  statDivider: { width: 1, backgroundColor: colors.border },
  statValue: { fontFamily: fonts.extrabold, fontSize: 20, color: colors.accent },
  statLabel: {
    fontFamily: fonts.monoBold,
    fontSize: 10,
    letterSpacing: 0.75,
    textTransform: "uppercase",
    color: colors.textMuted,
    marginTop: 2,
  },
  actionRow: { flexDirection: "row", gap: 10, marginBottom: 12 },
  actionButton: {
    flex: 1,
    height: 42,
    borderRadius: 9,
    borderWidth: 1,
    borderColor: colors.accent,
    alignItems: "center",
    justifyContent: "center",
  },
  actionButtonText: { fontFamily: fonts.bold, fontSize: 13, color: colors.accent },
  spinner: { marginTop: 24 },
  listContainer: { flex: 1 },
  list: { gap: 8, paddingBottom: 24 },
  unassignedSection: { marginBottom: 16, gap: 8 },
  sectionLabel: {
    fontFamily: fonts.monoBold,
    fontSize: 11,
    letterSpacing: 1.5,
    textTransform: "uppercase",
    color: colors.textMuted,
    marginBottom: 4,
  },
  unassignedRow: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 10,
    padding: 12,
    backgroundColor: colors.surface,
    gap: 8,
  },
  unassignedHeader: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  unassignedName: { fontFamily: fonts.semibold, fontSize: 15, color: colors.textPrimary },
  unassignedTime: { fontFamily: fonts.monoMedium, fontSize: 11, color: colors.textMuted },
  chipRow: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  chip: { paddingVertical: 6, paddingHorizontal: 12, borderRadius: 14, backgroundColor: colors.chipBg },
  chipText: { fontFamily: fonts.monoMedium, fontSize: 12, color: colors.accent },
  chipHint: { fontFamily: fonts.body, fontSize: 12, color: colors.textMuted },
  row: {
    flexDirection: "row",
    alignItems: "center",
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 10,
    backgroundColor: colors.surface,
  },
  rowAllocated: { backgroundColor: colors.successBg, borderColor: colors.successBorder },
  rowMain: { flex: 1, padding: 16 },
  rowTitle: { fontFamily: fonts.semibold, fontSize: 16, color: colors.textPrimary },
  rowSubtitle: { fontFamily: fonts.body, fontSize: 13, color: colors.textSecondary, marginTop: 4 },
  completeButton: {
    marginRight: 14,
    minWidth: 92,
    alignItems: "center",
    justifyContent: "center",
    paddingVertical: 7,
    paddingHorizontal: 12,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: colors.warning,
  },
  completeButtonText: { fontFamily: fonts.monoBold, fontSize: 11, color: colors.warning },
  amendButton: {
    marginRight: 14,
    minWidth: 92,
    alignItems: "center",
    justifyContent: "center",
    paddingVertical: 7,
    paddingHorizontal: 12,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: colors.error,
  },
  amendButtonText: { fontFamily: fonts.monoBold, fontSize: 11, color: colors.error },
  error: { color: colors.errorText, fontFamily: fonts.body, marginTop: 16 },
  empty: { textAlign: "center", color: colors.textSecondary, fontFamily: fonts.body, marginTop: 40 },
});
