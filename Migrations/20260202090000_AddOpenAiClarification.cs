using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenAiClarification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Clarification",
                table: "OpenAiTranscriptionTasks",
                type: "nvarchar(max)",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Clarification",
                table: "OpenAiTranscriptionTasks"
            );
        }
    }
}
