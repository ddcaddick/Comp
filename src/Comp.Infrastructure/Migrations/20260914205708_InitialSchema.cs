using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Comp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shooters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nickname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    membership_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shooters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    penalty_seconds = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    runs_per_shooter = table.Column<int>(type: "integer", nullable: false),
                    counts_for_standings = table.Column<bool>(type: "boolean", nullable: false),
                    scoring_rules_version = table.Column<int>(type: "integer", nullable: false),
                    finalised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finalised_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_events_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leagues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tier = table.Column<int>(type: "integer", nullable: false),
                    points_for_first = table.Column<int>(type: "integer", nullable: false),
                    points_decrement = table.Column<int>(type: "integer", nullable: false),
                    drop_worst_count = table.Column<int>(type: "integer", nullable: false),
                    absences_count_as_zero = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leagues", x => x.id);
                    table.ForeignKey(
                        name: "fk_leagues_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entry_sessions",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_sessions", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_entry_sessions_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "squads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    squad_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_squads", x => x.id);
                    table.ForeignKey(
                        name: "fk_squads_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "league_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shooter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_league_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_league_memberships_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_league_memberships_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_league_memberships_shooters_shooter_id",
                        column: x => x.shooter_id,
                        principalTable: "shooters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shooter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: true),
                    squad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_in_squad = table.Column<int>(type: "integer", nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_participants_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_participants_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_participants_shooters_shooter_id",
                        column: x => x.shooter_id,
                        principalTable: "shooters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_participants_squads_squad_id",
                        column: x => x.squad_id,
                        principalTable: "squads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_number = table.Column<int>(type: "integer", nullable: false),
                    raw_time_ms = table.Column<int>(type: "integer", nullable: true),
                    penalty_count = table.Column<int>(type: "integer", nullable: false),
                    is_dnf = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.id);
                    table.CheckConstraint("ck_runs_dnf_has_no_time", "(is_dnf and raw_time_ms is null) or (not is_dnf and raw_time_ms is not null)");
                    table.CheckConstraint("ck_runs_penalty_count_non_negative", "penalty_count >= 0");
                    table.CheckConstraint("ck_runs_raw_time_ms_positive", "raw_time_ms is null or raw_time_ms > 0");
                    table.ForeignKey(
                        name: "fk_runs_event_participants_event_participant_id",
                        column: x => x.event_participant_id,
                        principalTable: "event_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: true),
                    best_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_time_ms = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    overall_position = table.Column<int>(type: "integer", nullable: true),
                    league_position = table.Column<int>(type: "integer", nullable: true),
                    league_points = table.Column<int>(type: "integer", nullable: false),
                    rules_version = table.Column<int>(type: "integer", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_results_event_participants_event_participant_id",
                        column: x => x.event_participant_id,
                        principalTable: "event_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_results_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_results_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_results_runs_best_run_id",
                        column: x => x.best_run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_type_entity_id_occurred_at",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id", "occurred_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_competitions_year_name",
                table: "competitions",
                columns: new[] { "year", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_participants_event_id_shooter_id",
                table: "event_participants",
                columns: new[] { "event_id", "shooter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_participants_league_id",
                table: "event_participants",
                column: "league_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_participants_shooter_id",
                table: "event_participants",
                column: "shooter_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_participants_squad_id",
                table: "event_participants",
                column: "squad_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_results_best_run_id",
                table: "event_results",
                column: "best_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_results_event_id_league_id_league_position",
                table: "event_results",
                columns: new[] { "event_id", "league_id", "league_position" });

            migrationBuilder.CreateIndex(
                name: "ix_event_results_event_participant_id",
                table: "event_results",
                column: "event_participant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_results_league_id_event_participant_id",
                table: "event_results",
                columns: new[] { "league_id", "event_participant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_events_competition_id_event_number",
                table: "events",
                columns: new[] { "competition_id", "event_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_league_memberships_competition_id_shooter_id",
                table: "league_memberships",
                columns: new[] { "competition_id", "shooter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_league_memberships_league_id",
                table: "league_memberships",
                column: "league_id");

            migrationBuilder.CreateIndex(
                name: "ix_league_memberships_shooter_id",
                table: "league_memberships",
                column: "shooter_id");

            migrationBuilder.CreateIndex(
                name: "ix_leagues_competition_id_name",
                table: "leagues",
                columns: new[] { "competition_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leagues_competition_id_tier",
                table: "leagues",
                columns: new[] { "competition_id", "tier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runs_event_participant_id_run_number",
                table: "runs",
                columns: new[] { "event_participant_id", "run_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runs_idempotency_key",
                table: "runs",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_squads_event_id_squad_number",
                table: "squads",
                columns: new[] { "event_id", "squad_number" },
                unique: true);

            // The recent-first type-ahead search needs a trigram index over an expression
            // EF Core cannot represent.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql("""
                CREATE INDEX ix_shooters_search
                ON shooters
                USING gin ((first_name || ' ' || last_name || ' ' || coalesce(nickname, '')) gin_trgm_ops);
                """);

            // Locks finalised events at the database level, so the guarantee holds even if
            // a future code path forgets, or if someone reaches for psql at 11pm. The
            // application layer refuses writes first; this is the backstop.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_runs_reject_finalised ON runs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS reject_write_to_finalised_event();");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_shooters_search;");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "entry_sessions");

            migrationBuilder.DropTable(
                name: "event_results");

            migrationBuilder.DropTable(
                name: "league_memberships");

            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "event_participants");

            migrationBuilder.DropTable(
                name: "leagues");

            migrationBuilder.DropTable(
                name: "shooters");

            migrationBuilder.DropTable(
                name: "squads");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "competitions");
        }
    }
}
