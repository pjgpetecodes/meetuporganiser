using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventFormatAndSubtitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Format",
                table: "MeetupEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Subtitle",
                table: "MeetupEvents",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueName",
                table: "MeetupEvents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Format",
                table: "MeetupEvents");

            migrationBuilder.DropColumn(
                name: "Subtitle",
                table: "MeetupEvents");

            migrationBuilder.DropColumn(
                name: "VenueName",
                table: "MeetupEvents");
        }
    }
}
