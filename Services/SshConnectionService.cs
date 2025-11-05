using backlite.Models;
using backlite.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.Diagnostics;
using System.Text;

namespace backlite.Services;

public class SshConnectionService : ISshConnectionService, IDisposable
{
    private readonly ILogger<SshConnectionService> _logger;
    private readonly DbBackupManagerConfig _config;
    private readonly Dictionary<string, SshClient> _sshClients = new();
    private readonly Dictionary<string, SftpClient> _sftpClients = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);

    public SshConnectionService(
        ILogger<SshConnectionService> logger,
        IOptions<DbBackupManagerConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task<SshCommandResult> ExecuteCommandAsync(
        ServerConnection server,
        string command,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var key = GetConnectionKey(server);

        try
        {
            _logger.LogDebug("Executing command on {Server}: {Command}", 
                server.Name, MaskSensitiveData(command));

            var client = await GetOrCreateSshClientAsync(server, cancellationToken);
            
            using var sshCommand = client.CreateCommand(command);
            sshCommand.CommandTimeout = TimeSpan.FromSeconds(_config.SSH.TimeoutSeconds);

            var result = await Task.Run(async () =>
            {
                var asyncResult = sshCommand.BeginExecute();

                while (!asyncResult.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(100, cancellationToken);
                }

                sshCommand.EndExecute(asyncResult);

                return new SshCommandResult
                {
                    IsSuccess = sshCommand.ExitStatus == 0,
                    Output = sshCommand.Result,
                    Error = sshCommand.Error,
                    ExitCode = sshCommand.ExitStatus,
                    Duration = stopwatch.Elapsed
                };
            }, cancellationToken);

            _logger.LogDebug("Command completed on {Server} in {Duration}ms with exit code {ExitCode}", 
                server.Name, stopwatch.ElapsedMilliseconds, result.ExitCode);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Command execution cancelled on {Server}", server.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command on {Server}: {Message}", 
                server.Name, ex.Message);

            // Remove potentially broken connection
            await RemoveConnectionAsync(key);

            return new SshCommandResult
            {
                IsSuccess = false,
                Error = ex.Message,
                ExitCode = -1,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public async Task<bool> TestConnectionAsync(
        ServerConnection server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Testing connection to {Server} ({Host}:{Port})", 
                server.Name, server.Host, server.Port);

            var client = await GetOrCreateSshClientAsync(server, cancellationToken);
            
            // Test with a simple command
            var result = await ExecuteCommandAsync(server, "echo 'connection test'", cancellationToken);
            
            var isConnected = client.IsConnected && result.IsSuccess;
            
            _logger.LogInformation("Connection test to {Server}: {Result}", 
                server.Name, isConnected ? "Success" : "Failed");

            return isConnected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed for {Server}: {Message}", 
                server.Name, ex.Message);
            return false;
        }
    }

    public async Task<SftpTransferResult> DownloadFileAsync(
        ServerConnection server,
        string remotePath,
        string localPath,
        IProgress<SftpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Downloading file from {Server}: {RemotePath} -> {LocalPath}", 
                server.Name, remotePath, localPath);

            var client = await GetOrCreateSftpClientAsync(server, cancellationToken);

            // Ensure local directory exists
            var localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }

            long totalBytes = 0;
            long bytesTransferred = 0;

            // Get file size for progress reporting
            try
            {
                var fileInfo = client.GetAttributes(remotePath);
                totalBytes = fileInfo.Size;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get remote file size for {RemotePath}", remotePath);
            }

            await Task.Run(() =>
            {
                using var fileStream = File.Create(localPath);
                
                client.DownloadFile(remotePath, fileStream, bytesUploaded =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    bytesTransferred = (long)bytesUploaded;
                    progress?.Report(new SftpProgress
                    {
                        BytesTransferred = bytesTransferred,
                        TotalBytes = totalBytes,
                        Elapsed = stopwatch.Elapsed
                    });
                });
            }, cancellationToken);

            _logger.LogInformation("Successfully downloaded {BytesTransferred} bytes from {Server} in {Duration}ms", 
                bytesTransferred, server.Name, stopwatch.ElapsedMilliseconds);

            return new SftpTransferResult
            {
                IsSuccess = true,
                BytesTransferred = bytesTransferred,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("File download cancelled for {RemotePath} from {Server}", 
                remotePath, server.Name);
            
            // Clean up partial file
            try
            {
                if (File.Exists(localPath))
                    File.Delete(localPath);
            }
            catch { }
            
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {RemotePath} from {Server}: {Message}", 
                remotePath, server.Name, ex.Message);

            return new SftpTransferResult
            {
                IsSuccess = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public async Task<SftpTransferResult> UploadFileAsync(
        ServerConnection server,
        string localPath,
        string remotePath,
        IProgress<SftpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Uploading file to {Server}: {LocalPath} -> {RemotePath}", 
                server.Name, localPath, remotePath);

            if (!File.Exists(localPath))
            {
                throw new FileNotFoundException($"Local file not found: {localPath}");
            }

            var client = await GetOrCreateSftpClientAsync(server, cancellationToken);
            var fileInfo = new FileInfo(localPath);
            long bytesTransferred = 0;

            await Task.Run(() =>
            {
                using var fileStream = File.OpenRead(localPath);
                
                client.UploadFile(fileStream, remotePath, bytesUploaded =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    bytesTransferred = (long)bytesUploaded;
                    progress?.Report(new SftpProgress
                    {
                        BytesTransferred = bytesTransferred,
                        TotalBytes = fileInfo.Length,
                        Elapsed = stopwatch.Elapsed
                    });
                });
            }, cancellationToken);

            _logger.LogInformation("Successfully uploaded {BytesTransferred} bytes to {Server} in {Duration}ms", 
                bytesTransferred, server.Name, stopwatch.ElapsedMilliseconds);

            return new SftpTransferResult
            {
                IsSuccess = true,
                BytesTransferred = bytesTransferred,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("File upload cancelled for {LocalPath} to {Server}", 
                localPath, server.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {LocalPath} to {Server}: {Message}", 
                localPath, server.Name, ex.Message);

            return new SftpTransferResult
            {
                IsSuccess = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public async Task<SftpTransferResult> CopyFileAsync(
        ServerConnection sourceServer,
        string sourcePath,
        ServerConnection destinationServer,
        string destinationPath,
        IProgress<SftpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Copying file from {SourceServer}:{SourcePath} to {DestServer}:{DestPath}", 
                sourceServer.Name, sourcePath, destinationServer.Name, destinationPath);

            // If same server, use server-side copy
            if (sourceServer.Host == destinationServer.Host && sourceServer.Port == destinationServer.Port)
            {
                var copyCommand = $@"cp --preserve=timestamps,mode ""{sourcePath}"" ""{destinationPath}""";
                var result = await ExecuteCommandAsync(sourceServer, copyCommand, cancellationToken);
                
                if (result.IsSuccess)
                {
                    _logger.LogInformation("Server-side copy completed for {SourcePath} in {Duration}ms", 
                        sourcePath, stopwatch.ElapsedMilliseconds);

                    return new SftpTransferResult
                    {
                        IsSuccess = true,
                        Duration = stopwatch.Elapsed
                    };
                }
                else
                {
                    _logger.LogWarning("Server-side copy failed, falling back to stream copy: {Error}", 
                        result.Error);
                }
            }

            // Stream through application (download then upload)
            var tempPath = Path.GetTempFileName();
            long totalBytes = 0;

            try
            {
                // Download from source
                var downloadResult = await DownloadFileAsync(sourceServer, sourcePath, tempPath, 
                    new Progress<SftpProgress>(p =>
                    {
                        totalBytes = p.TotalBytes;
                        progress?.Report(new SftpProgress
                        {
                            BytesTransferred = p.BytesTransferred / 2, // First half is download
                            TotalBytes = p.TotalBytes,
                            Elapsed = stopwatch.Elapsed
                        });
                    }), cancellationToken);

                if (!downloadResult.IsSuccess)
                {
                    return downloadResult;
                }

                // Upload to destination
                var uploadResult = await UploadFileAsync(destinationServer, tempPath, destinationPath,
                    new Progress<SftpProgress>(p =>
                    {
                        progress?.Report(new SftpProgress
                        {
                            BytesTransferred = totalBytes / 2 + p.BytesTransferred / 2, // Second half is upload
                            TotalBytes = totalBytes,
                            Elapsed = stopwatch.Elapsed
                        });
                    }), cancellationToken);

                _logger.LogInformation("Stream copy completed for {SourcePath} in {Duration}ms", 
                    sourcePath, stopwatch.ElapsedMilliseconds);

                return uploadResult;
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
            _logger.LogError(ex, "Failed to copy file from {SourceServer} to {DestServer}: {Message}", 
                sourceServer.Name, destinationServer.Name, ex.Message);

            return new SftpTransferResult
            {
                IsSuccess = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<SshClient> GetOrCreateSshClientAsync(
        ServerConnection server, 
        CancellationToken cancellationToken)
    {
        var key = GetConnectionKey(server);

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_sshClients.TryGetValue(key, out var existingClient) && existingClient.IsConnected)
            {
                return existingClient;
            }

            // Remove any existing broken connection
            if (_sshClients.ContainsKey(key))
            {
                _sshClients[key].Dispose();
                _sshClients.Remove(key);
            }

            var connectionInfo = CreateConnectionInfo(server);
            var client = new SshClient(connectionInfo);

            await Task.Run(() => client.Connect(), cancellationToken);

            _sshClients[key] = client;
            
            _logger.LogInformation("Established SSH connection to {Server} ({Host}:{Port})", 
                server.Name, server.Host, server.Port);

            return client;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task<SftpClient> GetOrCreateSftpClientAsync(
        ServerConnection server, 
        CancellationToken cancellationToken)
    {
        var key = GetConnectionKey(server);

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_sftpClients.TryGetValue(key, out var existingClient) && existingClient.IsConnected)
            {
                return existingClient;
            }

            // Remove any existing broken connection
            if (_sftpClients.ContainsKey(key))
            {
                _sftpClients[key].Dispose();
                _sftpClients.Remove(key);
            }

            var connectionInfo = CreateConnectionInfo(server);
            var client = new SftpClient(connectionInfo);

            await Task.Run(() => client.Connect(), cancellationToken);

            _sftpClients[key] = client;
            
            _logger.LogInformation("Established SFTP connection to {Server} ({Host}:{Port})", 
                server.Name, server.Host, server.Port);

            return client;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private Renci.SshNet.ConnectionInfo CreateConnectionInfo(ServerConnection server)
    {
        var methods = new List<AuthenticationMethod>();

        if (server.AuthKind == AuthKind.Password && !string.IsNullOrEmpty(server.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(server.Username, server.Password));
        }
        else if (server.AuthKind == AuthKind.SshKey && !string.IsNullOrEmpty(server.PrivateKeyPem))
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKeyPem));
            var privateKey = new PrivateKeyFile(keyStream);
            methods.Add(new PrivateKeyAuthenticationMethod(server.Username, privateKey));
        }
        else
        {
            throw new InvalidOperationException($"No valid authentication method configured for server {server.Name}");
        }

        return new Renci.SshNet.ConnectionInfo(server.Host, server.Port, server.Username, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(_config.SSH.TimeoutSeconds)
        };
    }

    private async Task RemoveConnectionAsync(string key)
    {
        await _connectionSemaphore.WaitAsync();
        try
        {
            if (_sshClients.TryGetValue(key, out var sshClient))
            {
                sshClient.Dispose();
                _sshClients.Remove(key);
            }

            if (_sftpClients.TryGetValue(key, out var sftpClient))
            {
                sftpClient.Dispose();
                _sftpClients.Remove(key);
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private static string GetConnectionKey(ServerConnection server)
    {
        return $"{server.Host}:{server.Port}:{server.Username}";
    }

    private string MaskSensitiveData(string command)
    {
        if (!_config.Security.MaskSecretsInLogs)
            return command;

        // Simple masking - could be enhanced with more sophisticated patterns
        return command.Contains("password") || command.Contains("passwd") || command.Contains("secret")
            ? "[MASKED COMMAND]"
            : command;
    }

    public void Dispose()
    {
        foreach (var client in _sshClients.Values)
        {
            try
            {
                client.Dispose();
            }
            catch { }
        }

        foreach (var client in _sftpClients.Values)
        {
            try
            {
                client.Dispose();
            }
            catch { }
        }

        _connectionSemaphore.Dispose();
    }
}