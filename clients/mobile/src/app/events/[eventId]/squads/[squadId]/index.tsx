import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { formatMillis } from "@comp/core";
import type { components } from "@comp/api-types";
import { api } from "@/lib/api";
import { preferredName } from "@/lib/shooterName";
import { findInitialOutstanding } from "@/lib/squadRunner";
import { colors, fonts } from "@/lib/theme";

type RunState = components["schemas"]["RunnerRunState"];

export function runnerQueryKey(eventId: string, squadId: string) {
  return ["runner", eventId, squadId] as const;
}

export default function SquadRunnerScreen() {
  const { eventId, squadId } = useLocalSearchParams<{ eventId: string; squadId: string }>();
  const router = useRouter();

  const runnerQuery = useQuery({
    queryKey: runnerQueryKey(eventId, squadId),
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/squads/{squadId}/runner", {
        params: { path: { id: eventId, squadId } },
      });
      if (error) throw error;
      return data ?? null;
    },
  });

  useEffect(() => {
    api.POST("/events/{id}/entry-session/heartbeat", { params: { path: { id: eventId } } });
  }, [eventId]);

  if (runnerQuery.isLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator color={colors.accent} />
      </View>
    );
  }

  if (runnerQuery.isError || !runnerQuery.data) {
    return (
      <View style={styles.center}>
        <Text style={styles.error}>Could not load this squad.</Text>
      </View>
    );
  }

  const runner = runnerQuery.data;
  const nextUp = findInitialOutstanding(runner.participants);

  function goToEntry(
    participantId: string,
    firstName: string,
    lastName: string,
    nickname: string | null,
    run: RunState,
  ) {
    router.push({
      pathname: "/events/[eventId]/squads/[squadId]/entry",
      params: {
        eventId,
        squadId,
        participantId,
        runNumber: String(run.runNumber),
        firstName,
        lastName,
        nickname: nickname ?? "",
        existingRawTimeMs: run.rawTimeMs === null ? "" : String(run.rawTimeMs),
        existingPenaltyCount: String(run.penaltyCount),
        existingIsDnf: String(run.isDnf),
        existingIsRecorded: String(run.isRecorded),
      },
    });
  }

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ headerShown: true, title: runner.squadName ?? `Squad ${runner.squadNumber}` }} />

      {nextUp && (
        <Pressable
          style={styles.nextUpCard}
          onPress={() =>
            goToEntry(
              nextUp.participant.participantId,
              nextUp.participant.firstName,
              nextUp.participant.lastName,
              nextUp.participant.nickname,
              nextUp.participant.runs[nextUp.runNumber - 1],
            )
          }
        >
          <Text style={styles.nextUpLabel}>Next up · Run {nextUp.runNumber}</Text>
          <Text style={styles.nextUpName}>{preferredName(nextUp.participant)}</Text>
          <Text style={styles.nextUpAction}>Enter time →</Text>
        </Pressable>
      )}
      {!nextUp && (
        <View style={styles.doneCard}>
          <Text style={styles.doneText}>Every run in this squad is recorded.</Text>
        </View>
      )}

      <FlatList
        data={runner.participants}
        keyExtractor={(item) => item.participantId}
        contentContainerStyle={styles.list}
        renderItem={({ item }) => (
          <View style={styles.row}>
            <Text style={styles.rowName}>{preferredName(item)}</Text>
            <View style={styles.runCells}>
              {item.runs.map((run) => (
                <Pressable
                  key={run.runNumber}
                  style={[styles.runCell, run.isRecorded && styles.runCellRecorded]}
                  onPress={() => goToEntry(item.participantId, item.firstName, item.lastName, item.nickname, run)}
                >
                  <Text style={styles.runCellText}>
                    {run.isDnf ? "DNF" : run.isRecorded ? formatMillis(Number(run.rawTimeMs)) : `R${run.runNumber}`}
                  </Text>
                </Pressable>
              ))}
            </View>
          </View>
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingHorizontal: 20, paddingTop: 16 },
  center: { flex: 1, justifyContent: "center", alignItems: "center", backgroundColor: colors.background },
  error: { color: colors.errorText, fontFamily: fonts.body },
  nextUpCard: {
    backgroundColor: colors.accent,
    borderRadius: 12,
    padding: 20,
    marginBottom: 16,
  },
  nextUpLabel: {
    color: colors.accentText,
    fontFamily: fonts.monoBold,
    fontSize: 11,
    letterSpacing: 1.5,
    textTransform: "uppercase",
    opacity: 0.75,
  },
  nextUpName: { color: colors.accentText, fontFamily: fonts.extrabold, fontSize: 28, marginTop: 4 },
  nextUpAction: { color: colors.accentText, fontFamily: fonts.semibold, fontSize: 15, marginTop: 8, opacity: 0.85 },
  doneCard: { backgroundColor: colors.successBg, borderWidth: 1, borderColor: colors.successBorder, borderRadius: 12, padding: 20, marginBottom: 16 },
  doneText: { color: colors.success, fontFamily: fonts.semibold, fontSize: 16, textAlign: "center" },
  list: { gap: 8, paddingBottom: 24 },
  row: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 10,
    padding: 14,
    backgroundColor: colors.surface,
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  rowName: { fontFamily: fonts.semibold, fontSize: 16, color: colors.textPrimary, flex: 1 },
  runCells: { flexDirection: "row", gap: 8 },
  runCell: {
    minWidth: 64,
    paddingVertical: 8,
    paddingHorizontal: 10,
    borderRadius: 8,
    backgroundColor: colors.inputBg,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: "center",
  },
  runCellRecorded: { backgroundColor: colors.chipBg, borderColor: colors.accent },
  runCellText: { fontFamily: fonts.monoMedium, fontSize: 13, color: colors.textPrimary },
});
