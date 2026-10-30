using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using static SQLitePCL.raw;

namespace SurveyBackend.Services.Backups;

public sealed class SqliteBackupSnapshot
{
    public async Task<BackupRecord> CreateAsync(string sourcePath, BackupRecord record, BackupStore store,
        BackupOptions options, CancellationToken cancellationToken)
    {
        var temporary = store.GetPath(record.Id, ".db.part");
        if (File.Exists(temporary) || File.Exists(store.GetPath(record.Id, ".db")))
            throw new IOException("BackupAlreadyExists");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1
        }.ToString());
        await source.OpenAsync(cancellationToken);
        var pageSize = Convert.ToInt64(ExecuteScalar(source, "PRAGMA page_size;"));
        var pageCount = Convert.ToInt64(ExecuteScalar(source, "PRAGMA page_count;"));
        store.EnsureCapacity(options, checked(pageSize * pageCount + 65536));

        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = temporary, Pooling = false, DefaultTimeout = 1
        }.ToString()))
        {
            await destination.OpenAsync(cancellationToken);
            ExecuteScalar(destination, $"PRAGMA page_size={pageSize};");
            using (var backup = sqlite3_backup_init(destination.Handle, "main", source.Handle, "main"))
            {
                if (backup.IsInvalid)
                    throw new SqliteException("BackupInitFailed", sqlite3_errcode(destination.Handle));
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remaining = sqlite3_backup_pagecount(backup) == 0 ? pageCount : sqlite3_backup_remaining(backup);
                    store.EnsureCapacity(options, checked(remaining * pageSize + 65536));
                    var code = sqlite3_backup_step(backup, options.PagesPerStep);
                    if (code == SQLITE_DONE)
                        break;
                    if (code is not (SQLITE_OK or SQLITE_BUSY or SQLITE_LOCKED))
                        throw new SqliteException("BackupStepFailed", code);
                    var delay = code is SQLITE_BUSY or SQLITE_LOCKED ? TimeSpan.FromMilliseconds(100) : options.StepDelay;
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken);
                    else
                        await Task.Yield();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            // WAL 源库的页头也会被复制，发布前将副本转换为单文件。
            ExecuteScalar(destination, "PRAGMA journal_mode=DELETE;");
            using var registration = cancellationToken.Register(() => sqlite3_interrupt(destination.Handle));
            Check(destination, record.FullCheck ? "PRAGMA integrity_check;" : "PRAGMA quick_check;", "ok");
            Check(destination, "PRAGMA foreign_key_check;", null);
            var migrations = new List<string>();
            using var command = destination.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                migrations.Add(reader.GetString(0));
            record = record with { Migrations = migrations.ToArray() };
        }
        cancellationToken.ThrowIfCancellationRequested();
        using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            stream.Flush(flushToDisk: true);
        await using (var stream = File.OpenRead(temporary))
            record = record with { Size = stream.Length, Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)) };
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(temporary, store.GetPath(record.Id, ".db"));
        return record;
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Check(SqliteConnection connection, string sql, string? expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        if (expected is null)
        {
            if (reader.Read())
                throw new InvalidDataException("BackupForeignKeyCheckFailed");
        }
        else if (!reader.Read() || reader.GetString(0) != expected || reader.Read())
            throw new InvalidDataException("BackupIntegrityCheckFailed");
    }
}
