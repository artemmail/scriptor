using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTelegramLinkCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TelegramAccountLinks_AspNetUsers_UserId",
                table: "TelegramAccountLinks");

            migrationBuilder.AddForeignKey(
                name: "FK_TelegramAccountLinks_AspNetUsers_UserId",
                table: "TelegramAccountLinks",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TelegramAccountLinks_AspNetUsers_UserId",
                table: "TelegramAccountLinks");

            migrationBuilder.AddForeignKey(
                name: "FK_TelegramAccountLinks_AspNetUsers_UserId",
                table: "TelegramAccountLinks",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
