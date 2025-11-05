namespace backlite.Models;

public class DbBackupManagerConfig
{
    public SshConfig SSH { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public JobsConfig Jobs { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public BackupConfig Backup { get; set; } = new();
}

public class SshConfig
{
    public int DefaultPort { get; set; } = 22;
    public int TimeoutSeconds { get; set; } = 30;
    public int KeepAliveIntervalSeconds { get; set; } = 5;
    public int MaxRetries { get; set; } = 3;
}

public class SecurityConfig
{
    public string[] GlobalAllowedRoots { get; set; } = Array.Empty<string>();
    public bool AllowSudo { get; set; } = true;
    public bool MaskSecretsInLogs { get; set; } = true;
}

public class JobsConfig
{
    public int MaxConcurrentJobs { get; set; } = 3;
    public int JobTimeoutMinutes { get; set; } = 60;
    public int LogRetentionDays { get; set; } = 30;
}

public class StorageConfig
{
    public string TempWorkspaceDirectory { get; set; } = "./temp";
    public string BackupRootDirectory { get; set; } = "./backups";
    public int MaxBackupSizeGB { get; set; } = 10;
}

public class BackupConfig
{
    public int DefaultCompressionLevel { get; set; } = 6;
    public int DefaultRetentionCount { get; set; } = 10;
    public bool VerifyChecksums { get; set; } = true;
    public bool PreserveTimestamps { get; set; } = true;
}