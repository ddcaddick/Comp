import { BrowserRouter, Navigate, Outlet, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthProvider, useAuth } from "./lib/auth";
import { AppShell } from "./components/layout/AppShell";
import { LoginPage } from "./routes/LoginPage";
import { ShootersPage } from "./routes/ShootersPage";
import { CompetitionsPage } from "./routes/CompetitionsPage";
import { EventsPage } from "./routes/EventsPage";
import { LeaguesPage } from "./routes/LeaguesPage";
import { LeagueRosterPage } from "./routes/LeagueRosterPage";

const queryClient = new QueryClient();

function ProtectedLayout() {
  const { isAuthenticated } = useAuth();
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route element={<ProtectedLayout />}>
              <Route path="/" element={<Navigate to="/shooters" replace />} />
              <Route path="/shooters" element={<ShootersPage />} />
              <Route path="/competitions" element={<CompetitionsPage />} />
              <Route path="/competitions/:competitionId/events" element={<EventsPage />} />
              <Route path="/competitions/:competitionId/leagues" element={<LeaguesPage />} />
              <Route path="/competitions/:competitionId/leagues/:leagueId" element={<LeagueRosterPage />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}
