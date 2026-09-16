using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotRunStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EventResultStatus gained a third value, NotRun, distinguishing "no runs
            // recorded at all" from an actual DNF -- previously both collapsed into DNF.
            // Re-derivable from existing data: Run rows aren't deleted after finalisation,
            // so any already-frozen "DNF" row whose participant has zero Run rows was
            // always really "never attempted", not an explicit DNF. EF's migration diff
            // never catches an enum member addition on its own (it only tracks the CLR
            // type and conversion, not member names), hence the manual UPDATE.
            migrationBuilder.Sql("""
                UPDATE event_results
                SET status = 'NotRun'
                WHERE status = 'DNF'
                  AND NOT EXISTS (
                      SELECT 1 FROM runs WHERE runs.event_participant_id = event_results.event_participant_id
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE event_results SET status = 'DNF' WHERE status = 'NotRun';");
        }
    }
}
