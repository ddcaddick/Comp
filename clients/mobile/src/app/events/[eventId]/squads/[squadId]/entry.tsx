import { useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import * as Crypto from "expo-crypto";
import * as Haptics from "expo-haptics";
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { digitsToMillis, formatMillis } from "@comp/core";
import type { components } from "@comp/api-types";
import { api } from "@/lib/api";
import { preferredName } from "@/lib/shooterName";
import { findInitialOutstanding } from "@/lib/squadRunner";
import { colors, fonts } from "@/lib/theme";
import { runnerQueryKey } from "./index";

type SaveRunRequest = components["schemas"]["SaveRunRequest"];

const MAX_PENALTIES = 20;
const KEYPAD_ROWS = [
  ["1", "2", "3"],
  ["4", "5", "6"],
  ["7", "8", "9"],
  ["", "0", "back"],
];

export default function EntryScreen() {
  const params = useLocalSearchParams<{
    eventId: string;
    squadId: string;
    participantId: string;
    runNumber: string;
    firstName: string;
    lastName: string;
    nickname?: string;
    existingRawTimeMs?: string;
    existingPenaltyCount?: string;
    existingIsDnf?: string;
    existingIsRecorded?: string;
  }>();
  const router = useRouter();
  const queryClient = useQueryClient();
  const insets = useSafeAreaInsets();

  const runNumber = Number(params.runNumber);
  const displayName = preferredName({ firstName: params.firstName, lastName: params.lastName, nickname: params.nickname });
  const wasRecorded = params.existingIsRecorded === "true";
  const previousSummary = wasRecorded
    ? params.existingIsDnf === "true"
      ? "DNF"
      : formatMillis(Number(params.existingRawTimeMs))
    : null;

  const [digits, setDigits] = useState("");
  const [penaltyCount, setPenaltyCount] = useState(Number(params.existingPenaltyCount ?? 0));
  const [isDnf, setIsDnf] = useState(params.existingIsDnf === "true");
  const [saveError, setSaveError] = useState<string | null>(null);
  const [lastSaved, setLastSaved] = useState<string | null>(null);

  const lastAttemptRef = useRef<{ key: string; body: SaveRunRequest } | null>(null);

  function idempotencyKeyFor(body: SaveRunRequest): string {
    const last = lastAttemptRef.current;
    if (last && last.body.rawTimeMs === body.rawTimeMs && last.body.penaltyCount === body.penaltyCount && last.body.isDnf === body.isDnf) {
      return last.key;
    }
    const key = Crypto.randomUUID();
    lastAttemptRef.current = { key, body };
    return key;
  }

  const saveMutation = useMutation({
    mutationFn: async (body: SaveRunRequest) => {
      const idempotencyKey = idempotencyKeyFor(body);
      const { data, error } = await api.PUT("/events/{id}/participants/{participantId}/runs/{runNumber}", {
        params: {
          path: { id: params.eventId, participantId: params.participantId, runNumber },
          header: { "Idempotency-Key": idempotencyKey },
        },
        body,
      });
      if (error) throw error;
      return data!;
    },
    onSuccess: (data) => {
      setSaveError(null);
      setLastSaved(data.savedRun.isDnf ? "DNF" : formatMillis(Number(data.savedRun.rawTimeMs)));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.invalidateQueries({ queryKey: runnerQueryKey(params.eventId, params.squadId) });

      const next = data.nextOutstanding ? findInitialOutstanding([data.nextOutstanding]) : null;
      if (next) {
        router.replace({
          pathname: "/events/[eventId]/squads/[squadId]/entry",
          params: {
            eventId: params.eventId,
            squadId: params.squadId,
            participantId: next.participant.participantId,
            runNumber: String(next.runNumber),
            firstName: next.participant.firstName,
            lastName: next.participant.lastName,
            nickname: next.participant.nickname ?? "",
            existingRawTimeMs: "",
            existingPenaltyCount: "0",
            existingIsDnf: "false",
            existingIsRecorded: "false",
          },
        });
      } else {
        router.replace({
          pathname: "/events/[eventId]/squads/[squadId]",
          params: { eventId: params.eventId, squadId: params.squadId },
        });
      }
    },
    onError: (error: unknown) => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      const detail = (error as { detail?: string | null } | undefined)?.detail;
      setSaveError(detail ?? "Save failed — check your connection and try again.");
    },
  });

  function buildRequestBody(): SaveRunRequest {
    return {
      rawTimeMs: isDnf ? null : digitsToMillis(digits),
      penaltyCount,
      isDnf,
    };
  }

  function handleSave() {
    const body = buildRequestBody();
    if (wasRecorded) {
      Alert.alert(
        "Overwrite recorded time?",
        `${displayName}'s run ${runNumber} is currently ${previousSummary}.`,
        [
          { text: "Cancel", style: "cancel" },
          { text: "Overwrite", style: "destructive", onPress: () => saveMutation.mutate(body) },
        ],
      );
      return;
    }
    saveMutation.mutate(body);
  }

  function handleToggleDnf() {
    if (isDnf) {
      setIsDnf(false);
      return;
    }
    Alert.alert(
      "Mark as DNF?",
      `This clears any time entered for ${displayName}'s run ${runNumber}.`,
      [
        { text: "Cancel", style: "cancel" },
        {
          text: "Mark DNF",
          style: "destructive",
          onPress: () => {
            setDigits("");
            setIsDnf(true);
          },
        },
      ],
    );
  }

  function handleKeyPress(key: string) {
    if (isDnf) return;
    if (key === "back") {
      setDigits((current) => current.slice(0, -1));
      return;
    }
    if (key === "") return;
    setDigits((current) => (current.length >= 6 ? current : current + key));
  }

  const canSave = isDnf || digits.length > 0;
  const displayTime = isDnf ? "DNF" : formatMillis(digitsToMillis(digits));

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ headerShown: true, title: `Run ${runNumber}` }} />

      <ScrollView contentContainerStyle={styles.scrollContent} showsVerticalScrollIndicator={false}>
        <Text style={styles.name}>{displayName}</Text>
        {previousSummary && <Text style={styles.previous}>Previously: {previousSummary}</Text>}

        <Text style={[styles.timeDisplay, isDnf && styles.timeDisplayDnf]}>{displayTime}</Text>

        <Pressable style={[styles.dnfButton, isDnf && styles.dnfButtonActive]} onPress={handleToggleDnf}>
          <Text style={[styles.dnfButtonText, isDnf && styles.dnfButtonTextActive]}>
            {isDnf ? "DNF — tap to undo" : "Mark DNF"}
          </Text>
        </Pressable>

        <View style={styles.penaltyRow}>
          <Pressable
            style={styles.penaltyButton}
            disabled={penaltyCount === 0}
            onPress={() => setPenaltyCount((count) => Math.max(0, count - 1))}
          >
            <Text style={styles.penaltyButtonText}>−</Text>
          </Pressable>
          <View style={styles.penaltyCountBox}>
            <Text style={styles.penaltyCount}>{penaltyCount}</Text>
            <Text style={styles.penaltyLabel}>penalties</Text>
          </View>
          <Pressable
            style={styles.penaltyButton}
            disabled={penaltyCount === MAX_PENALTIES}
            onPress={() => setPenaltyCount((count) => Math.min(MAX_PENALTIES, count + 1))}
          >
            <Text style={styles.penaltyButtonText}>+</Text>
          </Pressable>
          <Pressable
            style={styles.penaltyChip}
            disabled={penaltyCount === MAX_PENALTIES}
            onPress={() => setPenaltyCount((count) => Math.min(MAX_PENALTIES, count + 5))}
          >
            <Text style={styles.penaltyChipText}>+5</Text>
          </Pressable>
        </View>

        <View style={styles.keypad}>
          {KEYPAD_ROWS.map((row, rowIndex) => (
            <View key={rowIndex} style={styles.keypadRow}>
              {row.map((key, keyIndex) =>
                key === "" ? (
                  <View key={keyIndex} style={styles.keypadKey} />
                ) : (
                  <Pressable
                    key={keyIndex}
                    style={styles.keypadKey}
                    disabled={isDnf}
                    onPress={() => handleKeyPress(key)}
                  >
                    <Text style={styles.keypadKeyText}>{key === "back" ? "⌫" : key}</Text>
                  </Pressable>
                ),
              )}
            </View>
          ))}
        </View>

        {saveError && <Text style={styles.error}>{saveError}</Text>}
        {lastSaved && !saveError && <Text style={styles.lastSaved}>Saved {lastSaved} ✓</Text>}

        <Pressable
          style={[styles.saveButton, { marginBottom: 24 + insets.bottom }, !canSave && styles.saveButtonDisabled]}
          disabled={!canSave || saveMutation.isPending}
          onPress={handleSave}
        >
          <Text style={styles.saveButtonText}>{saveMutation.isPending ? "Saving…" : "Save"}</Text>
        </Pressable>
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  scrollContent: { flexGrow: 1, paddingHorizontal: 20, paddingTop: 16 },
  name: { fontFamily: fonts.extrabold, fontSize: 30, textAlign: "center", color: colors.textPrimary },
  previous: { textAlign: "center", fontFamily: fonts.body, color: colors.textSecondary, marginTop: 4 },
  timeDisplay: {
    fontFamily: fonts.monoBold,
    fontSize: 56,
    textAlign: "center",
    marginVertical: 20,
    color: colors.textPrimary,
    fontVariant: ["tabular-nums"],
  },
  timeDisplayDnf: { color: colors.error },
  dnfButton: {
    alignSelf: "center",
    borderWidth: 1,
    borderColor: colors.error,
    borderRadius: 20,
    paddingVertical: 8,
    paddingHorizontal: 20,
    marginBottom: 20,
  },
  dnfButtonActive: { backgroundColor: colors.error },
  dnfButtonText: { color: colors.error, fontFamily: fonts.semibold },
  dnfButtonTextActive: { color: colors.textPrimary },
  penaltyRow: { flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 12, marginBottom: 20 },
  penaltyButton: {
    width: 48,
    height: 48,
    borderRadius: 24,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: "center",
    justifyContent: "center",
  },
  penaltyButtonText: { fontFamily: fonts.bold, fontSize: 26, color: colors.textPrimary },
  penaltyCountBox: { alignItems: "center", minWidth: 70 },
  penaltyCount: { fontFamily: fonts.extrabold, fontSize: 28, color: colors.textPrimary },
  penaltyLabel: { fontFamily: fonts.monoMedium, fontSize: 11, color: colors.textSecondary },
  penaltyChip: {
    backgroundColor: colors.chipBg,
    borderWidth: 1,
    borderColor: colors.accent,
    borderRadius: 16,
    paddingVertical: 8,
    paddingHorizontal: 14,
  },
  penaltyChipText: { fontFamily: fonts.bold, color: colors.accent },
  keypad: { gap: 10 },
  keypadRow: { flexDirection: "row", gap: 10 },
  keypadKey: {
    flex: 1,
    height: 56,
    borderRadius: 10,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: "center",
    justifyContent: "center",
  },
  keypadKeyText: { fontFamily: fonts.semibold, fontSize: 22, color: colors.textPrimary },
  error: { color: colors.errorText, fontFamily: fonts.body, textAlign: "center", marginTop: 16 },
  lastSaved: { color: colors.success, fontFamily: fonts.body, textAlign: "center", marginTop: 16 },
  saveButton: {
    backgroundColor: colors.accent,
    borderRadius: 10,
    paddingVertical: 16,
    marginTop: 20,
    alignItems: "center",
  },
  saveButtonDisabled: { backgroundColor: colors.borderStrong },
  saveButtonText: { color: colors.accentText, fontFamily: fonts.bold, fontSize: 17 },
});
