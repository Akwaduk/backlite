namespace backlite.Models;

public sealed record Job(
    Guid Id,
    string Kind,
    string DisplayName,
    string Status,
    int ProgressPercent,
    DateTimeOffset Started,
    DateTimeOffset? Ended,
    string? Error,
    Guid? CorrelationId
)
{
    public Job() : this(
        Guid.NewGuid(),
        string.Empty,
        string.Empty,
        "Queued",
        0,
        DateTimeOffset.UtcNow,
        null,
        null,
        null
    ) { }
    
    public TimeSpan? Duration => Ended.HasValue ? Ended.Value - Started : DateTimeOffset.UtcNow - Started;
    
    public string DisplayDuration => Duration?.ToString(@"hh\:mm\:ss") ?? "00:00:00";
    
    public bool IsRunning => Status == "Running" || Status == "Queued";
    
    public bool IsCompleted => Status == "Completed" || Status == "Failed" || Status == "Cancelled";
    
    public bool HasError => !string.IsNullOrEmpty(Error);
}

public static class JobKinds
{
    public const string Discovery = "Discovery";
    public const string Backup = "Backup";
    public const string Restore = "Restore";
    public const string Copy = "Copy";
    public const string Inspect = "Inspect";
}

public static class JobStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}