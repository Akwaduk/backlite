using backlite.Models;

namespace backlite.Services;

public interface ISshConnectionService
{
    Task<SshCommandResult> ExecuteCommandAsync(
        ServerConnection server,
        string command,
        CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(
        ServerConnection server,
        CancellationToken cancellationToken = default);

    Task<SftpTransferResult> DownloadFileAsync(
        ServerConnection server,
        string remotePath,
        string localPath,
        IProgress<SftpProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<SftpTransferResult> UploadFileAsync(
        ServerConnection server,
        string localPath,
        string remotePath,
        IProgress<SftpProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<SftpTransferResult> CopyFileAsync(
        ServerConnection sourceServer,
        string sourcePath,
        ServerConnection destinationServer,
        string destinationPath,
        IProgress<SftpProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public class SshCommandResult
{
    public bool IsSuccess { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public TimeSpan Duration { get; set; }
}

public class SftpTransferResult
{
    public bool IsSuccess { get; set; }
    public string Error { get; set; } = string.Empty;
    public long BytesTransferred { get; set; }
    public TimeSpan Duration { get; set; }
}

public class SftpProgress
{
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public double PercentComplete => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100 : 0;
    public TimeSpan Elapsed { get; set; }
}