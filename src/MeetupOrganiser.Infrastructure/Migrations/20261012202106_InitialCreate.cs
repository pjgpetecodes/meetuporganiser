using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeetupGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LogoBlobUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PptxTemplateBlobUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PrimaryColorHex = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SecondaryColorHex = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultHashtags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetupGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialPostTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TemplateText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Speakers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Bio = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PhotoBlobUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LinkedInUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TwitterHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BlueSkyHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GitHubHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Speakers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeetupEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeetupGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    EventDateTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    LocationOrPlatform = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ZoomMeetingId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ZoomMeetingUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ZoomPasscode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MeetupEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MeetupEventUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ShortBitlyUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetupEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetupEvents_MeetupGroups_MeetupGroupId",
                        column: x => x.MeetupGroupId,
                        principalTable: "MeetupGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventSpeakerSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeetupEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpeakerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TalkTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TalkDescription = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSpeakerSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSpeakerSessions_MeetupEvents_MeetupEventId",
                        column: x => x.MeetupEventId,
                        principalTable: "MeetupEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventSpeakerSessions_Speakers_SpeakerId",
                        column: x => x.SpeakerId,
                        principalTable: "Speakers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeetupEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BlobUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedAssets_MeetupEvents_MeetupEventId",
                        column: x => x.MeetupEventId,
                        principalTable: "MeetupEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MagicLinkTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SpeakerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventSpeakerSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicLinkTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MagicLinkTokens_EventSpeakerSessions_EventSpeakerSessionId",
                        column: x => x.EventSpeakerSessionId,
                        principalTable: "EventSpeakerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MagicLinkTokens_Speakers_SpeakerId",
                        column: x => x.SpeakerId,
                        principalTable: "Speakers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventSpeakerSessions_MeetupEventId",
                table: "EventSpeakerSessions",
                column: "MeetupEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSpeakerSessions_SpeakerId",
                table: "EventSpeakerSessions",
                column: "SpeakerId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedAssets_MeetupEventId",
                table: "GeneratedAssets",
                column: "MeetupEventId");

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinkTokens_EventSpeakerSessionId",
                table: "MagicLinkTokens",
                column: "EventSpeakerSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinkTokens_SpeakerId",
                table: "MagicLinkTokens",
                column: "SpeakerId");

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinkTokens_TokenHash",
                table: "MagicLinkTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeetupEvents_EventDateTimeUtc",
                table: "MeetupEvents",
                column: "EventDateTimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MeetupEvents_MeetupGroupId",
                table: "MeetupEvents",
                column: "MeetupGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Speakers_Email",
                table: "Speakers",
                column: "Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedAssets");

            migrationBuilder.DropTable(
                name: "MagicLinkTokens");

            migrationBuilder.DropTable(
                name: "SocialPostTemplates");

            migrationBuilder.DropTable(
                name: "EventSpeakerSessions");

            migrationBuilder.DropTable(
                name: "MeetupEvents");

            migrationBuilder.DropTable(
                name: "Speakers");

            migrationBuilder.DropTable(
                name: "MeetupGroups");
        }
    }
}
