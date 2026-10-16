using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalsAndHabits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "GeneratedForPeriodStart",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeneratedIndex",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HabitId",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GoalProgressEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GoalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Percent = table.Column<decimal>(type: "TEXT", nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", nullable: true),
                    Target = table.Column<decimal>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalProgressEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Horizon = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LifeAreaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentGoalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    TargetValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    CurrentValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ManualPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    AchievedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goals_Goals_ParentGoalId",
                        column: x => x.ParentGoalId,
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Goals_LifeAreas_LifeAreaId",
                        column: x => x.LifeAreaId,
                        principalTable: "LifeAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoalMilestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GoalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalMilestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalMilestones_Goals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Habits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Period = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TargetCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LifeAreaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GoalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GoalIncrement = table.Column<decimal>(type: "TEXT", nullable: false),
                    EstimateMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    GenerateSprintTasks = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Habits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Habits_Goals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Habits_LifeAreas_LifeAreaId",
                        column: x => x.LifeAreaId,
                        principalTable: "LifeAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HabitCompletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HabitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TaskItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitCompletions_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_GoalId",
                table: "Tasks",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_HabitId_GeneratedForPeriodStart_GeneratedIndex",
                table: "Tasks",
                columns: new[] { "HabitId", "GeneratedForPeriodStart", "GeneratedIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_GoalId",
                table: "Tasks",
                columns: new[] { "OwnerId", "GoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_GoalId",
                table: "Projects",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalMilestones_GoalId_SortOrder",
                table: "GoalMilestones",
                columns: new[] { "GoalId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalMilestones_OwnerId",
                table: "GoalMilestones",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalProgressEntries_GoalId_RecordedUtc",
                table: "GoalProgressEntries",
                columns: new[] { "GoalId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalProgressEntries_OwnerId",
                table: "GoalProgressEntries",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_LifeAreaId",
                table: "Goals",
                column: "LifeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_OwnerId",
                table: "Goals",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_OwnerId_Horizon_PeriodStart",
                table: "Goals",
                columns: new[] { "OwnerId", "Horizon", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_ParentGoalId",
                table: "Goals",
                column: "ParentGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_HabitId_Date",
                table: "HabitCompletions",
                columns: new[] { "HabitId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_OwnerId",
                table: "HabitCompletions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_TaskItemId",
                table: "HabitCompletions",
                column: "TaskItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Habits_GoalId",
                table: "Habits",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_Habits_LifeAreaId",
                table: "Habits",
                column: "LifeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Habits_OwnerId",
                table: "Habits",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Habits_OwnerId_IsArchived",
                table: "Habits",
                columns: new[] { "OwnerId", "IsArchived" });

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Goals_GoalId",
                table: "Projects",
                column: "GoalId",
                principalTable: "Goals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Goals_GoalId",
                table: "Tasks",
                column: "GoalId",
                principalTable: "Goals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Habits_HabitId",
                table: "Tasks",
                column: "HabitId",
                principalTable: "Habits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Goals_GoalId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Goals_GoalId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Habits_HabitId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "GoalMilestones");

            migrationBuilder.DropTable(
                name: "GoalProgressEntries");

            migrationBuilder.DropTable(
                name: "HabitCompletions");

            migrationBuilder.DropTable(
                name: "Habits");

            migrationBuilder.DropTable(
                name: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_GoalId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_HabitId_GeneratedForPeriodStart_GeneratedIndex",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_OwnerId_GoalId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_GoalId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "GeneratedForPeriodStart",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "GeneratedIndex",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "HabitId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Projects");
        }
    }
}
