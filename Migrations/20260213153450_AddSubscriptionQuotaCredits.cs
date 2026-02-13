using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YandexSpeech.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionQuotaCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpenAiTranscriptionTasks_RecognitionProfiles_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.DropColumn(
                name: "RecognitionsResetAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RecognitionsToday",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotaChargedAt",
                table: "YoutubeCaptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantedTranscriptionMinutes",
                table: "UserSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GrantedVideos",
                table: "UserSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RemainingTranscriptionMinutes",
                table: "UserSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RemainingVideos",
                table: "UserSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IncludedTranscriptionMinutes",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IncludedVideos",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotaChargedAt",
                table: "OpenAiTranscriptionTasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedTranscriptionMinutes",
                table: "OpenAiTranscriptionTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceDurationSeconds",
                table: "OpenAiTranscriptionTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OpenAiTranscriptionTasks_RecognitionProfiles_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks",
                column: "RecognitionProfileId",
                principalTable: "RecognitionProfiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpenAiTranscriptionTasks_RecognitionProfiles_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.DropColumn(
                name: "QuotaChargedAt",
                table: "YoutubeCaptions");

            migrationBuilder.DropColumn(
                name: "GrantedTranscriptionMinutes",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "GrantedVideos",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "RemainingTranscriptionMinutes",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "RemainingVideos",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "IncludedTranscriptionMinutes",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludedVideos",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "QuotaChargedAt",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.DropColumn(
                name: "RequestedTranscriptionMinutes",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.DropColumn(
                name: "SourceDurationSeconds",
                table: "OpenAiTranscriptionTasks");

            migrationBuilder.AddColumn<DateTime>(
                name: "RecognitionsResetAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecognitionsToday",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_OpenAiTranscriptionTasks_RecognitionProfiles_RecognitionProfileId",
                table: "OpenAiTranscriptionTasks",
                column: "RecognitionProfileId",
                principalTable: "RecognitionProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
