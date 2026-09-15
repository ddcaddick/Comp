import * as SecureStore from "expo-secure-store";

const ACCESS_TOKEN_KEY = "comp.accessToken";
const REFRESH_TOKEN_KEY = "comp.refreshToken";

// Kept in memory so the API client's onRequest hook (which must run synchronously
// per fetch call) never has to await SecureStore itself. SecureStore is the
// durable copy, hydrated into memory once at startup by loadPersistedTokens.
let accessToken: string | null = null;
let refreshToken: string | null = null;

// Remembers the sign-in screen's "Keep me signed in" choice so a later token refresh
// (which has no reason of its own to know it) keeps honouring it.
let persistEnabled = true;

export function getTokens() {
  return { accessToken, refreshToken };
}

/**
 * `persist: false` (the sign-in screen's "Keep me signed in" switch, off) keeps the
 * session working for this app launch only — the tokens still live in memory so every
 * request and the refresh flow behave normally, but nothing is written to SecureStore,
 * and anything from a previous persisted session is explicitly cleared so it can't come
 * back on the next launch. Omit `persist` (as every refresh does) to keep whatever the
 * most recent explicit choice was.
 */
export async function setTokens(nextAccessToken: string, nextRefreshToken: string, persist?: boolean) {
  if (persist !== undefined) {
    persistEnabled = persist;
  }
  accessToken = nextAccessToken;
  refreshToken = nextRefreshToken;
  if (persistEnabled) {
    await Promise.all([
      SecureStore.setItemAsync(ACCESS_TOKEN_KEY, nextAccessToken),
      SecureStore.setItemAsync(REFRESH_TOKEN_KEY, nextRefreshToken),
    ]);
  } else {
    await Promise.all([SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY), SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY)]);
  }
}

export async function clearTokens() {
  accessToken = null;
  refreshToken = null;
  await Promise.all([
    SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY),
    SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY),
  ]);
}

export async function loadPersistedTokens() {
  const [storedAccessToken, storedRefreshToken] = await Promise.all([
    SecureStore.getItemAsync(ACCESS_TOKEN_KEY),
    SecureStore.getItemAsync(REFRESH_TOKEN_KEY),
  ]);
  accessToken = storedAccessToken;
  refreshToken = storedRefreshToken;
  return { accessToken, refreshToken };
}
