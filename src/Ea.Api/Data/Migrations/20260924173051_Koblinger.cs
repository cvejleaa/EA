using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ea.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Koblinger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retired_at",
                schema: "ea",
                table: "capabilities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retired_path",
                schema: "ea",
                table: "capabilities",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "system_capabilities",
                schema: "ea",
                columns: table => new
                {
                    system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_capabilities", x => new { x.system_id, x.capability_id });
                    table.ForeignKey(
                        name: "fk_system_capabilities_capabilities_capability_id",
                        column: x => x.capability_id,
                        principalSchema: "ea",
                        principalTable: "capabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_system_capabilities_systems_system_id",
                        column: x => x.system_id,
                        principalSchema: "ea",
                        principalTable: "systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_capabilities_capability_id",
                schema: "ea",
                table: "system_capabilities",
                column: "capability_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_capabilities",
                schema: "ea");

            migrationBuilder.DropColumn(
                name: "retired_at",
                schema: "ea",
                table: "capabilities");

            migrationBuilder.DropColumn(
                name: "retired_path",
                schema: "ea",
                table: "capabilities");
        }
    }
}
