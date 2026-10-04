using Microsoft.Data.Sqlite;

namespace SurveyBackend.Data;

public static class SqliteDatabase
{
    public static string ConnectionString(string path, bool pooling = true)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            DefaultTimeout = 60,
            Pooling = pooling
        }.ToString();
    }

    public static MainDbContext CreateContext(string path)
    {
        var options = new DbContextOptionsBuilder<MainDbContext>()
            .UseSqlite(ConnectionString(path, pooling: false), sqlite => sqlite.CommandTimeout(60))
            .Options;
        return new MainDbContext(options);
    }

    public static void Initialize(MainDbContext db)
    {
        // 新库可直接创建；已有数据库的结构变更必须由管理员显式执行。
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        var tableCount = Convert.ToInt64(command.ExecuteScalar());
        if (tableCount == 0)
        {
            db.Database.Migrate();
        }
        else if (db.Database.GetPendingMigrations().Any())
        {
            throw new InvalidOperationException("数据库存在未应用的 SQLite 迁移。请先备份，再运行 --migrate-database <数据库路径>。");
        }

        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteScalar();
        db.Database.CloseConnection();
    }
}
