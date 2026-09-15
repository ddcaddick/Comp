import { useQuery } from "@tanstack/react-query";
import { Redirect, useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";

export default function EventListScreen() {
  const { isLoading: authLoading, isAuthenticated, logout } = useAuth();
  const router = useRouter();

  const eventsQuery = useQuery({
    queryKey: ["events"],
    queryFn: async () => {
      const { data, error } = await api.GET("/events");
      if (error) throw error;
      return data ?? [];
    },
    enabled: isAuthenticated,
  });

  if (authLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator />
      </View>
    );
  }

  if (!isAuthenticated) {
    return <Redirect href="/" />;
  }

  const events = [...(eventsQuery.data ?? [])].sort((a, b) => b.eventDate.localeCompare(a.eventDate));

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Events</Text>
        <Pressable onPress={logout}>
          <Text style={styles.signOut}>Sign out</Text>
        </Pressable>
      </View>

      {eventsQuery.isLoading && <ActivityIndicator style={styles.spinner} />}
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
            <Text style={styles.rowTitle}>{item.name}</Text>
            <Text style={styles.rowSubtitle}>
              {item.eventDate} · {item.status}
            </Text>
          </Pressable>
        )}
        ListEmptyComponent={!eventsQuery.isLoading ? <Text style={styles.empty}>No events yet.</Text> : null}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, paddingTop: 60, paddingHorizontal: 20 },
  center: { flex: 1, justifyContent: "center", alignItems: "center" },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: 16 },
  title: { fontSize: 28, fontWeight: "700" },
  signOut: { color: "#208AEF", fontSize: 16 },
  spinner: { marginTop: 24 },
  list: { gap: 8, paddingBottom: 24 },
  row: { borderWidth: 1, borderColor: "#e0e0e0", borderRadius: 10, padding: 16 },
  rowTitle: { fontSize: 17, fontWeight: "600" },
  rowSubtitle: { fontSize: 14, color: "#666", marginTop: 4 },
  error: { color: "#c0392b", marginTop: 16 },
  empty: { textAlign: "center", color: "#666", marginTop: 40 },
});
