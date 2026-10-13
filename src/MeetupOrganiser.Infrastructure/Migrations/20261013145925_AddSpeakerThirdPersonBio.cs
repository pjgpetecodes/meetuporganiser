using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakerThirdPersonBio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BioThirdPerson",
                table: "Speakers",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pronouns",
                table: "Speakers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BioThirdPerson",
                table: "Speakers");

            migrationBuilder.DropColumn(
                name: "Pronouns",
                table: "Speakers");
        }
    }
}
