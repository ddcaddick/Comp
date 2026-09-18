import { Document, Image, Page, StyleSheet, Text, View, pdf } from "@react-pdf/renderer";
import type { components } from "@comp/api-types";
import { preferredName } from "./shooterName";
import { clubBadgeUrl, colors, pdfStyles, slugifyForFilename } from "./pdfTheme";
import { todayIso } from "./dates";

type LeagueStandings = components["schemas"]["LeagueStandingsResponse"];

// A4 landscape is 842x595pt. This is what's left of that height for the tile grid once the
// page's own padding, title row, and footer are subtracted -- see computeScale below. The
// grid must always fit within it: a standings sheet that silently spills onto a second page
// is easy to miss when printing, so growth in roster size shrinks the tiles instead.
const GRID_AVAILABLE_HEIGHT_PT = 472;
const MIN_SCALE = 0.55;

// Every number here is today's fixed sizing (scale=1); computeScale finds the largest factor
// <=1 that lets the tallest column of tiles fit inside GRID_AVAILABLE_HEIGHT_PT, so a
// competition with a couple of small leagues prints at full size while one with a long
// roster shrinks just enough to still fit on the one page.
const BASE = {
  tilePadding: 8,
  tileMarginBottom: 10,
  panelHeadingLine: 18,
  dropNoteLine: 12.4,
  headerRowLine: 15,
  rowLine: 14,
  fontSizePanelHeading: 10,
  fontSizeDropNote: 7,
  fontSizeHeaderCell: 7.5,
  fontSizeCell: 7.5,
  rowPaddingVertical: 2.5,
};

function chooseColumnCount(leagueCount: number): number {
  if (leagueCount <= 2) return Math.max(leagueCount, 1);
  if (leagueCount <= 4) return 2;
  return 3;
}

// Greedy longest-first bin-packing so columns end up roughly equal height, rather than the
// naive left-to-right wrap that used to leave one long division stranded alone in its own row.
function assignColumns(leagues: LeagueStandings[], columnCount: number): LeagueStandings[][] {
  const columns: LeagueStandings[][] = Array.from({ length: columnCount }, () => []);
  const heightUnits = new Array(columnCount).fill(0);
  const byRowsDesc = [...leagues].sort((a, b) => b.standings.length - a.standings.length);
  for (const league of byRowsDesc) {
    const shortest = heightUnits.indexOf(Math.min(...heightUnits));
    columns[shortest].push(league);
    heightUnits[shortest] += league.standings.length + 3; // +3 ~ tile chrome, in "row" units
  }
  for (const column of columns) {
    column.sort((a, b) => a.leagueName.localeCompare(b.leagueName));
  }
  return columns;
}

function tileHeightPt(league: LeagueStandings, scale: number): number {
  const dropWorstCount = Number(league.dropWorstCount);
  return (
    BASE.tilePadding * 2 * scale +
    BASE.tileMarginBottom * scale +
    BASE.panelHeadingLine * scale +
    (dropWorstCount > 0 ? BASE.dropNoteLine * scale : 0) +
    BASE.headerRowLine * scale +
    Math.max(league.standings.length, 1) * BASE.rowLine * scale
  );
}

function tallestColumnHeightPt(columns: LeagueStandings[][], scale: number): number {
  let max = 0;
  for (const column of columns) {
    const height = column.reduce((sum, league) => sum + tileHeightPt(league, scale), 0);
    max = Math.max(max, height);
  }
  return max;
}

function computeScale(columns: LeagueStandings[][]): number {
  const atFullSize = tallestColumnHeightPt(columns, 1);
  if (atFullSize <= GRID_AVAILABLE_HEIGHT_PT || atFullSize === 0) return 1;
  return Math.max(MIN_SCALE, GRID_AVAILABLE_HEIGHT_PT / atFullSize);
}

function buildScaledStyles(scale: number) {
  return StyleSheet.create({
    column: { flex: 1, flexDirection: "column" },
    tile: {
      backgroundColor: colors.panel,
      borderRadius: 4,
      borderWidth: 1,
      borderColor: colors.border,
      padding: BASE.tilePadding * scale,
      marginBottom: BASE.tileMarginBottom * scale,
    },
    panelHeading: { fontSize: BASE.fontSizePanelHeading * scale, fontFamily: "Helvetica-Bold", color: colors.accent, marginBottom: 6 * scale },
    dropNote: { fontSize: BASE.fontSizeDropNote * scale, color: colors.accent, marginBottom: 4 * scale },
    headerRow: { flexDirection: "row", borderBottomWidth: 1, borderBottomColor: colors.accent, paddingBottom: 3 * scale, marginBottom: 2 * scale },
    headerCell: { fontFamily: "Helvetica-Bold", color: colors.accent, fontSize: BASE.fontSizeHeaderCell * scale },
    row: { flexDirection: "row", borderBottomWidth: 0.5, borderBottomColor: colors.border, paddingVertical: BASE.rowPaddingVertical * scale },
    cell: { fontSize: BASE.fontSizeCell * scale, color: colors.text },
    cellMuted: { fontSize: BASE.fontSizeCell * scale, color: colors.textMuted },
  });
}

type ScaledStyles = ReturnType<typeof buildScaledStyles>;

function StandingsTile({ standings, styles }: { standings: LeagueStandings; styles: ScaledStyles }) {
  const sorted = [...standings.standings].sort((a, b) => Number(a.position) - Number(b.position));
  const dropWorstCount = Number(standings.dropWorstCount);

  return (
    // wrap={false} is a safety net, not the primary fix: computeScale already sizes tiles to
    // fit one page, but if a roster ever grows past MIN_SCALE's floor, this keeps a table
    // intact on the next page instead of slicing it mid-row like the old fixed-size layout did.
    <View style={styles.tile} wrap={false}>
      <Text style={styles.panelHeading}>{standings.leagueName}</Text>
      {dropWorstCount > 0 && (
        <Text style={styles.dropNote}>This includes your lowest {dropWorstCount} scores removed.</Text>
      )}
      <View style={styles.headerRow}>
        <Text style={[styles.headerCell, { width: "15%" }]}>Pos</Text>
        <Text style={[styles.headerCell, { width: "45%" }]}>Shooter</Text>
        <Text style={[styles.headerCell, { width: "20%", textAlign: "right" }]}>Dropped</Text>
        <Text style={[styles.headerCell, { width: "20%", textAlign: "right" }]}>Points</Text>
      </View>
      {sorted.map((s) => (
        <View key={s.shooterId} style={styles.row}>
          <Text style={[styles.cell, { width: "15%" }]}>{s.position}</Text>
          <Text style={[styles.cell, { width: "45%" }]}>
            {preferredName(s)}
            {s.isProvisional ? " (prov.)" : ""}
          </Text>
          <Text style={[styles.cellMuted, { width: "20%", textAlign: "right" }]}>{s.missedEvents}</Text>
          <Text style={[styles.cell, { width: "20%", textAlign: "right" }]}>{s.countingTotal}</Text>
        </View>
      ))}
      {sorted.length === 0 && <Text style={styles.cellMuted}>No events finalised yet.</Text>}
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
  const columnCount = chooseColumnCount(leagues.length);
  const columns = assignColumns(leagues, columnCount);
  const scale = computeScale(columns);
  const styles = buildScaledStyles(scale);

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
        <View style={{ flexDirection: "row", gap: 10, marginTop: 14 }}>
          {columns.map((column, i) => (
            <View key={i} style={styles.column}>
              {column.map((league) => (
                <StandingsTile key={league.leagueId} standings={league} styles={styles} />
              ))}
            </View>
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
