using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealtimeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnreadEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "notified_seq",
                table: "members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notified_seq",
                table: "members");
        }
    }
}
