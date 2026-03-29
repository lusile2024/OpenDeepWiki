using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDeepWiki.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowDeepAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowAnalysisSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    WorkflowTemplateSessionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BranchId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LanguageCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ProfileKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DraftVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ChapterKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Objective = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TotalTasks = table.Column<int>(type: "integer", nullable: false),
                    CompletedTasks = table.Column<int>(type: "integer", nullable: false),
                    FailedTasks = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CreatedByUserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAnalysisSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowAnalysisSessions_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowAnalysisSessions_WorkflowTemplateSessions_WorkflowT~",
                        column: x => x.WorkflowTemplateSessionId,
                        principalTable: "WorkflowTemplateSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowAnalysisTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AnalysisSessionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ParentTaskId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    TaskType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FocusSymbolsJson = table.Column<string>(type: "text", nullable: true),
                    FocusFilesJson = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAnalysisTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowAnalysisTasks_WorkflowAnalysisSessions_AnalysisSess~",
                        column: x => x.AnalysisSessionId,
                        principalTable: "WorkflowAnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowAnalysisArtifacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AnalysisSessionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    TaskId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    ArtifactType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentFormat = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAnalysisArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowAnalysisArtifacts_WorkflowAnalysisSessions_Analysis~",
                        column: x => x.AnalysisSessionId,
                        principalTable: "WorkflowAnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowAnalysisArtifacts_WorkflowAnalysisTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "WorkflowAnalysisTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisArtifacts_AnalysisSessionId_ArtifactType",
                table: "WorkflowAnalysisArtifacts",
                columns: new[] { "AnalysisSessionId", "ArtifactType" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisArtifacts_TaskId",
                table: "WorkflowAnalysisArtifacts",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisSessions_RepositoryId_ProfileKey_ChapterKey",
                table: "WorkflowAnalysisSessions",
                columns: new[] { "RepositoryId", "ProfileKey", "ChapterKey" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisSessions_WorkflowTemplateSessionId_CreatedAt",
                table: "WorkflowAnalysisSessions",
                columns: new[] { "WorkflowTemplateSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisTasks_AnalysisSessionId_SequenceNumber",
                table: "WorkflowAnalysisTasks",
                columns: new[] { "AnalysisSessionId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAnalysisTasks_AnalysisSessionId_Status",
                table: "WorkflowAnalysisTasks",
                columns: new[] { "AnalysisSessionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowAnalysisArtifacts");

            migrationBuilder.DropTable(
                name: "WorkflowAnalysisTasks");

            migrationBuilder.DropTable(
                name: "WorkflowAnalysisSessions");
        }
    }
}
