import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import { api } from "@/lib/api";
import { colors, fonts } from "@/lib/theme";

export default function AddParticipantScreen() {
  const { eventId } = useLocalSearchParams<{ eventId: string }>();
  const router = useRouter();
  const queryClient = useQueryClient();

  const [search, setSearch] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [error, setError] = useState<string | null>(null);

  const shootersQuery = useQuery({
    queryKey: ["shooters", search],
    queryFn: async () => {
      const { data, error } = await api.GET("/shooters", { params: { query: { q: search, active: true } } });
      if (error) throw error;
      return data ?? [];
    },
  });

  function addParticipant(shooterId: string) {
    addMutation.mutate(shooterId);
  }

  const addMutation = useMutation({
    mutationFn: async (shooterId: string) => {
      const { error } = await api.POST("/events/{id}/participants", {
        params: { path: { id: eventId } },
        body: { shooterId, squadId: null },
      });
      if (error) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? "Could not add this shooter to the event.");
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["participants", eventId] });
      router.back();
    },
    onError: (err: Error) => setError(err.message),
  });

  const createAndAddMutation = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/shooters", {
        body: { firstName, lastName, nickname: null, membershipNo: null },
      });
      if (error || !data) {
        const detail = (error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? "Could not create this shooter.");
      }
      const addResult = await api.POST("/events/{id}/participants", {
        params: { path: { id: eventId } },
        body: { shooterId: data.id, squadId: null },
      });
      if (addResult.error) {
        const detail = (addResult.error as { detail?: string | null } | undefined)?.detail;
        throw new Error(detail ?? "Shooter created, but could not add them to the event.");
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["participants", eventId] });
      router.back();
    },
    onError: (err: Error) => setError(err.message),
  });

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ headerShown: true, title: "Add shooter" }} />

      <TextInput
        style={styles.search}
        placeholder="Search by name..."
        placeholderTextColor={colors.placeholder}
        value={search}
        onChangeText={setSearch}
      />

      {error && <Text style={styles.error}>{error}</Text>}

      {shootersQuery.isLoading && <ActivityIndicator color={colors.accent} style={styles.spinner} />}

      <ScrollView contentContainerStyle={styles.list}>
        {(shootersQuery.data ?? []).map((shooter) => (
          <Pressable
            key={shooter.id}
            style={styles.row}
            disabled={addMutation.isPending}
            onPress={() => addParticipant(shooter.id)}
          >
            <Text style={styles.rowName}>
              {shooter.firstName} {shooter.lastName}
            </Text>
            <Text style={styles.rowAction}>Add</Text>
          </Pressable>
        ))}
        {!shootersQuery.isLoading && (shootersQuery.data ?? []).length === 0 && (
          <Text style={styles.empty}>No matching shooters.</Text>
        )}

        {!showCreate && (
          <Pressable style={styles.createToggle} onPress={() => setShowCreate(true)}>
            <Text style={styles.createToggleText}>Can't find them? Add a new shooter</Text>
          </Pressable>
        )}

        {showCreate && (
          <View style={styles.createForm}>
            <TextInput
              style={styles.search}
              placeholder="First name"
              placeholderTextColor={colors.placeholder}
              value={firstName}
              onChangeText={setFirstName}
            />
            <TextInput
              style={[styles.search, { marginTop: 10 }]}
              placeholder="Last name"
              placeholderTextColor={colors.placeholder}
              value={lastName}
              onChangeText={setLastName}
            />
            <Pressable
              style={[styles.primaryButton, (!firstName.trim() || !lastName.trim()) && styles.primaryButtonDisabled]}
              disabled={!firstName.trim() || !lastName.trim() || createAndAddMutation.isPending}
              onPress={() => createAndAddMutation.mutate()}
            >
              <Text style={styles.primaryButtonText}>
                {createAndAddMutation.isPending ? "Adding..." : "Create & add"}
              </Text>
            </Pressable>
          </View>
        )}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingHorizontal: 20, paddingTop: 16 },
  search: {
    height: 48,
    backgroundColor: colors.inputBg,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 9,
    paddingHorizontal: 14,
    fontFamily: fonts.body,
    fontSize: 15,
    color: colors.textPrimary,
  },
  error: { color: colors.errorText, fontFamily: fonts.body, marginTop: 12 },
  spinner: { marginTop: 20 },
  list: { gap: 8, paddingTop: 16, paddingBottom: 32 },
  row: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 10,
    padding: 14,
    backgroundColor: colors.surface,
  },
  rowName: { fontFamily: fonts.semibold, fontSize: 15, color: colors.textPrimary },
  rowAction: { fontFamily: fonts.bold, fontSize: 13, color: colors.accent },
  empty: { textAlign: "center", fontFamily: fonts.body, color: colors.textSecondary, marginTop: 20 },
  createToggle: { marginTop: 12, alignItems: "center" },
  createToggleText: { fontFamily: fonts.medium, fontSize: 13, color: colors.accent },
  createForm: { marginTop: 16, gap: 0 },
  primaryButton: {
    height: 48,
    borderRadius: 9,
    backgroundColor: colors.accent,
    alignItems: "center",
    justifyContent: "center",
    marginTop: 14,
  },
  primaryButtonDisabled: { backgroundColor: colors.borderStrong },
  primaryButtonText: { fontFamily: fonts.bold, fontSize: 14, color: colors.accentText },
});
