using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMeetupSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MeetupApiKeyOverride",
                table: "MeetupGroups",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeetupUrlName",
                table: "MeetupGroups",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeetupApiKeyOverride",
                table: "MeetupGroups");

            migrationBuilder.DropColumn(
                name: "MeetupUrlName",
                table: "MeetupGroups");
        }
    }
}
