namespace backlite.Models;

public sealed record DiscoveredDbFile(
    string FullPath,
    long? SizeBytes,
    DateTimeOffset? LastWrite,
    string? Owner,
    string? Permissions
)
{
    public string DisplaySize => SizeBytes.HasValue ? FormatFileSize(SizeBytes.Value) : "Unknown";
    
    public string DisplayPath => Path.GetFileName(FullPath);
    
    public string DisplayDirectory => Path.GetDirectoryName(FullPath) ?? string.Empty;
    
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