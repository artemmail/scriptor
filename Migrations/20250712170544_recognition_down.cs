using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class recognition_down : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AudioFiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalFilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConvertedFileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConvertedFilePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConvertedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SpeechRecognitionTaskId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioFiles_SpeechRecognitionTasks_SpeechRecognitionTaskId",
                        column: x => x.SpeechRecognitionTaskId,
                        principalTable: "SpeechRecognitionTasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AudioWorkflowTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AudioFileId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BucketName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OperationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecognizedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Preview = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Done = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SegmentsTotal = table.Column<int>(type: "int", nullable: false),
                    SegmentsProcessed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioWorkflowTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioWorkflowTasks_AudioFiles_AudioFileId",
                        column: x => x.AudioFileId,
                        principalTable: "AudioFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AudioWorkflowSegments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsProcessing = table.Column<bool>(type: "bit", nullable: false),
                    IsProcessed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioWorkflowSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioWorkflowSegments_AudioWorkflowTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AudioWorkflowTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioFiles_SpeechRecognitionTaskId",
                table: "AudioFiles",
                column: "SpeechRecognitionTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioWorkflowSegments_TaskId",
                table: "AudioWorkflowSegments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioWorkflowTasks_AudioFileId",
                table: "AudioWorkflowTasks",
                column: "AudioFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioWorkflowSegments");

            migrationBuilder.DropTable(
                name: "AudioWorkflowTasks");

            migrationBuilder.DropTable(
                name: "AudioFiles");
        }
    }
}
