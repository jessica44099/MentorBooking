using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorBooking.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRecordAuditSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                table: "SessionRecordAudits",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewPrivateMentorNote",
                table: "SessionRecordAudits",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewResolution",
                table: "SessionRecordAudits",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewSummary",
                table: "SessionRecordAudits",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldPrivateMentorNote",
                table: "SessionRecordAudits",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldResolution",
                table: "SessionRecordAudits",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldSummary",
                table: "SessionRecordAudits",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeReason",
                table: "SessionRecordAudits");

            migrationBuilder.DropColumn(
                name: "NewPrivateMentorNote",
                table: "SessionRecordAudits");

            migrationBuilder.DropColumn(
                name: "NewResolution",
                table: "SessionRecordAudits");

            migrationBuilder.DropColumn(
                name: "NewSummary",
                table: "SessionRecordAudits");

            migrationBuilder.DropColumn(
                name: "OldPrivateMentorNote",
                table: "SessionRecordAudits");

            migrationBuilder.DropColumn(
                name: "OldResolution",
                table: "SessionRecordAudits");

            migrationBuilder.DropColumn(
                name: "OldSummary",
                table: "SessionRecordAudits");
        }
    }
}
