using Microsoft.EntityFrameworkCore;


namespace SurveyBackend
{
    public class MainDbContext : DbContext
    {
        public MainDbContext(DbContextOptions<MainDbContext> options) : base(options)
        {
        }
        // 定义 DbSet 属性
        public DbSet<User> Users { get; set; }
        public DbSet<SurveyResponse> SurveyResponses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 全局设置默认 Collation
            modelBuilder.UseCollation("utf8mb4_0900_ai_ci");
            modelBuilder.HasCharSet("utf8mb4");

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(x => x.UserId);

                entity.Property(x => x.UserId)
                      .HasMaxLength(16);
                entity.Property(x => x.QQId)
                      .HasMaxLength(16)
                      .IsRequired();

                entity.HasIndex(x => x.QQId)
                      .IsUnique();
            });

            // TODO: Other entities' configurations



            base.OnModelCreating(modelBuilder);
        }
    }
}
