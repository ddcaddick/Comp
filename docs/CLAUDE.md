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

## Testing expectations

`Comp.Scoring` and `clients/packages/core` need near-total coverage, including property-based
tests and golden fixtures taken from real past events. Everywhere else, be pragmatic.

A wrong league table discovered in November, after ten months of events, is the worst outcome
this project has available to it. Write the test.

## Current position

Milestone M2 is in progress. Done so far:

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
  registered in `Program.cs`. JWT bearer authentication is wired on the validation side only
  — no endpoint issues a token yet. Tested in `tests/Comp.Api.Tests/IdentityTests.cs`.

Not yet built:

- **Auth endpoints.** `/auth/login` and `/auth/refresh` — token issuance, refresh-token
  rotation and hashing, rate limiting on login. Needed before any client can authenticate.
- **OpenAPI type generation in CI** (section G/M of the architecture doc) — generates
  `clients/mobile/api/api-types.ts` from `Comp.Contracts` and fails the build on drift.
- Everything past M2: shooter CRUD, competitions/leagues, events/squads, the scoring engine,
  result entry, standings.

Do not build admin screens yet, and do not build a competition-data write endpoint before
deciding how it authenticates — the audit interceptor throws if `SaveChangesAsync` runs with
no current user, by design.
