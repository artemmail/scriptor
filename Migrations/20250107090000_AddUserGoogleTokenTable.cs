using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGoogleTokenTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserGoogleTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TokenType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "calendar"),
                    Scope = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AccessToken = table.Column<string>(type: "nvarchar(4096)", maxLength: 4096, nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AccessTokenUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshToken = table.Column<string>(type: "nvarchar(4096)", maxLength: 4096, nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsentGrantedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGoogleTokens", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserGoogleTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
                INSERT INTO UserGoogleTokens (
                    UserId,
                    TokenType,
                    Scope,
                    AccessToken,
                    AccessTokenExpiresAt,
                    AccessTokenUpdatedAt,
                    RefreshToken,
                    RefreshTokenExpiresAt,
                    ConsentGrantedAt,
                    RevokedAt,
                    UpdatedAt)
                SELECT
                    Id,
                    'calendar',
                    NULL,
                    GoogleAccessToken,
                    GoogleAccessTokenExpiresAt,
                    GoogleAccessTokenUpdatedAt,
                    GoogleRefreshToken,
                    GoogleRefreshTokenExpiresAt,
                    GoogleCalendarConsentAt,
                    GoogleTokensRevokedAt,
                    GoogleAccessTokenUpdatedAt
                FROM AspNetUsers
                WHERE GoogleAccessToken IS NOT NULL
                    OR GoogleRefreshToken IS NOT NULL
                    OR GoogleCalendarConsentAt IS NOT NULL
                    OR GoogleTokensRevokedAt IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "GoogleAccessToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleAccessTokenExpiresAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleAccessTokenUpdatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleCalendarConsentAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleRefreshToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleRefreshTokenExpiresAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleTokensRevokedAt",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleAccessToken",
                table: "AspNetUsers",
                type: "nvarchar(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTime?>(
                name: "GoogleAccessTokenExpiresAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime?>(
                name: "GoogleAccessTokenUpdatedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime?>(
                name: "GoogleCalendarConsentAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleRefreshToken",
                table: "AspNetUsers",
                type: "nvarchar(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTime?>(
                name: "GoogleRefreshTokenExpiresAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime?>(
                name: "GoogleTokensRevokedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE u
                SET
                    u.GoogleAccessToken = t.AccessToken,
                    u.GoogleAccessTokenExpiresAt = t.AccessTokenExpiresAt,
                    u.GoogleAccessTokenUpdatedAt = t.AccessTokenUpdatedAt,
                    u.GoogleRefreshToken = t.RefreshToken,
                    u.GoogleRefreshTokenExpiresAt = t.RefreshTokenExpiresAt,
                    u.GoogleCalendarConsentAt = t.ConsentGrantedAt,
                    u.GoogleTokensRevokedAt = t.RevokedAt
                FROM AspNetUsers u
                INNER JOIN UserGoogleTokens t ON t.UserId = u.Id
                WHERE t.TokenType = 'calendar';
            ");

            migrationBuilder.DropTable(
                name: "UserGoogleTokens");
        }
    }
}
