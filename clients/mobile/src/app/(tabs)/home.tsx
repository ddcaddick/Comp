import { Ionicons } from "@expo/vector-icons";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { colors, fonts } from "@/lib/theme";

// Local calendar date, not UTC -- a shooting club's "today" is whatever day it is where the
// club actually is, and toISOString() would drift a day around midnight in most timezones.
function todayIso(): string {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export default function HomeScreen() {
  const { user, logout } = useAuth();
  const router = useRouter();

  const competitionsQuery = useQuery({
    queryKey: ["competitions"],
    queryFn: async () => {
      const { data, error } = await api.GET("/competitions");
      if (error) throw error;
      return data ?? [];
    },
  });

  const eventsQuery = useQuery({
    queryKey: ["events"],
    queryFn: async () => {
      const { data, error } = await api.GET("/events");
      if (error) throw error;
      return data ?? [];
    },
  });

  const competitionNameById = new Map((competitionsQuery.data ?? []).map((c) => [c.id, c.name]));
  const today = todayIso();
  const todaysEvents = (eventsQuery.data ?? [])
    .filter((e) => e.eventDate === today)
    .sort((a, b) => a.name.localeCompare(b.name));

  const isLoading = eventsQuery.isLoading || competitionsQuery.isLoading;

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <View style={styles.brandRow}>
          <View style={styles.badge}>
            <Text style={styles.badgeText}>SR</Text>
          </View>
          <Text style={styles.wordmark}>
            SHOOTER<Text style={{ color: colors.accent }}>RSG</Text>
          </Text>
        </View>
        <View style={styles.welcomeBlock}>
          <Text style={styles.welcomeText} numberOfLines={1}>
            Welcome, {user?.displayName ?? "there"}
          </Text>
          <Pressable onPress={logout} hitSlop={8}>
            <Text style={styles.signOut}>Sign out</Text>
          </Pressable>
        </View>
      </View>

      <View style={styles.sectionHeader}>
        <Ionicons name="today" size={16} color={colors.accent} />
        <Text style={styles.sectionTitle}>Today's Events</Text>
      </View>

      {isLoading && <ActivityIndicator color={colors.accent} style={styles.spinner} />}
      {!isLoading && eventsQuery.isError && <Text style={styles.error}>Could not load today's events.</Text>}

      {!isLoading && !eventsQuery.isError && (
        <FlatList
          data={todaysEvents}
          keyExtractor={(item) => item.id}
          contentContainerStyle={styles.list}
          renderItem={({ item }) => (
            <Pressable
              style={styles.row}
              onPress={() => router.push({ pathname: "/events/[eventId]", params: { eventId: item.id, name: item.name } })}
            >
              <View style={{ flex: 1 }}>
                <Text style={styles.rowTitle}>{item.name}</Text>
                <Text style={styles.rowSubtitle}>
                  {competitionNameById.get(item.competitionId) ?? "Competition"} · {item.status}
                </Text>
              </View>
              <Ionicons name="chevron-forward" size={20} color={colors.textMuted} />
            </Pressable>
          )}
          ListEmptyComponent={
            <View style={styles.empty}>
              <Ionicons name="calendar-outline" size={28} color={colors.textMuted} />
              <Text style={styles.emptyText}>No events today.</Text>
            </View>
          }
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingTop: 60, paddingHorizontal: 20 },
  header: { marginBottom: 28, gap: 16 },
  brandRow: { flexDirection: "row", alignItems: "center", gap: 10 },
  badge: { width: 32, height: 32, borderRadius: 8, backgroundColor: colors.accent, alignItems: "center", justifyContent: "center" },
  badgeText: { fontFamily: fonts.extrabold, fontSize: 13, color: colors.accentText, letterSpacing: -0.5 },
  wordmark: { fontFamily: fonts.extrabold, fontSize: 13, letterSpacing: 2, color: colors.textPrimary },
  welcomeBlock: { alignItems: "flex-end", gap: 4 },
  welcomeText: { fontFamily: fonts.semibold, fontSize: 17, color: colors.textPrimary },
  signOut: { fontFamily: fonts.medium, fontSize: 13, color: colors.accent },
  sectionHeader: { flexDirection: "row", alignItems: "center", gap: 8, marginBottom: 12 },
  sectionTitle: { fontFamily: fonts.bold, fontSize: 20, color: colors.textPrimary },
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
  empty: { alignItems: "center", gap: 10, marginTop: 40 },
  emptyText: { textAlign: "center", color: colors.textSecondary, fontFamily: fonts.body },
});
