using Microsoft.EntityFrameworkCore.Design;

namespace SurveyBackend.Data;

public sealed class MainDbContextFactory : IDesignTimeDbContextFactory<MainDbContext>
{
    public MainDbContext CreateDbContext(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("Database__Path") ?? "data.db";
        return SqliteDatabase.CreateContext(new DatabaseOptions { Path = path }.GetFullPath(AppContext.BaseDirectory));
    }
}
