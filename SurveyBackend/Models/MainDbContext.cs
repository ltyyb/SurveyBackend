using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;

namespace SurveyBackend.Models
{
    public class MainDbContext : DbContext
    {
        public MainDbContext(DbContextOptions<MainDbContext> options) : base(options)
        {
        }
        // 定义 DbSet 属性
        public required DbSet<User> Users { get; set; }
        public required DbSet<Survey> Surveys { get; set; }
        public required DbSet<Questionnaire> Questionnaires { get; set; }
        public required DbSet<Submission> Submissions { get; set; }
        public required DbSet<ReviewSubmissionData> ReviewSubmissions { get; set; }
        public required DbSet<ReviewVote> ReviewVotes { get; set; }
        public required DbSet<Request> Requests { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置 用户表 实体
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(x => x.UserId);

                entity.Property(x => x.UserId)
                      .HasMaxLength(16);
                entity.Property(x => x.QQId)
                      .HasMaxLength(16)
                      .IsRequired();
                entity.Property(x => x.UserGroup)
                      .IsRequired();
                entity.HasIndex(x => x.QQId)
                      .IsUnique();
            });
            modelBuilder.Entity<Survey>(entity =>
            {
                entity.ToTable("surveys");
                entity.HasKey(x => x.SurveyId);
                entity.Property(x => x.SurveyId)
                      .HasMaxLength(8);
                entity.Property(x => x.Title)
                      .HasMaxLength(200)
                      .IsRequired();
                entity.Property(x => x.Description)
                      .HasMaxLength(1000);
                entity.Property(x => x.UniquePerUser)
                      .IsRequired();
                entity.Property(x => x.NeedReview)
                      .IsRequired();
                entity.Property(x => x.IsVerifySurvey)
                      .IsRequired();
                entity.Property(x => x.CreatedAt)
                      .IsRequired();
            });

            // 配置 问卷表 实体
            modelBuilder.Entity<Questionnaire>(entity =>
            {
                entity.ToTable("questionnaires");
                entity.HasKey(x => x.QuestionnaireId);
                entity.Property(x => x.QuestionnaireId)
                      .HasMaxLength(8);
                entity.Property(x => x.SurveyId)
                      .HasMaxLength(8)
                      .IsRequired();
                entity.HasOne(x => x.Survey)
                      .WithMany()
                      .HasForeignKey(x => x.SurveyId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.Property(x => x.ReleaseDate)
                      .IsRequired();
                entity.Property(x => x.SurveyJson)
                      .IsRequired();
            });

            // 配置 提交表 实体
            modelBuilder.Entity<Submission>(entity =>
            {
                entity.ToTable("submissions");
                entity.HasKey(x => x.SubmissionId);
                entity.Property(x => x.SubmissionId)
                      .HasMaxLength(16);
                entity.Ignore(x => x.ShortSubmissionId);
                entity.Property(x => x.QuestionnaireId)
                      .HasMaxLength(8)
                      .IsRequired();
                entity.Property(x => x.UserId)
                      .HasMaxLength(16)
                      .IsRequired();
                entity.HasOne(x => x.Questionnaire)
                      .WithMany()
                      .HasForeignKey(x => x.QuestionnaireId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.User)
                      .WithMany()
                      .HasForeignKey(x => x.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.Property(x => x.CreatedAt)
                      .IsRequired();
                entity.Property(x => x.IsDisabled)
                      .IsRequired();
                entity.Property(x => x.SurveyData)
                      .IsRequired();
                entity.HasIndex(x => x.UserId);
                entity.HasIndex(x => x.QuestionnaireId);
            });

            // 配置 需审核提交表 实体
            modelBuilder.Entity<ReviewSubmissionData>(entity =>
            {
                entity.ToTable("review_submissions");
                entity.HasKey(x => x.ReviewSubmissionDataId);
                entity.Property(x => x.ReviewSubmissionDataId)
                      .HasMaxLength(16);
                entity.Property(x => x.SubmissionId)
                      .HasMaxLength(16)
                      .IsRequired();
                entity.HasOne(x => x.Submission)
                      .WithMany()
                      .HasForeignKey(x => x.SubmissionId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.Property(x => x.Status)
                      .IsRequired();
                entity.Property(x => x.AIInsights);
                entity.HasIndex(x => x.SubmissionId);
            });

            // 配置 审核投票表 实体
            modelBuilder.Entity<ReviewVote>(entity =>
            {
                entity.ToTable("review_votes");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ReviewSubmissionDataId)
                      .HasMaxLength(16)
                      .IsRequired();
                entity.Property(x => x.UserId)
                      .HasMaxLength(16)
                      .IsRequired();
                entity.HasOne(x => x.ReviewSubmissionData)
                      .WithMany()
                      .HasForeignKey(x => x.ReviewSubmissionDataId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.User)
                      .WithMany()
                      .HasForeignKey(x => x.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.Property(x => x.VoteType)
                      .IsRequired();
                entity.Property(x => x.VoteTime)
                      .IsRequired();
                entity.HasIndex(x => x.ReviewSubmissionDataId);
                entity.HasIndex(x => x.UserId);
            });

            modelBuilder.Entity<Request>(entity =>
            {
                entity.ToTable("requests");
                entity.HasKey(x => x.RequestId);
                entity.Property(x => x.RequestId)
                      .HasMaxLength(16);
                entity.Property(x => x.RequestType)
                      .IsRequired();
                entity.Property(x => x.UserId)
                      .HasMaxLength(16)
                      .IsRequired();
                entity.HasOne(x => x.User)
                      .WithMany()
                      .HasForeignKey(x => x.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.Property(x => x.IsDisabled)
                      .IsRequired();
                entity.Property(x => x.CreatedAt)
                      .IsRequired();
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
