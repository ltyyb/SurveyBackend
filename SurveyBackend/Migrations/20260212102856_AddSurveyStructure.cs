using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FriendlyName",
                table: "questionnaires");

            migrationBuilder.DropColumn(
                name: "IsVerifyQuestionnaire",
                table: "questionnaires");

            migrationBuilder.DropColumn(
                name: "NeedReview",
                table: "questionnaires");

            migrationBuilder.DropColumn(
                name: "UniquePerUser",
                table: "questionnaires");

            migrationBuilder.AddColumn<string>(
                name: "SurveyId",
                table: "questionnaires",
                type: "varchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "surveys",
                columns: table => new
                {
                    SurveyId = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    UniquePerUser = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NeedReview = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsVerifySurvey = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_surveys", x => x.SurveyId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_questionnaires_SurveyId",
                table: "questionnaires",
                column: "SurveyId");

            migrationBuilder.AddForeignKey(
                name: "FK_questionnaires_surveys_SurveyId",
                table: "questionnaires",
                column: "SurveyId",
                principalTable: "surveys",
                principalColumn: "SurveyId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_questionnaires_surveys_SurveyId",
                table: "questionnaires");

            migrationBuilder.DropTable(
                name: "surveys");

            migrationBuilder.DropIndex(
                name: "IX_questionnaires_SurveyId",
                table: "questionnaires");

            migrationBuilder.DropColumn(
                name: "SurveyId",
                table: "questionnaires");

            migrationBuilder.AddColumn<string>(
                name: "FriendlyName",
                table: "questionnaires",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsVerifyQuestionnaire",
                table: "questionnaires",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NeedReview",
                table: "questionnaires",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UniquePerUser",
                table: "questionnaires",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
