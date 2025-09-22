using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class subt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSubtitleTask",
                table: "SpeechRecognitionTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "SpeechRecognitionTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YoutubeId",
                table: "SpeechRecognitionTasks",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSubtitleTask",
                table: "SpeechRecognitionTasks");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "SpeechRecognitionTasks");

            migrationBuilder.DropColumn(
                name: "YoutubeId",
                table: "SpeechRecognitionTasks");
        }
    }
}
