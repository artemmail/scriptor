using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class downuser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "YoutubeDownloadTasks");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "YoutubeDownloadTasks",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeDownloadTasks_UserId",
                table: "YoutubeDownloadTasks",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_YoutubeDownloadTasks_AspNetUsers_UserId",
                table: "YoutubeDownloadTasks",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_YoutubeDownloadTasks_AspNetUsers_UserId",
                table: "YoutubeDownloadTasks");

            migrationBuilder.DropIndex(
                name: "IX_YoutubeDownloadTasks_UserId",
                table: "YoutubeDownloadTasks");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "YoutubeDownloadTasks");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "YoutubeDownloadTasks",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
