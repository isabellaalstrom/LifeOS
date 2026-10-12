namespace LifeOS.Application.Abstractions;

/// <summary>Creates consistent online backups of the application database.</summary>
public interface IDatabaseBackupService
{
    /// <summary>Writes a consistent snapshot of the live database to <paramref name="destinationPath"/>.</summary>
    Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default);
}
