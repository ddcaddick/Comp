import createClient from "openapi-fetch";
import type { paths } from "@comp/api-types";

const ACCESS_TOKEN_KEY = "comp.accessToken";
const REFRESH_TOKEN_KEY = "comp.refreshToken";

export function getTokens() {
  return {
    accessToken: localStorage.getItem(ACCESS_TOKEN_KEY),
    refreshToken: localStorage.getItem(REFRESH_TOKEN_KEY),
  };
}

export function setTokens(accessToken: string, refreshToken: string) {
  localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
}

export function clearTokens() {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5200";

export const api = createClient<paths>({ baseUrl });

// Serialises concurrent refresh attempts into one shared in-flight call.
let refreshPromise: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const { refreshToken } = getTokens();
  if (!refreshToken) {
    return false;
  }

  const { data, error } = await api.POST("/auth/refresh", {
    body: { refreshToken },
  });
  if (error || !data) {
    clearTokens();
    return false;
  }

  setTokens(data.accessToken, data.refreshToken);
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
      clearTokens();
      window.location.assign("/login");
      return response;
    }

    // A GET has no body to worry about re-sending; a body-bearing request that hit the
    // 15-minute token boundary just surfaces its original 401 — tokens are refreshed for
    // the user's next action, but retrying an already-sent body safely needs more care
    // than this first pass takes on.
    if (request.method !== "GET") {
      return response;
    }

    const { accessToken } = getTokens();
    const retryHeaders = new Headers(request.headers);
    retryHeaders.set("Authorization", `Bearer ${accessToken}`);
    return fetch(request.url, { headers: retryHeaders });
  },
});
