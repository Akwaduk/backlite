using backlite.Data;
using backlite.Models;
using backlite.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace backlite.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ISshConnectionService _sshService;
    private readonly ILogger<ServersController> _logger;

    public ServersController(
        ApplicationDbContext context,
        ISshConnectionService sshService,
        ILogger<ServersController> logger)
    {
        _context = context;
        _sshService = sshService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServerConnection>>> GetServers()
    {
        var entities = await _context.ServerConnections.ToListAsync();
        return Ok(entities.Select(e => e.ToModel()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServerConnection>> GetServer(Guid id)
    {
        var entity = await _context.ServerConnections.FindAsync(id);
        if (entity == null)
        {
            return NotFound();
        }

        return Ok(entity.ToModel());
    }

    [HttpPost]
    public async Task<ActionResult<ServerConnection>> CreateServer(CreateServerRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var server = new ServerConnection(
            Guid.NewGuid(),
            request.Name,
            request.Host,
            request.Port,
            request.Username,
            request.AuthKind,
            request.Password,
            request.PrivateKeyPem,
            request.UseSudoForDiscovery,
            request.AllowedRoots ?? Array.Empty<string>()
        );

        var entity = ServerConnectionEntity.FromModel(server);
        _context.ServerConnections.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created server {ServerName} ({Host})", server.Name, server.Host);

        return CreatedAtAction(nameof(GetServer), new { id = server.Id }, server);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateServer(Guid id, UpdateServerRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var entity = await _context.ServerConnections.FindAsync(id);
        if (entity == null)
        {
            return NotFound();
        }

        entity.Name = request.Name;
        entity.Host = request.Host;
        entity.Port = request.Port;
        entity.Username = request.Username;
        entity.AuthKind = request.AuthKind;
        entity.Password = request.Password;
        entity.PrivateKeyPem = request.PrivateKeyPem;
        entity.UseSudoForDiscovery = request.UseSudoForDiscovery;
        entity.AllowedRoots = request.AllowedRoots ?? Array.Empty<string>();

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated server {ServerName} ({Host})", entity.Name, entity.Host);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteServer(Guid id)
    {
        var entity = await _context.ServerConnections.FindAsync(id);
        if (entity == null)
        {
            return NotFound();
        }

        _context.ServerConnections.Remove(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted server {ServerName}", entity.Name);

        return NoContent();
    }

    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestConnectionResponse>> TestConnection(Guid id)
    {
        var entity = await _context.ServerConnections.FindAsync(id);
        if (entity == null)
        {
            return NotFound();
        }

        var server = entity.ToModel();

        try
        {
            var isConnected = await _sshService.TestConnectionAsync(server);
            
            if (isConnected)
            {
                entity.LastConnectedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new TestConnectionResponse
            {
                IsSuccess = isConnected,
                Message = isConnected ? "Connection successful" : "Connection failed",
                TestedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed for server {ServerName}: {Message}", 
                server.Name, ex.Message);

            return Ok(new TestConnectionResponse
            {
                IsSuccess = false,
                Message = ex.Message,
                TestedAt = DateTimeOffset.UtcNow
            });
        }
    }
}

public class CreateServerRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 22;

    [Required]
    public string Username { get; set; } = string.Empty;

    public AuthKind AuthKind { get; set; }

    public string? Password { get; set; }

    public string? PrivateKeyPem { get; set; }

    public bool UseSudoForDiscovery { get; set; }

    public string[]? AllowedRoots { get; set; }
}

public class UpdateServerRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 22;

    [Required]
    public string Username { get; set; } = string.Empty;

    public AuthKind AuthKind { get; set; }

    public string? Password { get; set; }

    public string? PrivateKeyPem { get; set; }

    public bool UseSudoForDiscovery { get; set; }

    public string[]? AllowedRoots { get; set; }
}

public class TestConnectionResponse
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset TestedAt { get; set; }
}