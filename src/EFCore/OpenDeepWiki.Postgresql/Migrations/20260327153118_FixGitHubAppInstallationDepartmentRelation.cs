using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDeepWiki.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class FixGitHubAppInstallationDepartmentRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GitHubAppInstallations_Departments_DepartmentId1",
                table: "GitHubAppInstallations");

            migrationBuilder.DropIndex(
                name: "IX_GitHubAppInstallations_DepartmentId1",
                table: "GitHubAppInstallations");

            migrationBuilder.DropColumn(
                name: "DepartmentId1",
                table: "GitHubAppInstallations");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "WorkflowAnalysisTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentTaskId",
                table: "WorkflowAnalysisSessions",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingTaskCount",
                table: "WorkflowAnalysisSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProgressMessage",
                table: "WorkflowAnalysisSessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedAt",
                table: "WorkflowAnalysisSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunningTaskCount",
                table: "WorkflowAnalysisSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "WorkflowAnalysisLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AnalysisSessionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    TaskId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAnalysisLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowAnalysisLogs_WorkflowAnalysisSessions_AnalysisSessi~",
                        column: x => x.AnalysisSessionId,
                        principalTable: "WorkflowAnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisSessions_Status_QueuedAt_CreatedAt",
                table: "WorkflowAnalysisSessions",
                columns: new[] { "Status", "QueuedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisLogs_AnalysisSessionId_CreatedAt",
                table: "WorkflowAnalysisLogs",
                columns: new[] { "AnalysisSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisLogs_TaskId_CreatedAt",
                table: "WorkflowAnalysisLogs",
                columns: new[] { "TaskId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowAnalysisLogs");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowAnalysisSessions_Status_QueuedAt_CreatedAt",
                table: "WorkflowAnalysisSessions");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "WorkflowAnalysisTasks");

            migrationBuilder.DropColumn(
                name: "CurrentTaskId",
                table: "WorkflowAnalysisSessions");

            migrationBuilder.DropColumn(
                name: "PendingTaskCount",
                table: "WorkflowAnalysisSessions");

            migrationBuilder.DropColumn(
                name: "ProgressMessage",
                table: "WorkflowAnalysisSessions");

            migrationBuilder.DropColumn(
                name: "QueuedAt",
                table: "WorkflowAnalysisSessions");

            migrationBuilder.DropColumn(
                name: "RunningTaskCount",
                table: "WorkflowAnalysisSessions");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentId1",
                table: "GitHubAppInstallations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallations_DepartmentId1",
                table: "GitHubAppInstallations",
                column: "DepartmentId1");

            migrationBuilder.AddForeignKey(
                name: "FK_GitHubAppInstallations_Departments_DepartmentId1",
                table: "GitHubAppInstallations",
                column: "DepartmentId1",
                principalTable: "Departments",
                principalColumn: "Id");
        }
    }
}
