using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDeepWiki.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddDocTopicContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repositories_OwnerUserId_OrgName_RepoName",
                table: "Repositories");

            migrationBuilder.AddColumn<bool>(
                name: "IsDepartmentOwned",
                table: "Repositories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DocTopicContexts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BranchLanguageId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    CatalogPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TopicKind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocTopicContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocTopicContexts_BranchLanguages_BranchLanguageId",
                        column: x => x.BranchLanguageId,
                        principalTable: "BranchLanguages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitHubAppInstallations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    InstallationId = table.Column<long>(type: "bigint", nullable: false),
                    AccountLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DepartmentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CachedAccessToken = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DepartmentId1 = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubAppInstallations_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GitHubAppInstallations_Departments_DepartmentId1",
                        column: x => x.DepartmentId1,
                        principalTable: "Departments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OrgName_RepoName",
                table: "Repositories",
                columns: new[] { "OrgName", "RepoName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OwnerUserId",
                table: "Repositories",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocTopicContexts_BranchLanguageId_CatalogPath",
                table: "DocTopicContexts",
                columns: new[] { "BranchLanguageId", "CatalogPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallations_DepartmentId",
                table: "GitHubAppInstallations",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallations_DepartmentId1",
                table: "GitHubAppInstallations",
                column: "DepartmentId1");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallations_InstallationId",
                table: "GitHubAppInstallations",
                column: "InstallationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocTopicContexts");

            migrationBuilder.DropTable(
                name: "GitHubAppInstallations");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_OrgName_RepoName",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_OwnerUserId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "IsDepartmentOwned",
                table: "Repositories");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OwnerUserId_OrgName_RepoName",
                table: "Repositories",
                columns: new[] { "OwnerUserId", "OrgName", "RepoName" },
                unique: true);
        }
    }
}
