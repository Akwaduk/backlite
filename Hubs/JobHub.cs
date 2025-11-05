using backlite.Models;
using Microsoft.AspNetCore.SignalR;

namespace backlite.Hubs;

public class JobHub : Hub<IJobHubClient>
{
    public async Task JoinJobGroup(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job_{jobId}");
    }

    public async Task LeaveJobGroup(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job_{jobId}");
    }

    public async Task JoinJobsGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "jobs");
    }

    public async Task LeaveJobsGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "jobs");
    }
}

public interface IJobHubClient
{
    Task JobStarted(Job job);
    Task JobProgress(Guid jobId, JobProgressEvent progress);
    Task JobLog(Guid jobId, JobLogEvent logEvent);
    Task JobCompleted(Job job);
    Task JobFailed(Job job, string error);
    Task JobCancelled(Job job);
}