using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class mig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobData",
                table: "YoutubeDownloadFiles");

            migrationBuilder.DropColumn(
                name: "FileType",
                table: "YoutubeDownloadFiles");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "YoutubeDownloadTasks",
                newName: "StreamsJson");

            migrationBuilder.AddColumn<string>(
                name: "MergedFilePath",
                table: "YoutubeDownloadTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Container",
                table: "YoutubeDownloadFiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualityLabel",
                table: "YoutubeDownloadFiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreamType",
                table: "YoutubeDownloadFiles",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MergedFilePath",
                table: "YoutubeDownloadTasks");

            migrationBuilder.DropColumn(
                name: "Container",
                table: "YoutubeDownloadFiles");

            migrationBuilder.DropColumn(
                name: "QualityLabel",
                table: "YoutubeDownloadFiles");

            migrationBuilder.DropColumn(
                name: "StreamType",
                table: "YoutubeDownloadFiles");

            migrationBuilder.RenameColumn(
                name: "StreamsJson",
                table: "YoutubeDownloadTasks",
                newName: "Title");

            migrationBuilder.AddColumn<byte[]>(
                name: "BlobData",
                table: "YoutubeDownloadFiles",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FileType",
                table: "YoutubeDownloadFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
