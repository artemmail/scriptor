using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations;

[DbContext(typeof(MyDbContext))]
[Migration("20261003000001_CaptionLanguageSlugs")]
public sealed class CaptionLanguageSlugsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PreviousSlug",
            table: "YoutubeCaptions",
            type: "nvarchar(max)",
            nullable: true);

        var renameExistingSlugs = """
            DECLARE @id nvarchar(450), @old nvarchar(max), @key nvarchar(max), @video nvarchar(max);
            DECLARE @base nvarchar(max), @candidate nvarchar(max), @lang nvarchar(50), @primary nvarchar(50);
            DECLARE @kind nvarchar(20), @tail nvarchar(50), @prefix nvarchar(max), @rest nvarchar(max);
            DECLARE caption_tasks CURSOR LOCAL FAST_FORWARD FOR
                SELECT Id, Slug, CaptionTrackKey, VideoId
                FROM YoutubeCaptions
                WHERE CaptionTrackKey IS NOT NULL AND Slug IS NOT NULL
                ORDER BY CreatedAt, Id;
            OPEN caption_tasks;
            FETCH NEXT FROM caption_tasks INTO @id, @old, @key, @video;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @lang = LOWER(LEFT(@key, CHARINDEX('|', @key + '|') - 1));
                SET @primary = LEFT(@lang, CHARINDEX('-', @lang + '-') - 1);
                SET @rest = SUBSTRING(@key, CHARINDEX('|', @key + '|') + 1, 100);
                SET @kind = LEFT(@rest, CHARINDEX('|', @rest + '|') - 1);
                SET @base = @old;
                IF CHARINDEX('-', REVERSE(@old)) > 0
                BEGIN
                    SET @tail = RIGHT(@old, CHARINDEX('-', REVERSE(@old)) - 1);
                    SET @prefix = LEFT(@old, LEN(@old) - LEN(@tail) - 1);
                    IF TRY_CONVERT(int, @tail) IS NOT NULL AND EXISTS
                        (SELECT 1 FROM YoutubeCaptions WHERE Slug = @prefix OR PreviousSlug = @prefix)
                        SET @base = @prefix;
                END;

                SET @candidate = @base + '-' + @primary;
                IF EXISTS (SELECT 1 FROM YoutubeCaptions WHERE Id <> @id AND (Slug = @candidate OR PreviousSlug = @candidate))
                    SET @candidate = @base + '-' + @lang;
                IF EXISTS (SELECT 1 FROM YoutubeCaptions WHERE Id <> @id AND (Slug = @candidate OR PreviousSlug = @candidate))
                    SET @candidate = @base + '-' + @lang + '-' + @kind;
                IF EXISTS (SELECT 1 FROM YoutubeCaptions WHERE Id <> @id AND (Slug = @candidate OR PreviousSlug = @candidate))
                    SET @candidate = @base + '-' + @lang + '-' + COALESCE(@video, @id);
                IF EXISTS (SELECT 1 FROM YoutubeCaptions WHERE Id <> @id AND (Slug = @candidate OR PreviousSlug = @candidate))
                    SET @candidate = @base + '-' + @lang + '-' + @id;

                UPDATE YoutubeCaptions SET PreviousSlug = @old, Slug = @candidate WHERE Id = @id;
                FETCH NEXT FROM caption_tasks INTO @id, @old, @key, @video;
            END;
            CLOSE caption_tasks;
            DEALLOCATE caption_tasks;
            """;
        // SQL Server compiles a batch before ALTER TABLE runs, so the new column
        // must be referenced from a separately compiled statement.
        migrationBuilder.Sql("EXEC(N'" + renameExistingSlugs.Replace("'", "''") + "')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE YoutubeCaptions SET Slug = PreviousSlug WHERE PreviousSlug IS NOT NULL;");
        migrationBuilder.DropColumn(name: "PreviousSlug", table: "YoutubeCaptions");
    }
}
