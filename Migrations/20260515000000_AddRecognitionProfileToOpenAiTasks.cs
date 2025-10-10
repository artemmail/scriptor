using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class AddRecognitionProfileToOpenAiTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecognitionProfileDisplayedName",
                table: "OpenAiTranscriptionTasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RecognitionProfileId",
                table: "OpenAiTranscriptionTasks",
                type: "int",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_OpenAiTranscriptionTasks_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks",
                column: "RecognitionProfileId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_OpenAiTranscriptionTasks_RecognitionProfiles_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks",
                column: "RecognitionProfileId",
                principalTable: "RecognitionProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecognitionProfileDisplayedName",
                table: "OpenAiTranscriptionTasks"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_OpenAiTranscriptionTasks_RecognitionProfiles_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks"
            );

            migrationBuilder.DropIndex(
                name: "IX_OpenAiTranscriptionTasks_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks"
            );

            migrationBuilder.DropColumn(
                name: "RecognitionProfileId",
                table: "OpenAiTranscriptionTasks"
            );
        }
    }
}
