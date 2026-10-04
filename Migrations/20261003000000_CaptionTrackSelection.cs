using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations;

[DbContext(typeof(MyDbContext))]
[Migration("20261003000000_CaptionTrackSelection")]
public sealed class CaptionTrackSelectionMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "VideoId",
            table: "YoutubeCaptions",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CaptionTrackKey",
            table: "YoutubeCaptions",
            type: "nvarchar(max)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "VideoId", table: "YoutubeCaptions");
        migrationBuilder.DropColumn(name: "CaptionTrackKey", table: "YoutubeCaptions");
    }
}
