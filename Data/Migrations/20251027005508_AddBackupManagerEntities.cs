using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backlite.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupManagerEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    Started = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Ended = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AuthKind = table.Column<string>(type: "TEXT", nullable: false),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    PrivateKeyPem = table.Column<string>(type: "TEXT", nullable: true),
                    UseSudoForDiscovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedRoots = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastConnectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DbPaths = table.Column<string>(type: "TEXT", nullable: false),
                    DestinationDir = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Compress = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetentionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupPlans_ServerConnections_ServerId",
                        column: x => x.ServerId,
                        principalTable: "ServerConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Started = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Ended = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ArtifactPath = table.Column<string>(type: "TEXT", nullable: true),
                    ArtifactSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LogLines = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRuns_BackupPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "BackupPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlans_ServerId",
                table: "BackupPlans",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_PlanId",
                table: "BackupRuns",
                column: "PlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRuns");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "BackupPlans");

            migrationBuilder.DropTable(
                name: "ServerConnections");
        }
    }
}
