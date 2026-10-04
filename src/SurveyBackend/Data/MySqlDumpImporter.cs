using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SurveyBackend.Data;

internal static class MySqlDumpImporter
{
    internal sealed record Result(string SourceSha256, IReadOnlyDictionary<string, int> RowCounts);
    private sealed record Column(string Name, Type Type, bool Nullable, int? MaxLength);
    private sealed record Table(string Name, string TargetName, string Key, Column[] Columns);

    public static Result Import(string dumpPath, string targetPath)
    {
        dumpPath = Path.GetFullPath(dumpPath);
        targetPath = Path.GetFullPath(targetPath);
        if (File.Exists(targetPath) || File.Exists(targetPath + "-wal") || File.Exists(targetPath + "-shm"))
        {
            throw new IOException("Import requires a new target database.");
        }

        var bytes = File.ReadAllBytes(dumpPath);
        var sql = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var dump = MySqlDumpParser.Parse(sql);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var stagingPath = targetPath + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            Result result;
            using (var db = SqliteDatabase.CreateContext(stagingPath))
            {
                db.Database.Migrate();
                db.Database.OpenConnection();
                var connection = (SqliteConnection)db.Database.GetDbConnection();
                var tables = GetTables(db.Model);
                ValidateSchema(dump, tables);
                Execute(connection, "CREATE TABLE \"__MySqlMigrationsHistory\" (\"MigrationId\" TEXT PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);");
                // Dump 的表顺序不保证父表优先。所有约束在发布前统一核查。
                Execute(connection, "PRAGMA foreign_keys=OFF;");
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var row in dump.Rows)
                    {
                        InsertRow(connection, transaction, tables[row.Table], row);
                    }

                    var voteTable = dump.Tables.Single(table => table.Name == "review_votes");
                    if (voteTable.NextId is { } nextId)
                    {
                        if (nextId < 1)
                        {
                            throw new InvalidDataException("Invalid AUTO_INCREMENT value.");
                        }
                        using var sequence = connection.CreateCommand();
                        sequence.Transaction = transaction;
                        sequence.CommandText = "UPDATE sqlite_sequence SET seq = MAX(seq, $seq) WHERE name = 'review_votes';";
                        sequence.Parameters.AddWithValue("$seq", nextId - 1);
                        if (sequence.ExecuteNonQuery() == 0)
                        {
                            sequence.CommandText = "INSERT INTO sqlite_sequence(name, seq) VALUES ('review_votes', $seq);";
                            sequence.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }
                Execute(connection, "PRAGMA foreign_keys=ON;");
                VerifyIntegrity(connection);
                VerifyRows(connection, dump, tables);
                var rowCounts = dump.Tables.ToDictionary(table => table.Name,
                    table => dump.Rows.Count(row => row.Table == table.Name), StringComparer.Ordinal);
                using (var audit = connection.CreateCommand())
                {
                    audit.CommandText = "CREATE TABLE \"__DataImport\" (\"SourceSha256\" TEXT PRIMARY KEY, \"ImportedAtUtc\" TEXT NOT NULL, \"RowCounts\" TEXT NOT NULL);";
                    audit.ExecuteNonQuery();
                    audit.CommandText = "INSERT INTO \"__DataImport\" VALUES ($hash, $time, $counts);";
                    audit.Parameters.AddWithValue("$hash", sha256);
                    audit.Parameters.AddWithValue("$time", DateTime.UtcNow);
                    audit.Parameters.AddWithValue("$counts", JsonSerializer.Serialize(rowCounts));
                    audit.ExecuteNonQuery();
                }
                // 发布单个完整文件，避免遗留 WAL 中尚未合并的数据。
                Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
                Execute(connection, "PRAGMA journal_mode=DELETE;");
                result = new Result(sha256, rowCounts);
            }

            // 同目录重命名；目标即使在导入期间出现也不会被覆盖。
            File.Move(stagingPath, targetPath, overwrite: false);
            return result;
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                File.Delete(stagingPath + suffix);
            }
        }
    }

    private static Dictionary<string, Table> GetTables(IModel model)
    {
        var result = new Dictionary<string, Table>(StringComparer.Ordinal);
        foreach (var entity in model.GetEntityTypes())
        {
            var name = entity.GetTableName()!;
            var store = StoreObjectIdentifier.Table(name, null);
            var columns = entity.GetProperties().Select(property => new Column(
                property.GetColumnName(store)!,
                property.GetValueConverter()?.ProviderClrType ?? property.ClrType,
                property.IsNullable, property.GetMaxLength())).ToArray();
            result.Add(name, new Table(name, name, entity.FindPrimaryKey()!.Properties.Single().GetColumnName(store)!, columns));
        }
        result.Add("__efmigrationshistory", new Table("__efmigrationshistory", "__MySqlMigrationsHistory", "MigrationId",
            [new("MigrationId", typeof(string), false, 150), new("ProductVersion", typeof(string), false, 32)]));
        return result;
    }

    private static void ValidateSchema(MySqlDumpParser.Dump dump, Dictionary<string, Table> tables)
    {
        if (dump.Tables.Select(table => table.Name).Distinct(StringComparer.Ordinal).Count() != dump.Tables.Count
            || !dump.Tables.Select(table => table.Name).Order().SequenceEqual(tables.Keys.Order()))
        {
            throw new InvalidDataException("Dump must contain exactly the expected application tables and migration history.");
        }
        foreach (var source in dump.Tables)
        {
            ValidateColumns(source.Columns, tables[source.Name]);
        }
        foreach (var row in dump.Rows)
        {
            if (!tables.TryGetValue(row.Table, out var table))
            {
                throw new InvalidDataException("Unknown INSERT table.");
            }
            ValidateColumns(row.Columns, table);
        }
    }

    private static void ValidateColumns(string[] names, Table table)
    {
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length
            || !names.Order().SequenceEqual(table.Columns.Select(column => column.Name).Order()))
        {
            throw new InvalidDataException($"Dump columns differ from the EF model: {table.Name}.");
        }
    }

    private static object ConvertValue(Column column, string? value)
    {
        if (value is null)
        {
            return column.Nullable ? DBNull.Value : throw new InvalidDataException($"Unexpected NULL: {column.Name}.");
        }
        if (column.Type == typeof(string))
        {
            if (column.MaxLength is { } length && value.EnumerateRunes().Count() > length)
            {
                throw new InvalidDataException($"Value exceeds model length: {column.Name}.");
            }
            if (column.Name.EndsWith("Id", StringComparison.Ordinal) && value.Any(character => character > 127))
            {
                throw new InvalidDataException("Non-ASCII ID requires explicit collation review.");
            }
            if (column.Name is "SurveyJson" or "SurveyData" or "LLMPageNames")
            {
                using var document = JsonDocument.Parse(value);
                if (column.Name == "LLMPageNames")
                {
                    _ = JsonSerializer.Deserialize<string[]>(value);
                }
            }
            return value;
        }
        if (column.Type == typeof(DateTime))
        {
            return DateTime.ParseExact(value, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"],
                CultureInfo.InvariantCulture, DateTimeStyles.None);
        }
        if (column.Type == typeof(bool))
        {
            return value switch { "0" => false, "1" => true, _ => throw new InvalidDataException("Invalid Boolean value.") };
        }
        if (column.Type.IsEnum)
        {
            var number = int.Parse(value, CultureInfo.InvariantCulture);
            return Enum.IsDefined(column.Type, number) ? number : throw new InvalidDataException("Unknown enum value.");
        }
        if (column.Type == typeof(int))
        {
            return int.Parse(value, CultureInfo.InvariantCulture);
        }
        throw new InvalidDataException($"Unsupported model type: {column.Type.Name}.");
    }

    private static object[] GetValues(Table table, MySqlDumpParser.Row row)
    {
        return row.Columns.Select((name, index) => ConvertValue(table.Columns.Single(column => column.Name == name), row.Values[index])).ToArray();
    }

    private static void InsertRow(SqliteConnection connection, SqliteTransaction transaction, Table table, MySqlDumpParser.Row row)
    {
        var values = GetValues(table, row);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO \"{table.TargetName}\" ({string.Join(",", row.Columns.Select(name => $"\"{name}\""))}) VALUES ({string.Join(",", values.Select((_, index) => $"$p{index}"))});";
        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue($"$p{index}", values[index]);
        }
        command.ExecuteNonQuery();
    }

    private static void VerifyRows(SqliteConnection connection, MySqlDumpParser.Dump dump, Dictionary<string, Table> tables)
    {
        foreach (var table in tables.Values)
        {
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{table.TargetName}\";";
            if (Convert.ToInt64(count.ExecuteScalar()) != dump.Rows.Count(row => row.Table == table.Name))
            {
                throw new InvalidDataException($"Row count differs: {table.Name}.");
            }
        }
        foreach (var row in dump.Rows)
        {
            var table = tables[row.Table];
            var values = GetValues(table, row);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(",", row.Columns.Select(name => $"\"{name}\""))} FROM \"{table.TargetName}\" WHERE \"{table.Key}\" = $key;";
            command.Parameters.AddWithValue("$key", values[Array.IndexOf(row.Columns, table.Key)]);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidDataException($"Missing row: {table.Name}.");
            }
            for (var index = 0; index < values.Length; index++)
            {
                var expected = values[index];
                var actual = reader.GetValue(index);
                var matches = expected switch
                {
                    DBNull => actual is DBNull,
                    DateTime date => reader.GetDateTime(index) == date,
                    bool boolean => reader.GetInt64(index) == (boolean ? 1 : 0),
                    int number => reader.GetInt64(index) == number,
                    string text => actual is string stored && string.Equals(stored, text, StringComparison.Ordinal),
                    _ => false
                };
                if (!matches)
                {
                    throw new InvalidDataException($"Field verification failed: {table.Name}.{row.Columns[index]}.");
                }
            }
        }
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                throw new InvalidDataException("Orphaned foreign key in dump.");
            }
        }
        command.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
        {
            throw new InvalidDataException("SQLite integrity check failed.");
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
