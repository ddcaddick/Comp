import { useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import * as Crypto from "expo-crypto";
import * as Haptics from "expo-haptics";
import { Alert, Pressable, StyleSheet, Text, View } from "react-native";
import { digitsToMillis, formatMillis } from "@comp/core";
import type { components } from "@comp/api-types";
import { api } from "@/lib/api";
import { findInitialOutstanding } from "@/lib/squadRunner";
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
    existingRawTimeMs?: string;
    existingPenaltyCount?: string;
    existingIsDnf?: string;
    existingIsRecorded?: string;
  }>();
  const router = useRouter();
  const queryClient = useQueryClient();

  const runNumber = Number(params.runNumber);
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
        `${params.firstName} ${params.lastName}'s run ${runNumber} is currently ${previousSummary}.`,
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
      `This clears any time entered for ${params.firstName} ${params.lastName}'s run ${runNumber}.`,
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

      <Text style={styles.name}>
        {params.firstName} {params.lastName}
      </Text>
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
        style={[styles.saveButton, !canSave && styles.saveButtonDisabled]}
        disabled={!canSave || saveMutation.isPending}
        onPress={handleSave}
      >
        <Text style={styles.saveButtonText}>{saveMutation.isPending ? "Saving…" : "Save"}</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, paddingHorizontal: 20, paddingTop: 16 },
  name: { fontSize: 30, fontWeight: "700", textAlign: "center" },
  previous: { textAlign: "center", color: "#666", marginTop: 4 },
  timeDisplay: { fontSize: 56, fontWeight: "700", textAlign: "center", marginVertical: 20, fontVariant: ["tabular-nums"] },
  timeDisplayDnf: { color: "#c0392b" },
  dnfButton: {
    alignSelf: "center",
    borderWidth: 1,
    borderColor: "#c0392b",
    borderRadius: 20,
    paddingVertical: 8,
    paddingHorizontal: 20,
    marginBottom: 20,
  },
  dnfButtonActive: { backgroundColor: "#c0392b" },
  dnfButtonText: { color: "#c0392b", fontWeight: "600" },
  dnfButtonTextActive: { color: "#fff" },
  penaltyRow: { flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 12, marginBottom: 20 },
  penaltyButton: {
    width: 48,
    height: 48,
    borderRadius: 24,
    backgroundColor: "#f0f0f0",
    alignItems: "center",
    justifyContent: "center",
  },
  penaltyButtonText: { fontSize: 26, fontWeight: "700" },
  penaltyCountBox: { alignItems: "center", minWidth: 70 },
  penaltyCount: { fontSize: 28, fontWeight: "700" },
  penaltyLabel: { fontSize: 12, color: "#666" },
  penaltyChip: {
    backgroundColor: "#FFE8CC",
    borderRadius: 16,
    paddingVertical: 8,
    paddingHorizontal: 14,
  },
  penaltyChipText: { fontWeight: "700", color: "#8A5300" },
  keypad: { gap: 10 },
  keypadRow: { flexDirection: "row", gap: 10 },
  keypadKey: {
    flex: 1,
    height: 56,
    borderRadius: 10,
    backgroundColor: "#f0f0f0",
    alignItems: "center",
    justifyContent: "center",
  },
  keypadKeyText: { fontSize: 22, fontWeight: "600" },
  error: { color: "#c0392b", textAlign: "center", marginTop: 16 },
  lastSaved: { color: "#256029", textAlign: "center", marginTop: 16 },
  saveButton: {
    backgroundColor: "#208AEF",
    borderRadius: 10,
    paddingVertical: 16,
    marginTop: 20,
    marginBottom: 24,
    alignItems: "center",
  },
  saveButtonDisabled: { backgroundColor: "#a9d0f5" },
  saveButtonText: { color: "#fff", fontSize: 17, fontWeight: "700" },
});
