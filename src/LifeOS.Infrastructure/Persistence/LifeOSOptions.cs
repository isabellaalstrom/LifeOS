namespace LifeOS.Infrastructure.Persistence;

public class LifeOSOptions
{
    public const string SectionName = "LifeOS";

    /// <summary>Directory holding the SQLite file, Data Protection keys and backups. /data in containers.</summary>
    public string DataPath { get; set; } = "data";

    public string DatabaseFilePath => Path.Combine(DataPath, "lifeos.db");

    public string ConnectionString => $"Data Source={DatabaseFilePath};Default Timeout=10;Foreign Keys=True";
}
