import { useQuery } from "@tanstack/react-query";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { api } from "@/lib/api";

// A heartbeat older than this is treated as a stale, no-longer-relevant session rather
// than someone actively entering right now.
const RECENT_SESSION_MINUTES = 10;

export default function SquadListScreen() {
  const { eventId, name } = useLocalSearchParams<{ eventId: string; name?: string }>();
  const router = useRouter();

  const squadsQuery = useQuery({
    queryKey: ["squads", eventId],
    queryFn: async () => {
      const { data, error } = await api.GET("/events/{id}/squads", { params: { path: { id: eventId } } });
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

  const session = sessionQuery.data;
  const minutesAgo = session?.lastSeenAt
    ? Math.round((Date.now() - new Date(session.lastSeenAt).getTime()) / 60_000)
    : null;
  const showSessionBanner = session?.displayName && minutesAgo !== null && minutesAgo < RECENT_SESSION_MINUTES;

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

      {squadsQuery.isLoading && <ActivityIndicator style={styles.spinner} />}
      {squadsQuery.isError && <Text style={styles.error}>Could not load squads.</Text>}

      <FlatList
        data={squadsQuery.data ?? []}
        keyExtractor={(item) => item.id}
        contentContainerStyle={styles.list}
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
  container: { flex: 1, paddingHorizontal: 20, paddingTop: 16 },
  banner: { backgroundColor: "#FFF3CD", borderRadius: 8, padding: 12, marginBottom: 12 },
  bannerText: { color: "#7A5B00", fontSize: 14 },
  spinner: { marginTop: 24 },
  list: { gap: 8, paddingBottom: 24 },
  row: { borderWidth: 1, borderColor: "#e0e0e0", borderRadius: 10, padding: 16 },
  rowTitle: { fontSize: 17, fontWeight: "600" },
  rowSubtitle: { fontSize: 14, color: "#666", marginTop: 4 },
  error: { color: "#c0392b", marginTop: 16 },
  empty: { textAlign: "center", color: "#666", marginTop: 40 },
});
