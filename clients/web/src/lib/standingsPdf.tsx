import { Document, Image, Page, StyleSheet, Text, View, pdf } from "@react-pdf/renderer";
import type { components } from "@comp/api-types";
import { preferredName } from "./shooterName";
import { clubBadgeUrl, pdfStyles, slugifyForFilename } from "./pdfTheme";
import { todayIso } from "./dates";

type LeagueStandings = components["schemas"]["LeagueStandingsResponse"];

// Layout unique to the standings PDF (a single wrapping grid of league tiles, no left-hand
// overall table since standings have no cross-league ranking); everything else (page, title,
// tile, row, cell, footer, ...) comes from `pdfStyles`, shared with the results PDF.
const styles = StyleSheet.create({
  grid: { flexDirection: "row", flexWrap: "wrap", gap: 10, alignContent: "flex-start" },
});

function StandingsTile({ standings }: { standings: LeagueStandings }) {
  const sorted = [...standings.standings].sort((a, b) => Number(a.position) - Number(b.position));

  return (
    <View style={pdfStyles.tile}>
      <Text style={pdfStyles.panelHeading}>{standings.leagueName}</Text>
      <View style={pdfStyles.headerRow}>
        <Text style={[pdfStyles.headerCell, { width: "12%" }]}>Pos</Text>
        <Text style={[pdfStyles.headerCell, { width: "40%" }]}>Shooter</Text>
        <Text style={[pdfStyles.headerCell, { width: "18%", textAlign: "right" }]}>Count</Text>
        <Text style={[pdfStyles.headerCell, { width: "15%", textAlign: "right" }]}>Missed</Text>
        <Text style={[pdfStyles.headerCell, { width: "15%", textAlign: "right" }]}>Total</Text>
      </View>
      {sorted.map((s) => (
        <View key={s.shooterId} style={pdfStyles.row}>
          <Text style={[pdfStyles.cell, { width: "12%" }]}>{s.position}</Text>
          <Text style={[pdfStyles.cell, { width: "40%" }]}>
            {preferredName(s)}
            {s.isProvisional ? " (prov.)" : ""}
          </Text>
          <Text style={[pdfStyles.cell, { width: "18%", textAlign: "right" }]}>{s.countingTotal}</Text>
          <Text style={[pdfStyles.cellMuted, { width: "15%", textAlign: "right" }]}>{s.missedEvents}</Text>
          <Text style={[pdfStyles.cell, { width: "15%", textAlign: "right" }]}>{s.runningTotal}</Text>
        </View>
      ))}
      {sorted.length === 0 && <Text style={pdfStyles.cellMuted}>No events finalised yet.</Text>}
    </View>
  );
}

function StandingsPdfDocument({
  competitionName,
  leagues,
}: {
  competitionName: string;
  leagues: LeagueStandings[];
}) {
  const sorted = [...leagues].sort((a, b) => a.leagueName.localeCompare(b.leagueName));

  return (
    <Document>
      <Page size="A4" orientation="landscape" style={pdfStyles.page}>
        <View style={pdfStyles.titleRow}>
          <View>
            <Text style={pdfStyles.title}>{competitionName}</Text>
            <Text style={pdfStyles.heading}>As of {todayIso()}</Text>
          </View>
          <Image src={clubBadgeUrl} style={pdfStyles.clubBadge} />
        </View>
        <View style={[styles.grid, { marginTop: 14 }]}>
          {sorted.map((league) => (
            <StandingsTile key={league.leagueId} standings={league} />
          ))}
        </View>
        <View style={pdfStyles.footer} fixed>
          <Text>Generated {new Date().toLocaleString()} from Comp</Text>
          <Text render={({ pageNumber, totalPages }) => `${pageNumber} / ${totalPages}`} />
        </View>
      </Page>
    </Document>
  );
}

export async function downloadStandingsPdf(args: { competitionName: string; leagues: LeagueStandings[] }) {
  const blob = await pdf(<StandingsPdfDocument {...args} />).toBlob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `${slugifyForFilename(args.competitionName, "league-standings")}.pdf`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
