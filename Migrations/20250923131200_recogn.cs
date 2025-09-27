using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class Recogn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenAiTranscriptionTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SourceFilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConvertedFilePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecognizedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MarkdownText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Done = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenAiTranscriptionTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenAiTranscriptionSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenAiTranscriptionSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenAiTranscriptionSteps_OpenAiTranscriptionTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OpenAiTranscriptionTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenAiTranscriptionSteps_TaskId",
                table: "OpenAiTranscriptionSteps",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenAiTranscriptionSteps");

            migrationBuilder.DropTable(
                name: "OpenAiTranscriptionTasks");
        }
    }
}
