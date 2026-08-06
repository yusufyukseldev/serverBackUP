using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerBackup.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Snapshots",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Snapshots");
        }
    }
}
