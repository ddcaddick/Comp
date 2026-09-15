import { Document, Image, Page, StyleSheet, Text, View, pdf } from "@react-pdf/renderer";
import { formatMillis } from "@comp/core";
import type { components } from "@comp/api-types";
import { preferredName } from "./shooterName";
import { clubBadgeUrl, pdfStyles, slugifyForFilename } from "./pdfTheme";

type Participant = components["schemas"]["EventParticipantResultResponse"];

// Layout unique to the results PDF (the overall-table-left / league-tiles-right split);
// everything else (page, title, tile, row, cell, footer, ...) comes from `pdfStyles`, shared
// with every other generated PDF (see standingsPdf.tsx) so they read as one product.
const styles = StyleSheet.create({
  body: { flexDirection: "row", gap: 14 },
  leftColumn: { width: "34%" },
  rightColumn: { flex: 1, flexDirection: "row", flexWrap: "wrap", gap: 10, alignContent: "flex-start" },
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
    <View style={pdfStyles.panel}>
      <Text style={pdfStyles.panelHeading}>Overall Results</Text>
      <View style={pdfStyles.headerRow}>
        <Text style={[pdfStyles.headerCell, { width: "14%" }]}>Pos</Text>
        <Text style={[pdfStyles.headerCell, { width: "46%" }]}>Shooter</Text>
        <Text style={[pdfStyles.headerCell, { width: "24%" }]}>League</Text>
        <Text style={[pdfStyles.headerCell, { width: "16%", textAlign: "right" }]}>Time</Text>
      </View>
      {sorted.map((p) => (
        <View key={p.participantId} style={pdfStyles.row}>
          <Text style={[pdfStyles.cell, { width: "14%" }]}>{p.status === "Dnf" ? "—" : p.overallPosition}</Text>
          <Text style={[pdfStyles.cell, { width: "46%" }]}>{preferredName(p)}</Text>
          <Text style={[pdfStyles.cellMuted, { width: "24%" }]}>{p.leagueName ?? "—"}</Text>
          <Text style={[p.status === "Dnf" ? pdfStyles.dnf : pdfStyles.cell, { width: "16%", textAlign: "right" }]}>
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
    <View style={pdfStyles.tile}>
      <Text style={pdfStyles.panelHeading}>{leagueName}</Text>
      <View style={pdfStyles.headerRow}>
        <Text style={[pdfStyles.headerCell, { width: "14%" }]}>Pos</Text>
        <Text style={[pdfStyles.headerCell, { width: "42%" }]}>Shooter</Text>
        <Text style={[pdfStyles.headerCell, { width: "22%", textAlign: "right" }]}>Time</Text>
        <Text style={[pdfStyles.headerCell, { width: "22%", textAlign: "right" }]}>Pts</Text>
      </View>
      {sorted.map((p) => (
        <View key={p.participantId} style={pdfStyles.row}>
          <Text style={[pdfStyles.cell, { width: "14%" }]}>{p.status === "Dnf" ? "—" : p.leaguePosition}</Text>
          <Text style={[pdfStyles.cell, { width: "42%" }]}>{preferredName(p)}</Text>
          <Text style={[p.status === "Dnf" ? pdfStyles.dnf : pdfStyles.cell, { width: "22%", textAlign: "right" }]}>
            {timeOrDnf(p)}
          </Text>
          <Text style={[pdfStyles.cell, { width: "22%", textAlign: "right" }]}>{p.leaguePoints}</Text>
        </View>
      ))}
    </View>
  );
}

function ResultsPdfDocument({
  competitionName,
  eventName,
  eventDate,
  isFinal,
  participants,
}: {
  competitionName: string;
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
      <Page size="A4" orientation="landscape" style={pdfStyles.page}>
        <View style={pdfStyles.titleRow}>
          <View>
            <Text style={pdfStyles.title}>{competitionName}</Text>
            <Text style={pdfStyles.heading}>{eventName} — Results</Text>
            <Text style={pdfStyles.subtitle}>
              {eventDate} · {isFinal ? "Final" : "Provisional (live)"}
            </Text>
          </View>
          <Image src={clubBadgeUrl} style={pdfStyles.clubBadge} />
        </View>
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
        <View style={pdfStyles.footer} fixed>
          <Text>Generated {new Date().toLocaleString()} from Comp</Text>
          <Text render={({ pageNumber, totalPages }) => `${pageNumber} / ${totalPages}`} />
        </View>
      </Page>
    </Document>
  );
}

export async function downloadResultsPdf(args: {
  competitionName: string;
  eventName: string;
  eventDate: string;
  isFinal: boolean;
  participants: Participant[];
}) {
  const blob = await pdf(<ResultsPdfDocument {...args} />).toBlob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  // Includes the competition name -- "Week 1" alone is ambiguous once more than one
  // competition has an event by that name (this project's own demo data does).
  link.download = `${slugifyForFilename(args.competitionName, args.eventName)}-results.pdf`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
