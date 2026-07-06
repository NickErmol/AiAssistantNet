using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIHelperNET.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistTranscriptSpeakerAndConfidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "Confidence",
                table: "TranscriptItem",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "Speaker",
                table: "TranscriptItem",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "TranscriptItem");

            migrationBuilder.DropColumn(
                name: "Speaker",
                table: "TranscriptItem");
        }
    }
}
