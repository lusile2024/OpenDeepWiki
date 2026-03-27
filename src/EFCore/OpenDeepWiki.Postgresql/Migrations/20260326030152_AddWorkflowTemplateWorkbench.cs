using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDeepWiki.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTemplateWorkbench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowTemplateSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BranchId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LanguageCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrentDraftKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CurrentDraftName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrentVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    AdoptedVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CreatedByUserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTemplateSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplateSessions_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTemplateDraftVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DraftJson = table.Column<string>(type: "text", nullable: false),
                    RiskNotesJson = table.Column<string>(type: "text", nullable: true),
                    EvidenceFilesJson = table.Column<string>(type: "text", nullable: true),
                    ValidationIssuesJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTemplateDraftVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplateDraftVersions_WorkflowTemplateSessions_Sess~",
                        column: x => x.SessionId,
                        principalTable: "WorkflowTemplateSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTemplateMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MessageTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTemplateMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplateMessages_WorkflowTemplateSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WorkflowTemplateSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplateDraftVersions_SessionId_VersionNumber",
                table: "WorkflowTemplateDraftVersions",
                columns: new[] { "SessionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplateMessages_SessionId_MessageTimestamp",
                table: "WorkflowTemplateMessages",
                columns: new[] { "SessionId", "MessageTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplateMessages_SessionId_SequenceNumber",
                table: "WorkflowTemplateMessages",
                columns: new[] { "SessionId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplateSessions_RepositoryId_LastActivityAt",
                table: "WorkflowTemplateSessions",
                columns: new[] { "RepositoryId", "LastActivityAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowTemplateDraftVersions");

            migrationBuilder.DropTable(
                name: "WorkflowTemplateMessages");

            migrationBuilder.DropTable(
                name: "WorkflowTemplateSessions");
        }
    }
}
