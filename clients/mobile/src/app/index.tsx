import { useState } from "react";
import { ActivityIndicator, Alert, Image, Pressable, StyleSheet, Text, TextInput, View } from "react-native";
import { Redirect } from "expo-router";
import { LinearGradient } from "expo-linear-gradient";
import { useAuth } from "@/lib/auth";
import { colors, fonts } from "@/lib/theme";

export default function SignInScreen() {
  const { isLoading, isAuthenticated, login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (isLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator color={colors.accent} />
      </View>
    );
  }

  if (isAuthenticated) {
    return <Redirect href="/events" />;
  }

  async function handleSubmit() {
    if (!email.trim() || !password) {
      setError("Enter your email and password.");
      return;
    }
    setSubmitting(true);
    setError(null);
    const result = await login(email.trim(), password, rememberMe);
    setSubmitting(false);
    if (!result.success) {
      setError(result.error);
    }
  }

  return (
    <View style={styles.container}>
      <View style={styles.hero}>
        <Image
          source={require("../../assets/images/sign-in-hero.png")}
          style={styles.heroImage}
          resizeMode="contain"
        />
        <LinearGradient
          colors={["rgba(11,12,15,0.35)", "rgba(11,12,15,0.18)", "rgba(11,12,15,0.85)", colors.background]}
          locations={[0, 0.26, 0.58, 0.85]}
          style={StyleSheet.absoluteFill}
        />
        <View style={styles.brandRow}>
          <View style={styles.badge}>
            <Text style={styles.badgeText}>SR</Text>
          </View>
          <View>
            <Text style={styles.wordmark}>
              SHOOTER<Text style={{ color: colors.accent }}>RSG</Text>
            </Text>
            <Text style={styles.tagline}>Ready. Standby. Go</Text>
          </View>
        </View>
      </View>

      <View style={styles.form}>
        <Text style={styles.heading}>Sign in</Text>

        <View style={styles.field}>
          <Text style={styles.label}>Email</Text>
          <TextInput
            style={styles.input}
            placeholder="official@club.org"
            placeholderTextColor={colors.placeholder}
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="email-address"
            value={email}
            onChangeText={setEmail}
          />
        </View>

        <View style={styles.field}>
          <Text style={styles.label}>Password</Text>
          <View>
            <TextInput
              style={[styles.input, styles.passwordInput]}
              placeholder="••••••••"
              placeholderTextColor={colors.placeholder}
              secureTextEntry={!showPassword}
              value={password}
              onChangeText={setPassword}
            />
            <Pressable style={styles.showButton} onPress={() => setShowPassword((v) => !v)}>
              <Text style={styles.showButtonText}>{showPassword ? "HIDE" : "SHOW"}</Text>
            </Pressable>
          </View>
        </View>

        {error && (
          <View style={styles.errorBanner}>
            <View style={styles.errorDot} />
            <Text style={styles.errorText}>{error}</Text>
          </View>
        )}

        <View style={styles.optionsRow}>
          <Pressable style={styles.rememberRow} onPress={() => setRememberMe((v) => !v)}>
            <View style={[styles.switchTrack, rememberMe && styles.switchTrackOn]}>
              <View style={[styles.switchKnob, rememberMe && styles.switchKnobOn]} />
            </View>
            <Text style={styles.rememberText}>Keep me signed in</Text>
          </Pressable>
          <Pressable
            onPress={() =>
              Alert.alert("Forgot password", "Contact your club administrator to reset your password.")
            }
          >
            <Text style={styles.link}>Forgot password</Text>
          </Pressable>
        </View>

        <Pressable style={styles.primaryButton} disabled={submitting} onPress={handleSubmit}>
          {submitting && <ActivityIndicator size="small" color={colors.accentText} style={{ marginRight: 10 }} />}
          <Text style={styles.primaryButtonText}>{submitting ? "Verifying" : "Sign in"}</Text>
        </Pressable>

        <Text style={styles.footer}>Comp · v1.0.0</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  center: { flex: 1, justifyContent: "center", alignItems: "center", backgroundColor: colors.background },
  hero: { height: "38%", overflow: "hidden" },
  heroImage: {
    position: "absolute",
    left: "50%",
    top: "45%",
    width: 520,
    height: 520,
    transform: [{ translateX: -260 }, { translateY: -260 }, { rotate: "-25deg" }],
    opacity: 0.85,
  },
  brandRow: {
    position: "absolute",
    left: 28,
    right: 28,
    bottom: 24,
    flexDirection: "row",
    alignItems: "center",
    gap: 13,
  },
  badge: {
    width: 46,
    height: 46,
    borderRadius: 10,
    backgroundColor: colors.accent,
    alignItems: "center",
    justifyContent: "center",
  },
  badgeText: { fontFamily: fonts.extrabold, fontSize: 18, color: colors.accentText, letterSpacing: -0.5 },
  wordmark: { fontFamily: fonts.extrabold, fontSize: 17, letterSpacing: 3, color: colors.textPrimary },
  tagline: {
    marginTop: 5,
    fontFamily: fonts.monoMedium,
    fontSize: 9,
    letterSpacing: 2,
    textTransform: "uppercase",
    color: colors.textSecondary,
  },
  form: { flex: 1, paddingHorizontal: 28, paddingTop: 26, gap: 18 },
  heading: { fontFamily: fonts.semibold, fontSize: 23, color: colors.textPrimary },
  field: { gap: 7 },
  label: {
    fontFamily: fonts.monoBold,
    fontSize: 10,
    letterSpacing: 1.5,
    textTransform: "uppercase",
    color: colors.textMuted,
  },
  input: {
    height: 50,
    backgroundColor: colors.inputBg,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 9,
    paddingHorizontal: 15,
    fontFamily: fonts.body,
    fontSize: 15.5,
    color: colors.textPrimary,
  },
  passwordInput: { paddingRight: 70 },
  showButton: {
    position: "absolute",
    right: 6,
    top: 6,
    height: 38,
    paddingHorizontal: 12,
    borderRadius: 7,
    alignItems: "center",
    justifyContent: "center",
  },
  showButtonText: { fontFamily: fonts.monoBold, fontSize: 9.5, letterSpacing: 1.5, color: colors.textSecondary },
  errorBanner: {
    flexDirection: "row",
    gap: 9,
    alignItems: "flex-start",
    padding: 12,
    borderRadius: 8,
    backgroundColor: colors.errorBg,
    borderWidth: 1,
    borderColor: colors.errorBorder,
  },
  errorDot: { width: 6, height: 6, borderRadius: 3, backgroundColor: colors.error, marginTop: 6 },
  errorText: { flex: 1, fontFamily: fonts.body, fontSize: 12.5, lineHeight: 18, color: colors.errorText },
  optionsRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  rememberRow: { flexDirection: "row", alignItems: "center", gap: 10 },
  switchTrack: {
    width: 38,
    height: 22,
    borderRadius: 11,
    padding: 3,
    backgroundColor: colors.borderStrong,
  },
  switchTrackOn: { backgroundColor: colors.accent },
  switchKnob: { width: 16, height: 16, borderRadius: 8, backgroundColor: "#fff" },
  switchKnobOn: { transform: [{ translateX: 16 }] },
  rememberText: { fontFamily: fonts.body, fontSize: 12.5, color: colors.textSecondary },
  link: { fontFamily: fonts.medium, fontSize: 12.5, color: colors.accent },
  primaryButton: {
    height: 54,
    borderRadius: 10,
    backgroundColor: colors.accent,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
  },
  primaryButtonText: {
    fontFamily: fonts.bold,
    fontSize: 14.5,
    letterSpacing: 1.5,
    textTransform: "uppercase",
    color: colors.accentText,
  },
  footer: {
    marginTop: "auto",
    marginBottom: 16,
    textAlign: "center",
    fontFamily: fonts.mono,
    fontSize: 10,
    color: colors.textMuted,
  },
});
