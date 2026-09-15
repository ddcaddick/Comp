import { Ionicons } from "@expo/vector-icons";
import { useQuery } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { api } from "@/lib/api";
import { colors, fonts } from "@/lib/theme";

export default function CompetitionEventsScreen() {
  const { competitionId } = useLocalSearchParams<{ competitionId: string }>();
  const router = useRouter();

  const competitionsQuery = useQuery({
    queryKey: ["competitions"],
    queryFn: async () => {
      const { data, error } = await api.GET("/competitions");
      if (error) throw error;
      return data ?? [];
    },
  });
  const competition = competitionsQuery.data?.find((c) => c.id === competitionId);

  const eventsQuery = useQuery({
    queryKey: ["events", competitionId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events", { params: { query: { competitionId } } });
      if (error) throw error;
      return data ?? [];
    },
    enabled: !!competitionId,
  });

  const events = [...(eventsQuery.data ?? [])].sort((a, b) => b.eventDate.localeCompare(a.eventDate));

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ headerShown: true, title: competition?.name ?? "Competition" }} />

      {eventsQuery.isLoading && <ActivityIndicator color={colors.accent} style={styles.spinner} />}
      {eventsQuery.isError && <Text style={styles.error}>Could not load events.</Text>}

      <FlatList
        data={events}
        keyExtractor={(item) => item.id}
        contentContainerStyle={styles.list}
        renderItem={({ item }) => (
          <Pressable
            style={styles.row}
            onPress={() =>
              router.push({ pathname: "/events/[eventId]", params: { eventId: item.id, name: item.name } })
            }
          >
            <View style={{ flex: 1 }}>
              <Text style={styles.rowTitle}>{item.name}</Text>
              <Text style={styles.rowSubtitle}>
                {item.eventDate} · {item.status}
              </Text>
            </View>
            <Ionicons name="chevron-forward" size={20} color={colors.textMuted} />
          </Pressable>
        )}
        ListEmptyComponent={
          !eventsQuery.isLoading ? <Text style={styles.empty}>No events yet for this competition.</Text> : null
        }
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingHorizontal: 20, paddingTop: 16 },
  spinner: { marginTop: 24 },
  list: { gap: 8, paddingBottom: 24 },
  row: {
    flexDirection: "row",
    alignItems: "center",
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 10,
    padding: 16,
    backgroundColor: colors.surface,
  },
  rowTitle: { fontFamily: fonts.semibold, fontSize: 16, color: colors.textPrimary },
  rowSubtitle: { fontFamily: fonts.body, fontSize: 13, color: colors.textSecondary, marginTop: 4 },
  error: { color: colors.errorText, fontFamily: fonts.body, marginTop: 16 },
  empty: { textAlign: "center", color: colors.textSecondary, fontFamily: fonts.body, marginTop: 40 },
});
