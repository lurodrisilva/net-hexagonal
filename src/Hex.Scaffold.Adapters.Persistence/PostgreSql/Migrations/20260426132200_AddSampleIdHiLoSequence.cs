using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleIdHiLoSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "samples_hilo_seq",
                incrementBy: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "samples_hilo_seq");
        }
    }
}
