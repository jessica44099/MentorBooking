using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorBooking.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRecordsAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.SessionId);
                    table.CheckConstraint("CK_Reviews_Rating", "\"Rating\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_Reviews_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionRecords",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DoubtsCleared = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PrivateMentorNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRecords", x => x.SessionId);
                    table.CheckConstraint("CK_SessionRecords_DoubtsCleared", "\"DoubtsCleared\" BETWEEN 0 AND 20");
                    table.ForeignKey(
                        name: "FK_SessionRecords_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionStatusHistory_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionStatusHistory_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionRecordAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangedByMentorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OldValue = table.Column<int>(type: "INTEGER", nullable: false),
                    NewValue = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRecordAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionRecordAudits_SessionRecords_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SessionRecords",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionRecordAudits_Users_ChangedByMentorId",
                        column: x => x.ChangedByMentorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role",
                table: "Users",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordAudits_ChangedByMentorId",
                table: "SessionRecordAudits",
                column: "ChangedByMentorId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordAudits_SessionId_ChangedAtUtc",
                table: "SessionRecordAudits",
                columns: new[] { "SessionId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionStatusHistory_ChangedByUserId",
                table: "SessionStatusHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionStatusHistory_SessionId_ChangedAtUtc",
                table: "SessionStatusHistory",
                columns: new[] { "SessionId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "SessionRecordAudits");

            migrationBuilder.DropTable(
                name: "SessionStatusHistory");

            migrationBuilder.DropTable(
                name: "SessionRecords");

            migrationBuilder.DropIndex(
                name: "IX_Users_Role",
                table: "Users");
        }
    }
}
