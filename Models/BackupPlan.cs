using System.ComponentModel.DataAnnotations;

namespace backlite.Models;

public sealed record BackupPlan(
    Guid Id,
    Guid ServerId,
    [Required] string Name,
    string[] DbPaths,
    [Required] string DestinationDir,
    bool Compress,
    int RetentionCount,
    string? CronExpression,
    bool Enabled
)
{
    public BackupPlan() : this(
        Guid.NewGuid(),
        Guid.Empty,
        string.Empty,
        Array.Empty<string>(),
        string.Empty,
        true,
        10,
        null,
        true
    ) { }
}

public sealed record BackupRun(
    Guid Id,
    Guid PlanId,
    DateTimeOffset Started,
    DateTimeOffset? Ended,
    string Status,
    string? ArtifactPath,
    long? ArtifactSizeBytes,
    string[] LogLines
)
{
    public BackupRun() : this(
        Guid.NewGuid(),
        Guid.Empty,
        DateTimeOffset.UtcNow,
        null,
        "Running",
        null,
        null,
        Array.Empty<string>()
    ) { }
    
    public TimeSpan? Duration => Ended.HasValue ? Ended.Value - Started : null;
    
    public string DisplayDuration => Duration?.ToString(@"hh\:mm\:ss") ?? "Running...";
    
    public string DisplaySize => ArtifactSizeBytes.HasValue ? FormatFileSize(ArtifactSizeBytes.Value) : "N/A";
    
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        
        return $"{number:n1} {suffixes[counter]}";
    }
}