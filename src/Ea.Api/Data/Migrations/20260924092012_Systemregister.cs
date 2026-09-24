using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ea.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Systemregister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ea");

            migrationBuilder.CreateTable(
                name: "persons",
                schema: "ea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    entra_object_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "ea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "systems",
                schema: "ea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aliases = table.Column<List<string>>(type: "text[]", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    lifecycle_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    managing_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_confirmed_by_oid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_confirmed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_systems", x => x.id);
                    table.ForeignKey(
                        name: "fk_systems_systems_parent_system_id",
                        column: x => x.parent_system_id,
                        principalSchema: "ea",
                        principalTable: "systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_systems_teams_managing_team_id",
                        column: x => x.managing_team_id,
                        principalSchema: "ea",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_roles",
                schema: "ea",
                columns: table => new
                {
                    system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_roles", x => new { x.system_id, x.role, x.person_id });
                    table.ForeignKey(
                        name: "fk_system_roles_persons_person_id",
                        column: x => x.person_id,
                        principalSchema: "ea",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_system_roles_systems_system_id",
                        column: x => x.system_id,
                        principalSchema: "ea",
                        principalTable: "systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "ea",
                table: "teams",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { new Guid("0199a000-0000-7000-8000-000000000001"), "Kerneapplikationer" },
                    { new Guid("0199a000-0000-7000-8000-000000000002"), "Specialiserede Løsninger" },
                    { new Guid("0199a000-0000-7000-8000-000000000003"), "Data & Integrationer" },
                    { new Guid("0199a000-0000-7000-8000-000000000004"), "Digital Arbejdsplads" },
                    { new Guid("0199a000-0000-7000-8000-000000000005"), "Stab" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_persons_entra_object_id",
                schema: "ea",
                table: "persons",
                column: "entra_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_roles_person_id",
                schema: "ea",
                table: "system_roles",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_systems_managing_team_id",
                schema: "ea",
                table: "systems",
                column: "managing_team_id");

            migrationBuilder.CreateIndex(
                name: "ux_systems_parent_name",
                schema: "ea",
                table: "systems",
                columns: new[] { "parent_system_id", "name_normalized" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_teams_name",
                schema: "ea",
                table: "teams",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_roles",
                schema: "ea");

            migrationBuilder.DropTable(
                name: "persons",
                schema: "ea");

            migrationBuilder.DropTable(
                name: "systems",
                schema: "ea");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "ea");
        }
    }
}
