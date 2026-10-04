using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations;

[DbContext(typeof(MyDbContext))]
[Migration("20261003000002_UniqueYooMoneyOperation")]
public sealed class UniqueYooMoneyOperationMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_PaymentOperations_ExternalOperationId",
            table: "PaymentOperations",
            column: "ExternalOperationId",
            unique: true,
            filter: "[Provider] = 1 AND [ExternalOperationId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PaymentOperations_ExternalOperationId",
            table: "PaymentOperations");
    }
}
