using backlite.Models;
using backlite.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace backlite.Services;

public sealed class DebianDatabaseDiscoveryProvider : IDatabaseDiscoveryProvider
{
    private readonly ISshConnectionService _sshService;
    private readonly ILogger<DebianDatabaseDiscoveryProvider> _logger;
    private readonly DbBackupManagerConfig _config;

    // The exact Debian discovery command as specified
    private const string FIND_COMMAND = @"
sudo find / -type f -name ""*.db"" \
  -not -path ""*/var/cache/*"" \
  -not -path ""*/var/lib/apt/*"" \
  -not -path ""*/var/lib/dpkg/*"" \
  -not -path ""*/var/lib/PackageKit/*"" \
  -not -path ""*/var/lib/docker/*"" \
  -not -path ""*/var/lib/containerd/*"" \
  -not -path ""*/var/lib/rancher/*"" \
  -not -path ""*/var/lib/snapd/*"" \
  -not -path ""*/var/lib/gdm3/*"" \
  -not -path ""*/var/lib/colord/*"" \
  -not -path ""*/var/lib/fwupd/*"" \
  -not -path ""*/.cache/*"" \
  -not -path ""*/.mozilla/*"" \
  -not -path ""*/.local/share/evolution/*"" \
  -not -path ""*/.local/share/tracker/*"" \
  -not -path ""*/usr/bin/*"" \
  -not -path ""*/usr/sbin/*"" \
  -not -path ""*/usr/lib/*"" \
  -not -path ""*/snap/*"" \
  -not -path ""*/proc/*"" \
  -not -path ""*/sys/*"" \
  -not -path ""*/dev/*"" \
  -not -path ""*/run/*"" \
  -not -path ""*/tmp/*"" \
  2>/dev/null";

    private const string FIND_COMMAND_NO_SUDO = @"
find / -type f -name ""*.db"" \
  -not -path ""*/var/cache/*"" \
  -not -path ""*/var/lib/apt/*"" \
  -not -path ""*/var/lib/dpkg/*"" \
  -not -path ""*/var/lib/PackageKit/*"" \
  -not -path ""*/var/lib/docker/*"" \
  -not -path ""*/var/lib/containerd/*"" \
  -not -path ""*/var/lib/rancher/*"" \
  -not -path ""*/var/lib/snapd/*"" \
  -not -path ""*/var/lib/gdm3/*"" \
  -not -path ""*/var/lib/colord/*"" \
  -not -path ""*/var/lib/fwupd/*"" \
  -not -path ""*/.cache/*"" \
  -not -path ""*/.mozilla/*"" \
  -not -path ""*/.local/share/evolution/*"" \
  -not -path ""*/.local/share/tracker/*"" \
  -not -path ""*/usr/bin/*"" \
  -not -path ""*/usr/sbin/*"" \
  -not -path ""*/usr/lib/*"" \
  -not -path ""*/snap/*"" \
  -not -path ""*/proc/*"" \
  -not -path ""*/sys/*"" \
  -not -path ""*/dev/*"" \
  -not -path ""*/run/*"" \
  -not -path ""*/tmp/*"" \
  2>/dev/null";

    public DebianDatabaseDiscoveryProvider(
        ISshConnectionService sshService,
        ILogger<DebianDatabaseDiscoveryProvider> logger,
        IOptions<DbBackupManagerConfig> config)
    {
        _sshService = sshService;
        _logger = logger;
        _config = config.Value;
    }

    public async Task<IReadOnlyList<DiscoveredDbFile>> FindDatabasesAsync(
        ServerConnection server, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting database discovery on server {ServerName} ({Host})", 
            server.Name, server.Host);

        try
        {
            // Step 1: Execute the find command to get database file paths
            var findCommand = server.UseSudoForDiscovery ? FIND_COMMAND : FIND_COMMAND_NO_SUDO;
            var dbPaths = await ExecuteFindCommandAsync(server, findCommand, cancellationToken);

            if (!dbPaths.Any())
            {
                _logger.LogInformation("No database files found on server {ServerName}", server.Name);
                return Array.Empty<DiscoveredDbFile>();
            }

            _logger.LogInformation("Found {Count} potential database files on server {ServerName}", 
                dbPaths.Count, server.Name);

            // Step 2: Get detailed information for each file using stat
            var dbFiles = await EnrichWithFileDetailsAsync(server, dbPaths, cancellationToken);

            // Step 3: Filter based on allowed roots
            var filteredFiles = FilterByAllowedRoots(dbFiles, server.AllowedRoots);

            _logger.LogInformation("Discovered {Count} database files after filtering on server {ServerName}", 
                filteredFiles.Count, server.Name);

            return filteredFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover databases on server {ServerName}: {Message}", 
                server.Name, ex.Message);
            throw;
        }
    }

    private async Task<List<string>> ExecuteFindCommandAsync(
        ServerConnection server, 
        string findCommand, 
        CancellationToken cancellationToken)
    {
        var result = await _sshService.ExecuteCommandAsync(server, findCommand, cancellationToken);
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Find command failed: {result.Error}");
        }

        var paths = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line) && line.EndsWith(".db"))
            .ToList();

        return paths;
    }

    private async Task<List<DiscoveredDbFile>> EnrichWithFileDetailsAsync(
        ServerConnection server, 
        List<string> dbPaths, 
        CancellationToken cancellationToken)
    {
        var discoveredFiles = new List<DiscoveredDbFile>();

        // Process files in batches to avoid overwhelming the server
        const int batchSize = 50;
        for (int i = 0; i < dbPaths.Count; i += batchSize)
        {
            var batch = dbPaths.Skip(i).Take(batchSize).ToList();
            var batchResults = await ProcessBatchAsync(server, batch, cancellationToken);
            discoveredFiles.AddRange(batchResults);
        }

        return discoveredFiles;
    }

    private async Task<List<DiscoveredDbFile>> ProcessBatchAsync(
        ServerConnection server, 
        List<string> filePaths, 
        CancellationToken cancellationToken)
    {
        var results = new List<DiscoveredDbFile>();

        // Build a single stat command for all files in the batch
        var statCommand = BuildStatCommand(filePaths);
        var statResult = await _sshService.ExecuteCommandAsync(server, statCommand, cancellationToken);

        if (!statResult.IsSuccess)
        {
            _logger.LogWarning("Stat command failed for batch, falling back to individual files: {Error}", 
                statResult.Error);

            // Fallback: process files individually
            foreach (var filePath in filePaths)
            {
                var file = await ProcessSingleFileAsync(server, filePath, cancellationToken);
                if (file != null)
                {
                    results.Add(file);
                }
            }
            return results;
        }

        // Parse the stat output
        var statLines = statResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < Math.Min(filePaths.Count, statLines.Length); i++)
        {
            var file = ParseStatOutput(filePaths[i], statLines[i]);
            if (file != null)
            {
                results.Add(file);
            }
        }

        return results;
    }

    private async Task<DiscoveredDbFile?> ProcessSingleFileAsync(
        ServerConnection server, 
        string filePath, 
        CancellationToken cancellationToken)
    {
        try
        {
            var statCommand = $@"stat -c ""%s|%y|%U|%A"" ""{filePath}"" 2>/dev/null || echo ""ERROR""";
            var result = await _sshService.ExecuteCommandAsync(server, statCommand, cancellationToken);

            if (!result.IsSuccess || result.Output.Contains("ERROR"))
            {
                return new DiscoveredDbFile(filePath, null, null, null, null);
            }

            return ParseStatOutput(filePath, result.Output.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get details for file {FilePath}", filePath);
            return new DiscoveredDbFile(filePath, null, null, null, null);
        }
    }

    private static string BuildStatCommand(List<string> filePaths)
    {
        var sb = new StringBuilder();
        sb.Append("for file in");
        
        foreach (var path in filePaths)
        {
            sb.Append($@" ""{path}""");
        }
        
        sb.Append(@"; do stat -c ""%s|%y|%U|%A"" ""$file"" 2>/dev/null || echo ""ERROR""; done");
        
        return sb.ToString();
    }

    private static DiscoveredDbFile? ParseStatOutput(string filePath, string statOutput)
    {
        if (string.IsNullOrEmpty(statOutput) || statOutput.Contains("ERROR"))
        {
            return new DiscoveredDbFile(filePath, null, null, null, null);
        }

        try
        {
            // Expected format: size|datetime|owner|permissions
            var parts = statOutput.Split('|');
            if (parts.Length < 4)
            {
                return new DiscoveredDbFile(filePath, null, null, null, null);
            }

            var sizeBytes = long.TryParse(parts[0], out var size) ? size : (long?)null;
            var lastWrite = TryParseDateTime(parts[1]);
            var owner = parts[2];
            var permissions = parts[3];

            return new DiscoveredDbFile(filePath, sizeBytes, lastWrite, owner, permissions);
        }
        catch (Exception)
        {
            return new DiscoveredDbFile(filePath, null, null, null, null);
        }
    }

    private static DateTimeOffset? TryParseDateTime(string dateTimeStr)
    {
        if (string.IsNullOrEmpty(dateTimeStr))
            return null;

        try
        {
            // Remove timezone info and fractional seconds for simpler parsing
            var cleanedStr = Regex.Replace(dateTimeStr, @"\.\d+ [+-]\d{4}$", "");
            
            if (DateTime.TryParse(cleanedStr, out var dateTime))
            {
                return new DateTimeOffset(dateTime, TimeSpan.Zero);
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    private List<DiscoveredDbFile> FilterByAllowedRoots(
        List<DiscoveredDbFile> files, 
        string[] allowedRoots)
    {
        if (!allowedRoots.Any())
        {
            // If no allowed roots specified, use global defaults
            allowedRoots = _config.Security.GlobalAllowedRoots;
        }

        if (!allowedRoots.Any())
        {
            // If still no roots specified, allow all
            return files;
        }

        return files.Where(file => 
            allowedRoots.Any(root => file.FullPath.StartsWith(root + "/") || file.FullPath == root)
        ).ToList();
    }
}