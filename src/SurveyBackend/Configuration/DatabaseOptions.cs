namespace SurveyBackend.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Path { get; set; } = "data.db";

    public string GetFullPath(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            throw new InvalidOperationException("Database:Path 不能为空。");
        }

        return System.IO.Path.GetFullPath(Path, baseDirectory);
    }
}
