using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramIntegrationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramAccountLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TelegramId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastStatusCheckAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramAccountLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramAccountLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelegramLinkTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsOneTime = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramLinkTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramLinkTokens_TelegramAccountLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "TelegramAccountLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramAccountLinks_TelegramId",
                table: "TelegramAccountLinks",
                column: "TelegramId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramAccountLinks_UserId",
                table: "TelegramAccountLinks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinkTokens_LinkId",
                table: "TelegramLinkTokens",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinkTokens_TokenHash",
                table: "TelegramLinkTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramLinkTokens");

            migrationBuilder.DropTable(
                name: "TelegramAccountLinks");
        }
    }
}
