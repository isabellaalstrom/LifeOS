using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiCallLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCallLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiInsights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ContextHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Observations = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Hypotheses = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    GeneratedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiInsights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Context = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DecidedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    AppliedEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiRetroMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetroId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsUser = table.Column<bool>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Observations = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Hypotheses = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiRetroMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AttemptedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TasksEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GoalsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetrosEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    HabitsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SendTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    QuietStart = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    QuietEnd = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    NtfyEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    LastSuccessUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ScheduledForUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LockedUntilUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    SentUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AiEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AiMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_OwnerId",
                table: "AiCallLogs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_OwnerId_OccurredUtc",
                table: "AiCallLogs",
                columns: new[] { "OwnerId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_OwnerId",
                table: "AiInsights",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_OwnerId_Feature_GeneratedUtc",
                table: "AiInsights",
                columns: new[] { "OwnerId", "Feature", "GeneratedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProposals_OwnerId",
                table: "AiProposals",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiProposals_OwnerId_Context_Status",
                table: "AiProposals",
                columns: new[] { "OwnerId", "Context", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiRetroMessages_OwnerId",
                table: "AiRetroMessages",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiRetroMessages_RetroId_CreatedUtc",
                table: "AiRetroMessages",
                columns: new[] { "RetroId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_NotificationId",
                table: "NotificationDeliveries",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_OwnerId",
                table: "NotificationDeliveries",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_OwnerId",
                table: "NotificationPreferences",
                column: "OwnerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_OwnerId",
                table: "PushSubscriptions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledNotifications_OwnerId",
                table: "ScheduledNotifications",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledNotifications_OwnerId_DedupeKey",
                table: "ScheduledNotifications",
                columns: new[] { "OwnerId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledNotifications_Status_ScheduledForUtc",
                table: "ScheduledNotifications",
                columns: new[] { "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_OwnerId",
                table: "UserPreferences",
                column: "OwnerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCallLogs");

            migrationBuilder.DropTable(
                name: "AiInsights");

            migrationBuilder.DropTable(
                name: "AiProposals");

            migrationBuilder.DropTable(
                name: "AiRetroMessages");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "ScheduledNotifications");

            migrationBuilder.DropTable(
                name: "UserPreferences");
        }
    }
}
