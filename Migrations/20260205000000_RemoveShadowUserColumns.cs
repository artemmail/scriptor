using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShadowUserColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOperations_AspNetUsers_UserId1",
                table: "PaymentOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_RecognitionUsage_AspNetUsers_UserId1",
                table: "RecognitionUsage");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOperations_UserId1",
                table: "PaymentOperations");

            migrationBuilder.DropIndex(
                name: "IX_RecognitionUsage_UserId1",
                table: "RecognitionUsage");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "PaymentOperations");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "RecognitionUsage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId1",
                table: "RecognitionUsage",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId1",
                table: "PaymentOperations",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOperations_UserId1",
                table: "PaymentOperations",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_RecognitionUsage_UserId1",
                table: "RecognitionUsage",
                column: "UserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOperations_AspNetUsers_UserId1",
                table: "PaymentOperations",
                column: "UserId1",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecognitionUsage_AspNetUsers_UserId1",
                table: "RecognitionUsage",
                column: "UserId1",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
