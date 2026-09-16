# Shooting Competition Management System

A system of record for a weekly timed shooting competition. Officials record run times on a
phone at the range; administrators manage the register, events and league tables on the web.

Full design: `docs/architecture.md`. Read it before any structural change.

## Stack

- **Backend:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 16
- **Web admin:** React 19 + Vite, TanStack Query, TanStack Table, React Router, Tailwind, shadcn/ui,
  `lucide-react` (nav/UI icons), `@react-pdf/renderer` (the one generated-PDF exception, D12)
- **Mobile:** Expo / React Native, TypeScript, `@expo/vector-icons`. Prototyping phase targets
  **Android only**, as a sideloadable APK via EAS Build's internal-distribution profile — no
  Apple/Google developer account needed yet. Store submission on both platforms is still the
  eventual goal; it's deferred to a later milestone (folded into M10), not dropped. See the
  architecture doc's decision D11.
- **Shared clients code:** pnpm workspace under `clients/`

## Non-negotiable rules

These are architectural decisions, not preferences. If a task seems to require breaking one,
stop and say so rather than working around it.

1. **Scoring exists only in `Comp.Scoring`.** Never calculate an adjusted time, an event time,
   a position or league points anywhere else — not in the API layer, not in a client, not in
   a SQL query. A second implementation will eventually disagree with the first, and the
   disagreement surfaces as a wrong league table.

2. **`Comp.Scoring` and `Comp.Domain` reference nothing.** No EF Core, no ASP.NET, not each
   other. `Comp.Application` maps domain entities into the plain records `Comp.Scoring`
   accepts.

3. **Neither client scores.** The web app and the mobile app display what the server returns.
   No "live provisional table" computed on the device.

4. **Times are integer milliseconds.** No floating point anywhere near a result. Parsing and
   formatting lives in `clients/packages/core/src/time.ts` and nowhere else.

5. **Penalties are stored as a count, not as seconds.** The seconds each one costs comes from
   the event, so the raw fact stays independent of the rule.

6. **Every write to competition data is audited.** The EF Core interceptor handles this. Never
   bypass it with raw SQL or `ExecuteUpdate` for entity changes.

7. **Nothing is hard deleted.** Shooters deactivate. Runs are corrected, never removed.

8. **Finalised events are locked.** Writes are blocked by the service layer and by a database
   trigger. Amendment moves the event out of `Finalised` first, with a privilege check and a
   recorded reason.

9. **League standings are never stored.** They are a query over `event_results`. This is why
   amending a result recalculates the tables for free — do not "optimise" it into a table.

10. **`league_id` on `event_participants` is a snapshot.** Do not replace it with a join
    through `league_memberships` at read time.

## Conventions

- Database is snake_case via `EFCore.NamingConventions`. Check constraints are written against
  snake_case column names.
- Enums are stored as text, not integers.
- Entity ids are `Guid.CreateVersion7()` for index locality.
- Migrations are forward-only and additive where possible; a rollback must never require a
  down migration during a live event.
- Client-side lint and format is Biome. C# follows `.editorconfig`.

## Commands

```powershell
docker compose up -d                      # database
dotnet run --project src/Comp.Api         # API on :5200
dotnet run --project src/Comp.Api --launch-profile lan   # reachable from a phone
pnpm --filter web dev                     # web app on :5173
pnpm --filter mobile start                # Expo — press a for an Android emulator, or scan the QR with Expo Go
dotnet test                               # backend tests
pnpm -r test                              # client tests
```

The Android emulator reaches the API at `http://10.0.2.2:5200` (its built-in alias for the host
machine's localhost) — `clients/mobile/src/lib/api.ts` defaults to that. A real device on the
same Wi-Fi instead needs `EXPO_PUBLIC_API_BASE_URL` set to the dev machine's LAN address, with
the API started via the `lan` launch profile above.

Building the Android APK (decision D11 — internal distribution, no Play Console account):

```powershell
cd clients/mobile
npx eas login                             # one-time, needs a free Expo account
npx eas build --platform android --profile internal
```

`eas-cli` is a devDependency of `clients/mobile` (not a global install) so `npx eas` always
resolves to the pinned version — `npx eas` alone (no `-cli` suffix) is correct once it's
installed; `npx eas-cli login` also works and is what you'd need before it was added here.

Migrations:

```powershell
dotnet ef migrations add <Name> --project src/Comp.Infrastructure --startup-project src/Comp.Api
dotnet ef database update --project src/Comp.Infrastructure --startup-project src/Comp.Api
```

Regenerate the mobile client's API types after changing a `Comp.Contracts` DTO or an
endpoint's shape (CI fails the build if this drifts from what's committed):

```powershell
cd tools/generate-api-types
npm run generate
```

## Deployment (staging)

A staging deployment exists so the web app and a real APK can both be tested from outside
the local network — this is deliberately **staging only**, not a production setup; see the
architecture doc's roadmap for what M9/M10 still need before this is a real go-live.

**Render** (chosen over a VPS or Azure for this stage: least setup effort, a free tier is
enough for "can people reach this," and it deploys straight from GitHub with no new CI/CD
work). `render.yaml` at the repo root is a Blueprint defining all three pieces:

- `comp-staging-db` — a managed Postgres database.
- `comp-api-staging` — the API, built from `src/Comp.Api/Dockerfile` as a Docker web
  service.
- `comp-web-staging` — the web admin, a static site built with
  `pnpm --filter web build` and served with a catch-all rewrite to `index.html` (React
  Router routes client-side; without the rewrite, refreshing on anything but `/` 404s).

One-time setup: push `render.yaml` to the repo, then in the Render dashboard choose
**New → Blueprint** and point it at this repo. Render proposes all three resources above.
It will prompt for `InitialAdmin:Email` (a real email you choose — never commit this);
everything marked `generateValue: true` in `render.yaml` (the JWT signing key, the initial
admin's password) is generated by Render itself and never appears in git. After the first
deploy finishes, the API is reachable at `https://comp-api-staging.onrender.com/health` and
the web admin at `https://comp-web-staging.onrender.com`, sign-in with whatever email you
gave it and the generated password from Render's dashboard.

Redeploying is automatic — Render redeploys both services on every push to the connected
branch, same as any other PaaS Blueprint.

Building an APK that talks to staging instead of your LAN (still the same `internal` EAS
profile as local testing — see the Commands section above):

```powershell
cd clients/mobile
npx eas build --platform android --profile internal
```

The `internal` profile's `env.EXPO_PUBLIC_API_BASE_URL` in `eas.json` is what actually sets
the URL baked into the APK — **not** a local shell env var. `eas build` (without `--local`)
runs on Expo's own servers, which have no visibility into this machine's environment at all;
a `$env:EXPO_PUBLIC_API_BASE_URL` set in PowerShell before running the command above does
nothing for a cloud build (confirmed by EAS's own "No environment variables found for the
'preview' environment" message when a build has neither this nor an EAS-hosted env var
configured — it's not a benign notice, it means the build is about to silently embed the
default `10.0.2.2` emulator-only address instead). To point a build at somewhere other than
this staging URL (e.g. back at a LAN address for on-site testing), edit `eas.json`'s value
and rebuild, or use `eas env:create` to manage it from EAS's dashboard instead of a committed
file.

**Two real, environment-specific things `Program.cs` now handles that local dev never
exercises**, both found by actually running the built Docker image against a real Postgres
before trusting it on Render, not just from a successful `docker build`:

- **CORS origins are configurable** (`Cors:AllowedOrigins`, comma-separated; defaults to
  `http://localhost:5173` when unset) rather than hardcoded, so staging can allow its own
  web origin instead of localhost.
- **The Postgres connection string is normalised.** Render (and most managed-Postgres
  hosts) hand out a `postgres://user:pass@host/db` URI, which `UseNpgsql` cannot parse
  directly — `NormalizeConnectionString` converts it to the `Host=...;` form Npgsql expects,
  only when the configured string actually looks like a URI, so the local `Host=...`
  connection string in `appsettings.Development.json` passes through completely unchanged.
- **The base runtime image was missing `libgssapi-krb5-2`.** Npgsql negotiates GSS
  encryption as part of connecting, even though this app only ever authenticates by
  password — without that library, connecting failed with a native
  `"cannot open shared object file"` error before Postgres was even involved, on *any*
  connection, SSL or not. `mcr.microsoft.com/dotnet/aspnet:10.0` doesn't include it; the
  Dockerfile's runtime stage now installs it via `apt-get`. This would have broken the real
  Render deployment identically to how it broke a local container run — caught here only
  because the image was actually run against a real Postgres locally first.
- **Startup migrations and the initial-admin seed are both now configurable, not just
  Development-gated.** `RunMigrationsOnStartup=true` runs `Database.MigrateAsync()` once at
  boot (a safe no-op once the schema is current — there's no shell access to a Render
  service to run `dotnet ef database update` by hand). `SeedInitialAdmin=true` plus
  `InitialAdmin:Email`/`:Password` bootstraps exactly one Super Admin the same way the
  local dev seed always has, but **never** with the fixed `dev-admin@comp.local` /
  `Comp1234!` local convenience — those credentials are effectively public (this very file
  documents them), and staging is reachable from the internet. Neither flag does anything
  unless explicitly set, so local dev and `Comp.Api.Tests` are completely unaffected —
  verified by rerunning the full 88-test suite after these changes.

**Known limitations of Render's free tier** (fine for "quick staging test," worth knowing
about): the web service spins down after ~15 minutes idle and takes 30–50 seconds to wake on
the next request; the free Postgres plan is deleted after 30 days, so upgrade it before then
if staging needs to keep accumulating data past that point; and the mobile APK bakes in a
fixed URL at build time, so renaming or recreating `comp-api-staging` means rebuilding the
APK to match.

## Testing expectations

`Comp.Scoring` and `clients/packages/core` need near-total coverage, including property-based
tests and golden fixtures taken from real past events. Everywhere else, be pragmatic.

A wrong league table discovered in November, after ten months of events, is the worst outcome
this project has available to it. Write the test.

## Current position

**At a glance:** M0–M8 of the architecture doc's roadmap are complete — schema/audit/Identity,
auth, the shooter register, competitions and leagues, the scoring engine, events/participants/
squads, live result entry, and results/finalisation/standings — across the API, the web admin
and the mobile app, each verified end-to-end on a real Android emulator/Expo Go session rather
than trusted from typecheck and build output alone. Beyond the milestone plan itself: a
generated results PDF (decision D12), a Home screen and real navigation on both clients (bottom
tabs + icons on mobile, a matching Home page + nav icons on web), and a squad-allocation
workflow (locking a squad's roster, arrival-ordered sign-on) added after actually using the
mobile app for a real event's sign-on. Remaining: **M9** (hardening — CSV export, print
stylesheets, rate limiting, an error-handling pass, a backup/restore test) and **M10** (pilot —
the actual EAS Android build and store submission, per D11). The detailed log below is in the
order it was built; skip to whatever milestone or feature name you need.

Milestone M2 is complete. Delivered:

- **Domain and schema.** All entities from the architecture doc's section D exist in
  `Comp.Domain`, mapped by `Comp.Infrastructure/CompDbContext.cs` and the per-entity
  configurations under `Comp.Infrastructure/Configurations/`. Migration `InitialSchema` is
  applied to the local database and verified in psql: snake_case tables, every check
  constraint, the `pg_trgm` search index on `shooters`, and the finalised-event trigger on
  `runs`. See `docs/m2-wiring.md` for how this was wired up (its steps are complete).
- **Audit interceptor.** `AuditSaveChangesInterceptor` writes an `AuditLog` row for every
  tracked create/update/delete, actor from `ICurrentUserAccessor`, excludes operational
  tables (`entry_sessions`, `refresh_tokens`, Identity's join/token tables), strips
  `PasswordHash`/`SecurityStamp` from anything it audits, and honours a
  `CompDbContext.PendingAuditReason` for amendments. Tested against real Postgres in
  `tests/Comp.Api.Tests/AuditSaveChangesInterceptorTests.cs`.
- **Identity and roles.** Migration `AddIdentity` adds `AppUser` (extends
  `IdentityUser<Guid>` with `DisplayName`, `CanAmendPublished`, `IsActive`), the four roles
  (`SUPER_ADMIN`, `ADMIN`, `OFFICIAL`, `READ_ONLY`) seeded by the migration itself, and
  `refresh_tokens`. `AppUserClaimsPrincipalFactory` turns `CanAmendPublished` into an
  `amend_published` claim at sign-in, and a `CanAmendPublished` authorisation policy is
  registered in `Program.cs`. Tested in `tests/Comp.Api.Tests/IdentityTests.cs`.
- **Auth endpoints.** `POST /auth/login` and `POST /auth/refresh` are live. `AuthService`
  (`Comp.Infrastructure/Identity/AuthService.cs`) checks credentials via `UserManager`,
  enforces Identity's lockout after repeated failures, issues a 15-minute JWT access token
  (`JwtAccessTokenGenerator`) plus a 90-day opaque refresh token stored only as a SHA-256
  hash (`RefreshTokenGenerator`). Refresh rotates the token and revokes the old one; presenting
  an already-rotated token revokes the whole chain for that user (theft signal). Both
  endpoints run FluentValidation via `ValidationFilter<T>` and are rate-limited per client IP
  (5/minute). Login never distinguishes "no such account" from "wrong password" in what it
  returns. Tested end-to-end via `WebApplicationFactory` + Testcontainers in
  `tests/Comp.Api.Tests/AuthEndpointTests.cs`.
  - Note: a flow with no `ClaimsPrincipal` yet that still needs to write to an audited
    entity (e.g. login updating the account's own lockout counters) sets
    `CompDbContext.PendingActorOverride` to that user's id before saving, so the audit
    interceptor's no-current-user guard doesn't fire. Follow this pattern for any future
    anonymous-but-self-attributable write; the guard itself is not to be relaxed.
- **OpenAPI type generation in CI.** `Comp.Api` writes its OpenAPI document to
  `/openapi/Comp.Api.json` as a build step (`Microsoft.Extensions.ApiDescription.Server`,
  configured via `OpenApiDocumentsDirectory` in `Comp.Api.csproj` — gitignored, regenerated
  every build). `tools/generate-api-types` turns that into
  `clients/packages/api-types/src/index.ts` via `openapi-typescript`'s JS API (not its CLI,
  to avoid shelling out); the generator itself is a standalone npm package, not part of the
  `clients/` pnpm workspace, but its output **is** a workspace package (`@comp/api-types`)
  that both `web` and `mobile` depend on. (Originally generated straight into
  `clients/mobile/api/`, back when the admin app was still going to be Blazor WASM sharing
  `Comp.Contracts` directly — moved to a shared package once `web` also needed it in
  TypeScript, before mobile existed to have a path baked in against it.) The
  `openapi-types` job in `.github/workflows/ci.yml` runs the generator and fails the build
  on any diff against the committed `index.ts`. If a PR changes a `Comp.Contracts` DTO or
  an endpoint's shape, run `npm run generate` in `tools/generate-api-types` and commit the
  result.

**Milestone M3 (register, competitions, leagues) is complete.** Delivered:

- **Shooter endpoints.** `GET /shooters` (search — `q`, `active`, `recentFirst`; any
  authenticated role), `GET /shooters/{id}/history` (audit trail; any authenticated role),
  `POST /shooters` (Super Admin/Admin/Official), `PATCH /shooters/{id}`,
  `POST /shooters/{id}/deactivate`, `POST /shooters/{id}/reactivate` (Super Admin/Admin
  only), matching the security model in the architecture doc's section K. The search query
  is written to match `ix_shooters_search`'s indexed expression exactly (verified with
  `EXPLAIN`/`enable_seqscan = off` that the index is actually usable by it — Postgres
  correctly prefers a seq scan at the current tiny table size, which is not a bug).
  `active` is not filtered by default (only the future participant-selection workflow
  should default to active-only, per `docs/m2-wiring.md`'s "what is deliberately absent").
  `recentFirst` currently just means "recently created"; it should probably mean
  "recently entered in an event" once M6 (events, participants) exists. Deactivate/
  reactivate are idempotent and don't audit a no-op. Tested end-to-end, including the
  mandatory negative authorization cases (an Official must not edit/deactivate a shooter,
  a Read-only user must not write at all), in
  `tests/Comp.Api.Tests/ShooterEndpointTests.cs`.
  - `Comp.Api/Endpoints/` now holds one static class per resource
    (`MapAuthEndpoints`/`MapShooterEndpoints`) registered from `Program.cs`, rather than
    endpoints living inline — follow this pattern for the next resource.
  - Fixed along the way: `HttpContextCurrentUserAccessor` was reading
    `ClaimTypes.NameIdentifier`, but `JwtAccessTokenGenerator` only ever issued a `sub`
    claim, and whether the JWT bearer handler remaps one to the other depends on
    `MapInboundClaims`. This was never exercised until an endpoint actually required a
    bearer token — every authenticated write would have silently hit the audit
    interceptor's "no current user" guard. Fixed by setting `MapInboundClaims = false` in
    `Program.cs` and reading `sub` (falling back to `NameIdentifier`) in the accessor.
- **Competition and league endpoints.** `GET/POST /competitions`,
  `POST /competitions/{id}/close`, `GET/POST /competitions/{id}/leagues`,
  `GET/PUT /leagues/{id}/members` — writes are Super Admin only, reads are any
  authenticated role, per section K. Closing a competition is one-way (no reopen
  endpoint); closing an already-closed one is a 409, not a silent no-op. A closed
  competition also locks adding new leagues and changing membership in them.
  `PUT /leagues/{id}/members` is a true full-replace: a shooter dropped from the list is
  removed from that league outright (a membership row is current status, not a permanent
  record like a run); a shooter already in a *different* league in the same competition is
  moved rather than rejected — the no-mid-year-move rule only protects
  `EventParticipant.LeagueId`'s historical snapshot, not the current `LeagueMembership`
  row. Enforces the 20-member cap from the architecture doc's decision D4. Tested in
  `tests/Comp.Api.Tests/CompetitionEndpointTests.cs` and `LeagueEndpointTests.cs`.
  - Fixed along the way: `LeagueService`'s member-listing query did
    `.Join(...).OrderBy(response => response.LastName)` — ordering on a property of an
    already-projected record isn't translatable and threw at runtime (missed by the
    build, only caught by the test actually calling the endpoint). Order on the raw
    joined columns, then `.Select()` into the DTO — see `LoadMembersAsync`.
- **Web admin shell.** `clients/web` — Vite + React 19 + TypeScript, React Router,
  TanStack Query, Tailwind v4 (`@tailwindcss/vite`, no `tailwind.config.js` needed), and
  shadcn-style components (`cn` + `class-variance-authority`, not the shadcn CLI itself —
  see below). A login screen (`routes/LoginPage.tsx`) calls `/auth/login` and stores
  tokens in `localStorage`; `lib/api.ts` is an `openapi-fetch` client typed against
  `@comp/api-types` that attaches the bearer token to every request and does a
  single-shot refresh-and-retry on a 401 for GET requests (a body-bearing request that
  hits the 15-minute token boundary surfaces its 401 as-is — retrying an already-sent
  body safely needs more care than this first pass took on). `routes/ShootersPage.tsx`
  proves the whole chain end-to-end: search box → TanStack Query → typed fetch → TanStack
  Table. Verified: `pnpm --filter web build`, `typecheck`, and `test` all pass; the dev
  server serves on :5173; the API's CORS preflight actually allows that origin (checked
  live, not just via the CORS config existing).
  - `@tanstack/react-table` is pinned to `^8.9.9`, not the newly-published `9.x` — v9
    replaced the familiar `useReactTable`/`getCoreRowModel`/`createColumnHelper` API with
    a new reactive-store-based one (`useTable`, `createTableHook`), and even v9's own
    `/legacy` compatibility layer marks every v8-style export deprecated. Don't float to
    `^9` without deliberately learning and adopting the new API — the deprecated shim
    isn't worth building fresh code on.
  - **shadcn/ui scope note:** this uses shadcn's *component pattern* (the CSS variable
    theme layer in `index.css`, `cn`, `cva`-based variants) rather than the actual shadcn
    CLI, which is inherently interactive (prompts for style/base color/etc.) and wasn't
    run here. Running `npx shadcn init`/`add` for real, or hand-building more primitives
    matching this same convention, are both reasonable next steps — just don't assume
    `components.json` or a shadcn-managed component tree exists yet.
  - Moved `clients/mobile/api/api-types.ts` to the new `@comp/api-types` workspace
    package (see the OpenAPI bullet above) so `web` could depend on it without reaching
    into a sibling app's folder.

Not yet built for M3: nothing — promotion/relegation isn't in M3's scope per the roadmap
(the acceptance criterion only requires shooters assigned across leagues, not promoted
between competitions) and stays deferred unless asked for explicitly. There's also no
"activate" transition for a competition (Planning → Active) — the API design doesn't call
for one, so competitions stay in `Planning` until something needs `Active` specifically.

**`clients/packages/core/src/time.ts` is now implemented** (it and its test file had been
empty stubs since M0, silently contradicting rule 4 and the testing-expectations section —
found while verifying `pnpm -r test` during the web shell work, above). `formatMillis`,
`parseTime` and `digitsToMillis` cover the full round trip described in the architecture
doc: display/entry is `m:ss.cc` (D2), and the custom keypad's right-to-left digit fill
(section J's own example, "24256" → "2:42.56") is implemented and tested as its own
function rather than duplicated per-client. `formatMillis` throws on invalid input (a
programming error — it takes a value already known to be a valid time); `parseTime`
returns `null`, never throws, since it's the boundary for untrusted text. 27 tests in
`time.test.ts`: examples, boundaries (the 60-second and 60-minute rollovers, rounding to
the nearest centisecond), and `fast-check` property tests (round-trip through both
`formatMillis`/`parseTime` and `digitsToMillis`, plus a fuzzed-input safety property on
`digitsToMillis`). `core`'s `package.json` also had its `typescript`/`vitest`
devDependencies lost at some point (silently working off whatever `web` happened to hoist)
and an empty `tsconfig.json` (so `tsc --noEmit` type-checked nothing) — both fixed.

**Milestone M5 (scoring engine) is complete.** `Comp.Scoring` implements the architecture
doc's section I exactly, with one necessary extension:

- **`EventScorer.Score(participants, eventRules, leagueRules)`** — the doc's illustrative
  signature only takes one `EventRules`, but a single event scores for every league in
  its competition simultaneously, each potentially configured with a different
  `PointsForFirst`/`PointsDecrement`. `Score` takes a `IReadOnlyDictionary<Guid,
  LeagueRules>` keyed by league instead, and throws if a participant's snapshotted league
  has no entry in it. The D3 tie-break (compare the other run, faster wins; two valid
  runs beats one; still equal, share the position) is implemented as a single sort key
  (`EventTimeMs`, then whether there's a valid other run, then its value) rather than a
  separate comparison pass — a true tie shares the position via standard competition
  ranking (1, 2, 2, 4), applied identically to the overall table and to each league's.
  `BestRunNumber` identifies the counting run by its `RunNumber`, not a database id — this
  library has no concept of one; the caller maps back to the actual `Run` row via
  (ParticipantId, BestRunNumber). No points floor (D4): the raw formula is never clamped.
- **`StandingsCalculator.Calculate(points, leagueRules, eventsHeld)`** — `AbsencesCountAsZero`
  is handled inside the calculator (pads a shooter's entries to `eventsHeld` with zeros)
  rather than pushed onto every caller; when it's `false`, a missed event is simply
  absent from that shooter's own list and doesn't count against or for them at all. The
  drop rule is continuous (D7): with `dropWorstCount = N`, standings stay provisional
  through event `N`, and from event `N+1` on always exclude the worst `N` to date.
  Standings ties share position too, with no secondary tie-break (none is specified,
  unlike event-night ties which have D3).
- **Tests** (`tests/Comp.Scoring.Tests`, 21 tests): every edge case the testing-strategy
  table names explicitly (both DNF, one DNF, single run, exact ties by each tie-break
  step, zero and twenty penalties, absences with and without `AbsencesCountAsZero`, the
  drop boundary at exactly `dropCount + 1`), `FsCheck` property tests for the four named
  invariants (event time never slower than the best adjusted run; adding a penalty never
  improves a position; counting total never exceeds running total; standings stable under
  input reordering), a synthetic regression case covering the tie-break and DNF scenarios
  the real fixture below happens not to contain, and **a real golden fixture**:
  `RealEventGoldenFixtureTests` loads `Fixtures/2026-09-14-wednesbury-marksmen.json` (a
  real scorecard the user supplied — the Wednesbury Marksmen Mini Rifle Competition,
  46 shooters across 5 divisions, no penalties or DNFs recorded) and asserts
  `EventScorer` reproduces every one of its overall positions and division
  positions/points exactly. Per the testing-strategy table's "xUnit + committed JSON"
  convention, the fixture data lives in its own JSON file, not hardcoded in the test —
  add more real events as more JSON files under `Fixtures/` and a fact per file, following
  this one's shape (`name`, `division`, `timeMs`, `overallPosition`, `divisionPosition`,
  `divisionPoints`).
- Deliberately not built in M5: persisting `EventScoringResult`/`LeagueStanding` into
  `EventResult` rows, or any endpoint calling this library at all — that's M8 (results,
  finalisation, standings), which needs events and runs to exist first (M6, M7).

**Milestone M6 (events, participants, squads) is complete.** `GET/POST /events`,
`PATCH /events/{id}`, `POST /events/{id}/transition`; `GET/POST /events/{id}/participants`,
`PATCH`/`DELETE .../participants/{pid}`; `GET/POST /events/{id}/squads`. Per section K:
creating/editing events is Super Admin/Admin only; managing participants and squads also
allows Official; all GETs are open to any authenticated role.

- **Transition is deliberately narrower than the full doc-described lifecycle**: it only
  moves one step at a time along Draft ↔ Setup ↔ InProgress ↔ Review (either direction,
  e.g. to undo an accidental advance), and refuses a "to" of Finalised outright —
  finalising needs the scoring engine wired in to actually compute and freeze results,
  which is M8. A finalised event (there's no way to reach one through the API yet;
  tests set it directly via the DbContext to prove the lock itself) refuses every write
  this milestone adds — participants, squads, and further transitions alike.
- **Adding a participant snapshots their current league membership** into
  `EventParticipant.LeagueId` at that moment, via a lookup at add time, never a live join
  — exactly the snapshot the architecture doc's section D calls for, so a later membership
  correction can't rewrite an earlier night's division tables. Requires the shooter to be
  active and not already entered in the event.
- **Moving a participant between squads** (`PATCH .../participants/{pid}`) is a full
  replace of the squad assignment, not a partial patch: omitting `squadId` unassigns them;
  giving a `squadId` with no position appends them to the end of it; an explicit position
  is set as-is, with **no renumbering of anyone else** — matching `docs/m2-wiring.md`'s
  "what is deliberately absent" (a duplicated position is a display-order glitch, not a
  corrupted result, so it doesn't justify the complexity of shifting other rows).
- **Removing a participant** is a real delete (not a soft one) — checked first against
  whether any `Run` rows already reference them (the FK is `Restrict`, so the database
  would refuse it anyway; this just gives a clear 409 instead of a raw constraint-violation
  500). A participant with no runs recorded yet carries no history worth protecting.
- Tested end-to-end in `tests/Comp.Api.Tests/EventEndpointTests.cs` and
  `EventParticipantAndSquadEndpointTests.cs` (22 tests): the transition state machine,
  the league snapshot, auto-appended vs. explicit squad positions, the finalised-event
  lock across every M6 write, and the negative-authorization cases. 66/66 passing overall.
- Deliberately not built in M6: `GET /events/{id}/squads/{sid}/runner` (the entry screen's
  source of truth) and the runs `PUT` endpoint — those are M7 (live result entry), which
  this milestone's schema and services are already shaped to support.

**Plan change (15 Sep 2026):** the architecture doc bumped to Version 4 — mobile deployment
re-phased to an Android-only, sideloadable-APK prototyping stage (decision D11), with store
submission on both platforms deferred to M10 rather than run in parallel from week 6. Nothing
about M1–M6's work changes; this only affects M4 and M9/M10's store-related steps. See the
doc's changelog and D11 for the full reasoning.

**Milestone M7 (live result entry) — backend and the mobile app are both complete; only the
actual EAS Android build is left, which needs the user's own Expo account.** `GET
/events/{id}/squads/{sid}/runner`, `PUT
/events/{id}/participants/{pid}/runs/{runNumber}`, `GET /events/{id}/entry-session`,
`POST /events/{id}/entry-session/heartbeat` (`Comp.Api/Endpoints/LiveEntryEndpoints.cs`).
Per section K: recording runs and the heartbeat are Super Admin/Admin/Official; the runner
view and the session GET are open to any authenticated role.

- **The runner view is the entry screen's only source of truth** (section G): it lists every
  participant in the squad with every run slot's recorded/DNF/time state, computed fresh
  from `Run` rows on every call — the app keeps no notion of progress of its own, which is
  what makes resuming the same squad from a second phone work with no dedicated code.
  `RunnerService.BuildParticipantResponse` builds one participant's view and is `internal
  static` specifically so `RunService` can reuse it for `NextOutstanding` without
  duplicating the shape.
- **Saving a run returns who's next** (`SaveRunResponse.NextOutstanding`) so the app never
  makes a separate round trip to ask. The advance algorithm
  (`RunService.FindNextOutstandingAsync`) is a necessary extension beyond the doc's
  pseudocode: it walks the squad in `PositionInSquad` order starting just after the
  participant who was just saved, first looking for anyone still missing *that same run
  number* (finish round 1 for the whole squad before anyone starts round 2), and only once
  every run number that round is settled falls back to the first participant with any
  outstanding run at all (loop back for run 2). Returns `null` once every participant's
  every run is recorded, or if the participant isn't in a squad at all.
- **Idempotency-Key header makes a retried save safe.** A dropped response after a
  successful write is indistinguishable from a genuinely failed one from the app's side; if
  the same key is presented again, `RunService` replays the original `Run` row and its
  original `NextOutstanding` rather than re-applying whatever the retry's body says — this
  is deliberate (a buggy or stale retry body can't silently overwrite a value), not just
  an optimisation. The key is stored on the `Run` row itself, one key per row (a genuine
  edit — same participant and run number, different save — gets no key or a new one, and
  simply overwrites in place; overwriting an already-recorded time is allowed outright, per
  section J, since the app confirms and shows the old value before calling this).
- **Run number range and the finalised-event lock are both checked in the service** (a
  clear 409 beats a raw constraint-violation 500 for the same cases the database trigger
  from `docs/m2-wiring.md` also backs up) — validated directly here rather than only
  through the trigger, matching the pattern M6 already established for its own writes.
- **Entry sessions are a single row per event** (`EntrySession`, keyed by `EventId`,
  upserted on every heartbeat) recording who's currently entering results and when they
  last confirmed it — not a history, just current occupancy, so a second official opening
  the same squad sees who's already on it. `GetAsync` returns an all-null response rather
  than 404 when no one has ever sent a heartbeat for an otherwise-real event.
- Tested end-to-end in `tests/Comp.Api.Tests/LiveEntryEndpointTests.cs` (12 tests): the
  round-robin advance across two full rounds, DNF vs. a required raw time, overwriting a
  run in place (one row, not two), idempotent replay ignoring a divergent retry body, the
  run-number-out-of-range and finalised-event conflicts, the negative-authorization case,
  and the entry-session heartbeat/read-back/unknown-event cases. 78/78 passing overall
  (66 pre-existing + 12 new).

**The mobile app** (`clients/mobile`) is a new pnpm workspace member — Expo Router, TypeScript,
SDK 57 — scaffolded via `create-expo-app` and then stripped back to just what section J's
screens need (its demo tabs/components/assets, its own nested `.git`, and its bundled
`AGENTS.md`/`CLAUDE.md` were all deleted; this repo's own `docs/CLAUDE.md` is the only one
that applies). Screens as originally built: sign-in (`app/index.tsx`) → event list
(`app/events/index.tsx`) → squad list (`app/events/[eventId]/index.tsx`) → squad runner
(`app/events/[eventId]/squads/[squadId]/index.tsx`) → entry
(`.../squads/[squadId]/entry.tsx`). (The event list later moved to `app/(tabs)/events.tsx`
when a Home screen and tab bar were added — see further down.)

- **Auth mirrors the web app's pattern** (`lib/api.ts`/`lib/auth.tsx`) — same `openapi-fetch`
  client, same login/401-refresh/retry interceptor — with one necessary difference:
  `localStorage` is synchronous and `expo-secure-store` is not. `lib/tokenStore.ts` keeps the
  tokens in an in-memory module variable that the interceptor reads synchronously, persists
  every change to SecureStore, and hydrates that variable from SecureStore once at startup
  (`AuthProvider`'s `isLoading` gates the sign-in/event-list screens until that hydration
  finishes, avoiding a sign-in flash on a warm start).
- **The squad runner is the entry screen's only source of truth**, exactly as the backend
  models it: it renders whatever `GET .../runner` returns fresh, with no local idea of
  progress. The one thing the server doesn't hand back directly is *which* run is next
  before anything has been saved this session (`SaveRunResponse.nextOutstanding` only exists
  after a save) — `lib/squadRunner.ts`'s `findInitialOutstanding` derives it client-side
  (earliest run number not yet recorded by everyone, first participant in squad order still
  missing it), unit-tested in `squadRunner.test.ts`. After every save, the server's own
  `nextOutstanding` drives the auto-advance instead of recomputing anything.
- **The entry screen** follows section J's design rules directly: the shooter's name is the
  largest element; a custom keypad (reusing `@comp/core`'s already-tested `digitsToMillis`/
  `formatMillis` rather than reimplementing time parsing a third time) fills right-to-left,
  never the system keyboard; a penalty stepper with a `+5` chip caps at 20; DNF is a
  separated control that confirms before clearing the time; overwriting an already-recorded
  run confirms and shows the old value first; a save sends an `Idempotency-Key` (from
  `expo-crypto`'s `randomUUID`, reused across a retry of the *same* body so a dropped
  response replays safely, regenerated if the official actually changed something before
  retrying) and gives haptic feedback (`expo-haptics`) on both success and failure.
- **Detect-and-warn is a dismissible banner on the squad list**, not the blocking modal in
  section G's sequence diagram — `GET .../entry-session` is shown as "Last entered by
  {name}, Xm ago" when under 10 minutes old, and a heartbeat fires on entering the squad
  runner screen. Simpler than the doc's confirm-to-continue dialog, and a reasonable
  first-pass simplification for the prototyping phase; revisit if it proves too easy to miss
  in practice.
- Removed from the scaffold's defaults as out of scope for D11's Android-only prototyping
  phase: `react-dom`/`react-native-web` (no web target), `react-native-reanimated`/
  `react-native-worklets` (native-stack navigation doesn't need them), and the
  `typedRoutes`/`reactCompiler` experiments (both still experimental in this Expo SDK;
  not worth the friction without a concrete need).
- `eas.json` is committed with `development`/`internal`/`production` build profiles
  (`internal` is D11's sideloadable-APK profile), but nothing has actually been built yet —
  that needs `npx eas login` with a free Expo account, which only the user can do. See the
  Commands section above.
- Verified: `pnpm --filter mobile typecheck` and `test` (5 new tests in
  `squadRunner.test.ts`, plus `packages/core`'s and `web`'s existing suites all still green
  — 35 tests total across the workspace), and `npx expo export --platform android` bundles
  all 1300+ modules with Metro with no errors. Not verified: actually running the app on an
  emulator or device — there wasn't one available in this environment. Run
  `pnpm --filter mobile start` and open it in Expo Go or an Android emulator before trusting
  the UI itself; only its types, logic, and bundling are confirmed so far.

**Visual design** now follows a real mock the user supplied (a "ShooterRSG" sign-in screen,
built with Claude's design tool) rather than the plain default styling above — applied
exactly to sign-in, and extended by judgment to every other screen since only sign-in was
mocked.

- `lib/theme.ts` holds the design tokens pulled from the mock: a near-black ground
  (`#0b0c0f`/`#141519`), an orange accent (`#ff8a3d`), Archivo for headings/body, JetBrains
  Mono for labels and small monospace accents (loaded via `@expo-google-fonts/archivo` and
  `@expo-google-fonts/jetbrains-mono` in `_layout.tsx`, which keeps the splash screen up
  until they're ready). `app.json`'s `userInterfaceStyle` is `"dark"` (the mock has no light
  variant) and its splash/adaptive-icon background colors were updated to match.
- The sign-in screen's hero image (`assets/images/sign-in-hero.png`) was extracted from the
  mock artifact's own asset bundle — the user confirmed they hold the rights to it before it
  was committed. Layered under a `expo-linear-gradient` fade rather than the mock's CSS
  `mix-blend-mode`/`radial-gradient`, which have no React Native equivalent.
- Two things in the mock don't correspond to real backend behaviour, resolved with the
  user's explicit steer: the **"Use passkey" option was dropped entirely** (no passkey
  support exists), and **"Forgot password" is a live link that shows a message** ("contact
  your club administrator") rather than a dead link, since there's no password-reset
  endpoint. The mock's demo lockout-after-3-attempts logic was also not ported — the real
  `/auth/login` endpoint already has its own rate limiting and Identity lockout.
- **"Keep me signed in" is real, not decorative**: `lib/tokenStore.ts`'s `setTokens` takes a
  `persist` flag — off, the session works normally for this app launch (tokens still live in
  the in-memory copy every request reads) but nothing is written to `SecureStore`, and any
  previously-persisted session is cleared so it can't reappear on the next launch. A token
  refresh omits the flag and reuses whichever choice was last made explicitly, so the
  behaviour survives the access-token boundary without the refresh flow needing to know
  about it.
- The mock's fake iPhone status bar, its "SESSION OPEN" success card (login here just
  redirects to the event list immediately, matching the rest of the app), and its
  Terms/Privacy footer links (no such pages exist) were left out as artifacts of previewing
  a static mock rather than real app chrome.
- The other four screens (event list, squad list, squad runner, entry) were re-themed to the
  same tokens — dark backgrounds, the orange accent on primary actions, Archivo/JetBrains
  Mono throughout — without changing their layout or behaviour, since only sign-in had a
  mock to match.

**The mobile app has now actually been run and signed into**, on a local Android emulator
(installed into `%LOCALAPPDATA%\Android\Sdk` via the command-line tools — no Android Studio
IDE needed; `android emulator create/start` and `android screen capture` made it possible to
drive and literally see the emulator from the terminal). Two real bugs surfaced and were
fixed:

- **A network-level login failure hung on "Verifying" forever with no visible error.**
  `openapi-fetch`'s `api.POST(...)` only resolves `{ error }` for an HTTP error *response*;
  a failure before any response arrives (unreachable host, timeout) instead *rejects* the
  promise. `lib/auth.tsx`'s `login()` had no `try/catch` around it, so that rejection
  propagated straight out of the sign-in screen's un-guarded `await login(...)`, skipping
  its `setSubmitting(false)`. Fixed by wrapping the whole request in `login()` in a
  `try/catch` that always returns a normal `{ success: false, error }` result.
- **Every real request — including a completely successful login — failed with `Error:
  onResponse: must return new Response() when modifying the response`,** thrown by
  `openapi-fetch` itself (`dist/index.cjs`'s `onResponse` handling: any truthy return is
  treated as "replace the response with this" and must pass `instanceof Response`, or it
  throws). `lib/api.ts`'s interceptor returned the *same* `response` object back to mean
  "unchanged" — correct-looking, but the library's actual contract is to return void/nothing
  for that case and only return a value when constructing a genuinely new `Response` (the
  GET-retry-after-refresh branch is the one legitimate case, and was already doing that
  correctly). This one was mid-diagnosis mistaken for a network/emulator connectivity
  problem — confirmed as a code bug, not networking, by adding a temporary debug line that
  surfaced the real thrown error text instead of the friendly message.
- Getting to that point also needed a **development-only seed** — there was no account
  anywhere to sign in with at all (no registration endpoint, no seed data; user creation is
  otherwise always an authenticated Super Admin/Admin/Official action per the security
  model). `Program.cs` now creates one Super Admin (`dev-admin@comp.local` /
  `Comp1234!`) the first time the API starts against a **migrated** Development database
  with zero users — gated on `IsDevelopment()` *and* `GetAppliedMigrationsAsync().Any()`,
  the latter specifically because `Comp.Api.Tests`' `WebApplicationFactory` also runs in
  Development and starts the host (running this seed block) against a brand-new
  Testcontainers database *before* its own `MigrateAsync()` call — querying `Users` first
  broke 69 of 78 tests with "relation asp_net_users does not exist" until this was added.
  `GetAppliedMigrationsAsync()` tolerates a missing history table; a direct query does not.
- `expo-crypto`/`expo-font`/`expo-haptics`/`expo-linear-gradient`/`expo-secure-store`'s
  versions in `package.json` were guessed wrong when first added (copied a `~15.x`/`~14.x`
  pattern from an unrelated package); running the app for real against Expo's own tooling
  corrected them to the actual SDK-57-aligned `~57.0.x` versions the rest of the Expo
  packages use. `app.json` also picked up an EAS project id and the `expo-font` config
  plugin automatically from actually running `expo start`/`expo export`.

**Web admin gained Competitions and Events pages** (`routes/CompetitionsPage.tsx`,
`routes/EventsPage.tsx`, wired into `App.tsx` and `AppShell`'s new nav bar) — the actual
blocker the user hit trying to test the mobile app: M3 and M6 built the competitions/events
backend, but no client had ever exposed *creating* one, so there was no way to get an event
to point the mobile app at. Each page pairs a small create form with the existing list,
using the same `useMutation`/`useQueryClient` pattern already established (mirrors
`ShootersPage`'s `useQuery` conventions). `EventsPage` also has a one-button "Advance to
{next status}" per event, computed client-side from the fixed Draft→Setup→InProgress→Review
sequence (never offering Finalised — that's M8). Verified end-to-end on the Android emulator
via Chrome (through `adb reverse tcp:5173`/`tcp:5200`, so the emulator's browser sees the
dev server and API at the exact `localhost` origin the API's CORS policy already allows, no
config changes needed): created a competition, created an event under it, and watched that
event immediately show up in the mobile app's own event list.

**Decision (15 Sep 2026):** per explicit user direction, squad and participant management
(add a participant to an event, build/edit squads, move shooters between squads) is built on
**mobile**, not web — Officials are already permitted to do this per the security model
(section K), and the architecture doc's M6 milestone already describes it as night-of-event
work. Creating a competition or event itself stays web-only, Super Admin/Admin, per the
security model — that is not a client-side choice, the backend rejects it for any other role
regardless of which app asks. This is a deliberate, narrow extension of section J's "mobile
stays small" boundary, not a reversal of it.

**Mobile squad/participant management is built**, on the squad list screen
(`app/events/[eventId]/index.tsx`) plus a new `app/events/[eventId]/add-participant.tsx`:

- **"+ Squad"** creates one with no dialog (`POST .../squads` with `squadNumber: null`,
  matching the backend's own auto-numbering default) — deliberately zero-friction, since
  naming a squad is optional and the common case at the range is "just give me another
  squad".
- **"+ Shooter"** opens a search screen (`GET /shooters?q=...&active=true`, matching
  `docs/m2-wiring.md`'s note that this specific participant-selection workflow should
  default to active-only unlike the general shooter search) with a "Can't find them? Add a
  new shooter" fallback that creates the shooter (`POST /shooters`) and adds them to the
  event (`POST .../participants`) in one action. Newly added participants start
  unassigned (`squadId: null`) — matching the same shape `AddParticipantRequest` already
  requires.
- **Unassigned participants** get their own section on the squad list, each with a row of
  squad chips to tap-assign (`PATCH .../participants/{pid}`) — the simplest possible
  interaction for what's normally a handful of squads. Moving an already-assigned
  participant to a *different* squad, or removing one, isn't built yet (not blocking; flag
  if it's needed).
- Verified on the Android emulator, screenshotted end to end: created a shooter inline,
  watched her appear under "Unassigned", tapped the squad chip to assign her, and confirmed
  the squad runner screen immediately showed her as "Next up · Run 1" — the same screen
  M7's entry flow already exercises.
- Fixed one real, unrelated-to-the-story bug hit while testing this: the search input's
  `autoFocus` was suspected of stealing focus from the name fields below it and was
  removed, but the actual cause turned out to be nothing in the app at all — every "tap
  landed in the wrong field" was this session's own test coordinates not being scaled from
  the screenshot's display size to the emulator's actual pixel size. `autoFocus` stayed
  removed anyway (harmless, arguably better UX not to pop the keyboard immediately), but
  nothing was actually broken here.

**Web admin now shares the mobile app's ShooterRSG design** (per explicit user request) —
same palette, same fonts, same brand mark, so the two clients read as one product rather than
an admin tool that happens to talk to the same API.

- `index.css`'s CSS variables (the app's "minimal slice of shadcn/ui theming", `--background`/
  `--primary`/`--muted`/etc. feeding Tailwind's `@theme inline`) now hold the same hex values
  as `clients/mobile/src/lib/theme.ts`, plus a Google Fonts `@import` for Archivo and
  JetBrains Mono wired into `--font-sans`/`--font-mono`. Because `Button`/`Input`/every page
  already used semantic classes (`bg-background`, `text-muted-foreground`, `border-border`,
  never a raw color), this one file re-themes the whole app — no page had to change to
  pick up the new palette.
- This codebase's existing convention (not shadcn's default) is `--muted` for the page
  canvas and `--background` for a raised panel/header/card — kept rather than inverted, so
  `--muted: #0b0c0f` (mobile's `background`) and `--background: #141519` (mobile's
  `surface`). A new `--input: #0f1116` (mobile's `inputBg`) gives form fields their own
  darker fill, distinct from the cards they sit in — `Input` now uses `bg-input`, and both
  `Input` and the `Button` outline variant were moved from `border-input` to `border-border`
  so the border isn't the same color as the fill it's drawn on.
- Fixed a real dark-theme regression the token swap would otherwise have caused: the
  `outline`/`ghost` button variants' `hover:bg-muted` would have *darkened* a button that
  already sits on the lighter `background` surface (since `muted` is now the darker canvas,
  not a light hover tint) — changed to `hover:bg-white/5`, a translucent overlay that
  lightens regardless of the surface underneath.
- `AppShell` and `LoginPage` both gained the SR badge + "SHOOTER**RSG**" wordmark (an
  "Admin"/"Admin console" mono tag distinguishes this from the mobile app's own mark rather
  than pretending to be the same screen). Active nav link uses the accent color, matching
  mobile's convention for the selected/current state. `CompetitionsPage`/`EventsPage`'s
  form containers gained `bg-background` so they read as raised cards against the canvas,
  matching mobile's card treatment for equivalent content.
- Verified: full `pnpm -r typecheck`/`test` (35 tests, all green), a production
  `vite build`, and a live look on the Android emulator's Chrome (via `adb reverse`) —
  sign-in, the shooter register, and Competitions all screenshotted against the real API
  and real data (the "Test Competition"/"Ada Lovelace" records from earlier testing).

**Web admin gained Leagues UI**, the last real gap left over from M3 — `GET/POST
/competitions/{id}/leagues` and `GET/PUT /leagues/{id}/members` have existed since M3 with
nothing ever exposing them. Two new pages, both under a competition (sibling to
`EventsPage`, sharing a new `CompetitionTabs` component so the two are one click apart):

- `LeaguesPage` (`/competitions/:competitionId/leagues`) — list + a create form (name,
  tier only; `pointsForFirst`/`pointsDecrement`/`dropWorstCount`/`absencesCountAsZero` are
  all left `null` so the backend's own defaults apply — 50→−1 scoring, no drops, absences
  count as zero — matching what a normal league actually wants, per the architecture doc's
  settled decisions). Each row links to that league's roster.
- `LeagueRosterPage` (`/competitions/:competitionId/leagues/:leagueId`) — the roster table
  (`GET /leagues/{id}/members`) plus a shooter search (`GET /shooters?q=&active=true`) to
  add one. `PUT /leagues/{id}/members` is full-replace, not an add/remove endpoint, so both
  "Add" and "Remove" compute the next complete member-id list client-side from what's
  already loaded and PUT that — there's no dedicated add/remove call to make. The search
  result disables "Add" and labels it "Already in league" for existing members, and shows
  a live `N/20 shooters` count against the architecture doc's D4 cap so a 409 from
  exceeding it is more a confirmation of what the UI already showed than a surprise.
  Assumes the shooter is already registered (via the Shooters page) — no inline
  create-shooter here, unlike mobile's add-participant screen, since assigning an existing
  register to leagues and registering a brand-new shooter are different enough workflows
  to not conflate.
- Verified end to end against the real API on the Android emulator's Chrome: created
  "Division A" (tier 1, defaults applied), added Ada Lovelace to it, watched the count go
  to 1/20 and the search immediately reflect "Already in league".

**Milestone M8 (results, finalisation, standings) backend is complete.** `POST
/events/{id}/transition` now accepts `Finalised` (only from `Review`), `POST
/events/{id}/amend`, and `GET /events/{id}/results?leagueId=` are all live
(`Comp.Api/Endpoints/EventEndpoints.cs`), plus `GET /leagues/{id}/standings`
(`Comp.Api/Endpoints/LeagueEndpoints.cs`). This is the first thing in the whole codebase to
actually call `Comp.Scoring` from a running service.

- **`IResultsService`/`ResultsService`** (`Comp.Infrastructure/Events/ResultsService.cs`) is
  the new service. Every query loads flat lists and joins in C# memory rather than writing a
  deep LINQ-to-SQL join — a deliberate choice, not an oversight, after M3's
  `LoadMembersAsync` bug (see above) already showed EF Core's translator can silently refuse
  to run a query shaped that way.
  - `GetEventResultsAsync` returns results computed **live** via `EventScorer.Score` for any
    event not yet `Finalised` (`IsFinal: false` in the response), or the **frozen**
    `event_results` rows for one that is (`IsFinal: true`) — never recomputes a finalised
    event's numbers on read. Filters/orders by `leagueId` (on `LeaguePosition`) when given,
    else by `OverallPosition`, DNF/unranked sorted last.
  - `RecalculateAndPersistAsync` builds a `ScoringContext` from the event's participants,
    runs, and each involved league's rules, calls `EventScorer.Score`, deletes any existing
    `EventResult` rows for the event and inserts fresh ones — this is what "finalise"
    actually does to the database, and it's also exactly what re-finalising after an amend
    repeats.
  - `GetLeagueStandingsAsync` finds every `Finalised` **and** `CountsForStandings` event in
    the league's competition ordered by `EventNumber` (this ordering *is* "events held" and
    the chronological sequence `StandingsCalculator` needs), loads that league's
    `EventResult` rows across them, groups into one `ShooterEventPoints` list per shooter
    (via `EventParticipant.ShooterId` — a shooter has a different `EventParticipantId` each
    event), and calls `StandingsCalculator.Calculate`. Confirms rule 9 directly: nothing
    here is ever read back from a standings table, because there isn't one.
- **Finalise is also publish — there is no separate status or endpoint for it.** The
  architecture doc's `EventStatus` enum has no "Published" value; `TransitionAsync`'s
  `Finalised` branch requires the current status to be exactly `Review` (else 409), then
  calls `RecalculateAndPersistAsync` before flipping `Status`/`FinalisedAt`/
  `FinalisedByUserId` in a second `SaveChangesAsync` (a separate audit entry from the result
  rows' own inserts).
- **Amend requires the `Finalised` status, the Super Admin/Admin role, *and* the
  `CanAmendPublished` claim** — the endpoint's authorization policy combines
  `.RequireRole(...)` and `.RequireClaim(AppUserClaimsPrincipalFactory.AmendPublishedClaimType,
  "true")` on the same policy, the first time this codebase has combined the two. `AmendAsync`
  deletes the event's `EventResult` rows, moves it back to `Review`, clears
  `FinalisedAt`/`FinalisedByUserId`, and sets `CompDbContext.PendingAuditReason` from the
  request body before saving so the audit log records *why* — this is `CanAmendPublished`
  actually being used for the first time since it was wired up in M2.
- Tested end-to-end in `tests/Comp.Api.Tests/ResultsEndpointTests.cs` (6 new tests, 84/84
  passing overall): live-vs-frozen results before/after finalising; correct overall and
  league points on finalise; the Review-only and role-gated finalise conflicts; the
  role-and-claim-and-reason gate on amend (including the deliberate negative case of an Admin
  *without* the claim); the milestone's actual acceptance criterion end-to-end (finalise,
  amend with a reason, correct a run so the faster/slower shooters swap, re-finalise, and see
  the standings positions actually flip); and the continuous drop-rule boundary
  (`eventsHeld == dropWorstCount` still provisional, matching D7).

**Milestone M8's web UI is complete.** Two new pages, plus small additions to the two M8
already had:

- `EventResultsPage` (`/competitions/:id/events/:eventId/results`) — `GET
  /events/{id}/results`, with a league dropdown (`GET /competitions/{id}/leagues`) that
  refetches with `?leagueId=` and switches the sort/position column from overall to that
  league's. Shows "Provisional (live)" or "Final" from the response's own `isFinal`, never
  inferred from the event's status client-side. Reuses `@comp/core`'s `formatMillis` for the
  time column — the first web usage of that package, added as a new `clients/web`
  dependency — rather than re-implementing `m:ss.cc` formatting a third time, per rule 4.
- `LeagueStandingsPage` (`/competitions/:id/leagues/:leagueId/standings`) — `GET
  /leagues/{id}/standings`, linked from both `LeaguesPage` and `LeagueRosterPage`. Shows
  "(provisional)" next to a shooter's name from the response's own `isProvisional`, and the
  header's "N events held" directly from `eventsHeld` — nothing about D7's drop-rule
  threshold is computed or duplicated client-side, it's just displayed.
- `EventsPage`'s status sequence now includes `Finalised`, reachable only from `Review`
  (the backend still enforces this; the client just stops offering it as a manual jump from
  elsewhere). The advance button reads "Finalise" specifically for that one transition,
  "Advance to {status}" for the rest. A `Finalised` event gets an "Amend" button that prompts
  for a reason with `window.prompt` (no dialog component exists yet in this codebase, and the
  mobile app already established `Alert`/plain-prompt as this project's go-to for a one-off
  confirmation) and calls `POST /events/{id}/amend` — there is no client-side hiding of this
  button for a user who lacks the `CanAmendPublished` claim, matching every other
  role-gated action in both clients: the backend's 403 is the actual enforcement.
- `EventsPage` and `LeaguesPage` rows both gained a "Results →"/"Standings →" link.
- Verified against the real API on the Android emulator's Chrome, not just typecheck/build:
  seeded two shooters into an event via a throwaway script hitting the API directly (faster
  and less error-prone than driving the whole add-participant flow through emulator taps for
  data setup that mobile already exercises), walked the event Setup → InProgress → Review →
  Finalised through the real UI, and confirmed the results table matches before/after
  finalising (live vs. frozen, correct time formatting, correct overall position and league
  points) and that Division A's standings page shows the finalised event's points. Also
  caught, in passing, that `pnpm install` must be run from `clients/` (the actual pnpm
  workspace root — there is no root-level `pnpm-workspace.yaml`), not the repo root, or a
  newly added workspace dependency's symlink silently never gets created.

**Fixed a real mobile bug found during manual testing**: the squad list screen's `createSquad`
and `assignToSquad` mutations (`app/events/[eventId]/index.tsx`) had no `onError` handler at
all — a failed request (most commonly a 409 from trying to write to a `Finalised` event, which
correctly locks every write per rule 8) just silently reverted the button with zero feedback,
reading as "the UI doesn't appear to work" rather than a clear error. Fixed to match the
pattern already used on `add-participant.tsx`: extract `detail` from the error response,
surface it via `error` state and a `<Text>` below the action row.

**Demo data**: the local dev database can be repopulated with a realistic dataset — two
competitions (Mini Rifle with Division 1/2/3, Underlever with League Standing), 30 shooters
(random names, real nicknames from the Wednesbury Marksmen scoresheet fixture), 5 finalised
events per competition with random times/penalties/absences/DNFs on both runs. Built via
throwaway Node scripts against the running API (not checked in) rather than a seed migration,
since it's illustrative test data, not schema. Regenerate by resetting the dev database
(`docker compose down -v && docker compose up -d`, then reapply migrations) and re-running the
same approach if needed again.

**Web admin gained a generated results PDF** (`lib/resultsPdf.tsx`, a "Download PDF" button on
`EventResultsPage`) — **decision D12**, a deliberate deviation from the architecture doc's
Phase 1 plan of print-stylesheet-only output, done at explicit user request ahead of schedule.
Built with `@react-pdf/renderer` (React-component-based PDF layout, not a print stylesheet or
a server-side library), matching the exact layout of a real club scoresheet the user supplied:
an overall-results table on the left (Position/Shooter/League/Time) and a grid of one tile per
league on the right (Position/Shooter/Time/Points), styled in the app's own dark ShooterRSG
palette rather than the reference document's plain black-on-white. Always generated from the
event's *unfiltered* results (a separate query from the on-screen league-dropdown-filtered
table), since the PDF shows every league's tile at once regardless of what's currently
selected on screen. Uses the built-in Helvetica font rather than embedding Archivo/JetBrains
Mono — the CDN-hosted fonts aren't currently packaged as files `@react-pdf/renderer` can embed
without adding network-fetch fragility to PDF generation; revisit if brand fidelity in the PDF
itself matters more than this first pass assumed. Verified by generating one on the Android
emulator (downloaded, then opened in the OS's own PDF viewer) against real finalised-event
demo data — DNF rows, league grouping and dark styling all render correctly.

**The mobile app gained a Home screen and a real navigation structure.** Previously it went
straight from sign-in to a single flat events list; it now has a bottom tab bar (`app/(tabs)/`,
Expo Router's group convention — invisible in the URL, so every existing `/events/[eventId]/...`
deep link kept working unchanged) with two tabs:

- **Home** (`app/(tabs)/home.tsx`) — "Welcome, {name}" (top right, decoded client-side from the
  access token's own `display_name`/`email` claims via the new `lib/jwt.ts` — display only,
  never trusted for authorization) and a "Today's Events" list (`eventDate` matching the
  device's *local* calendar date, not `toISOString()`'s UTC date, which drifts a day around
  midnight in most timezones — see `todayIso()`). Tapping an event jumps straight into its
  squad list, the same destination the old events list already used.
- **Events** (`app/(tabs)/events.tsx`) — the old list, now with a horizontal row of
  competition filter chips ("All" plus one per competition). Tapping "All" stays on this
  unfiltered list; tapping a specific competition navigates to a new drill-down page
  (`app/competitions/[competitionId].tsx`, outside the tabs group so it's a full-screen push
  that naturally hides the tab bar) showing just that competition's events — this *is* the
  "competition page," reached only through the filter, not a third tab.
- Deep screens (event detail, add-participant, squad runner, entry) deliberately stay **outside**
  `(tabs)/` at their original top-level paths rather than nested inside the Events tab's own
  stack — pushing a sibling root-level screen is what makes the tab bar disappear automatically
  on those screens without any manual `tabBarStyle` hiding logic.
- Icons: this app was icon-free everywhere until this session (`+ Shooter`/`+ Squad` are plain
  text buttons); per explicit user direction it now uses `@expo/vector-icons` (installed via
  `npx expo install`, which resolves the SDK-57-compatible version rather than a guessed one —
  see the EAS-CLI/font-version lesson above for why that matters) for the tab bar icons and a
  few list affordances (chevrons, a calendar glyph on Home's empty state).
- **Real bug found and fixed while building this**: the competition filter chips clipped the
  top few pixels of every glyph (an "e" reading as "o", a capital "A" losing its peak) — not a
  font problem (identical Archivo text elsewhere on the same screen, e.g. event row titles,
  rendered perfectly), and not fixed by adding `lineHeight` to the chip text either. The actual
  cause: the horizontal `ScrollView` had no explicit `style` height, only a
  `contentContainerStyle` — on Android that can lay the ScrollView itself out at a
  flex-collapsed height shorter than its content, clipping the content's paint to that shorter
  box even though the pills themselves appeared full-size. Fixed by giving the `ScrollView` an
  explicit `style={{ height: 44 }}` and moving the row's bottom margin onto that outer style
  (`marginBottom`) rather than `paddingBottom` inside `contentContainerStyle`, so the gap
  doesn't itself eat into the fixed height and reintroduce the same clipping. Worth remembering
  for any future horizontal `ScrollView` in this app.
- Sign-in now redirects to `/home` (was `/events`) after login and on an already-authenticated
  relaunch.

**Web admin gained a matching Home screen and navigation icons.** `routes/HomePage.tsx` is now
the post-login landing page (`/`, `/login`'s redirect, and the root `Navigate` all point at
`/home`): "Welcome, {name}" (same JWT-decode approach as mobile, `lib/jwt.ts`, browser `atob`
instead of the dependency-free decoder RN needed), an "Upcoming Events" section (`eventDate`
between today and today+7 inclusive) and a "Recent Events" section (today-14 through yesterday),
both using the same local-calendar-date math as mobile's `todayIso()` (`lib/dates.ts`). Clicking
an event card navigates to that event's competition's Events page — the actionable admin
surface (advance/finalise/amend), not the read-only results page, since a Draft/Setup event has
nothing to show there yet. `AppShell`'s nav gained icons via `lucide-react` (already an unused
dependency in `package.json` since early on — this is its first real use) and a "Home" link;
the signed-in user's name now shows in the header.

The mobile sign-in screen's brand lockup (SR badge + SHOOTERRSG wordmark + "Ready. Standby. Go"
tagline, grouped as one visual unit directly under the wordmark) is now replicated on
`LoginPage`, styled the same way. `AppShell`'s own compact header mark deliberately did **not**
gain the tagline — mobile itself only shows it on sign-in, never on every screen's header, so
matching that scope kept the persistent nav mark unchanged.

**Squad allocation is now a real workflow, not just squad-runner status text.** Requested after
using the mobile app for real sign-on: officials could see squads and drag shooters into them,
but couldn't tell how full a squad was, couldn't lock one once it was settled, and had no record
of when a shooter actually turned up (arrival order is meant to drive allocation, since shooters
sign on as they arrive, not in a pre-planned order).

- **`SquadStatus` was redefined** from an aspirational, never-wired-up "progress through the
  night's runs" enum (`Pending, Run1, Run2, Complete` — confirmed unused anywhere but the enum
  declaration itself before this change) to what's actually needed: `Pending`/`Allocated`,
  meaning "still open for shooters to be assigned into" vs. "roster locked." No migration
  needed — `SquadConfiguration` stores it as free text with no check constraint. `POST
  /events/{id}/squads/{squadId}/complete` (Super Admin/Admin/Official, same as squad
  creation) sets it, is idempotent (completing an already-allocated squad just returns it
  unchanged), and is refused on a `Finalised` event like every other squad/participant write.
- **An `Allocated` squad is closed to further additions**, enforced in
  `EventParticipantService` itself (not just hidden client-side): both adding a brand-new
  participant straight into that squad and moving an already-unassigned one into it later
  return 409 — but adjusting the position of someone already in the squad before it was locked
  still works, since that's not "moving in."
- **`EventParticipant.AddedAt` already existed** (set at creation, never previously exposed)
  — added to `EventParticipantResponse` rather than needing a new column. The mobile squad
  list now shows it next to each unassigned shooter's name and sorts that list by it
  ascending, so the top of the list is whoever's been waiting longest — matching how
  allocation is meant to actually work on the night (first come, first squadded).
- Mobile squad rows now show a live shooter count (`"N shooters · Pending/Allocated"`), turn
  green when Allocated (reusing the `success`/`successBg`/`successBorder` tokens that already
  existed in the palette but had no consumer yet), and the unassigned shooters' squad-chip
  list only offers squads that are still `Pending`. The "Complete" action button is
  deliberately **yellow** (`colors.warning`, a token added for this), not green — sharing the
  Allocated state's green read as "this is already done" at a glance, which is the opposite of
  what an action button should signal.
- **The add-shooter screen no longer boots you out after one add.** It used to navigate back
  immediately on a successful add, so adding several shooters meant reopening the search
  screen from scratch each time, and there was no way to tell who was already in the event
  from the search results. Now every result stays visible with either "Add+" or "Added" (the
  event's own participant list, fetched here too and shared via the same `["participants",
  eventId]` query key the squad list uses, so both screens invalidate together), and tapping
  "Add+" flips that one row to "Added" in place without leaving the screen.
- **Fixed the back button showing the literal word "tabs".** Screens pushed from inside the
  `(tabs)` group (e.g. tapping a Home event straight into its squad list) inherited a back
  button label derived from the tab group's own route name, since nothing had ever set
  `headerBackButtonDisplayMode`. Set to `"minimal"` (plus an empty `headerBackTitle`) globally
  in the root `_layout.tsx`, so every back button everywhere is just the arrow.

**Web admin: nicknames preferred over full names in results/standings, and shooters are now
editable.**

- **`LeagueStandingResponse` and `EventParticipantResultResponse` both gained a `Nickname`**
  field (the `Shooter.Nickname` that already existed, just never surfaced past the register
  itself). `LeagueStandingsPage`, `EventResultsPage`, and the generated results PDF
  (`lib/resultsPdf.tsx`) all now show a shooter's nickname instead of their full name when
  one is set, via a shared `clients/web/src/lib/shooterName.tsx` (`ShooterName` component
  for on-screen use — the full name shows as a native browser tooltip on hover via the
  standard HTML `title` attribute, since nothing else in this codebase needed a custom
  tooltip component yet; `preferredName()` as a plain-string version for the PDF, which has
  no hover to offer). The Shooters register page itself is deliberately **unchanged** — it
  already shows nickname as its own column alongside the full name, which is a genuinely
  different, correct use case (viewing/managing the register) from a leaderboard's "what do
  people actually call this shooter."
- **Shooters can now be edited.** `PATCH /shooters/{id}` (Super Admin/Admin) already existed
  and was never exposed on any client. `ShootersPage` gained an inline edit affordance — an
  "Edit" button per row swaps that row's cells for inputs (first name, last name, nickname,
  membership no.) plus Save/Cancel, avoiding a separate dialog component this codebase
  doesn't have yet. Deliberately **does not** cover league membership: a shooter can be in a
  different league in each competition simultaneously, so "edit this shooter's league" has
  no single well-defined meaning — per explicit user direction, that's out of scope for now
  and stays on the existing per-league Roster page.

**The results PDF and its filename now lead with the competition name, not just the event
name.** The header previously showed only `{eventName} — Results` with no competition name
anywhere, ambiguous once more than one competition has a "Week 1" (as this project's own
staging demo data does). `EventResultsPage` now also queries `/competitions` and looks up the
current one; `resultsPdf.tsx`'s `ResultsPdfDocument`/`downloadResultsPdf` both take a
`competitionName` prop, rendered as the main title line (`styles.title`) with the event name
underneath it in a new smaller, accent-colored `styles.eventHeading` line. The downloaded
filename is now `{competition}-{event}-results.pdf` (slugified) instead of just
`{event}-results.pdf`, for the same disambiguation reason. Verified via `pnpm --filter web
typecheck`/`test`/`build`; not re-verified visually on-device this round (no local API server
running at the time), but the change is small, typed, and follows the exact pattern already
used elsewhere on this page (`CompetitionsPage`/`EventsPage`/`LeaguesPage` all already query
`/competitions` the same way).

**`AppShell`'s top nav is now responsive.** It was built with no small-screen handling at all —
a single non-wrapping flex row, so the nav links and "Sign out" simply got clipped off the edge
of the viewport on a tablet or phone browser (this admin app had only ever been used on desktop
before). Below Tailwind's `md` breakpoint, the logo/wordmark and a hamburger button are all that
show in the header; tapping it opens a stacked panel (nav links, then the user's name and Sign
out) below the header. Desktop layout is unchanged — the existing horizontal nav and user/sign-out
row just gained `hidden md:flex`, and the hamburger button is `md:hidden`. Verified on the Android
emulator's Chrome (a real phone-width viewport, not a resized desktop window): logged in via
`adb input`, confirmed the header shows nothing clipped, and the opened menu shows all three nav
links, "Dev Admin", and "Sign out" fully visible.

**Fixed a real sign-in bug found on the user's own sideloaded APK**: the same staging admin
credentials worked on the web app but were consistently rejected as "Invalid email or password"
from the built APK, with no autofill involved and the text visually verified correct via the
sign-in screen's own SHOW toggle. The sign-in screen (`app/index.tsx`) already trimmed the email
before calling `login()` but sent the password as-is. A browser's `<input>` strips a trailing
newline/space picked up from a paste; React Native's `TextInput` on Android often does not — a
password copied from somewhere that included one invisible trailing character would look
identical on screen but silently fail to authenticate from the APK while the same paste worked
fine on web. Fixed by trimming the password too, both in the empty-field guard and at the actual
`login()` call.

**The Leagues page gained a "Download PDF" button** covering every league's current standings in
one document, matching the existing per-event results PDF's styling and pattern. The shared parts
of that styling (`colors`, `page`/`title`/`tile`/`row`/`cell`/`footer`/... styles) were pulled out
of `resultsPdf.tsx` into a new `lib/pdfTheme.ts` first, so the two generated PDFs can't drift apart
visually — `resultsPdf.tsx` now only keeps the layout unique to it (the overall-table-left /
league-tiles-right split).

- `lib/standingsPdf.tsx`'s `downloadStandingsPdf({ competitionName, leagues })` renders one tile
  per league (Pos/Shooter/Count/Drop/Total, nickname-preferred names via the same `preferredName`
  helper the results PDF uses) in a single wrapping grid — there's no "overall" table here, since
  standings have no cross-league ranking to show. Per explicit user direction, the header is just
  two lines: the competition name, then "As of {today's date}" underneath in the accent color
  (`todayIso()`, the same local-calendar-date helper the web/mobile Home screens already use) —
  deliberately not a per-event date, since this always reflects the *current* standings across
  however many events have been finalised so far.
- `LeaguesPage` fetches every league's standings via `useQueries` (one `GET /leagues/{id}/standings`
  per row already listed by the page's own `leaguesQuery`) rather than a new bulk endpoint — this
  competition's own league list is already loaded and rarely more than a handful of rows. The
  button is disabled until every one of those queries has actually resolved.
- Verified on the Android emulator's Chrome against real finalised-event demo data: downloaded and
  opened the PDF for Mini Rifle (3 divisions), confirmed the header, per-league tiles, and
  nickname-preferred names all render correctly and match the results PDF's dark styling.

**Replaced the default Expo template app icon with a real ShooterRSG one.** The sideloaded APK was
showing Expo's generic blue chevron everywhere (launcher, adaptive icon, splash) since the app was
scaffolded with those placeholder assets and nothing had ever swapped them out. Regenerated all
five referenced under `assets/images/` — `icon.png` (1024×1024, full-bleed orange with the dark
"SR" mark, for contexts with no OS-applied mask), `android-icon-foreground.png`/
`android-icon-background.png` (the two adaptive-icon layers Android composites and masks itself —
foreground keeps the glyph within the ~66% safe zone so it survives a circular or squircle
launcher mask without clipping), `android-icon-monochrome.png` (a white silhouette on transparent,
for Android 13+'s themed/tinted icon mode), and `splash-icon.png` (the same rounded orange badge
shown on the sign-in screen, since the splash screen already sits on the same dark background).
Built with .NET's `System.Drawing` via a throwaway PowerShell script (no image-editing tool was
otherwise available in this environment) using the app's own real palette (`colors.accent` /
`colors.accentText` from `lib/theme.ts`) rather than approximated hex values, then verified by
compositing the transparent layers over both a dark backdrop and a simulated circular launcher
mask before trusting them — the script itself was a one-off and isn't committed. `app.json`
already pointed at all five paths from the initial scaffold, so no config change was needed, only
the asset files themselves. A new EAS build is required to see this on a device — it's baked into
the app package, not something a running app can hot-reload.

The same splash badge is now also the browser favicon. `clients/web` had never had one at all —
no `public/` directory, no `<link rel="icon">` in `index.html`, just whatever default a browser
falls back to. Added `public/favicon.png` (Vite serves anything under `public/` from the site
root unchanged, and copies it into `dist/` on build — confirmed in the build output) and a
`<link rel="icon" type="image/png" href="/favicon.png" />` in `index.html`'s `<head>`. Mobile's
own orphaned `assets/images/favicon.png` (unused — `app.json` has no `web` section, so Expo never
builds a web target for this app) was updated to the same badge too, purely for consistency in
case that ever changes.

**Both home screens, and both generated PDFs, now show the club's real badge** (the user's own
Wednesbury Marksmen logo, supplied as an image and committed as `clients/mobile/assets/images/
club-badge.png` — the single source of truth both clients and the PDFs copy or import from,
rather than three independent copies).

- **Mobile Home** (`app/(tabs)/home.tsx`): the header's second row, previously just the
  right-aligned welcome block, is now a `topRow` (`flexDirection: "row"`,
  `justifyContent: "space-between"`) with the badge (`Image`, 56×56, `resizeMode="contain"`) on
  the left and the unchanged welcome block on the right — opposite it, at a comparable visual
  weight, per explicit user direction.
- **Web Home** (`routes/HomePage.tsx`): the same idea via a flex row wrapping the existing
  welcome `<h1>`/`<p>` and a new `<img>` (`clients/web/src/assets/club-badge.png`, imported as a
  Vite asset so it's hashed and bundled rather than served from `public/`, since — unlike the
  favicon below — this one is real page content, not a browser-chrome asset).
- **Both generated PDFs** (`lib/resultsPdf.tsx`, `lib/standingsPdf.tsx`): a new shared
  `pdfStyles.titleRow`/`pdfStyles.clubBadge` in `pdfTheme.ts` wraps each PDF's existing
  title/heading/subtitle block and a `<Image src={clubBadgeUrl} />` (react-pdf fetches the
  Vite-bundled same-origin URL itself at render time) into one row — the text stays where it
  was, the badge sits top-right, vertically level with the competition name, per explicit user
  direction. `clubBadgeUrl` is exported once from `pdfTheme.ts` so both PDFs reference the
  identical asset.
- Verified end-to-end: both home screens on the Android emulator (mobile via a live Expo Go
  session — confirmed the new "SR" launcher icon appears on Expo Go's own bundling splash
  screen too, a free side-effect of the earlier icon work; web via Chrome through `adb reverse`),
  and both PDFs downloaded and opened for real finalised-event demo data, confirming the badge
  renders correctly top-right of "Mini Rifle" in each.

**Rebranded the wordmark's accent word from "RSG" to "READY"** ("SHOOTER**READY**", orange, same
position) across every place it appears — mobile's sign-in and Home screens, web's login page and
`AppShell` nav — per explicit user direction; the "ShooterRSG"-named design tokens/comments in
`theme.ts`/`index.css`/`pdfTheme.ts` were deliberately left alone since the user asked to change
the on-screen wordmark, not rename the underlying design-system identifier. The installed mobile
app's display name (`app.json`'s `expo.name`, previously the scaffold default "Comp") is now
"ShooterReady" too — the launcher name is a separate field from the wordmark text and needed its
own change per a follow-up request. `slug`/`scheme` were left untouched since those are tied to
the already-registered EAS project and deep-link handling, not requested to change.

**The Events page gained edit and delete**, the last real gap in event management — creating and
transitioning events had UI since M6/M8, but nothing ever let you fix a typo'd name/date or remove
an event entirely.

- **Edit** reuses `PATCH /events/{id}`, which already existed and already blocks editing a
  Finalised event (unchanged, pre-existing behaviour) — `EventsPage` just never exposed it. Same
  inline-row-swap pattern as `ShootersPage`'s edit (a "Edit" button turns Name/Date into inputs
  plus Save/Cancel), sending the event's own unchanged `penaltySeconds`/`runsPerShooter`/
  `countsForStandings` back through since the endpoint takes the full record, not a partial patch.
- **Delete is new end-to-end**: `DELETE /events/{id}` (`IEventService.DeleteAsync`, Super
  Admin/Admin — same role gate as create/edit, not the stricter `CanAmendPublished` gate amend
  uses, since the user's own framing treated this as a normal admin action with a client-side
  warning, not a claim-gated one) cascades a real delete across everything that references the
  event — `Run`, `EventResult`, `EventParticipant`, `Squad`, `EntrySession` — all Restrict FKs, so
  the service loads and `RemoveRange`s each set itself; EF Core topologically sorts the actual
  DELETE statements by the model's FK graph within one `SaveChangesAsync`, so the removes don't
  need to be sequenced by hand. Allowed regardless of status, including Finalised — standings have
  no stored aggregate to reconcile (they're computed live from whichever finalised events still
  exist), so deleting a finalised event just removes it from that computation next time anyone
  looks, which is exactly what the client warns about.
- **The "challenge screen"** the user asked for is an inline confirmation panel (this codebase has
  no dialog/modal component; `window.prompt` was the existing lightweight-confirmation precedent
  for Amend) that replaces the event's row: states what gets removed, adds a sentence specifically
  about league standings only when `event.status === "Finalised"` (a non-finalised event never
  contributed to standings, so that clause would be misleading for one), and requires typing the
  event's exact name into a field before "Delete permanently" enables — modelled on the
  type-the-name-to-confirm pattern for a genuinely unrecoverable action.
- Tested end-to-end in `tests/Comp.Api.Tests/EventEndpointTests.cs` (3 new tests, 91/91 passing
  overall): the role gate, 404 for an unknown event, and a real cascade case (an event with a
  participant and a squad attached) confirming both the delete itself succeeds and everything
  scoped to that event now reports not-found rather than orphaned rows. Regenerated
  `@comp/api-types` for the new endpoint (`tools/generate-api-types`).
- Added a `destructive` `Button` variant (`bg-destructive`/`text-destructive-foreground` — the
  tokens already existed, just unused as a button style) for Delete and "Delete permanently"; wrapped
  the events table in `overflow-x-auto` since Edit+Delete pushed the action column past phone
  width, a regression the table had no protection against before this change added two more
  buttons to the row.
- Verified live on the Android emulator: at phone width, confirmed the new horizontal scrollbar
  reveals Delete; at desktop width (temporarily via `adb shell wm size`, avoiding the phone
  table's own reflow-during-interaction confusion), ran the full delete flow against real
  finalised-event demo data — warning text, exact-match gating, and the row actually disappearing
  after a real cascade delete.

**A DNF result's status now reads "DNF" everywhere, not "Dnf" in some places.** The league and
event results tables displayed the API's raw `Status` string directly, which came straight from
`EventResultStatus.Dnf.ToString()` — every *other* place DNF appears in this app (the results PDF's
time column, the mobile entry screen's DNF control) already spelled it in caps, so the status
column alone read inconsistently. Fixed at the source rather than patched at each display site, per
explicit user direction ("wherever it's stored"): renamed the enum member itself,
`Comp.Domain.Enums.EventResultStatus.Dnf` → `DNF`, so its `.ToString()` is correct everywhere by
construction. (`Comp.Scoring.ParticipantScoringStatus.Dnf` was deliberately left alone — it's an
internal scoring-domain enum that never reaches the API or a screen, so renaming it would only add
diff with no user-visible effect.)

- The column is stored as text (`EventResultConfiguration`'s `HasConversion<string>()`), so a
  rename alone would silently orphan every already-finalised event's existing "Dnf" rows — EF's
  own migration diffing never catches an enum member rename (it only tracks the CLR type and
  conversion, not member names), so `dotnet ef migrations add` produced an empty migration that
  needed a manual `UPDATE event_results SET status = 'DNF' WHERE status = 'Dnf'` added by hand
  (migration `UppercaseDnfStatus`). Verified against the local dev database directly (`docker exec
  comp-db psql`) before and after: 40 existing DNF rows, all correctly rewritten.
- This migration reaches staging automatically on the next deploy — `render.yaml` sets
  `RunMigrationsOnStartup: true`, so the API applies any pending migration (including this one) on
  every boot, no manual step needed.
- Fixed the two matching frontend checks in `lib/resultsPdf.tsx` (`p.status === "Dnf"` →
  `"DNF"`, used to decide DNF row styling and hide the position number) to match the new value.
- Verified: `dotnet test` (backend, 91 API tests + 21 scoring tests, all still green — no test
  asserted the literal old string) and the local database check above.

Not yet built: M9 (hardening) and the actual EAS Android build and store submission (M10,
per D11).

Do not build a competition-data write endpoint before deciding how it authenticates — the
audit interceptor throws if `SaveChangesAsync` runs with no current user (and no
`PendingActorOverride`), by design.
