using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetupOrganiser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakerBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CfpEnabled",
                table: "MeetupGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CfpKey",
                table: "MeetupGroups",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpenForBookings",
                table: "MeetupEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SpeakerBookingTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeetupGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpeakerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    PublicKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BookedSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerBookingTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakerBookingTokens_EventSpeakerSessions_BookedSessionId",
                        column: x => x.BookedSessionId,
                        principalTable: "EventSpeakerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpeakerBookingTokens_MeetupGroups_MeetupGroupId",
                        column: x => x.MeetupGroupId,
                        principalTable: "MeetupGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpeakerBookingTokens_Speakers_SpeakerId",
                        column: x => x.SpeakerId,
                        principalTable: "Speakers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerBookingTokens_BookedSessionId",
                table: "SpeakerBookingTokens",
                column: "BookedSessionId",
                unique: true,
                filter: "[BookedSessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerBookingTokens_MeetupGroupId_Email_CreatedAtUtc",
                table: "SpeakerBookingTokens",
                columns: new[] { "MeetupGroupId", "Email", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerBookingTokens_SpeakerId",
                table: "SpeakerBookingTokens",
                column: "SpeakerId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerBookingTokens_TokenHash",
                table: "SpeakerBookingTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpeakerBookingTokens");

            migrationBuilder.DropColumn(
                name: "CfpEnabled",
                table: "MeetupGroups");

            migrationBuilder.DropColumn(
                name: "CfpKey",
                table: "MeetupGroups");

            migrationBuilder.DropColumn(
                name: "IsOpenForBookings",
                table: "MeetupEvents");
        }
    }
}
