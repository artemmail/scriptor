using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleCalendarConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleAccessToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleAccessTokenExpiresAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleCalendarConsentAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GoogleRefreshToken",
                table: "AspNetUsers");
        }
    }
}
