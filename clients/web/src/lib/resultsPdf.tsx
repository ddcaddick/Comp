import { Document, Page, StyleSheet, Text, View, pdf } from "@react-pdf/renderer";
import { formatMillis } from "@comp/core";
import type { components } from "@comp/api-types";
import { preferredName } from "./shooterName";

type Participant = components["schemas"]["EventParticipantResultResponse"];

// The ShooterRSG dark palette (clients/web/src/index.css / clients/mobile/src/lib/theme.ts),
// repeated here rather than imported: react-pdf styles are plain objects evaluated outside
// the DOM/CSS-variable pipeline, so there is no `var(--...)` to read at PDF-generation time.
const colors = {
  page: "#0b0c0f",
  panel: "#141519",
  border: "#2a2c33",
  text: "#f2f2f0",
  textMuted: "#9b9ca3",
  accent: "#ff8a3d",
};

const styles = StyleSheet.create({
  page: { backgroundColor: colors.page, color: colors.text, padding: 24, fontSize: 8, fontFamily: "Helvetica" },
  title: { fontSize: 16, fontFamily: "Helvetica-Bold", color: colors.text },
  subtitle: { fontSize: 9, color: colors.textMuted, marginTop: 2, marginBottom: 14 },
  body: { flexDirection: "row", gap: 14 },
  leftColumn: { width: "34%" },
  rightColumn: { flex: 1, flexDirection: "row", flexWrap: "wrap", gap: 10, alignContent: "flex-start" },
  panelHeading: { fontSize: 10, fontFamily: "Helvetica-Bold", color: colors.accent, marginBottom: 6 },
  panel: { backgroundColor: colors.panel, borderRadius: 4, borderWidth: 1, borderColor: colors.border, padding: 8 },
  tile: { width: "48%", backgroundColor: colors.panel, borderRadius: 4, borderWidth: 1, borderColor: colors.border, padding: 8, marginBottom: 10 },
  row: { flexDirection: "row", borderBottomWidth: 0.5, borderBottomColor: colors.border, paddingVertical: 2.5 },
  headerRow: { flexDirection: "row", borderBottomWidth: 1, borderBottomColor: colors.accent, paddingBottom: 3, marginBottom: 2 },
  headerCell: { fontFamily: "Helvetica-Bold", color: colors.accent, fontSize: 7.5 },
  cell: { fontSize: 7.5, color: colors.text },
  cellMuted: { fontSize: 7.5, color: colors.textMuted },
  dnf: { color: colors.textMuted, fontStyle: "italic" },
  footer: { position: "absolute", bottom: 16, left: 24, right: 24, fontSize: 7, color: colors.textMuted, flexDirection: "row", justifyContent: "space-between" },
});

function timeOrDnf(participant: Participant): string {
  return participant.eventTimeMs != null ? formatMillis(Number(participant.eventTimeMs)) : "DNF";
}

function OverallTable({ participants }: { participants: Participant[] }) {
  const sorted = [...participants].sort((a, b) => {
    const posA = a.overallPosition != null ? Number(a.overallPosition) : Number.MAX_SAFE_INTEGER;
    const posB = b.overallPosition != null ? Number(b.overallPosition) : Number.MAX_SAFE_INTEGER;
    return posA - posB;
  });

  return (
    <View style={styles.panel}>
      <Text style={styles.panelHeading}>Overall Results</Text>
      <View style={styles.headerRow}>
        <Text style={[styles.headerCell, { width: "14%" }]}>Pos</Text>
        <Text style={[styles.headerCell, { width: "46%" }]}>Shooter</Text>
        <Text style={[styles.headerCell, { width: "24%" }]}>League</Text>
        <Text style={[styles.headerCell, { width: "16%", textAlign: "right" }]}>Time</Text>
      </View>
      {sorted.map((p) => (
        <View key={p.participantId} style={styles.row}>
          <Text style={[styles.cell, { width: "14%" }]}>{p.status === "Dnf" ? "—" : p.overallPosition}</Text>
          <Text style={[styles.cell, { width: "46%" }]}>{preferredName(p)}</Text>
          <Text style={[styles.cellMuted, { width: "24%" }]}>{p.leagueName ?? "—"}</Text>
          <Text style={[p.status === "Dnf" ? styles.dnf : styles.cell, { width: "16%", textAlign: "right" }]}>
            {timeOrDnf(p)}
          </Text>
        </View>
      ))}
    </View>
  );
}

function LeagueTile({ leagueName, participants }: { leagueName: string; participants: Participant[] }) {
  const sorted = [...participants].sort((a, b) => {
    const posA = a.leaguePosition != null ? Number(a.leaguePosition) : Number.MAX_SAFE_INTEGER;
    const posB = b.leaguePosition != null ? Number(b.leaguePosition) : Number.MAX_SAFE_INTEGER;
    return posA - posB;
  });

  return (
    <View style={styles.tile}>
      <Text style={styles.panelHeading}>{leagueName}</Text>
      <View style={styles.headerRow}>
        <Text style={[styles.headerCell, { width: "14%" }]}>Pos</Text>
        <Text style={[styles.headerCell, { width: "42%" }]}>Shooter</Text>
        <Text style={[styles.headerCell, { width: "22%", textAlign: "right" }]}>Time</Text>
        <Text style={[styles.headerCell, { width: "22%", textAlign: "right" }]}>Pts</Text>
      </View>
      {sorted.map((p) => (
        <View key={p.participantId} style={styles.row}>
          <Text style={[styles.cell, { width: "14%" }]}>{p.status === "Dnf" ? "—" : p.leaguePosition}</Text>
          <Text style={[styles.cell, { width: "42%" }]}>{preferredName(p)}</Text>
          <Text style={[p.status === "Dnf" ? styles.dnf : styles.cell, { width: "22%", textAlign: "right" }]}>
            {timeOrDnf(p)}
          </Text>
          <Text style={[styles.cell, { width: "22%", textAlign: "right" }]}>{p.leaguePoints}</Text>
        </View>
      ))}
    </View>
  );
}

function ResultsPdfDocument({
  eventName,
  eventDate,
  isFinal,
  participants,
}: {
  eventName: string;
  eventDate: string;
  isFinal: boolean;
  participants: Participant[];
}) {
  const leagueGroups = new Map<string, Participant[]>();
  for (const p of participants) {
    if (!p.leagueName) continue;
    const group = leagueGroups.get(p.leagueName) ?? [];
    group.push(p);
    leagueGroups.set(p.leagueName, group);
  }
  const leagues = [...leagueGroups.entries()].sort(([a], [b]) => a.localeCompare(b));

  return (
    <Document>
      <Page size="A4" orientation="landscape" style={styles.page}>
        <Text style={styles.title}>{eventName} — Results</Text>
        <Text style={styles.subtitle}>
          {eventDate} · {isFinal ? "Final" : "Provisional (live)"}
        </Text>
        <View style={styles.body}>
          <View style={styles.leftColumn}>
            <OverallTable participants={participants} />
          </View>
          <View style={styles.rightColumn}>
            {leagues.map(([leagueName, leagueParticipants]) => (
              <LeagueTile key={leagueName} leagueName={leagueName} participants={leagueParticipants} />
            ))}
          </View>
        </View>
        <View style={styles.footer} fixed>
          <Text>Generated {new Date().toLocaleString()} from Comp</Text>
          <Text render={({ pageNumber, totalPages }) => `${pageNumber} / ${totalPages}`} />
        </View>
      </Page>
    </Document>
  );
}

export async function downloadResultsPdf(args: {
  eventName: string;
  eventDate: string;
  isFinal: boolean;
  participants: Participant[];
}) {
  const blob = await pdf(<ResultsPdfDocument {...args} />).toBlob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `${args.eventName.replace(/[^a-z0-9]+/gi, "-")}-results.pdf`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
