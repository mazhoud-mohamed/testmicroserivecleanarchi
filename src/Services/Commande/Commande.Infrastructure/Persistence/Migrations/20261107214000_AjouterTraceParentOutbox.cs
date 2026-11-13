using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commande.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjouterTraceParentOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "MessagesOutbox",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "MessagesOutbox");
        }
    }
}
