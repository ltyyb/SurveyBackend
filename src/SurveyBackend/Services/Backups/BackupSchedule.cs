namespace SurveyBackend.Services.Backups;

public sealed record BackupSchedule(DateTimeOffset NextRunAt, TimeSpan Interval);
