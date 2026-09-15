import * as SecureStore from "expo-secure-store";

const ACCESS_TOKEN_KEY = "comp.accessToken";
const REFRESH_TOKEN_KEY = "comp.refreshToken";

// Kept in memory so the API client's onRequest hook (which must run synchronously
// per fetch call) never has to await SecureStore itself. SecureStore is the
// durable copy, hydrated into memory once at startup by loadPersistedTokens.
let accessToken: string | null = null;
let refreshToken: string | null = null;

export function getTokens() {
  return { accessToken, refreshToken };
}

export async function setTokens(nextAccessToken: string, nextRefreshToken: string) {
  accessToken = nextAccessToken;
  refreshToken = nextRefreshToken;
  await Promise.all([
    SecureStore.setItemAsync(ACCESS_TOKEN_KEY, nextAccessToken),
    SecureStore.setItemAsync(REFRESH_TOKEN_KEY, nextRefreshToken),
  ]);
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
