# Shooting Competition Management System

A system of record for a weekly timed shooting competition. Officials record run times on a
phone at the range; administrators manage the register, events and league tables on the web.

Full design: `docs/architecture.md`. Read it before any structural change.

## Stack

- **Backend:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 16
- **Web admin:** React 19 + Vite, TanStack Query, TanStack Table, React Router, Tailwind, shadcn/ui
- **Mobile:** Expo / React Native, TypeScript. Prototyping phase targets **Android only**, as
  a sideloadable APK via EAS Build's internal-distribution profile — no Apple/Google
  developer account needed yet. Store submission on both platforms is still the eventual
  goal; it's deferred to a later milestone (folded into M10), not dropped. See the
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

## Testing expectations

`Comp.Scoring` and `clients/packages/core` need near-total coverage, including property-based
tests and golden fixtures taken from real past events. Everywhere else, be pragmatic.

A wrong league table discovered in November, after ten months of events, is the worst outcome
this project has available to it. Write the test.

## Current position

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
that applies). Screens: sign-in (`app/index.tsx`) → event list (`app/events/index.tsx`) →
squad list (`app/events/[eventId]/index.tsx`) → squad runner
(`app/events/[eventId]/squads/[squadId]/index.tsx`) → entry
(`.../squads/[squadId]/entry.tsx`).

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

Not yet built: the M8 **web UI** (results tables, a per-league standings page, a "Finalise"
button on the Events page when a event is in Review, an "Amend" button with a reason prompt
when Finalised — the backend's role+claim gate is enough on its own, per this codebase's
established convention of not also hiding actions client-side based on assumed roles), M9
(hardening), and the actual EAS Android build and store submission (M10, per D11).

Do not build a competition-data write endpoint before deciding how it authenticates — the
audit interceptor throws if `SaveChangesAsync` runs with no current user (and no
`PendingActorOverride`), by design.
