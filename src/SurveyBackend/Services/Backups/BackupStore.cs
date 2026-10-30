using System.Text.Json;
using System.Text.RegularExpressions;

namespace SurveyBackend.Services.Backups;

public sealed class BackupStore(string directory, ILogger<BackupStore> logger)
{
    private static readonly Regex IdPattern = new(@"^\d{8}T\d{6}Z-[a-f0-9]{32}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);

    public string GetPath(string id, string suffix)
    {
        if (!IdPattern.IsMatch(id) || suffix is not (".db" or ".db.part" or ".json" or ".json.tmp"))
            throw new ArgumentException("Invalid backup file name.");
        return Path.Combine(DirectoryPath, id + suffix);
    }

    public FileStream AcquireLease()
    {
        Directory.CreateDirectory(DirectoryPath);
        return new FileStream(Path.Combine(DirectoryPath, ".backup.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    public void Save(BackupRecord record)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = GetPath(record.Id, ".json.tmp");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, record, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, GetPath(record.Id, ".json"), overwrite: true);
    }

    public IReadOnlyList<BackupRecord> ReadAll()
    {
        if (!Directory.Exists(DirectoryPath))
            return [];
        var records = new List<BackupRecord>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!IdPattern.IsMatch(id))
                continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var record = JsonSerializer.Deserialize<BackupRecord>(stream);
                if (record is null || record.Id != id)
                    throw new InvalidDataException();
                records.Add(record);
            }
            catch (FileNotFoundException) { }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                logger.LogError("备份记录 {BackupId} 无法读取：{ErrorType}", id, exception.GetType().Name);
            }
        }
        return records.OrderByDescending(record => record.StartedAt).ThenByDescending(record => record.Id).ToArray();
    }

    // 仅在持有目录锁时调用，避免将其他进程的任务判为中断。
    public void Recover(DateTimeOffset now)
    {
        foreach (var record in ReadAll())
        {
            if (record.State == "Running")
            {
                Save(record with { State = "Interrupted", CompletedAt = now, Error = "ProcessInterrupted" });
                DeleteTemporary(record.Id);
                // 已发布但未写入成功记录的文件仍保留供人工检查。
            }
            else if (record.UploadState == "Uploading")
                Save(record with { UploadState = "Retry", NextUploadAt = now, UploadError = "ProcessInterrupted" });
        }
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json.tmp"))
        {
            var id = Path.GetFileName(path)[..^9];
            if (IdPattern.IsMatch(id))
                File.Delete(path);
        }
    }

    public void DeleteTemporary(string id)
    {
        foreach (var suffix in new[] { ".db.part", ".db.part-wal", ".db.part-shm", ".db.part-journal" })
            File.Delete(Path.Combine(DirectoryPath, id + suffix));
    }

    public void EnsureCapacity(BackupOptions options, long additionalBytes)
    {
        var used = Directory.EnumerateFiles(DirectoryPath).Sum(path => new FileInfo(path).Length);
        if (additionalBytes > options.MaxLocalBytes - used ||
            new DriveInfo(DirectoryPath).AvailableFreeSpace - additionalBytes < options.MinFreeDiskBytes)
            throw new IOException("BackupCapacityExceeded");
    }

    public void Cleanup(BackupOptions options, DateTimeOffset now)
    {
        var records = ReadAll();
        var newestSuccess = records.FirstOrDefault(record => record.State == "Completed" && File.Exists(GetPath(record.Id, ".db")));
        var newestUploaded = records.FirstOrDefault(record => record.UploadState == "Uploaded" && File.Exists(GetPath(record.Id, ".db")));
        var newest = records.FirstOrDefault();
        foreach (var record in records)
        {
            if (record.Id == newest?.Id || record.Id == newestSuccess?.Id || record.Id == newestUploaded?.Id ||
                record.StartedAt >= now.AddDays(-options.LocalRetentionDays) || record.State == "Running")
                continue;
            if (record.State == "Completed" && record.UploadState is not ("Uploaded" or "Disabled"))
                continue;
            File.Delete(GetPath(record.Id, ".db"));
            File.Delete(GetPath(record.Id, ".json"));
        }
    }
}
