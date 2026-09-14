using System.Text.Json;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DCRManagementSystem.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserBusinessUnitAssignment> UserBusinessUnitAssignments => Set<UserBusinessUnitAssignment>();
    public DbSet<UserDepartmentAssignment> UserDepartmentAssignments => Set<UserDepartmentAssignment>();
    public DbSet<RoleDefinition> Roles => Set<RoleDefinition>();
    public DbSet<ProductLineDefinition> ProductLines => Set<ProductLineDefinition>();
    public DbSet<PartChangeTypeDefinition> PartChangeTypes => Set<PartChangeTypeDefinition>();
    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DCRRequest> DCRRequests => Set<DCRRequest>();
    public DbSet<DCRPart> DCRParts => Set<DCRPart>();
    public DbSet<DCRImpactedDepartment> DCRImpactedDepartments => Set<DCRImpactedDepartment>();
    public DbSet<DCRApprovalFlow> DCRApprovalFlows => Set<DCRApprovalFlow>();
    public DbSet<DCRApprovalPlanEntry> DCRApprovalPlanEntries => Set<DCRApprovalPlanEntry>();
    public DbSet<UserApprovalPlanEntry> UserApprovalPlanEntries => Set<UserApprovalPlanEntry>();
    public DbSet<ApprovalPlanTemplate> ApprovalPlanTemplates => Set<ApprovalPlanTemplate>();
    public DbSet<ApprovalPlanTemplateEntry> ApprovalPlanTemplateEntries => Set<ApprovalPlanTemplateEntry>();
    public DbSet<DCRAttachment> DCRAttachments => Set<DCRAttachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<WorkflowStageTemplate> WorkflowStageTemplates => Set<WorkflowStageTemplate>();
    public DbSet<ApprovalMatrixRule> ApprovalMatrixRules => Set<ApprovalMatrixRule>();
    public DbSet<DCRNotificationLog> DCRNotificationLogs => Set<DCRNotificationLog>();
    public DbSet<EmailOutboxItem> EmailOutbox => Set<EmailOutboxItem>();
    public DbSet<DCRDeletionLog> DCRDeletionLogs => Set<DCRDeletionLog>();
    public DbSet<DcrNumberSequence> DcrNumberSequences => Set<DcrNumberSequence>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<RoleDefinition>().HasIndex(x => x.RoleName).IsUnique();
        modelBuilder.Entity<ProductLineDefinition>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<PartChangeTypeDefinition>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<BusinessUnit>().HasIndex(x => x.UnitCode).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(x => x.WindowsAccount)
            .IsUnique()
            .HasFilter("[WindowsAccount] <> ''");
        modelBuilder.Entity<Department>().HasIndex(x => x.DepartmentCode).IsUnique();
        modelBuilder.Entity<DCRRequest>().HasIndex(x => x.DCRNumber).IsUnique();
        modelBuilder.Entity<DCRRequest>()
            .HasIndex(x => new { x.CreatedBy, x.CreationToken })
            .IsUnique()
            .HasFilter("[CreationToken] IS NOT NULL")
            .HasDatabaseName("UX_DCRRequests_CreatedBy_CreationToken");
        modelBuilder.Entity<DCRRequest>().HasIndex(x => new { x.Status, x.CreatedDate });
        modelBuilder.Entity<DCRRequest>().HasIndex(x => new { x.CreatedBy, x.CreatedDate });
        modelBuilder.Entity<DCRRequest>().HasIndex(x => new { x.RequestOwnerId, x.CreatedDate });
        modelBuilder.Entity<WorkflowStageTemplate>().HasIndex(x => x.StageNumber).IsUnique();
        modelBuilder.Entity<ApprovalPlanTemplate>()
            .HasIndex(x => new { x.Rank, x.Name })
            .IsUnique();
        modelBuilder.Entity<SystemSetting>().HasKey(x => x.Key);
        // Year is supplied by the application, matching Database/CreateDatabase.sql.
        modelBuilder.Entity<DcrNumberSequence>().Property(x => x.Year).ValueGeneratedNever();

        modelBuilder.Entity<User>()
            .HasOne(x => x.Department)
            .WithMany()
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasOne(x => x.DirectManager)
            .WithMany()
            .HasForeignKey(x => x.DirectManagerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasOne(x => x.BusinessUnit)
            .WithMany()
            .HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserBusinessUnitAssignment>()
            .HasKey(x => new { x.UserId, x.BusinessUnitId });
        modelBuilder.Entity<UserBusinessUnitAssignment>()
            .HasOne(x => x.User)
            .WithMany(x => x.BusinessUnitAssignments)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserBusinessUnitAssignment>()
            .HasOne(x => x.BusinessUnit)
            .WithMany()
            .HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserBusinessUnitAssignment>()
            .HasOne(x => x.ReportsToUser)
            .WithMany()
            .HasForeignKey(x => x.ReportsToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserDepartmentAssignment>()
            .HasKey(x => new { x.UserId, x.DepartmentId });
        modelBuilder.Entity<UserDepartmentAssignment>()
            .HasOne(x => x.User)
            .WithMany(x => x.DepartmentAssignments)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserDepartmentAssignment>()
            .HasOne(x => x.Department)
            .WithMany()
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserDepartmentAssignment>()
            .HasOne(x => x.ReportsToUser)
            .WithMany()
            .HasForeignKey(x => x.ReportsToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BusinessUnit>()
            .HasOne(x => x.DirectorUser)
            .WithMany()
            .HasForeignKey(x => x.DirectorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BusinessUnit>()
            .HasOne(x => x.ParentBusinessUnit)
            .WithMany(x => x.ChildBusinessUnits)
            .HasForeignKey(x => x.ParentBusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Department>()
            .HasOne(x => x.BusinessUnit)
            .WithMany()
            .HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Department>()
            .HasOne(x => x.ManagerUser)
            .WithMany()
            .HasForeignKey(x => x.ManagerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Department>()
            .HasOne(x => x.DirectorUser)
            .WithMany()
            .HasForeignKey(x => x.DirectorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRRequest>()
            .HasOne(x => x.RequestingDepartment)
            .WithMany()
            .HasForeignKey(x => x.RequestingDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRRequest>()
            .HasOne(x => x.RequestOwner)
            .WithMany()
            .HasForeignKey(x => x.RequestOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRRequest>()
            .HasOne(x => x.Creator)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRPart>()
            .HasOne(x => x.Request)
            .WithMany(x => x.Parts)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DCRImpactedDepartment>()
            .HasOne(x => x.Request)
            .WithMany(x => x.ImpactedDepartments)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DCRImpactedDepartment>()
            .HasOne(x => x.Department)
            .WithMany()
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<DCRApprovalPlanEntry>()
            .HasOne(x => x.Request)
            .WithMany(x => x.ApprovalPlan)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DCRApprovalPlanEntry>()
            .HasOne(x => x.Approver)
            .WithMany()
            .HasForeignKey(x => x.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserApprovalPlanEntry>()
            .HasOne(x => x.OwnerUser)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserApprovalPlanEntry>()
            .HasOne(x => x.Approver)
            .WithMany()
            .HasForeignKey(x => x.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalPlanTemplateEntry>()
            .HasOne(x => x.Template)
            .WithMany(x => x.Entries)
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApprovalPlanTemplateEntry>()
            .HasOne(x => x.Approver)
            .WithMany()
            .HasForeignKey(x => x.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRApprovalFlow>()
            .HasOne(x => x.Request)
            .WithMany(x => x.ApprovalFlow)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DCRApprovalFlow>()
            .HasOne(x => x.Approver)
            .WithMany()
            .HasForeignKey(x => x.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRApprovalFlow>()
            .HasOne(x => x.Department)
            .WithMany()
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRAttachment>()
            .HasOne(x => x.Request)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DCRAttachment>()
            .HasOne(x => x.Uploader)
            .WithMany()
            .HasForeignKey(x => x.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>()
            .HasOne(x => x.Request)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AuditLog>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalMatrixRule>()
            .HasOne(x => x.RequestingDepartment)
            .WithMany()
            .HasForeignKey(x => x.RequestingDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalMatrixRule>()
            .HasOne(x => x.TargetDepartment)
            .WithMany()
            .HasForeignKey(x => x.TargetDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalMatrixRule>()
            .HasOne(x => x.ApproverUser)
            .WithMany()
            .HasForeignKey(x => x.ApproverUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRNotificationLog>()
            .HasOne(x => x.Request)
            .WithMany()
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DCRNotificationLog>()
            .HasOne(x => x.ApprovalFlow)
            .WithMany()
            .HasForeignKey(x => x.ApprovalFlowId)
            .OnDelete(DeleteBehavior.NoAction);


        modelBuilder.Entity<EmailOutboxItem>()
            .Property(x => x.RowVersion)
            .IsRowVersion();

        modelBuilder.Entity<DCRDeletionLog>()
            .HasOne(x => x.Deleter)
            .WithMany()
            .HasForeignKey(x => x.DeletedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DCRRequest>()
            .Property(x => x.RowVersion)
            .IsRowVersion();

        modelBuilder.Entity<DCRImpactedDepartment>()
            .HasIndex(x => new { x.RequestId, x.DepartmentId })
            .IsUnique();


        modelBuilder.Entity<DCRApprovalPlanEntry>()
            .HasIndex(x => new { x.RequestId, x.ApproverId })
            .IsUnique();

        modelBuilder.Entity<DCRApprovalPlanEntry>()
            .HasIndex(x => new { x.RequestId, x.LevelNumber, x.Sequence });

        modelBuilder.Entity<UserApprovalPlanEntry>()
            .HasIndex(x => new { x.OwnerUserId, x.Rank, x.ApproverId })
            .IsUnique();

        modelBuilder.Entity<UserApprovalPlanEntry>()
            .HasIndex(x => new { x.OwnerUserId, x.Rank, x.LevelNumber, x.Sequence });

        modelBuilder.Entity<ApprovalPlanTemplateEntry>()
            .HasIndex(x => new { x.TemplateId, x.ApproverId })
            .IsUnique();

        modelBuilder.Entity<ApprovalPlanTemplateEntry>()
            .HasIndex(x => new { x.TemplateId, x.LevelNumber, x.Sequence });

        modelBuilder.Entity<DCRApprovalFlow>()
            .HasIndex(x => new { x.RequestId, x.RevisionNo, x.StageNumber, x.Sequence });

        modelBuilder.Entity<DCRApprovalFlow>()
            .HasIndex(x => new { x.ApproverId, x.Decision, x.RequestId, x.RevisionNo, x.StageNumber });

        modelBuilder.Entity<ApprovalMatrixRule>()
            .HasIndex(x => new
            {
                x.StageCode,
                x.RequestingDepartmentId,
                x.TargetDepartmentId,
                x.Priority
            });

        modelBuilder.Entity<DCRNotificationLog>()
            .HasIndex(x => new { x.RequestId, x.ApprovalFlowId, x.NotificationType, x.SentAt });


        modelBuilder.Entity<EmailOutboxItem>()
            .HasIndex(x => new { x.Status, x.NextAttemptAt, x.CreatedAt });

        modelBuilder.Entity<EmailOutboxItem>()
            .HasIndex(x => new { x.RequestId, x.NotificationType, x.CreatedAt });

        modelBuilder.Entity<DCRDeletionLog>()
            .HasIndex(x => new { x.DCRNumber, x.DeletedAt });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (!ChangeTracker.HasChanges())
            return base.SaveChanges(acceptAllChangesOnSuccess);

        var captures = CapturePendingAudits();
        if (captures.Count == 0)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        // With acceptAllChangesOnSuccess=false, write the automatic audit rows in the same
        // SaveChanges call so EF keeps the caller's entity states untouched as requested.
        // Database-generated keys on newly-added entities may still be represented by their
        // pre-save value in this uncommon mode; normal application calls use the true/default path.
        if (!acceptAllChangesOnSuccess)
        {
            AppendAutomaticAudits(captures);
            return base.SaveChanges(false);
        }

        var ownsTransaction = Database.CurrentTransaction is null;
        using var tx = ownsTransaction ? Database.BeginTransaction() : null;
        try
        {
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            AppendAutomaticAudits(captures);
            if (ChangeTracker.Entries<AuditLog>().Any(x => x.State == EntityState.Added))
                base.SaveChanges(acceptAllChangesOnSuccess);
            tx?.Commit();
            return result;
        }
        catch
        {
            tx?.Rollback();
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (!ChangeTracker.HasChanges())
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        var captures = CapturePendingAudits();
        if (captures.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (!acceptAllChangesOnSuccess)
        {
            AppendAutomaticAudits(captures);
            return await base.SaveChangesAsync(false, cancellationToken);
        }

        var ownsTransaction = Database.CurrentTransaction is null;
        await using var tx = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            AppendAutomaticAudits(captures);
            if (ChangeTracker.Entries<AuditLog>().Any(x => x.State == EntityState.Added))
                await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (tx is not null)
                await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (tx is not null)
                await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private List<AuditCapture> CapturePendingAudits()
    {
        ChangeTracker.DetectChanges();
        var captures = new List<AuditCapture>();
        foreach (var entry in ChangeTracker.Entries().Where(x =>
                     x.Entity is not AuditLog &&
                     x.Entity is not DCRNotificationLog &&
                     x.Entity is not EmailOutboxItem &&
                     !(x.Entity is SystemSetting setting && setting.Key.StartsWith("MailWorker.", StringComparison.Ordinal)) &&
                     x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var changedProperties = entry.State == EntityState.Modified
                ? entry.Properties.Where(x => x.IsModified).Select(x => x.Metadata.Name).ToHashSet(StringComparer.Ordinal)
                : entry.Properties.Select(x => x.Metadata.Name).ToHashSet(StringComparer.Ordinal);

            if (changedProperties.Count == 0)
                continue;

            captures.Add(new AuditCapture
            {
                Entry = entry,
                State = entry.State,
                PropertyNames = changedProperties,
                OldValue = entry.State == EntityState.Added ? string.Empty : SerializeProperties(entry, changedProperties, useOriginal: true),
                RequestIdBeforeSave = TryGetRequestId(entry.Entity),
                EntityIdBeforeSave = GetPrimaryKeyText(entry)
            });
        }
        return captures;
    }

    private void AppendAutomaticAudits(IEnumerable<AuditCapture> captures)
    {
        foreach (var capture in captures)
        {
            var entry = capture.Entry;
            var requestId = capture.RequestIdBeforeSave ?? TryGetRequestId(entry.Entity);
            var newValue = capture.State == EntityState.Deleted
                ? string.Empty
                : SerializeProperties(entry, capture.PropertyNames, useOriginal: false);

            AuditLogs.Add(new AuditLog
            {
                RequestId = requestId,
                UserId = CurrentUser.User?.UserId,
                Action = capture.State switch
                {
                    EntityState.Added => "AUTO INSERT",
                    EntityState.Modified => "AUTO UPDATE",
                    EntityState.Deleted => "AUTO DELETE",
                    _ => "AUTO CHANGE"
                },
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = capture.State == EntityState.Deleted && !string.IsNullOrWhiteSpace(capture.EntityIdBeforeSave)
                    ? capture.EntityIdBeforeSave
                    : GetPrimaryKeyText(entry),
                OldValue = capture.OldValue,
                NewValue = newValue,
                ComputerName = AuditEnvironment.ComputerName,
                IpAddress = AuditEnvironment.LocalIpAddress,
                WindowsIdentity = AuditEnvironment.WindowsIdentityName,
                SessionId = CurrentUser.SessionId,
                CreatedAt = DateTime.Now
            });
        }
    }

    private static string SerializeProperties(EntityEntry entry, IReadOnlyCollection<string> propertyNames, bool useOriginal)
    {
        var dictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entry.Properties.Where(x => propertyNames.Contains(x.Metadata.Name)))
        {
            var name = property.Metadata.Name;
            object? value = useOriginal ? property.OriginalValue : property.CurrentValue;
            dictionary[name] = RedactValue(entry.Entity, name, value);
        }
        return JsonSerializer.Serialize(dictionary);
    }

    private static object? RedactValue(object entity, string propertyName, object? value)
    {
        if (entity is User && propertyName.Equals(nameof(User.PasswordHash), StringComparison.Ordinal))
            return "***REDACTED***";
        if (entity is SystemSetting setting && propertyName.Equals(nameof(SystemSetting.Value), StringComparison.Ordinal) &&
            (setting.Key.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
             setting.Key.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
             setting.Key.Contains("SigningKey", StringComparison.OrdinalIgnoreCase) ||
             setting.Key.Contains("Webhook", StringComparison.OrdinalIgnoreCase)))
            return "***REDACTED***";
        if (value is byte[] bytes)
            return Convert.ToHexString(bytes);
        return value;
    }

    private static string GetPrimaryKeyText(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;
        return string.Join(";", key.Properties.Select(p => $"{p.Name}={entry.Property(p.Name).CurrentValue}"));
    }

    private static int? TryGetRequestId(object entity) => entity switch
    {
        DCRRequest x => x.Id > 0 ? x.Id : null,
        DCRPart x => x.RequestId > 0 ? x.RequestId : null,
        DCRImpactedDepartment x => x.RequestId > 0 ? x.RequestId : null,
        DCRApprovalFlow x => x.RequestId > 0 ? x.RequestId : null,
        DCRApprovalPlanEntry x => x.RequestId > 0 ? x.RequestId : null,
        DCRAttachment x => x.RequestId > 0 ? x.RequestId : null,
        DCRNotificationLog x => x.RequestId > 0 ? x.RequestId : null,
        _ => null
    };

    private sealed class AuditCapture
    {
        public required EntityEntry Entry { get; init; }
        public EntityState State { get; init; }
        public required HashSet<string> PropertyNames { get; init; }
        public string OldValue { get; init; } = string.Empty;
        public int? RequestIdBeforeSave { get; init; }
        public string EntityIdBeforeSave { get; init; } = string.Empty;
    }

}
