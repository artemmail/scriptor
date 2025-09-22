using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class remove : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessedText",
                table: "YoutubeCaptions");

            migrationBuilder.DropColumn(
                name: "RecognizedText",
                table: "YoutubeCaptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessedText",
                table: "YoutubeCaptions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecognizedText",
                table: "YoutubeCaptions",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
