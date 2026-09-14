# Wiring up M2 — schema and first migration

> **Status: complete.** All eight steps below are done and verified — see `docs/CLAUDE.md`'s
> "Current position" for the current state of M2 as a whole, including the audit interceptor
> and Identity work that followed this doc.

The `src/` tree from this drop maps onto your repository. Copy `Comp.Domain` and
`Comp.Infrastructure` over the existing folders, keeping the `.csproj` files you already have.

---

## 1. One extra package

```powershell
dotnet add src/Comp.Infrastructure package EFCore.NamingConventions
```

This maps `EventParticipant.PositionInSquad` to `event_participants.position_in_squad`
automatically. Without it, EF generates quoted PascalCase column names, which are awkward to
query by hand in psql — and you will be querying by hand.

It also matters for correctness here: the check constraints are written against snake_case
column names, so they only compile against a snake_case schema.

---

## 2. Register the context

In `src/Comp.Api/Program.cs`, above `var app = builder.Build();`:

```csharp
builder.Services.AddDbContext<CompDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseSnakeCaseNamingConvention());
```

with these usings at the top:

```csharp
using Comp.Infrastructure;
using Microsoft.EntityFrameworkCore;
```

---

## 3. Create and apply the migration

```powershell
dotnet ef migrations add InitialSchema `
  --project src/Comp.Infrastructure `
  --startup-project src/Comp.Api

dotnet ef database update `
  --project src/Comp.Infrastructure `
  --startup-project src/Comp.Api
```

Open the generated migration before applying it. Reading the first one properly is worth the
ten minutes: it is the clearest single view of the schema you will ever get, and mistakes are
cheap now and expensive in March.

---

## 4. Add the search index by hand

The trigram index behind the type-ahead needs an extension and an expression EF cannot
express. Open the generated migration and add this to the end of `Up`:

```csharp
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

migrationBuilder.Sql("""
    CREATE INDEX ix_shooters_search
    ON shooters
    USING gin ((first_name || ' ' || last_name || ' ' || coalesce(nickname, '')) gin_trgm_ops);
    """);
```

and the matching lines to `Down`:

```csharp
migrationBuilder.Sql("DROP INDEX IF EXISTS ix_shooters_search;");
```

That is what makes `WHERE ... ILIKE '%dav%'` fast enough to filter as the official types,
rather than scanning every shooter on each keystroke.

---

## 5. Lock finalised events at the database level

The application layer will refuse to write runs for a finalised event. A trigger makes that
guarantee hold even if a future code path forgets, or if someone reaches for psql at 11pm.

Add to `Up` in the same migration:

```csharp
migrationBuilder.Sql("""
    CREATE OR REPLACE FUNCTION reject_write_to_finalised_event()
    RETURNS trigger AS $$
    DECLARE
        v_status text;
    BEGIN
        SELECT e.status INTO v_status
        FROM event_participants p
        JOIN events e ON e.id = p.event_id
        WHERE p.id = COALESCE(NEW.event_participant_id, OLD.event_participant_id);

        IF v_status = 'Finalised' THEN
            RAISE EXCEPTION 'Event is finalised; results cannot be changed without an amendment';
        END IF;

        RETURN COALESCE(NEW, OLD);
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER trg_runs_reject_finalised
    BEFORE INSERT OR UPDATE OR DELETE ON runs
    FOR EACH ROW EXECUTE FUNCTION reject_write_to_finalised_event();
    """);
```

and to `Down`:

```csharp
migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_runs_reject_finalised ON runs;");
migrationBuilder.Sql("DROP FUNCTION IF EXISTS reject_write_to_finalised_event();");
```

The amendment flow moves the event out of `Finalised` first, with the privilege check and the
audit record, and only then writes. So the trigger never fires during a legitimate amendment
and always fires during an illegitimate one.

---

## 6. Check the shape

```powershell
docker exec -it comp-db psql -U comp -d comp_dev
```

```sql
\dt
\d runs
\d event_results
```

You should see snake_case tables, the check constraints listed under each table, and
`ck_runs_dnf_has_no_time` on `runs`. Prove it works:

```sql
INSERT INTO runs (id, event_participant_id, run_number, raw_time_ms, penalty_count,
                  is_dnf, recorded_by_user_id, recorded_at)
VALUES (gen_random_uuid(), gen_random_uuid(), 1, 42000, 0, true, gen_random_uuid(), now());
```

That should fail on the check constraint before it even reaches the foreign key. A database
that refuses to store a DNF with a time is doing a job that no amount of application code can
be relied on to do forever.

---

## 7. What is deliberately absent

**No global query filter on `Shooter.IsActive`.** A filter would quietly remove deactivated
shooters from historical results, which is the opposite of what soft delete is for. Only the
participant selection query filters, and it does so explicitly.

**No unique index on `(squad_id, position_in_squad)`.** Reordering a squad swaps two positions,
which a non-deferrable unique index rejects mid-transaction. A duplicated position is a
display-order glitch, not a corrupted result, so it does not justify a deferrable constraint.

**No navigation properties to `AppUser` from domain entities.** `Comp.Domain` references
nothing, so it holds a plain `Guid` user id. Joining to a user for display is the API layer's
job.

**No `Comp.Scoring` reference anywhere in `Comp.Infrastructure`.** Mapping entities into the
plain records the scoring engine accepts belongs to `Comp.Application`.

---

## 8. Next

The audit interceptor. It belongs before any write endpoint exists, so that no service is ever
written without one. It hooks `SaveChangesAsync`, walks the `ChangeTracker` for tracked
entities, and writes an `AuditLog` row per change with the actor taken from the current
`ClaimsPrincipal`.
