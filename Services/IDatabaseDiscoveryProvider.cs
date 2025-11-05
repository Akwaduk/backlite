using backlite.Models;

namespace backlite.Services;

public interface IDatabaseDiscoveryProvider
{
    Task<IReadOnlyList<DiscoveredDbFile>> FindDatabasesAsync(
        ServerConnection server, 
        CancellationToken cancellationToken = default);
}

public interface IDatabaseDiscoveryService
{
    Task<IReadOnlyList<DiscoveredDbFile>> DiscoverDatabasesAsync(
        Guid serverId, 
        string? pathFilter = null,
        CancellationToken cancellationToken = default);
}

public class DiscoveryOptions
{
    public string? PathPrefix { get; set; }
    public string[]? IncludePatterns { get; set; }
    public string[]? ExcludePatterns { get; set; }
    public bool UseSudo { get; set; }
    public int TimeoutSeconds { get; set; } = 300; // 5 minutes default
}