using backlite.Models;
using System.ComponentModel.DataAnnotations;

namespace backlite.Data;

public class ServerConnectionEntity
{
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;
    
    public int Port { get; set; } = 22;
    
    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;
    
    public AuthKind AuthKind { get; set; }
    
    public string? Password { get; set; }
    
    public string? PrivateKeyPem { get; set; }
    
    public bool UseSudoForDiscovery { get; set; }
    
    public string[] AllowedRoots { get; set; } = Array.Empty<string>();
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    public DateTimeOffset? LastConnectedAt { get; set; }

    public ServerConnection ToModel()
    {
        return new ServerConnection(
            Id,
            Name,
            Host,
            Port,
            Username,
            AuthKind,
            Password,
            PrivateKeyPem,
            UseSudoForDiscovery,
            AllowedRoots
        );
    }

    public static ServerConnectionEntity FromModel(ServerConnection model)
    {
        return new ServerConnectionEntity
        {
            Id = model.Id,
            Name = model.Name,
            Host = model.Host,
            Port = model.Port,
            Username = model.Username,
            AuthKind = model.AuthKind,
            Password = model.Password,
            PrivateKeyPem = model.PrivateKeyPem,
            UseSudoForDiscovery = model.UseSudoForDiscovery,
            AllowedRoots = model.AllowedRoots
        };
    }
}

public class BackupPlanEntity
{
    public Guid Id { get; set; }
    
    public Guid ServerId { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public string[] DbPaths { get; set; } = Array.Empty<string>();
    
    [Required]
    [MaxLength(500)]
    public string DestinationDir { get; set; } = string.Empty;
    
    public bool Compress { get; set; } = true;
    
    public int RetentionCount { get; set; } = 10;
    
    public string? CronExpression { get; set; }
    
    public bool Enabled { get; set; } = true;
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public BackupPlan ToModel()
    {
        return new BackupPlan(
            Id,
            ServerId,
            Name,
            DbPaths,
            DestinationDir,
            Compress,
            RetentionCount,
            CronExpression,
            Enabled
        );
    }

    public static BackupPlanEntity FromModel(BackupPlan model)
    {
        return new BackupPlanEntity
        {
            Id = model.Id,
            ServerId = model.ServerId,
            Name = model.Name,
            DbPaths = model.DbPaths,
            DestinationDir = model.DestinationDir,
            Compress = model.Compress,
            RetentionCount = model.RetentionCount,
            CronExpression = model.CronExpression,
            Enabled = model.Enabled
        };
    }
}

public class BackupRunEntity
{
    public Guid Id { get; set; }
    
    public Guid PlanId { get; set; }
    
    public DateTimeOffset Started { get; set; }
    
    public DateTimeOffset? Ended { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = string.Empty;
    
    public string? ArtifactPath { get; set; }
    
    public long? ArtifactSizeBytes { get; set; }
    
    public string[] LogLines { get; set; } = Array.Empty<string>();

    public BackupRun ToModel()
    {
        return new BackupRun(
            Id,
            PlanId,
            Started,
            Ended,
            Status,
            ArtifactPath,
            ArtifactSizeBytes,
            LogLines
        );
    }

    public static BackupRunEntity FromModel(BackupRun model)
    {
        return new BackupRunEntity
        {
            Id = model.Id,
            PlanId = model.PlanId,
            Started = model.Started,
            Ended = model.Ended,
            Status = model.Status,
            ArtifactPath = model.ArtifactPath,
            ArtifactSizeBytes = model.ArtifactSizeBytes,
            LogLines = model.LogLines
        };
    }
}

public class JobEntity
{
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Kind { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = string.Empty;
    
    public int ProgressPercent { get; set; }
    
    public DateTimeOffset Started { get; set; }
    
    public DateTimeOffset? Ended { get; set; }
    
    public string? Error { get; set; }
    
    public Guid? CorrelationId { get; set; }

    public Job ToModel()
    {
        return new Job(
            Id,
            Kind,
            DisplayName,
            Status,
            ProgressPercent,
            Started,
            Ended,
            Error,
            CorrelationId
        );
    }

    public static JobEntity FromModel(Job model)
    {
        return new JobEntity
        {
            Id = model.Id,
            Kind = model.Kind,
            DisplayName = model.DisplayName,
            Status = model.Status,
            ProgressPercent = model.ProgressPercent,
            Started = model.Started,
            Ended = model.Ended,
            Error = model.Error,
            CorrelationId = model.CorrelationId
        };
    }
}