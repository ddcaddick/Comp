/**
 * Design tokens lifted from the ShooterRSG sign-in mock: a dark ground with an orange
 * accent, Archivo for body/headings, JetBrains Mono for labels and small monospace
 * accents. Applied to the sign-in screen exactly, and extended by judgment to every
 * other screen (event list, squad list, squad runner, entry) since only sign-in was
 * mocked.
 */

export const colors = {
  background: "#0b0c0f",
  surface: "#141519",
  surfaceRaised: "#181a20",
  border: "rgba(255,255,255,0.13)",
  borderStrong: "rgba(255,255,255,0.16)",
  divider: "rgba(255,255,255,0.1)",

  textPrimary: "#eef1f4",
  textSecondary: "#8b929c",
  textMuted: "#5c636c",
  placeholder: "#565d66",

  accent: "#ff8a3d",
  accentHover: "#ffa062",
  accentText: "#1a1204",

  error: "#ff4d4d",
  errorText: "#ff9c9c",
  errorBg: "rgba(255,77,77,0.09)",
  errorBorder: "rgba(255,77,77,0.28)",

  success: "#3ddc97",
  successBg: "rgba(61,220,151,0.06)",
  successBorder: "rgba(61,220,151,0.3)",

  inputBg: "#0f1116",
  inputBgFocus: "#12151a",
  chipBg: "rgba(255,255,255,0.09)",
} as const;

export const fonts = {
  body: "Archivo_400Regular",
  medium: "Archivo_500Medium",
  semibold: "Archivo_600SemiBold",
  bold: "Archivo_700Bold",
  extrabold: "Archivo_800ExtraBold",
  mono: "JetBrainsMono_400Regular",
  monoMedium: "JetBrainsMono_500Medium",
  monoBold: "JetBrainsMono_700Bold",
} as const;
