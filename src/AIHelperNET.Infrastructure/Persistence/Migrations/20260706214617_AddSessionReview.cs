using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIHelperNET.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Markdown = table.Column<string>(type: "TEXT", maxLength: 64000, nullable: false),
                    ModelUsed = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    GeneratedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionReviews_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviews_SessionId",
                table: "SessionReviews",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionReviews");
        }
    }
}
