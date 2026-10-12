using LifeOS.Application.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LifeOS.Infrastructure.Persistence;

/// <summary>Consistent online backup using SQLite's VACUUM INTO; safe while the app is running (WAL).</summary>
public class SqliteBackupService(IOptions<LifeOSOptions> options) : IDatabaseBackupService
{
    public async Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (File.Exists(fullPath))
        {
            throw new IOException($"Backup target already exists: {fullPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var connection = new SqliteConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path";
        command.Parameters.AddWithValue("$path", fullPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
