using backlite.Data;
using backlite.Models;
using backlite.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace backlite.Services;

public class JobQueueService : IJobQueueService, IDisposable
{
    private readonly Channel<object> _jobChannel;
    private readonly ChannelWriter<object> _jobWriter;
    private readonly ChannelReader<object> _jobReader;
    private readonly ConcurrentDictionary<Guid, (CancellationTokenSource cts, TaskCompletionSource<bool> tcs)> _activeJobs = new();
    private readonly ConcurrentDictionary<Guid, Job> _jobCache = new();
    private readonly List<IJobEventHandler> _eventHandlers = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobQueueService> _logger;
    private readonly DbBackupManagerConfig _config;
    private readonly SemaphoreSlim _workerSemaphore;
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private readonly Task[] _workerTasks;

    public JobQueueService(
        IServiceProvider serviceProvider,
        ILogger<JobQueueService> logger,
        IOptions<DbBackupManagerConfig> config)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config.Value;

        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _jobChannel = Channel.CreateBounded<object>(options);
        _jobWriter = _jobChannel.Writer;
        _jobReader = _jobChannel.Reader;

        _workerSemaphore = new SemaphoreSlim(_config.Jobs.MaxConcurrentJobs, _config.Jobs.MaxConcurrentJobs);

        // Start worker tasks
        _workerTasks = new Task[_config.Jobs.MaxConcurrentJobs];
        for (int i = 0; i < _config.Jobs.MaxConcurrentJobs; i++)
        {
            _workerTasks[i] = Task.Run(WorkerLoopAsync);
        }

        _logger.LogInformation("Job queue service started with {MaxConcurrentJobs} workers", 
            _config.Jobs.MaxConcurrentJobs);
    }

    public async Task<Guid> EnqueueJobAsync<T>(
        string kind, 
        string displayName, 
        T payload, 
        CancellationToken cancellationToken = default) where T : class
    {
        var jobId = Guid.NewGuid();
        var queuedJob = new QueuedJob<T>
        {
            Id = jobId,
            Kind = kind,
            DisplayName = displayName,
            Payload = payload,
            QueuedAt = DateTimeOffset.UtcNow
        };

        var job = new Job(
            jobId,
            kind,
            displayName,
            JobStatuses.Queued,
            0,
            DateTimeOffset.UtcNow,
            null,
            null,
            null
        );

        // Store in cache and database
        _jobCache[jobId] = job;
        _activeJobs[jobId] = (queuedJob.CancellationTokenSource, queuedJob.CompletionSource);

        await PersistJobAsync(job, cancellationToken);
        await _jobWriter.WriteAsync(queuedJob, cancellationToken);

        _logger.LogInformation("Enqueued job {JobId} ({Kind}): {DisplayName}", 
            jobId, kind, displayName);

        return jobId;
    }

    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!_activeJobs.TryGetValue(jobId, out var jobInfo))
        {
            return false;
        }

        _logger.LogInformation("Cancelling job {JobId}", jobId);

        jobInfo.cts.Cancel();

        if (_jobCache.TryGetValue(jobId, out var job))
        {
            var cancelledJob = job with { Status = JobStatuses.Cancelled, Ended = DateTimeOffset.UtcNow };
            _jobCache[jobId] = cancelledJob;
            await PersistJobAsync(cancelledJob, cancellationToken);
            await NotifyJobCancelledAsync(cancelledJob);
        }

        return true;
    }

    public async Task<Job?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (_jobCache.TryGetValue(jobId, out var cachedJob))
        {
            return cachedJob;
        }

        // Fallback to database
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var entity = await context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<IReadOnlyList<Job>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var entities = await context.Jobs
            .OrderByDescending(j => j.Started)
            .Take(100) // Limit to recent jobs
            .ToListAsync(cancellationToken);
        
        return entities.Select(e => e.ToModel()).ToArray();
    }

    public async Task<IReadOnlyList<Job>> GetActiveJobsAsync(CancellationToken cancellationToken = default)
    {
        var activeJobs = _jobCache.Values
            .Where(job => job.IsRunning)
            .OrderByDescending(job => job.Started)
            .ToArray();

        return activeJobs;
    }

    public void Subscribe(IJobEventHandler handler)
    {
        lock (_eventHandlers)
        {
            _eventHandlers.Add(handler);
        }
    }

    public void Unsubscribe(IJobEventHandler handler)
    {
        lock (_eventHandlers)
        {
            _eventHandlers.Remove(handler);
        }
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var jobObj in _jobReader.ReadAllAsync(_shutdownTokenSource.Token))
        {
            await _workerSemaphore.WaitAsync(_shutdownTokenSource.Token);
            
            try
            {
                await ProcessJobAsync(jobObj);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in worker loop: {Message}", ex.Message);
            }
            finally
            {
                _workerSemaphore.Release();
            }
        }
    }

    private async Task ProcessJobAsync(object jobObj)
    {
        var jobType = jobObj.GetType();
        if (!jobType.IsGenericType || jobType.GetGenericTypeDefinition() != typeof(QueuedJob<>))
        {
            _logger.LogError("Invalid job type: {JobType}", jobType.Name);
            return;
        }

        var jobId = (Guid)jobType.GetProperty("Id")!.GetValue(jobObj)!;
        var kind = (string)jobType.GetProperty("Kind")!.GetValue(jobObj)!;

        if (!_activeJobs.TryGetValue(jobId, out var jobInfo))
        {
            _logger.LogWarning("Job {JobId} not found in active jobs", jobId);
            return;
        }

        try
        {
            _logger.LogInformation("Starting job {JobId} ({Kind})", jobId, kind);

            // Update job status
            if (_jobCache.TryGetValue(jobId, out var job))
            {
                var runningJob = job with { Status = JobStatuses.Running };
                _jobCache[jobId] = runningJob;
                await PersistJobAsync(runningJob, CancellationToken.None);
                await NotifyJobStartedAsync(runningJob);
            }

            // Create progress reporter
            var progressReporter = new JobProgressReporter(jobId, this);

            // Execute the job
            await ExecuteJobAsync(jobObj, progressReporter, jobInfo.cts.Token);

            // Mark as completed
            if (_jobCache.TryGetValue(jobId, out job))
            {
                var completedJob = job with { 
                    Status = JobStatuses.Completed, 
                    ProgressPercent = 100,
                    Ended = DateTimeOffset.UtcNow 
                };
                _jobCache[jobId] = completedJob;
                await PersistJobAsync(completedJob, CancellationToken.None);
                await NotifyJobCompletedAsync(completedJob);
            }

            jobInfo.tcs.SetResult(true);
            _logger.LogInformation("Completed job {JobId} ({Kind})", jobId, kind);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Job {JobId} ({Kind}) was cancelled", jobId, kind);
            jobInfo.tcs.SetCanceled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} ({Kind}) failed: {Message}", jobId, kind, ex.Message);

            if (_jobCache.TryGetValue(jobId, out var job))
            {
                var failedJob = job with { 
                    Status = JobStatuses.Failed, 
                    Error = ex.Message,
                    Ended = DateTimeOffset.UtcNow 
                };
                _jobCache[jobId] = failedJob;
                await PersistJobAsync(failedJob, CancellationToken.None);
                await NotifyJobFailedAsync(failedJob, ex.Message);
            }

            jobInfo.tcs.SetException(ex);
        }
        finally
        {
            _activeJobs.TryRemove(jobId, out _);
        }
    }

    private async Task ExecuteJobAsync(object jobObj, IJobProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        var jobType = jobObj.GetType();
        var payloadType = jobType.GetGenericArguments()[0];
        var handlerType = typeof(IJobHandler<>).MakeGenericType(payloadType);

        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No job handler registered for payload type {payloadType.Name}");
        }

        var executeMethod = handlerType.GetMethod("ExecuteAsync");
        if (executeMethod == null)
        {
            throw new InvalidOperationException($"ExecuteAsync method not found on handler {handlerType.Name}");
        }

        var task = (Task)executeMethod.Invoke(handler, new object[] { jobObj, progressReporter, cancellationToken })!;
        await task;
    }

    private async Task PersistJobAsync(Job job, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var entity = await context.Jobs.FirstOrDefaultAsync(j => j.Id == job.Id, cancellationToken);
            if (entity == null)
            {
                entity = JobEntity.FromModel(job);
                context.Jobs.Add(entity);
            }
            else
            {
                var updated = JobEntity.FromModel(job);
                context.Entry(entity).CurrentValues.SetValues(updated);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist job {JobId}: {Message}", job.Id, ex.Message);
        }
    }

    private async Task NotifyJobStartedAsync(Job job)
    {
        var handlers = GetEventHandlers();
        await Task.WhenAll(handlers.Select(h => h.OnJobStartedAsync(job)));
    }

    private async Task NotifyJobCompletedAsync(Job job)
    {
        var handlers = GetEventHandlers();
        await Task.WhenAll(handlers.Select(h => h.OnJobCompletedAsync(job)));
    }

    private async Task NotifyJobFailedAsync(Job job, string error)
    {
        var handlers = GetEventHandlers();
        await Task.WhenAll(handlers.Select(h => h.OnJobFailedAsync(job, error)));
    }

    private async Task NotifyJobCancelledAsync(Job job)
    {
        var handlers = GetEventHandlers();
        await Task.WhenAll(handlers.Select(h => h.OnJobCancelledAsync(job)));
    }

    internal async Task NotifyJobProgressAsync(Guid jobId, JobProgressEvent progress)
    {
        // Update job cache
        if (_jobCache.TryGetValue(jobId, out var job))
        {
            var updatedJob = job with { ProgressPercent = progress.ProgressPercent };
            _jobCache[jobId] = updatedJob;
        }

        var handlers = GetEventHandlers();
        await Task.WhenAll(handlers.Select(h => h.OnJobProgressAsync(jobId, progress)));
    }

    internal async Task NotifyJobLogAsync(Guid jobId, JobLogEvent logEvent)
    {
        var handlers = GetEventHandlers();
        await Task.WhenAll(handlers.Select(h => h.OnJobLogAsync(jobId, logEvent)));
    }

    private IJobEventHandler[] GetEventHandlers()
    {
        lock (_eventHandlers)
        {
            return _eventHandlers.ToArray();
        }
    }

    public void Dispose()
    {
        _shutdownTokenSource.Cancel();
        _jobWriter.Complete();

        try
        {
            Task.WaitAll(_workerTasks, TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Some worker tasks did not complete gracefully");
        }

        foreach (var (cts, tcs) in _activeJobs.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _workerSemaphore.Dispose();
        _shutdownTokenSource.Dispose();

        _logger.LogInformation("Job queue service disposed");
    }

    private class JobProgressReporter : IJobProgressReporter
    {
        private readonly Guid _jobId;
        private readonly JobQueueService _jobQueueService;

        public JobProgressReporter(Guid jobId, JobQueueService jobQueueService)
        {
            _jobId = jobId;
            _jobQueueService = jobQueueService;
        }

        public async Task ReportProgressAsync(int progressPercent, string phase, string? currentFile = null, 
            long? processedBytes = null, long? totalBytes = null)
        {
            var progress = new JobProgressEvent
            {
                JobId = _jobId,
                ProgressPercent = Math.Max(0, Math.Min(100, progressPercent)),
                Phase = phase,
                CurrentFile = currentFile,
                ProcessedBytes = processedBytes,
                TotalBytes = totalBytes,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _jobQueueService.NotifyJobProgressAsync(_jobId, progress);
        }

        public async Task LogAsync(Models.LogLevel level, string message, string? category = null, 
            Dictionary<string, object>? properties = null)
        {
            var logEvent = new JobLogEvent
            {
                JobId = _jobId,
                Timestamp = DateTimeOffset.UtcNow,
                Level = level,
                Message = message,
                Category = category,
                Properties = properties
            };

            await _jobQueueService.NotifyJobLogAsync(_jobId, logEvent);
        }
    }
}