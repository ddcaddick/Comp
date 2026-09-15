import { useQuery } from "@tanstack/react-query";
import { Redirect, useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { colors, fonts } from "@/lib/theme";

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
        <ActivityIndicator color={colors.accent} />
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
  container: { flex: 1, backgroundColor: colors.background, paddingTop: 60, paddingHorizontal: 20 },
  center: { flex: 1, justifyContent: "center", alignItems: "center", backgroundColor: colors.background },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: 16 },
  title: { fontFamily: fonts.bold, fontSize: 28, color: colors.textPrimary },
  signOut: { color: colors.accent, fontFamily: fonts.medium, fontSize: 15 },
  spinner: { marginTop: 24 },
  list: { gap: 8, paddingBottom: 24 },
  row: { borderWidth: 1, borderColor: colors.border, borderRadius: 10, padding: 16, backgroundColor: colors.surface },
  rowTitle: { fontFamily: fonts.semibold, fontSize: 16, color: colors.textPrimary },
  rowSubtitle: { fontFamily: fonts.body, fontSize: 13, color: colors.textSecondary, marginTop: 4 },
  error: { color: colors.errorText, fontFamily: fonts.body, marginTop: 16 },
  empty: { textAlign: "center", color: colors.textSecondary, fontFamily: fonts.body, marginTop: 40 },
});
