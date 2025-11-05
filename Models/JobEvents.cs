namespace backlite.Models;

public class JobLogEvent
{
    public Guid JobId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Category { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public class JobProgressEvent
{
    public Guid JobId { get; set; }
    public int ProgressPercent { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string? CurrentFile { get; set; }
    public long? ProcessedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class SqliteTableInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public string? EstimatedRowCount { get; set; }
    public List<SqliteColumnInfo> Columns { get; set; } = new();
    public List<SqliteIndexInfo> Indexes { get; set; } = new();
}

public class SqliteColumnInfo
{
    public int Cid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool NotNull { get; set; }
    public string? DefaultValue { get; set; }
    public bool PrimaryKey { get; set; }
}

public class SqliteIndexInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Unique { get; set; }
    public string Origin { get; set; } = string.Empty;
    public bool Partial { get; set; }
    public List<string> Columns { get; set; } = new();
}

public class SqliteDatabaseInfo
{
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int PageSize { get; set; }
    public int PageCount { get; set; }
    public int FreelistCount { get; set; }
    public string SchemaVersion { get; set; } = string.Empty;
    public List<SqliteTableInfo> Tables { get; set; } = new();
    public Dictionary<string, string> PragmaInfo { get; set; } = new();
}