using backlite.Models;
using System.Threading.Channels;

namespace backlite.Services;

public interface IJobQueueService
{
    Task<Guid> EnqueueJobAsync<T>(string kind, string displayName, T payload, CancellationToken cancellationToken = default)
        where T : class;
    
    Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    
    Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<Job>> GetJobsAsync(CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<Job>> GetActiveJobsAsync(CancellationToken cancellationToken = default);
    
    void Subscribe(IJobEventHandler handler);
    void Unsubscribe(IJobEventHandler handler);
}

public interface IJobEventHandler
{
    Task OnJobStartedAsync(Job job);
    Task OnJobProgressAsync(Guid jobId, JobProgressEvent progress);
    Task OnJobLogAsync(Guid jobId, JobLogEvent logEvent);
    Task OnJobCompletedAsync(Job job);
    Task OnJobFailedAsync(Job job, string error);
    Task OnJobCancelledAsync(Job job);
}

public class QueuedJob<T> where T : class
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public T Payload { get; set; } = default!;
    public DateTimeOffset QueuedAt { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    public TaskCompletionSource<bool> CompletionSource { get; set; } = new();
}

public interface IJobHandler<T> where T : class
{
    Task ExecuteAsync(QueuedJob<T> job, IJobProgressReporter progressReporter, CancellationToken cancellationToken);
}

public interface IJobProgressReporter
{
    Task ReportProgressAsync(int progressPercent, string phase, string? currentFile = null, 
        long? processedBytes = null, long? totalBytes = null);
    
    Task LogAsync(Models.LogLevel level, string message, string? category = null, 
        Dictionary<string, object>? properties = null);
}