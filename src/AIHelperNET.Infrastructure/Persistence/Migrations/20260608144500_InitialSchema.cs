using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIHelperNET.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerSettings_Length = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerSettings_Complexity = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerSettings_Style = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerSettings_Tone = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerSettings_Format = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerSettings_OutputLanguage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CodeProfile_ProgrammingLanguage = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_BackendFramework = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_FrontendFramework = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_Database = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_CloudDevOps = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_Messaging = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_ArchitectureStyle = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_TestingFramework = table.Column<string>(type: "TEXT", nullable: true),
                    CodeProfile_CustomNotes = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioSource = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClarificationQuestionIds = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    ClarificationResponseIds = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    QuestionFragments = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InitialQuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InitialQuestionText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdateReason = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationTurn_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DetectedQuestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DetectedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetectedQuestion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DetectedQuestion_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedAnswer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedAnswer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedAnswer_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TranscriptItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    BoundaryRole = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranscriptItem_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnswerVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConversationTurnId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnswerVersion_ConversationTurn_ConversationTurnId",
                        column: x => x.ConversationTurnId,
                        principalTable: "ConversationTurn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerVersion_ConversationTurnId",
                table: "AnswerVersion",
                column: "ConversationTurnId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurn_SessionId",
                table: "ConversationTurn",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DetectedQuestion_SessionId",
                table: "DetectedQuestion",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedAnswer_SessionId",
                table: "GeneratedAnswer",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptItem_SessionId",
                table: "TranscriptItem",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnswerVersion");

            migrationBuilder.DropTable(
                name: "DetectedQuestion");

            migrationBuilder.DropTable(
                name: "GeneratedAnswer");

            migrationBuilder.DropTable(
                name: "TranscriptItem");

            migrationBuilder.DropTable(
                name: "ConversationTurn");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
