using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerBackup.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanVerifySchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerifyCronSchedule",
                table: "Plans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifyLevel",
                table: "Plans",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerifyCronSchedule",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "VerifyLevel",
                table: "Plans");
        }
    }
}
