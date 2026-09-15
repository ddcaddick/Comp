import createClient from "openapi-fetch";
import type { paths } from "@comp/api-types";
import { router } from "expo-router";
import { clearTokens, getTokens, setTokens } from "./tokenStore";

// The Android emulator's alias for the host machine's localhost. A real device on the
// same network instead needs EXPO_PUBLIC_API_BASE_URL set to the dev machine's LAN
// address — see the "dotnet run ... --launch-profile lan" command in docs/CLAUDE.md.
const baseUrl = process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://10.0.2.2:5200";

export const api = createClient<paths>({ baseUrl });

// Serialises concurrent refresh attempts into one shared in-flight call.
let refreshPromise: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const { refreshToken } = getTokens();
  if (!refreshToken) {
    return false;
  }

  const { data, error } = await api.POST("/auth/refresh", { body: { refreshToken } });
  if (error || !data) {
    await clearTokens();
    return false;
  }

  await setTokens(data.accessToken, data.refreshToken);
  return true;
}

api.use({
  onRequest({ request }) {
    const { accessToken } = getTokens();
    if (accessToken) {
      request.headers.set("Authorization", `Bearer ${accessToken}`);
    }
    return request;
  },
  async onResponse({ request, response }) {
    const isAuthEndpoint = request.url.endsWith("/auth/login") || request.url.endsWith("/auth/refresh");
    if (response.status !== 401 || isAuthEndpoint) {
      return response;
    }

    refreshPromise ??= refreshAccessToken().finally(() => {
      refreshPromise = null;
    });
    const refreshed = await refreshPromise;

    if (!refreshed) {
      router.replace("/");
      return response;
    }

    // As on web: a GET is safe to silently retry, a body-bearing request that hit the
    // 15-minute token boundary just surfaces its original 401 (the run-save screen
    // reports the failure and the official retries — see the entry screen's save
    // handler for what that looks like against an idempotency key).
    if (request.method !== "GET") {
      return response;
    }

    const { accessToken } = getTokens();
    const retryHeaders = new Headers(request.headers);
    retryHeaders.set("Authorization", `Bearer ${accessToken}`);
    return fetch(request.url, { headers: retryHeaders });
  },
});
