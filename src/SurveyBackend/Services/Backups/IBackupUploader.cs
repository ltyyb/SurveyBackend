namespace SurveyBackend.Services.Backups;

public interface IBackupUploader
{
    Task UploadAsync(BackupRecord record, string databasePath, CancellationToken cancellationToken);
}
