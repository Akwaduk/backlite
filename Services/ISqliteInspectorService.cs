using backlite.Models;

namespace backlite.Services;

public interface ISqliteInspectorService
{
    Task<SqliteDatabaseInfo?> InspectDatabaseAsync(
        ServerConnection server,
        string remotePath,
        CancellationToken cancellationToken = default);

    Task<QueryResult> ExecuteQueryAsync(
        ServerConnection server,
        string remotePath,
        string query,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateQueryAsync(string query);
}

public class QueryResult
{
    public bool IsSuccess { get; set; }
    public string[] ColumnNames { get; set; } = Array.Empty<string>();
    public object[][] Rows { get; set; } = Array.Empty<object[]>();
    public string? Error { get; set; }
    public int RowCount { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public bool WasTruncated { get; set; }
}