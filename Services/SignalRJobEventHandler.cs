using backlite.Hubs;
using backlite.Models;
using backlite.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace backlite.Services;

public class SignalRJobEventHandler : IJobEventHandler
{
    private readonly IHubContext<JobHub, IJobHubClient> _hubContext;
    private readonly ILogger<SignalRJobEventHandler> _logger;

    public SignalRJobEventHandler(
        IHubContext<JobHub, IJobHubClient> hubContext,
        ILogger<SignalRJobEventHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task OnJobStartedAsync(Job job)
    {
        try
        {
            await _hubContext.Clients.Group("jobs").JobStarted(job);
            await _hubContext.Clients.Group($"job_{job.Id}").JobStarted(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send JobStarted notification for job {JobId}", job.Id);
        }
    }

    public async Task OnJobProgressAsync(Guid jobId, JobProgressEvent progress)
    {
        try
        {
            await _hubContext.Clients.Group("jobs").JobProgress(jobId, progress);
            await _hubContext.Clients.Group($"job_{jobId}").JobProgress(jobId, progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send JobProgress notification for job {JobId}", jobId);
        }
    }

    public async Task OnJobLogAsync(Guid jobId, JobLogEvent logEvent)
    {
        try
        {
            await _hubContext.Clients.Group($"job_{jobId}").JobLog(jobId, logEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send JobLog notification for job {JobId}", jobId);
        }
    }

    public async Task OnJobCompletedAsync(Job job)
    {
        try
        {
            await _hubContext.Clients.Group("jobs").JobCompleted(job);
            await _hubContext.Clients.Group($"job_{job.Id}").JobCompleted(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send JobCompleted notification for job {JobId}", job.Id);
        }
    }

    public async Task OnJobFailedAsync(Job job, string error)
    {
        try
        {
            await _hubContext.Clients.Group("jobs").JobFailed(job, error);
            await _hubContext.Clients.Group($"job_{job.Id}").JobFailed(job, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send JobFailed notification for job {JobId}", job.Id);
        }
    }

    public async Task OnJobCancelledAsync(Job job)
    {
        try
        {
            await _hubContext.Clients.Group("jobs").JobCancelled(job);
            await _hubContext.Clients.Group($"job_{job.Id}").JobCancelled(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send JobCancelled notification for job {JobId}", job.Id);
        }
    }
}