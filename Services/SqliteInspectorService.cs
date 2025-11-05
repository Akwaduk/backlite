using backlite.Models;
using backlite.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace backlite.Services;

public class SqliteInspectorService : ISqliteInspectorService
{
    private readonly ISshConnectionService _sshService;
    private readonly ILogger<SqliteInspectorService> _logger;
    private readonly DbBackupManagerConfig _config;

    private static readonly Regex SelectOnlyRegex = new(
        @"^\s*SELECT\s+",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex DangerousQueryRegex = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|REPLACE|TRUNCATE|VACUUM|PRAGMA(?!\s+(table_info|index_list|database_list|page_size|page_count|freelist_count|schema_version)))\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private const int MaxQueryRows = 1000;
    private const int QueryTimeoutSeconds = 30;

    public SqliteInspectorService(
        ISshConnectionService sshService,
        ILogger<SqliteInspectorService> logger,
        IOptions<DbBackupManagerConfig> config)
    {
        _sshService = sshService;
        _logger = logger;
        _config = config.Value;
    }

    public async Task<SqliteDatabaseInfo?> InspectDatabaseAsync(
        ServerConnection server,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inspecting SQLite database on {Server}: {Path}", 
            server.Name, remotePath);

        try
        {
            // First, create a temporary local copy for safe inspection
            var tempPath = Path.GetTempFileName();
            
            try
            {
                // Download the database file
                var downloadResult = await _sshService.DownloadFileAsync(
                    server, remotePath, tempPath, null, cancellationToken);

                if (!downloadResult.IsSuccess)
                {
                    _logger.LogError("Failed to download database file {RemotePath} from {Server}: {Error}", 
                        remotePath, server.Name, downloadResult.Error);
                    return null;
                }

                // Inspect the local copy
                return await InspectLocalDatabaseAsync(tempPath, remotePath, cancellationToken);
            }
            finally
            {
                // Clean up temp file
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temp file {TempPath}", tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect database {RemotePath} on {Server}: {Message}", 
                remotePath, server.Name, ex.Message);
            return null;
        }
    }

    public async Task<QueryResult> ExecuteQueryAsync(
        ServerConnection server,
        string remotePath,
        string query,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate query safety
            if (!await ValidateQueryAsync(query))
            {
                return new QueryResult
                {
                    IsSuccess = false,
                    Error = "Query contains potentially dangerous operations and is not allowed.",
                    ExecutionTime = stopwatch.Elapsed
                };
            }

            _logger.LogInformation("Executing query on {Server}:{Path}", server.Name, remotePath);

            // Create a temporary local copy for safe querying
            var tempPath = Path.GetTempFileName();
            
            try
            {
                // Download the database file
                var downloadResult = await _sshService.DownloadFileAsync(
                    server, remotePath, tempPath, null, cancellationToken);

                if (!downloadResult.IsSuccess)
                {
                    return new QueryResult
                    {
                        IsSuccess = false,
                        Error = $"Failed to download database: {downloadResult.Error}",
                        ExecutionTime = stopwatch.Elapsed
                    };
                }

                // Execute query on local copy
                return await ExecuteQueryOnLocalDatabaseAsync(tempPath, query, cancellationToken);
            }
            finally
            {
                // Clean up temp file
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query on {Server}:{Path}: {Message}", 
                server.Name, remotePath, ex.Message);

            return new QueryResult
            {
                IsSuccess = false,
                Error = ex.Message,
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }

    public async Task<bool> ValidateQueryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        // Must be a SELECT statement or safe PRAGMA
        if (!SelectOnlyRegex.IsMatch(query) && !query.Trim().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase))
            return false;

        // Must not contain dangerous operations
        if (DangerousQueryRegex.IsMatch(query))
            return false;

        return await Task.FromResult(true);
    }

    private async Task<SqliteDatabaseInfo> InspectLocalDatabaseAsync(
        string localPath, 
        string originalPath, 
        CancellationToken cancellationToken)
    {
        var connectionString = $"Data Source={localPath};Mode=ReadOnly;Cache=Shared";
        
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var dbInfo = new SqliteDatabaseInfo
        {
            FilePath = originalPath,
            FileSize = new FileInfo(localPath).Length,
            Tables = new List<SqliteTableInfo>(),
            PragmaInfo = new Dictionary<string, string>()
        };

        // Get basic PRAGMA information
        await PopulatePragmaInfoAsync(connection, dbInfo, cancellationToken);

        // Get table information
        await PopulateTableInfoAsync(connection, dbInfo, cancellationToken);

        return dbInfo;
    }

    private async Task PopulatePragmaInfoAsync(
        SqliteConnection connection, 
        SqliteDatabaseInfo dbInfo, 
        CancellationToken cancellationToken)
    {
        var pragmaQueries = new Dictionary<string, string>
        {
            ["page_size"] = "PRAGMA page_size",
            ["page_count"] = "PRAGMA page_count",
            ["freelist_count"] = "PRAGMA freelist_count",
            ["schema_version"] = "PRAGMA schema_version",
            ["user_version"] = "PRAGMA user_version",
            ["application_id"] = "PRAGMA application_id",
            ["encoding"] = "PRAGMA encoding",
            ["journal_mode"] = "PRAGMA journal_mode",
            ["synchronous"] = "PRAGMA synchronous"
        };

        foreach (var (key, query) in pragmaQueries)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = query;
                command.CommandTimeout = QueryTimeoutSeconds;

                var result = await command.ExecuteScalarAsync(cancellationToken);
                dbInfo.PragmaInfo[key] = result?.ToString() ?? "";

                // Set specific properties
                switch (key)
                {
                    case "page_size":
                        if (int.TryParse(result?.ToString(), out var pageSize))
                            dbInfo.PageSize = pageSize;
                        break;
                    case "page_count":
                        if (int.TryParse(result?.ToString(), out var pageCount))
                            dbInfo.PageCount = pageCount;
                        break;
                    case "freelist_count":
                        if (int.TryParse(result?.ToString(), out var freelistCount))
                            dbInfo.FreelistCount = freelistCount;
                        break;
                    case "schema_version":
                        dbInfo.SchemaVersion = result?.ToString() ?? "";
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get PRAGMA {Key}: {Message}", key, ex.Message);
                dbInfo.PragmaInfo[key] = $"Error: {ex.Message}";
            }
        }
    }

    private async Task PopulateTableInfoAsync(
        SqliteConnection connection, 
        SqliteDatabaseInfo dbInfo, 
        CancellationToken cancellationToken)
    {
        // Get list of tables
        using var tablesCommand = connection.CreateCommand();
        tablesCommand.CommandText = @"
            SELECT name, type 
            FROM sqlite_master 
            WHERE type IN ('table', 'view') 
            AND name NOT LIKE 'sqlite_%'
            ORDER BY name";
        tablesCommand.CommandTimeout = QueryTimeoutSeconds;

        using var tablesReader = await tablesCommand.ExecuteReaderAsync(cancellationToken);
        
        while (await tablesReader.ReadAsync(cancellationToken))
        {
            var tableName = tablesReader.GetString("name");
            var tableType = tablesReader.GetString("type");

            var tableInfo = new SqliteTableInfo
            {
                Name = tableName,
                Type = tableType,
                Columns = new List<SqliteColumnInfo>(),
                Indexes = new List<SqliteIndexInfo>()
            };

            // Get column information
            await PopulateColumnInfoAsync(connection, tableInfo, cancellationToken);

            // Get index information
            await PopulateIndexInfoAsync(connection, tableInfo, cancellationToken);

            // Get row count estimate
            await PopulateRowCountAsync(connection, tableInfo, cancellationToken);

            dbInfo.Tables.Add(tableInfo);
        }
    }

    private async Task PopulateColumnInfoAsync(
        SqliteConnection connection, 
        SqliteTableInfo tableInfo, 
        CancellationToken cancellationToken)
    {
        try
        {
            using var columnsCommand = connection.CreateCommand();
            columnsCommand.CommandText = $"PRAGMA table_info({tableInfo.Name})";
            columnsCommand.CommandTimeout = QueryTimeoutSeconds;

            using var columnsReader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            
            while (await columnsReader.ReadAsync(cancellationToken))
            {
                var columnInfo = new SqliteColumnInfo
                {
                    Cid = columnsReader.GetInt32("cid"),
                    Name = columnsReader.GetString("name"),
                    Type = columnsReader.GetString("type"),
                    NotNull = columnsReader.GetBoolean("notnull"),
                    DefaultValue = columnsReader.IsDBNull("dflt_value") ? null : columnsReader.GetString("dflt_value"),
                    PrimaryKey = columnsReader.GetBoolean("pk")
                };

                tableInfo.Columns.Add(columnInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get column info for table {TableName}: {Message}", 
                tableInfo.Name, ex.Message);
        }
    }

    private async Task PopulateIndexInfoAsync(
        SqliteConnection connection, 
        SqliteTableInfo tableInfo, 
        CancellationToken cancellationToken)
    {
        try
        {
            using var indexCommand = connection.CreateCommand();
            indexCommand.CommandText = $"PRAGMA index_list({tableInfo.Name})";
            indexCommand.CommandTimeout = QueryTimeoutSeconds;

            using var indexReader = await indexCommand.ExecuteReaderAsync(cancellationToken);
            
            while (await indexReader.ReadAsync(cancellationToken))
            {
                var indexName = indexReader.GetString("name");
                var unique = indexReader.GetBoolean("unique");
                var origin = indexReader.GetString("origin");
                var partial = indexReader.GetBoolean("partial");

                var indexInfo = new SqliteIndexInfo
                {
                    Name = indexName,
                    Unique = unique,
                    Origin = origin,
                    Partial = partial,
                    Columns = new List<string>()
                };

                // Get index columns
                using var indexColumnsCommand = connection.CreateCommand();
                indexColumnsCommand.CommandText = $"PRAGMA index_info({indexName})";
                indexColumnsCommand.CommandTimeout = QueryTimeoutSeconds;

                using var indexColumnsReader = await indexColumnsCommand.ExecuteReaderAsync(cancellationToken);
                
                while (await indexColumnsReader.ReadAsync(cancellationToken))
                {
                    var columnName = indexColumnsReader.IsDBNull("name") ? 
                        $"expr_{indexColumnsReader.GetInt32("seqno")}" : 
                        indexColumnsReader.GetString("name");
                    indexInfo.Columns.Add(columnName);
                }

                tableInfo.Indexes.Add(indexInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get index info for table {TableName}: {Message}", 
                tableInfo.Name, ex.Message);
        }
    }

    private async Task PopulateRowCountAsync(
        SqliteConnection connection, 
        SqliteTableInfo tableInfo, 
        CancellationToken cancellationToken)
    {
        try
        {
            if (tableInfo.Type == "view")
            {
                tableInfo.EstimatedRowCount = "N/A (view)";
                return;
            }

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"SELECT COUNT(*) FROM {tableInfo.Name} LIMIT 100001";
            countCommand.CommandTimeout = QueryTimeoutSeconds;

            var count = await countCommand.ExecuteScalarAsync(cancellationToken);
            if (count != null && long.TryParse(count.ToString(), out var rowCount))
            {
                tableInfo.RowCount = Math.Min(rowCount, 100000);
                tableInfo.EstimatedRowCount = rowCount > 100000 ? ">100k rows" : rowCount.ToString();
            }
            else
            {
                tableInfo.EstimatedRowCount = "Unknown";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get row count for table {TableName}: {Message}", 
                tableInfo.Name, ex.Message);
            tableInfo.EstimatedRowCount = "Error";
        }
    }

    private async Task<QueryResult> ExecuteQueryOnLocalDatabaseAsync(
        string localPath, 
        string query, 
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var connectionString = $"Data Source={localPath};Mode=ReadOnly;Cache=Shared";
        
        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = QueryTimeoutSeconds;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columnNames = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columnNames[i] = reader.GetName(i);
            }

            var rows = new List<object[]>();
            int rowCount = 0;
            bool wasTruncated = false;

            while (await reader.ReadAsync(cancellationToken) && rowCount < MaxQueryRows)
            {
                var row = new object[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
                rowCount++;
            }

            // Check if there are more rows
            if (await reader.ReadAsync(cancellationToken))
            {
                wasTruncated = true;
            }

            return new QueryResult
            {
                IsSuccess = true,
                ColumnNames = columnNames,
                Rows = rows.ToArray(),
                RowCount = rowCount,
                ExecutionTime = stopwatch.Elapsed,
                WasTruncated = wasTruncated
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query: {Message}", ex.Message);
            
            return new QueryResult
            {
                IsSuccess = false,
                Error = ex.Message,
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }
}