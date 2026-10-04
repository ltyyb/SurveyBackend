using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyBackend.Data.Migrations;

/// <inheritdoc />
public partial class InitialSqlite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "surveys",
            columns: table => new
            {
                SurveyId = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false, collation: "NOCASE"),
                Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                UniquePerUser = table.Column<bool>(type: "INTEGER", nullable: false),
                NeedReview = table.Column<bool>(type: "INTEGER", nullable: false),
                IsVerifySurvey = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_surveys", x => x.SurveyId);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                UserId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                QQId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                UserGroup = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.UserId);
            });

        migrationBuilder.CreateTable(
            name: "questionnaires",
            columns: table => new
            {
                QuestionnaireId = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false, collation: "NOCASE"),
                SurveyId = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false, collation: "NOCASE"),
                LLMPageNames = table.Column<string>(type: "TEXT", nullable: true),
                ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                SurveyJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_questionnaires", x => x.QuestionnaireId);
                table.ForeignKey(
                    name: "FK_questionnaires_surveys_SurveyId",
                    column: x => x.SurveyId,
                    principalTable: "surveys",
                    principalColumn: "SurveyId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "requests",
            columns: table => new
            {
                RequestId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                RequestType = table.Column<int>(type: "INTEGER", nullable: false),
                UserId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_requests", x => x.RequestId);
                table.ForeignKey(
                    name: "FK_requests_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submissions",
            columns: table => new
            {
                SubmissionId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                QuestionnaireId = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false, collation: "NOCASE"),
                UserId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                SurveyData = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submissions", x => x.SubmissionId);
                table.ForeignKey(
                    name: "FK_submissions_questionnaires_QuestionnaireId",
                    column: x => x.QuestionnaireId,
                    principalTable: "questionnaires",
                    principalColumn: "QuestionnaireId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_submissions_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "review_submissions",
            columns: table => new
            {
                ReviewSubmissionDataId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                SubmissionId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                AIInsights = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_review_submissions", x => x.ReviewSubmissionDataId);
                table.ForeignKey(
                    name: "FK_review_submissions_submissions_SubmissionId",
                    column: x => x.SubmissionId,
                    principalTable: "submissions",
                    principalColumn: "SubmissionId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "review_votes",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ReviewSubmissionDataId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                UserId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                VoteType = table.Column<int>(type: "INTEGER", nullable: false),
                VoteTime = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_review_votes", x => x.Id);
                table.ForeignKey(
                    name: "FK_review_votes_review_submissions_ReviewSubmissionDataId",
                    column: x => x.ReviewSubmissionDataId,
                    principalTable: "review_submissions",
                    principalColumn: "ReviewSubmissionDataId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_review_votes_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_questionnaires_SurveyId",
            table: "questionnaires",
            column: "SurveyId");

        migrationBuilder.CreateIndex(
            name: "IX_requests_UserId",
            table: "requests",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_review_submissions_SubmissionId",
            table: "review_submissions",
            column: "SubmissionId");

        migrationBuilder.CreateIndex(
            name: "IX_review_votes_ReviewSubmissionDataId",
            table: "review_votes",
            column: "ReviewSubmissionDataId");

        migrationBuilder.CreateIndex(
            name: "IX_review_votes_UserId",
            table: "review_votes",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_submissions_QuestionnaireId",
            table: "submissions",
            column: "QuestionnaireId");

        migrationBuilder.CreateIndex(
            name: "IX_submissions_UserId",
            table: "submissions",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_users_QQId",
            table: "users",
            column: "QQId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "requests");

        migrationBuilder.DropTable(
            name: "review_votes");

        migrationBuilder.DropTable(
            name: "review_submissions");

        migrationBuilder.DropTable(
            name: "submissions");

        migrationBuilder.DropTable(
            name: "questionnaires");

        migrationBuilder.DropTable(
            name: "users");

        migrationBuilder.DropTable(
            name: "surveys");
    }
}
