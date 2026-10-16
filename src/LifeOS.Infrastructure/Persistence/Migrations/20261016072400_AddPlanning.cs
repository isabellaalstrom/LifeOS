using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "LifeAreas",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "LifeAreas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LifeAreaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_LifeAreas_LifeAreaId",
                        column: x => x.LifeAreaId,
                        principalTable: "LifeAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScopeChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SprintId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskTitle = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    EstimateMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    RelatedSprintId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Goal = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CapacityMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    CommittedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sprints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Triage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EstimateMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ScheduledDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ScheduledTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    FocusDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LifeAreaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_LifeAreas_LifeAreaId",
                        column: x => x.LifeAreaId,
                        principalTable: "LifeAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Tasks_ParentTaskId",
                        column: x => x.ParentTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SprintTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SprintId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LeftUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LeaveReason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintTasks_Sprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SprintTasks_Tasks_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LifeAreaId",
                table: "Projects",
                column: "LifeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OwnerId",
                table: "Projects",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OwnerId_Status",
                table: "Projects",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScopeChanges_OwnerId",
                table: "ScopeChanges",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeChanges_SprintId_OccurredUtc",
                table: "ScopeChanges",
                columns: new[] { "SprintId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScopeChanges_TaskItemId",
                table: "ScopeChanges",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_OwnerId",
                table: "Sprints",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_OwnerId_WeekStart",
                table: "Sprints",
                columns: new[] { "OwnerId", "WeekStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SprintTasks_OwnerId",
                table: "SprintTasks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintTasks_SprintId_TaskItemId",
                table: "SprintTasks",
                columns: new[] { "SprintId", "TaskItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SprintTasks_TaskItemId",
                table: "SprintTasks",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_LifeAreaId",
                table: "Tasks",
                column: "LifeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId",
                table: "Tasks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_DueDate",
                table: "Tasks",
                columns: new[] { "OwnerId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_FocusDate",
                table: "Tasks",
                columns: new[] { "OwnerId", "FocusDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_ProjectId",
                table: "Tasks",
                columns: new[] { "OwnerId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_ScheduledDate",
                table: "Tasks",
                columns: new[] { "OwnerId", "ScheduledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_Triage_Status",
                table: "Tasks",
                columns: new[] { "OwnerId", "Triage", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ParentTaskId",
                table: "Tasks",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScopeChanges");

            migrationBuilder.DropTable(
                name: "SprintTasks");

            migrationBuilder.DropTable(
                name: "Sprints");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "LifeAreas");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "LifeAreas");
        }
    }
}
