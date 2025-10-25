using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleCalendarDeclineTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime?>(
                name: "ConsentDeclinedAt",
                table: "UserGoogleTokens",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsentDeclinedAt",
                table: "UserGoogleTokens");
        }
    }
}
