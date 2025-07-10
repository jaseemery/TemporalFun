using Temporalio.Activities;
using Microsoft.Extensions.Logging;

namespace TestDeletionActivities;

public static class FileOperations
{
    [Activity]
    public static async Task<string> CreateBackup(string filePath, string backupLocation)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Creating backup of {FilePath} to {BackupLocation}", filePath, backupLocation);
        
        await Task.Delay(500);
        
        var backupId = $"BACKUP-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        
        logger.LogInformation("Backup created with ID: {BackupId}", backupId);
        return backupId;
    }

    [Activity]
    public static async Task<bool> DeleteOldFiles(string directory, int daysOld)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Deleting files older than {DaysOld} days from {Directory}", daysOld, directory);
        
        await Task.Delay(800);
        
        // Simulate deletion logic
        var filesDeleted = Math.Max(0, daysOld - 1) * 5; // More days = more files to delete
        
        logger.LogInformation("Deleted {FilesDeleted} old files from {Directory}", filesDeleted, directory);
        return filesDeleted > 0;
    }
}