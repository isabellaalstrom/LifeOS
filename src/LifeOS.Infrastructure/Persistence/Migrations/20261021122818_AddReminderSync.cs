using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReminderLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReminderUid = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastTitle = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    LastDue = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    PendingSinceUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReminderLinks_Tasks_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReminderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PushEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLinks_OwnerId",
                table: "ReminderLinks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLinks_OwnerId_ReminderUid",
                table: "ReminderLinks",
                columns: new[] { "OwnerId", "ReminderUid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLinks_OwnerId_TaskItemId",
                table: "ReminderLinks",
                columns: new[] { "OwnerId", "TaskItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLinks_TaskItemId",
                table: "ReminderLinks",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderSettings_OwnerId",
                table: "ReminderSettings",
                column: "OwnerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderLinks");

            migrationBuilder.DropTable(
                name: "ReminderSettings");
        }
    }
}
