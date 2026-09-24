using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ea.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Integrationer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_objects",
                schema: "ea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integrations",
                schema: "ea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    via_platform_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_normalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrations", x => x.id);
                    table.CheckConstraint("ck_integrations_not_self", "source_system_id <> target_system_id");
                    table.ForeignKey(
                        name: "fk_integrations_systems_source_system_id",
                        column: x => x.source_system_id,
                        principalSchema: "ea",
                        principalTable: "systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integrations_systems_target_system_id",
                        column: x => x.target_system_id,
                        principalSchema: "ea",
                        principalTable: "systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integrations_systems_via_platform_id",
                        column: x => x.via_platform_id,
                        principalSchema: "ea",
                        principalTable: "systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_data_objects",
                schema: "ea",
                columns: table => new
                {
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_object_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_data_objects", x => new { x.integration_id, x.data_object_id });
                    table.ForeignKey(
                        name: "fk_integration_data_objects_data_objects_data_object_id",
                        column: x => x.data_object_id,
                        principalSchema: "ea",
                        principalTable: "data_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integration_data_objects_integrations_integration_id",
                        column: x => x.integration_id,
                        principalSchema: "ea",
                        principalTable: "integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_data_objects_name",
                schema: "ea",
                table: "data_objects",
                column: "name_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_data_objects_data_object_id",
                schema: "ea",
                table: "integration_data_objects",
                column: "data_object_id");

            migrationBuilder.CreateIndex(
                name: "ix_integrations_target_system_id",
                schema: "ea",
                table: "integrations",
                column: "target_system_id");

            migrationBuilder.CreateIndex(
                name: "ix_integrations_via_platform_id",
                schema: "ea",
                table: "integrations",
                column: "via_platform_id");

            migrationBuilder.CreateIndex(
                name: "ux_integrations_natural_key",
                schema: "ea",
                table: "integrations",
                columns: new[] { "source_system_id", "target_system_id", "type", "via_platform_id", "name_normalized" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_data_objects",
                schema: "ea");

            migrationBuilder.DropTable(
                name: "data_objects",
                schema: "ea");

            migrationBuilder.DropTable(
                name: "integrations",
                schema: "ea");
        }
    }
}
