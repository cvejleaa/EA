using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ea.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonOid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "entra_object_id",
                schema: "ea",
                table: "persons",
                newName: "oid");

            migrationBuilder.RenameIndex(
                name: "ix_persons_entra_object_id",
                schema: "ea",
                table: "persons",
                newName: "ix_persons_oid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "oid",
                schema: "ea",
                table: "persons",
                newName: "entra_object_id");

            migrationBuilder.RenameIndex(
                name: "ix_persons_oid",
                schema: "ea",
                table: "persons",
                newName: "ix_persons_entra_object_id");
        }
    }
}
