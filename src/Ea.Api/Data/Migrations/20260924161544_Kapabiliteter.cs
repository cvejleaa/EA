using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ea.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Kapabiliteter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capabilities",
                schema: "ea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code_normalized = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capabilities", x => x.id);
                    table.ForeignKey(
                        name: "fk_capabilities_capabilities_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "ea",
                        principalTable: "capabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_capabilities_parent_id",
                schema: "ea",
                table: "capabilities",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ux_capabilities_code",
                schema: "ea",
                table: "capabilities",
                column: "code_normalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capabilities",
                schema: "ea");
        }
    }
}
