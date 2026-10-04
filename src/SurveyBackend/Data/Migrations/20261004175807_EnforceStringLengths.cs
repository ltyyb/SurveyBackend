using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyBackend.Data.Migrations;

/// <inheritdoc />
public partial class EnforceStringLengths : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_users_QQId_Length",
            table: "users",
            sql: "length(\"QQId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_users_UserId_Length",
            table: "users",
            sql: "length(\"UserId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_surveys_Description_Length",
            table: "surveys",
            sql: "length(\"Description\") <= 1000");

        migrationBuilder.AddCheckConstraint(
            name: "CK_surveys_SurveyId_Length",
            table: "surveys",
            sql: "length(\"SurveyId\") <= 8");

        migrationBuilder.AddCheckConstraint(
            name: "CK_surveys_Title_Length",
            table: "surveys",
            sql: "length(\"Title\") <= 200");

        migrationBuilder.AddCheckConstraint(
            name: "CK_submissions_QuestionnaireId_Length",
            table: "submissions",
            sql: "length(\"QuestionnaireId\") <= 8");

        migrationBuilder.AddCheckConstraint(
            name: "CK_submissions_SubmissionId_Length",
            table: "submissions",
            sql: "length(\"SubmissionId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_submissions_UserId_Length",
            table: "submissions",
            sql: "length(\"UserId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_review_votes_ReviewSubmissionDataId_Length",
            table: "review_votes",
            sql: "length(\"ReviewSubmissionDataId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_review_votes_UserId_Length",
            table: "review_votes",
            sql: "length(\"UserId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_review_submissions_ReviewSubmissionDataId_Length",
            table: "review_submissions",
            sql: "length(\"ReviewSubmissionDataId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_review_submissions_SubmissionId_Length",
            table: "review_submissions",
            sql: "length(\"SubmissionId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_requests_RequestId_Length",
            table: "requests",
            sql: "length(\"RequestId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_requests_UserId_Length",
            table: "requests",
            sql: "length(\"UserId\") <= 16");

        migrationBuilder.AddCheckConstraint(
            name: "CK_questionnaires_QuestionnaireId_Length",
            table: "questionnaires",
            sql: "length(\"QuestionnaireId\") <= 8");

        migrationBuilder.AddCheckConstraint(
            name: "CK_questionnaires_SurveyId_Length",
            table: "questionnaires",
            sql: "length(\"SurveyId\") <= 8");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_users_QQId_Length",
            table: "users");

        migrationBuilder.DropCheckConstraint(
            name: "CK_users_UserId_Length",
            table: "users");

        migrationBuilder.DropCheckConstraint(
            name: "CK_surveys_Description_Length",
            table: "surveys");

        migrationBuilder.DropCheckConstraint(
            name: "CK_surveys_SurveyId_Length",
            table: "surveys");

        migrationBuilder.DropCheckConstraint(
            name: "CK_surveys_Title_Length",
            table: "surveys");

        migrationBuilder.DropCheckConstraint(
            name: "CK_submissions_QuestionnaireId_Length",
            table: "submissions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_submissions_SubmissionId_Length",
            table: "submissions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_submissions_UserId_Length",
            table: "submissions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_review_votes_ReviewSubmissionDataId_Length",
            table: "review_votes");

        migrationBuilder.DropCheckConstraint(
            name: "CK_review_votes_UserId_Length",
            table: "review_votes");

        migrationBuilder.DropCheckConstraint(
            name: "CK_review_submissions_ReviewSubmissionDataId_Length",
            table: "review_submissions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_review_submissions_SubmissionId_Length",
            table: "review_submissions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_requests_RequestId_Length",
            table: "requests");

        migrationBuilder.DropCheckConstraint(
            name: "CK_requests_UserId_Length",
            table: "requests");

        migrationBuilder.DropCheckConstraint(
            name: "CK_questionnaires_QuestionnaireId_Length",
            table: "questionnaires");

        migrationBuilder.DropCheckConstraint(
            name: "CK_questionnaires_SurveyId_Length",
            table: "questionnaires");
    }
}
