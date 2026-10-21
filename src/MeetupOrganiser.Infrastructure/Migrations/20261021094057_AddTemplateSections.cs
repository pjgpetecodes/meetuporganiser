using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TemplateSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Markdown = table.Column<string>(type: "nvarchar(max)", maxLength: 60000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateSections", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "TemplateSections",
                columns: new[] { "Id", "Description", "Markdown", "Name" },
                values: new object[,]
                {
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000001"), "The event title", "{{Value}}", "EventTitle" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000002"), "Event date, e.g. Tuesday, October 27, 2026", "{{Value}}", "EventDate" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000003"), "Start time in UK time, e.g. 6:30 PM", "{{Value}}", "EventTime" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000004"), "The event's own description text (blank if empty)", "{{Value}}", "EventDescription" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000005"), "Title of the first talk", "{{Value}}", "TalkTitle" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000006"), "Name of the first speaker (all speakers joined with \"and\" if more)", "{{Value}}", "SpeakerName" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000007"), "Each talk as \"Title - Speaker\" followed by its third-person description", "{{Value}}", "Talks" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000008"), "Just the talk descriptions, in third person", "{{Value}}", "TalkDescriptions" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000009"), "\"Our Speaker(s):\" block with each speaker's bio and contact links", "{{Value}}", "SpeakerSection" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000010"), "Location / platform for the event", "{{Value}}", "Location" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000011"), "Zoom join URL", "{{Value}}", "ZoomUrl" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000012"), "Zoom passcode", "{{Value}}", "ZoomPasscode" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000013"), "\"Online on Zoom: url\" plus the passcode, blank if there is no Zoom link", "{{Value}}", "ZoomSection" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000014"), "Meetup group name", "{{Value}}", "GroupName" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000015"), "Group website", "{{Value}}", "WebsiteUrl" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000016"), "Group default hashtags", "{{Value}}", "Hashtags" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateSections_Name",
                table: "TemplateSections",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateSections");
        }
    }
}
