using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyBackend.Migrations
{
    /// <inheritdoc />
    public partial class SetCascadeDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_questionnaires_surveys_SurveyId",
                table: "questionnaires");

            migrationBuilder.DropForeignKey(
                name: "FK_requests_users_UserId",
                table: "requests");

            migrationBuilder.DropForeignKey(
                name: "FK_review_submissions_submissions_SubmissionId",
                table: "review_submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_review_votes_review_submissions_ReviewSubmissionDataId",
                table: "review_votes");

            migrationBuilder.DropForeignKey(
                name: "FK_review_votes_users_UserId",
                table: "review_votes");

            migrationBuilder.DropForeignKey(
                name: "FK_submissions_questionnaires_QuestionnaireId",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_submissions_users_UserId",
                table: "submissions");

            migrationBuilder.AddForeignKey(
                name: "FK_questionnaires_surveys_SurveyId",
                table: "questionnaires",
                column: "SurveyId",
                principalTable: "surveys",
                principalColumn: "SurveyId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_requests_users_UserId",
                table: "requests",
                column: "UserId",
                principalTable: "users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_review_submissions_submissions_SubmissionId",
                table: "review_submissions",
                column: "SubmissionId",
                principalTable: "submissions",
                principalColumn: "SubmissionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_review_votes_review_submissions_ReviewSubmissionDataId",
                table: "review_votes",
                column: "ReviewSubmissionDataId",
                principalTable: "review_submissions",
                principalColumn: "ReviewSubmissionDataId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_review_votes_users_UserId",
                table: "review_votes",
                column: "UserId",
                principalTable: "users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_submissions_questionnaires_QuestionnaireId",
                table: "submissions",
                column: "QuestionnaireId",
                principalTable: "questionnaires",
                principalColumn: "QuestionnaireId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_submissions_users_UserId",
                table: "submissions",
                column: "UserId",
                principalTable: "users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_questionnaires_surveys_SurveyId",
                table: "questionnaires");

            migrationBuilder.DropForeignKey(
                name: "FK_requests_users_UserId",
                table: "requests");

            migrationBuilder.DropForeignKey(
                name: "FK_review_submissions_submissions_SubmissionId",
                table: "review_submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_review_votes_review_submissions_ReviewSubmissionDataId",
                table: "review_votes");

            migrationBuilder.DropForeignKey(
                name: "FK_review_votes_users_UserId",
                table: "review_votes");

            migrationBuilder.DropForeignKey(
                name: "FK_submissions_questionnaires_QuestionnaireId",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_submissions_users_UserId",
                table: "submissions");

            migrationBuilder.AddForeignKey(
                name: "FK_questionnaires_surveys_SurveyId",
                table: "questionnaires",
                column: "SurveyId",
                principalTable: "surveys",
                principalColumn: "SurveyId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_requests_users_UserId",
                table: "requests",
                column: "UserId",
                principalTable: "users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_review_submissions_submissions_SubmissionId",
                table: "review_submissions",
                column: "SubmissionId",
                principalTable: "submissions",
                principalColumn: "SubmissionId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_review_votes_review_submissions_ReviewSubmissionDataId",
                table: "review_votes",
                column: "ReviewSubmissionDataId",
                principalTable: "review_submissions",
                principalColumn: "ReviewSubmissionDataId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_review_votes_users_UserId",
                table: "review_votes",
                column: "UserId",
                principalTable: "users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_submissions_questionnaires_QuestionnaireId",
                table: "submissions",
                column: "QuestionnaireId",
                principalTable: "questionnaires",
                principalColumn: "QuestionnaireId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_submissions_users_UserId",
                table: "submissions",
                column: "UserId",
                principalTable: "users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
