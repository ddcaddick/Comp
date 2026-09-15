# Shooting Competition Management System

A system of record for a weekly timed shooting competition. Officials record run times on a
phone at the range; administrators manage the register, events and league tables on the web.

Full design: `docs/architecture.md`. Read it before any structural change.

## Stack

- **Backend:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 16
- **Web admin:** React 19 + Vite, TanStack Query, TanStack Table, React Router, Tailwind, shadcn/ui
- **Mobile:** Expo / React Native, TypeScript
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
pnpm --filter mobile start                # Expo
dotnet test                               # backend tests
pnpm -r test                              # client tests
```

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

Not yet built past M3: events/squads, the scoring engine, result entry, standings.

Do not build a competition-data write endpoint before deciding how it authenticates — the
audit interceptor throws if `SaveChangesAsync` runs with no current user (and no
`PendingActorOverride`), by design.
