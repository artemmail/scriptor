using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace YandexSpeech.Migrations;

[DbContext(typeof(MyDbContext))]
[Migration("20261023120000_AddDjvuBookQueue")]
public sealed class AddDjvuBookQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("DjvuBookJobs", table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
            Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
            Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
            TotalBlocks = table.Column<int>(type: "int", nullable: false),
            CompletedBlocks = table.Column<int>(type: "int", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
            CreditChargedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
            Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
            ResultXml = table.Column<string>(type: "nvarchar(max)", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DjvuBookJobs", x => x.Id);
            table.ForeignKey("FK_DjvuBookJobs_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable("DjvuBookChapters", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Ordinal = table.Column<int>(type: "int", nullable: false),
            Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            Depth = table.Column<int>(type: "int", nullable: false),
            Page = table.Column<int>(type: "int", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DjvuBookChapters", x => x.Id);
            table.ForeignKey("FK_DjvuBookChapters_DjvuBookJobs_JobId", x => x.JobId, "DjvuBookJobs", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable("DjvuBookBlocks", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Ordinal = table.Column<int>(type: "int", nullable: false),
            ChapterIndex = table.Column<int>(type: "int", nullable: false),
            Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
            Fragment = table.Column<string>(type: "nvarchar(max)", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DjvuBookBlocks", x => x.Id);
            table.ForeignKey("FK_DjvuBookBlocks_DjvuBookJobs_JobId", x => x.JobId, "DjvuBookJobs", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_DjvuBookJobs_UserId_CreatedAt", "DjvuBookJobs", new[] { "UserId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_DjvuBookChapters_JobId_Ordinal", "DjvuBookChapters", new[] { "JobId", "Ordinal" }, unique: true);
        migrationBuilder.CreateIndex("IX_DjvuBookBlocks_JobId_Ordinal", "DjvuBookBlocks", new[] { "JobId", "Ordinal" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("DjvuBookBlocks");
        migrationBuilder.DropTable("DjvuBookChapters");
        migrationBuilder.DropTable("DjvuBookJobs");
    }
}
