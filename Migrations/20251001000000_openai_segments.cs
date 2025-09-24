using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class openai_segments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessedText",
                table: "OpenAiTranscriptionTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SegmentsJson",
                table: "OpenAiTranscriptionTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SegmentsProcessed",
                table: "OpenAiTranscriptionTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SegmentsTotal",
                table: "OpenAiTranscriptionTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OpenAiRecognizedSegments",
                columns: table => new
                {
                    SegmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsProcessed = table.Column<bool>(type: "bit", nullable: false),
                    IsProcessing = table.Column<bool>(type: "bit", nullable: false),
                    StartSeconds = table.Column<double>(type: "float", nullable: true),
                    EndSeconds = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenAiRecognizedSegments", x => x.SegmentId);
                    table.ForeignKey(
                        name: "FK_OpenAiRecognizedSegments_OpenAiTranscriptionTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OpenAiTranscriptionTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenAiRecognizedSegments_TaskId",
                table: "OpenAiRecognizedSegments",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenAiRecognizedSegments");

            migrationBuilder.DropColumn(
                name: "ProcessedText",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.DropColumn(
                name: "SegmentsJson",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.DropColumn(
                name: "SegmentsProcessed",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.DropColumn(
                name: "SegmentsTotal",
                table: "OpenAiTranscriptionTasks");
        }
    }
}
