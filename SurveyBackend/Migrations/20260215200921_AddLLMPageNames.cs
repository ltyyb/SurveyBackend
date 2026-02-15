using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddLLMPageNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LLMPageNames",
                table: "questionnaires",
                type: "json",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LLMPageNames",
                table: "questionnaires");
        }
    }
}
