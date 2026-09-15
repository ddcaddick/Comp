import { Ionicons } from "@expo/vector-icons";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { ActivityIndicator, FlatList, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { api } from "@/lib/api";
import { colors, fonts } from "@/lib/theme";

export default function EventListScreen() {
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
  const events = [...(eventsQuery.data ?? [])].sort((a, b) => b.eventDate.localeCompare(a.eventDate));

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Events</Text>
      </View>

      <Text style={styles.filterLabel}>Filter by competition</Text>
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        style={styles.filterScroll}
        contentContainerStyle={styles.filterRow}
      >
        <View style={[styles.chip, styles.chipActive]}>
          <Text style={[styles.chipText, styles.chipTextActive]}>All</Text>
        </View>
        {(competitionsQuery.data ?? []).map((competition) => (
          <Pressable
            key={competition.id}
            style={styles.chip}
            onPress={() => router.push({ pathname: "/competitions/[competitionId]", params: { competitionId: competition.id } })}
          >
            <Text style={styles.chipText}>{competition.name}</Text>
          </Pressable>
        ))}
      </ScrollView>

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
                {competitionNameById.get(item.competitionId) ?? "Competition"} · {item.eventDate} · {item.status}
              </Text>
            </View>
            <Ionicons name="chevron-forward" size={20} color={colors.textMuted} />
          </Pressable>
        )}
        ListEmptyComponent={!eventsQuery.isLoading ? <Text style={styles.empty}>No events yet.</Text> : null}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingTop: 60, paddingHorizontal: 20 },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: 16 },
  title: { fontFamily: fonts.bold, fontSize: 28, color: colors.textPrimary },
  filterLabel: {
    fontFamily: fonts.monoBold,
    fontSize: 10,
    letterSpacing: 1.5,
    textTransform: "uppercase",
    color: colors.textMuted,
    marginBottom: 8,
  },
  // Explicit height on the ScrollView itself (not just contentContainerStyle) -- without
  // it, Android can lay this out at a collapsed flex-computed height and clip the pills'
  // text against that shorter box even though the pills themselves paint at full size.
  // The gap below the row lives here (marginBottom), not as padding inside the scrollable
  // content, so it doesn't eat into that fixed height and reintroduce the same clipping.
  filterScroll: { flexGrow: 0, height: 44, marginBottom: 16 },
  filterRow: { gap: 8, alignItems: "center" },
  chip: {
    paddingVertical: 10,
    paddingHorizontal: 14,
    borderRadius: 16,
    backgroundColor: colors.chipBg,
    borderWidth: 1,
    borderColor: colors.border,
    justifyContent: "center",
  },
  chipActive: { backgroundColor: colors.accent, borderColor: colors.accent },
  // Archivo's ascenders clip against the pill's top edge on Android without an explicit
  // lineHeight taller than the font size -- fontSize alone isn't enough box for it here.
  chipText: { fontFamily: fonts.medium, fontSize: 13, lineHeight: 18, color: colors.textSecondary },
  chipTextActive: { color: colors.accentText, fontFamily: fonts.semibold },
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
