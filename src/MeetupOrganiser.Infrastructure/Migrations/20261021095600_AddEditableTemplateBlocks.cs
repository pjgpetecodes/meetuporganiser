using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEditableTemplateBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "TemplateSections",
                columns: new[] { "Id", "Description", "Markdown", "Name" },
                values: new object[,]
                {
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000017"), "Current talk's description, in third person; first talk outside a talk block", "{{Value}}", "TalkDescription" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000018"), "Current speaker's bio; first speaker outside a speaker block", "{{Value}}", "SpeakerBio" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000019"), "Current speaker's first name; first speaker outside a speaker block", "{{Value}}", "SpeakerFirstName" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000020"), "Current contact URL, available inside contact link blocks", "{{Value}}", "ContactUrl" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000021"), "Formatted contact links for the current speaker", "{{Value}}", "SpeakerLinks" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000022"), "Contact heading and links for the current speaker, blank without links", "{{Value}}", "SpeakerContacts" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000023"), "Repeated for each talk in Talks", "{{TalkTitle}} - {{SpeakerName}}\n\n{{TalkDescription}}", "TalkBlock" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000024"), "Repeated for each non-empty talk description in TalkDescriptions", "{{TalkDescription}}", "TalkDescriptionBlock" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000025"), "Heading when there is one speaker", "Our Speaker:", "SingleSpeakerHeading" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000026"), "Heading when there is more than one speaker", "Our Speakers:", "MultipleSpeakersHeading" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000027"), "Repeated for each distinct speaker in SpeakerSection", "{{SpeakerName}}:\n\n{{SpeakerBio}}\n\n{{SpeakerContacts}}", "SpeakerBlock" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000028"), "Shown only when the current speaker has contact links", "How to find {{SpeakerFirstName}}:\n\n{{SpeakerLinks}}", "SpeakerContactsBlock" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000029"), "One Bluesky contact link", "Bluesky: {{ContactUrl}}", "BlueskyLink" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000030"), "One X contact link", "X: {{ContactUrl}}", "XLink" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000031"), "One LinkedIn contact link", "LinkedIn: {{ContactUrl}}", "LinkedInLink" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000032"), "One GitHub contact link", "GitHub: {{ContactUrl}}", "GitHubLink" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000033"), "One Facebook contact link", "Facebook: {{ContactUrl}}", "FacebookLink" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000034"), "One personal website contact link", "Personal Site: {{ContactUrl}}", "PersonalSiteLink" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000035"), "Shown only when the event has a Zoom URL", "Online on Zoom: {{ZoomUrl}}", "ZoomLinkBlock" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000036"), "Shown only when the Zoom meeting has a passcode", "Passcode: {{ZoomPasscode}}", "ZoomPasscodeBlock" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000037"), "Fallback when a talk has no speaker", "Guest Speaker", "GuestSpeakerName" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000038"), "Fallback when neither event nor group has a location", "Online on Zoom", "DefaultLocation" },
                    { new Guid("b4fd713a-0bee-4f49-8790-000000000039"), "Fallback when an event has no group", ".NET Notts", "DefaultGroupName" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000017"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000018"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000019"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000020"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000021"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000022"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000023"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000024"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000025"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000026"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000027"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000028"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000029"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000030"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000031"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000032"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000033"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000034"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000035"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000036"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000037"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000038"));

            migrationBuilder.DeleteData(
                table: "TemplateSections",
                keyColumn: "Id",
                keyValue: new Guid("b4fd713a-0bee-4f49-8790-000000000039"));
        }
    }
}
