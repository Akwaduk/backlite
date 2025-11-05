using backlite.Data;
using backlite.Models;
using backlite.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backlite.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJobQueueService _jobQueue;
    private readonly ILogger<JobsController> _logger;

    public JobsController(IJobQueueService jobQueue, ILogger<JobsController> logger)
    {
        _jobQueue = jobQueue;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Job>>> GetJobs()
    {
        var jobs = await _jobQueue.GetJobsAsync();
        return Ok(jobs);
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<Job>>> GetActiveJobs()
    {
        var jobs = await _jobQueue.GetActiveJobsAsync();
        return Ok(jobs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Job>> GetJob(Guid id)
    {
        var job = await _jobQueue.GetJobAsync(id);
        if (job == null)
        {
            return NotFound();
        }

        return Ok(job);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelJob(Guid id)
    {
        var cancelled = await _jobQueue.CancelJobAsync(id);
        if (!cancelled)
        {
            return NotFound();
        }

        _logger.LogInformation("Job {JobId} cancellation requested", id);
        return NoContent();
    }
}

[ApiController]
[Route("api/[controller]")]
public class DiscoveryController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJobQueueService _jobQueue;
    private readonly ILogger<DiscoveryController> _logger;

    public DiscoveryController(
        ApplicationDbContext context,
        IJobQueueService jobQueue,
        ILogger<DiscoveryController> logger)
    {
        _context = context;
        _jobQueue = jobQueue;
        _logger = logger;
    }

    [HttpPost("run")]
    public async Task<ActionResult<RunDiscoveryResponse>> RunDiscovery(RunDiscoveryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var server = await _context.ServerConnections.FindAsync(request.ServerId);
        if (server == null)
        {
            return NotFound("Server not found");
        }

        var jobId = await _jobQueue.EnqueueJobAsync(
            JobKinds.Discovery,
            $"Discover databases on {server.Name}",
            new DiscoveryJobPayload
            {
                ServerId = request.ServerId,
                PathFilter = request.PathFilter
            });

        _logger.LogInformation("Queued discovery job {JobId} for server {ServerName}", 
            jobId, server.Name);

        return Ok(new RunDiscoveryResponse { JobId = jobId });
    }

    // TODO: Add endpoint to get discovery results
}

public class RunDiscoveryRequest
{
    public Guid ServerId { get; set; }
    public string? PathFilter { get; set; }
}

public class RunDiscoveryResponse
{
    public Guid JobId { get; set; }
}

public class DiscoveryJobPayload
{
    public Guid ServerId { get; set; }
    public string? PathFilter { get; set; }
}