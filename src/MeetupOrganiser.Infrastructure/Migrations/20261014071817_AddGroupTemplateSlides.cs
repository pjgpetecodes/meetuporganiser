using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupTemplateSlides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TemplateSlideOneSpeaker",
                table: "MeetupGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateSlideTwoSpeakers",
                table: "MeetupGroups",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TemplateSlideOneSpeaker",
                table: "MeetupGroups");

            migrationBuilder.DropColumn(
                name: "TemplateSlideTwoSpeakers",
                table: "MeetupGroups");
        }
    }
}
