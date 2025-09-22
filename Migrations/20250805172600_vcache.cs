using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class vcache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChannelId",
                table: "YoutubeDownloadTasks",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "YoutubeChannels",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChannelTitle = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YoutubeChannels", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "YoutubeStreamCaches",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StreamsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YoutubeStreamCaches", x => x.VideoId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeDownloadTasks_ChannelId",
                table: "YoutubeDownloadTasks",
                column: "ChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_YoutubeDownloadTasks_YoutubeChannels_ChannelId",
                table: "YoutubeDownloadTasks",
                column: "ChannelId",
                principalTable: "YoutubeChannels",
                principalColumn: "ChannelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_YoutubeDownloadTasks_YoutubeChannels_ChannelId",
                table: "YoutubeDownloadTasks");

            migrationBuilder.DropTable(
                name: "YoutubeChannels");

            migrationBuilder.DropTable(
                name: "YoutubeStreamCaches");

            migrationBuilder.DropIndex(
                name: "IX_YoutubeDownloadTasks_ChannelId",
                table: "YoutubeDownloadTasks");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "YoutubeDownloadTasks");
        }
    }
}
