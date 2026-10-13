using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakerFacebookUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "Speakers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "Speakers");
        }
    }
}
