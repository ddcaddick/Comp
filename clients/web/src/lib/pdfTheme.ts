import { StyleSheet } from "@react-pdf/renderer";

// The ShooterRSG dark palette (clients/web/src/index.css / clients/mobile/src/lib/theme.ts),
// repeated here rather than imported: react-pdf styles are plain objects evaluated outside
// the DOM/CSS-variable pipeline, so there is no `var(--...)` to read at PDF-generation time.
// Shared by every generated PDF (results, standings, ...) so they read as one product.
export const colors = {
  page: "#0b0c0f",
  panel: "#141519",
  border: "#2a2c33",
  text: "#f2f2f0",
  textMuted: "#9b9ca3",
  accent: "#ff8a3d",
};

export const pdfStyles = StyleSheet.create({
  page: { backgroundColor: colors.page, color: colors.text, padding: 24, fontSize: 8, fontFamily: "Helvetica" },
  title: { fontSize: 16, fontFamily: "Helvetica-Bold", color: colors.text },
  heading: { fontSize: 11, fontFamily: "Helvetica-Bold", color: colors.accent, marginTop: 2 },
  subtitle: { fontSize: 9, color: colors.textMuted, marginTop: 2, marginBottom: 14 },
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

export function slugifyForFilename(...parts: string[]): string {
  return parts.join("-").replace(/[^a-z0-9]+/gi, "-");
}
