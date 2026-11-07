using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commande.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjouterInboxEtMotifAnnulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotifAnnulation",
                table: "Commandes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MessagesTraites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraiteLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagesTraites", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessagesTraites");

            migrationBuilder.DropColumn(
                name: "MotifAnnulation",
                table: "Commandes");
        }
    }
}
