namespace SurveyBackend.Data;

internal static class DatabaseCommands
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] is not ("--migrate-database" or "--import-mysql-dump"))
        {
            return false;
        }

        try
        {
            if (args is ["--migrate-database", var path])
            {
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var db = SqliteDatabase.CreateContext(path);
                db.Database.Migrate();
                SqliteDatabase.Initialize(db);
                Console.WriteLine("SQLite migrations applied.");
            }
            else if (args is ["--import-mysql-dump", var dump, var target])
            {
                var result = MySqlDumpImporter.Import(dump, target);
                Console.WriteLine($"Dump SHA-256: {result.SourceSha256}");
                foreach (var (table, count) in result.RowCounts)
                {
                    Console.WriteLine($"{table}: {count} rows verified");
                }
                Console.WriteLine("All values, foreign keys and integrity verified. Database published.");
            }
            else
            {
                throw new ArgumentException("Usage: --migrate-database <db> | --import-mysql-dump <dump.sql> <new.db>");
            }
        }
        catch (Exception exception)
        {
            // 不输出 SQL 或行内容，避免将问卷和用户数据写入日志。
            var reason = exception is InvalidDataException or ArgumentException ? exception.Message : exception.GetType().Name;
            Console.Error.WriteLine($"Database operation failed: {reason}. No import target was replaced.");
            exitCode = 1;
        }

        return true;
    }
}
