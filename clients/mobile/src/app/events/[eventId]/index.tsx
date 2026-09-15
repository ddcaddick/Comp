import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { api } from "@/lib/api";
import { colors, fonts } from "@/lib/theme";

function errorDetail(error: unknown, fallback: string): string {
  const detail = (error as { detail?: string | null } | undefined)?.detail;
  return detail ?? fallback;
}

// A heartbeat older than this is treated as a stale, no-longer-relevant session rather
// than someone actively entering right now.
const RECENT_SESSION_MINUTES = 10;

export default function SquadListScreen() {
  const { eventId, name } = useLocalSearchParams<{ eventId: string; name?: string }>();
  const router = useRouter();
  const queryClient = useQueryClient();
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

  const sessionQuery = useQuery({
    queryKey: ["entry-session", eventId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/entry-session", { params: { path: { id: eventId } } });
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

  const session = sessionQuery.data;
  const minutesAgo = session?.lastSeenAt
    ? Math.round((Date.now() - new Date(session.lastSeenAt).getTime()) / 60_000)
    : null;
  const showSessionBanner = session?.displayName && minutesAgo !== null && minutesAgo < RECENT_SESSION_MINUTES;

  const unassigned = (participantsQuery.data ?? []).filter((p) => !p.squadId);
  const squads = squadsQuery.data ?? [];

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ headerShown: true, title: name ?? "Squads" }} />

      {showSessionBanner && (
        <View style={styles.banner}>
          <Text style={styles.bannerText}>
            Last entered by {session!.displayName}, {minutesAgo === 0 ? "just now" : `${minutesAgo}m ago`}
          </Text>
        </View>
      )}

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
        data={squads}
        keyExtractor={(item) => item.id}
        contentContainerStyle={styles.list}
        ListHeaderComponent={
          unassigned.length > 0 ? (
            <View style={styles.unassignedSection}>
              <Text style={styles.sectionLabel}>Unassigned ({unassigned.length})</Text>
              {unassigned.map((participant) => (
                <View key={participant.id} style={styles.unassignedRow}>
                  <Text style={styles.unassignedName}>
                    {participant.firstName} {participant.lastName}
                  </Text>
                  <View style={styles.chipRow}>
                    {squads.map((squad) => (
                      <Pressable
                        key={squad.id}
                        style={styles.chip}
                        disabled={assignToSquad.isPending}
                        onPress={() => assignToSquad.mutate({ participantId: participant.id, squadId: squad.id })}
                      >
                        <Text style={styles.chipText}>{squad.name ?? `Squad ${squad.squadNumber}`}</Text>
                      </Pressable>
                    ))}
                    {squads.length === 0 && <Text style={styles.chipHint}>Add a squad first</Text>}
                  </View>
                </View>
              ))}
            </View>
          ) : null
        }
        renderItem={({ item }) => (
          <Pressable
            style={styles.row}
            onPress={() =>
              router.push({
                pathname: "/events/[eventId]/squads/[squadId]",
                params: { eventId, squadId: item.id },
              })
            }
          >
            <Text style={styles.rowTitle}>{item.name ?? `Squad ${item.squadNumber}`}</Text>
            <Text style={styles.rowSubtitle}>{item.status}</Text>
          </Pressable>
        )}
        ListEmptyComponent={
          !squadsQuery.isLoading ? <Text style={styles.empty}>No squads yet for this event.</Text> : null
        }
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingHorizontal: 20, paddingTop: 16 },
  banner: {
    backgroundColor: colors.errorBg,
    borderWidth: 1,
    borderColor: colors.accent,
    borderRadius: 8,
    padding: 12,
    marginBottom: 12,
  },
  bannerText: { fontFamily: fonts.body, color: colors.accent, fontSize: 13 },
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
  unassignedName: { fontFamily: fonts.semibold, fontSize: 15, color: colors.textPrimary },
  chipRow: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  chip: { paddingVertical: 6, paddingHorizontal: 12, borderRadius: 14, backgroundColor: colors.chipBg },
  chipText: { fontFamily: fonts.monoMedium, fontSize: 12, color: colors.accent },
  chipHint: { fontFamily: fonts.body, fontSize: 12, color: colors.textMuted },
  row: { borderWidth: 1, borderColor: colors.border, borderRadius: 10, padding: 16, backgroundColor: colors.surface },
  rowTitle: { fontFamily: fonts.semibold, fontSize: 16, color: colors.textPrimary },
  rowSubtitle: { fontFamily: fonts.body, fontSize: 13, color: colors.textSecondary, marginTop: 4 },
  error: { color: colors.errorText, fontFamily: fonts.body, marginTop: 16 },
  empty: { textAlign: "center", color: colors.textSecondary, fontFamily: fonts.body, marginTop: 40 },
});
