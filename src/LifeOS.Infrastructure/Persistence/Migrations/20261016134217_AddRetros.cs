using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetroActionFollowUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetroId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetroActionFollowUps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetroActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetroId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    OwnerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Hypothesis = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ClosedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    TaskItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetroActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetroResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetroId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Answer = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetroResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Retrospectives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Energy = table.Column<int>(type: "INTEGER", nullable: true),
                    Balance = table.Column<int>(type: "INTEGER", nullable: true),
                    Stress = table.Column<int>(type: "INTEGER", nullable: true),
                    Satisfaction = table.Column<int>(type: "INTEGER", nullable: true),
                    StatsSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Retrospectives", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetroActionFollowUps_ActionId_RecordedUtc",
                table: "RetroActionFollowUps",
                columns: new[] { "ActionId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RetroActionFollowUps_OwnerId",
                table: "RetroActionFollowUps",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RetroActionFollowUps_RetroId",
                table: "RetroActionFollowUps",
                column: "RetroId");

            migrationBuilder.CreateIndex(
                name: "IX_RetroActions_OwnerId",
                table: "RetroActions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RetroActions_OwnerId_Status",
                table: "RetroActions",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RetroActions_RetroId",
                table: "RetroActions",
                column: "RetroId");

            migrationBuilder.CreateIndex(
                name: "IX_RetroResponses_OwnerId",
                table: "RetroResponses",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RetroResponses_RetroId_QuestionKey",
                table: "RetroResponses",
                columns: new[] { "RetroId", "QuestionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Retrospectives_OwnerId",
                table: "Retrospectives",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Retrospectives_OwnerId_Type_PeriodStart",
                table: "Retrospectives",
                columns: new[] { "OwnerId", "Type", "PeriodStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetroActionFollowUps");

            migrationBuilder.DropTable(
                name: "RetroActions");

            migrationBuilder.DropTable(
                name: "RetroResponses");

            migrationBuilder.DropTable(
                name: "Retrospectives");
        }
    }
}
