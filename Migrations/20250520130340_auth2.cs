using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class auth2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "YoutubeCaptions",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeCaptions_UserId",
                table: "YoutubeCaptions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_YoutubeCaptions_AspNetUsers_UserId",
                table: "YoutubeCaptions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_YoutubeCaptions_AspNetUsers_UserId",
                table: "YoutubeCaptions");

            migrationBuilder.DropIndex(
                name: "IX_YoutubeCaptions_UserId",
                table: "YoutubeCaptions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "YoutubeCaptions");
        }
    }
}
