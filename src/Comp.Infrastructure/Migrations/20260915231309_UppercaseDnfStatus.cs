using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UppercaseDnfStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EventResultStatus.Dnf was renamed to DNF (Comp.Domain.Enums.EventResultStatus) so
            // every client shows "DNF" consistently rather than "Dnf" -- the column is a plain
            // HasConversion<string>() text value, not a database enum/check constraint, so
            // existing rows need their text updated by hand; EF's own migration diff never
            // detects an enum member rename since it only tracks the CLR type and conversion,
            // not member names.
            migrationBuilder.Sql("UPDATE event_results SET status = 'DNF' WHERE status = 'Dnf';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE event_results SET status = 'Dnf' WHERE status = 'DNF';");
        }
    }
}
