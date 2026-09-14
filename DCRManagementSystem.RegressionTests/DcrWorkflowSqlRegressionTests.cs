using System.Security.Cryptography;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

// Explicit opt-in; always uses a fresh LocalDB database, never the application connection string.
public sealed class DcrLocalDbFactAttribute : FactAttribute
{
    public DcrLocalDbFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DCR_RUN_SQL_TESTS") != "1")
            Skip = "Set DCR_RUN_SQL_TESTS=1 to run isolated SQL Server LocalDB regression tests.";
    }
}

public sealed class DcrWorkflowSqlRegressionTests : IAsyncLifetime
{
    private readonly string _database = "DcrWorkflowTest_" + Guid.NewGuid().ToString("N");
    private readonly string _files = Path.Combine(Path.GetTempPath(), "DcrWorkflowTests", Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();
    private int _creator;
    private int _approver;
    private int _admin;
    private int _department;

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer($@"Server=(localdb)\MSSQLLocalDB;Database={_database};Integrated Security=true;TrustServerCertificate=true")
        .Options);

    private NotificationService Notifications => new(CreateDb, _settings);
    private DcrService Service => new(CreateDb, _settings, Notifications, new ApprovalRoutingService(),
        new ApprovalSignatureService(_settings), new PdfService(CreateDb, _settings));

    public async Task InitializeAsync()
    {
        _settings.StorageRoot = Path.Combine(_files, "Attachments");
        _settings.ApprovedPdfRoot = Path.Combine(_files, "Approved");
        _settings.Security.ApprovalSigningKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        _settings.InitializeRuntimeSecrets();
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
        DatabaseUpgradeService.Apply(db);
        var department = new Department { DepartmentCode = "TEST", DepartmentName = "Test" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        _department = department.Id;
        var creator = new User { Username = "creator", FullName = "Creator", Email = "creator@example.invalid", DepartmentId = _department };
        var approver = new User { Username = "approver", FullName = "Approver", Email = "approver@example.invalid", DepartmentId = _department };
        var admin = new User { Username = "admin", FullName = "Admin", Email = "admin@example.invalid", Role = RoleNames.Administrator };
        db.Users.AddRange(creator, approver, admin);
        db.WorkflowStageTemplates.Add(new WorkflowStageTemplate { StageNumber = 0, StageCode = WorkflowStageCodes.Submission, StageName = "Submission", IsActive = true });
        await db.SaveChangesAsync();
        _creator = creator.UserId;
        _approver = approver.UserId;
        _admin = admin.UserId;
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateDb();
        await db.Database.EnsureDeletedAsync();
        if (Directory.Exists(_files)) Directory.Delete(_files, recursive: true);
    }

    private DcrEditModel NewDraft(Guid? token = null) => new()
    {
        CreationToken = token ?? Guid.NewGuid(), Title = "Test request", RequestingDepartmentId = _department,
        Program = "Test product", BuildStage = "Test build", ProblemDescription = "Test problem", Solution = "Test solution",
        Parts = [new() { ChangeType = "Change", PartNumber = "TEST-1", PartName = "Test part", Quantity = "1" }],
        ImpactedDepartments = [new() { DepartmentId = _department }],
        ApprovalPlan = [new() { LevelNumber = 1, LevelName = "Review", ApproverId = _approver, Sequence = 1 }]
    };

    private async Task<int> SaveAsync(DcrEditModel model) => await Service.SaveDraftAsync(model, _creator, false);
    private Task<SubmitDcrResult> SubmitAsync(DcrEditModel model) =>
        Service.SubmitForApiAsync(model.Id!.Value, _creator, false, model.RowVersion, AuthMethods.InternalPassword, "Test");
    private async Task<DecisionProcessResult> DecideAsync(int id, string decision)
    {
        var model = await Service.LoadEditDataAsync(id);
        var authentication = await new AuthService(CreateDb, _settings)
            .AuthenticateDecisionForSessionAsync(_approver, "Test session");
        return await Service.ProcessDecisionAsync(id, _approver, decision, "Test reason",
            authentication, model.RowVersion);
    }

    [DcrLocalDbFact]
    public async Task LoggedInApproverCanDecideWithoutPasswordEvenWithLegacySettingEnabled()
    {
        _settings.Security.RequirePasswordReauthenticationForDecision = true;
        var authService = new AuthService(CreateDb, _settings);
        var model = NewDraft();
        var id = await SaveAsync(model);
        await SubmitAsync(model);
        var submitted = await Service.LoadEditDataAsync(id);
        await using var db = CreateDb();
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.UserId == _approver);
        try
        {
            foreach (var loginMethod in new[] { AuthMethods.InternalPassword, AuthMethods.WindowsIntegrated, AuthMethods.Ldap })
            {
                CurrentUser.Set(user, loginMethod, "Test identity");
                var session = await authService.AuthenticateDecisionAsync(_approver);
                Assert.Equal(AuthMethods.ApplicationSession, session.AuthMethod);
                Assert.Equal("Test identity", session.WindowsIdentity);
            }

            var authentication = await authService.AuthenticateDecisionAsync(_approver);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ProcessDecisionAsync(
                id, _approver, ApprovalDecisions.Returned, "", authentication, submitted.RowVersion));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ProcessDecisionAsync(
                id, _approver, ApprovalDecisions.Rejected, "", authentication, submitted.RowVersion));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.ProcessDecisionAsync(
                id, _creator, ApprovalDecisions.Approved, "", authentication, submitted.RowVersion));

            var result = await Service.ProcessDecisionAsync(id, _approver, ApprovalDecisions.Approved, "",
                authentication, submitted.RowVersion);
            Assert.Equal(RequestStatuses.Approved, result.RequestStatus);
            var flow = await db.DCRApprovalFlows.AsNoTracking().SingleAsync(x =>
                x.RequestId == id && x.Decision == ApprovalDecisions.Approved);
            Assert.Equal(_approver, flow.ApproverId);
            Assert.Equal(AuthMethods.ApplicationSession, flow.AuthMethod);
            Assert.False(string.IsNullOrWhiteSpace(flow.SignatureHash));
        }
        finally { CurrentUser.Clear(); }
    }

    [DcrLocalDbFact]
    public async Task SessionAuthenticationRejectsLoggedOutMismatchedAndDisabledUsers()
    {
        var authService = new AuthService(CreateDb, _settings);
        CurrentUser.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.AuthenticateDecisionAsync(_approver));
        await using var db = CreateDb();
        var user = await db.Users.SingleAsync(x => x.UserId == _approver);
        try
        {
            CurrentUser.Set(user);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.AuthenticateDecisionAsync(_creator));
            user.IsActive = false;
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.AuthenticateDecisionAsync(_approver));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.AuthenticateDecisionForSessionAsync(_approver, "API session"));
        }
        finally { CurrentUser.Clear(); }
    }

    [DcrLocalDbFact]
    public async Task ConcurrentCreateAndRetryUseOnePersistentIdentity()
    {
        var token = Guid.NewGuid();
        var ids = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            try { return await SaveAsync(NewDraft(token)); }
            catch (DcrAlreadyCreatedException ex) { return ex.RequestId; }
        }));
        Assert.Single(ids.Distinct());
        var changedReplay = NewDraft(token);
        changedReplay.Title = "Must not overwrite original";
        var replay = await Assert.ThrowsAsync<DcrAlreadyCreatedException>(() => SaveAsync(changedReplay));
        Assert.Equal(ids[0], replay.RequestId);
        await using var db = CreateDb();
        Assert.Equal(1, await db.DCRRequests.CountAsync());
        Assert.Equal("Test request", (await db.DCRRequests.SingleAsync()).Title);
        Assert.Equal(1, (await db.DcrNumberSequences.SingleAsync()).LastNumber);
    }

    [DcrLocalDbFact]
    public async Task ConcurrentSubmitCreatesOneWorkflow()
    {
        var model = NewDraft();
        await SaveAsync(model);
        var accepted = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { await SubmitAsync(model); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Single(accepted.Where(x => x));
        await using var db = CreateDb();
        Assert.Equal(1, await db.DCRRequests.CountAsync());
        Assert.Equal(1, await db.DCRApprovalFlows.CountAsync(x => x.Decision == ApprovalDecisions.Submitted));
        Assert.Equal(1, await db.DCRApprovalFlows.CountAsync(x => x.Decision == ApprovalDecisions.Pending));
    }

    [DcrLocalDbFact]
    public async Task ReturnedDcrRemainsReturnedWhileEditingAndCanBeResubmitted()
    {
        var model = NewDraft();
        var id = await SaveAsync(model);
        await SubmitAsync(model);
        var outcome = await DecideAsync(id, ApprovalDecisions.Returned);
        Assert.Equal(RequestStatuses.Returned, outcome.RequestStatus);
        Assert.Single(outcome.Notification.Details);
        Assert.Contains("creator@example.invalid", outcome.Notification.Details[0]);
        var returned = await Service.LoadEditDataAsync(id);
        Assert.Equal(2, returned.RevisionNo);
        returned.Title = "Supplemented information";
        await SaveAsync(returned);
        Assert.Equal(RequestStatuses.Returned, returned.Status);
        var file = Path.Combine(_files, "test.txt");
        Directory.CreateDirectory(_files);
        await File.WriteAllTextAsync(file, "Test attachment");
        var attachments = new AttachmentService(CreateDb, _settings);
        await attachments.UploadAsync(id, file, AttachmentTypes.General, _creator, false);
        var attachment = Assert.Single(await Service.GetAttachmentsAsync(id));
        await attachments.DeleteAsync(attachment.Id, _creator, false);
        await SubmitAsync(returned);
        Assert.Equal(RequestStatuses.InApproval, (await Service.LoadEditDataAsync(id)).Status);
        var rejected = await DecideAsync(id, ApprovalDecisions.Rejected);
        Assert.Equal(RequestStatuses.Rejected, rejected.RequestStatus);
        Assert.Single(rejected.Notification.Details);
        Assert.Contains("creator@example.invalid", rejected.Notification.Details[0]);
    }

    [DcrLocalDbFact]
    public async Task RepeatedReturnQueuesOneCreatorEmailPerDecisionWithoutTeamsBroadcast()
    {
        var model = NewDraft();
        var id = await SaveAsync(model);
        await SubmitAsync(model);
        await DecideAsync(id, ApprovalDecisions.Returned);
        _settings.Email.Enabled = true;
        _settings.Email.ClientId = "test-client-no-worker";
        _settings.Teams.Enabled = true;
        _settings.Teams.WebhookUrl = "http://127.0.0.1:1/never-broadcast";
        await Notifications.SendOutcomeAsync(id, NotificationTypes.Returned, "First return");
        await Notifications.SendOutcomeAsync(id, NotificationTypes.Returned, "Duplicate dispatch");
        _settings.Email.Enabled = false;
        _settings.Teams.Enabled = false;
        await SubmitAsync(await Service.LoadEditDataAsync(id));
        await DecideAsync(id, ApprovalDecisions.Returned);
        _settings.Email.Enabled = true;
        _settings.Teams.Enabled = true;
        await Notifications.SendOutcomeAsync(id, NotificationTypes.Returned, "Second return");
        await using var db = CreateDb();
        var mail = await db.EmailOutbox.Where(x => x.NotificationType == NotificationTypes.Returned).ToListAsync();
        Assert.Equal(2, mail.Count);
        Assert.All(mail, x => Assert.Equal("creator@example.invalid", x.Recipient));
        Assert.Equal(2, mail.Select(x => x.ApprovalFlowId).Distinct().Count());
        Assert.False(await db.DCRNotificationLogs.AnyAsync(x => x.Recipient == "MS Teams Webhook"));
    }

    [DcrLocalDbFact]
    public async Task AdministratorDeletesSubmittedAndApprovedDcrWithAuditAndCancelledMail()
    {
        foreach (var status in new[] { RequestStatuses.InApproval, RequestStatuses.Approved })
        {
            var model = NewDraft();
            var id = await SaveAsync(model);
            await SubmitAsync(model);
            await using (var db = CreateDb())
            {
                var request = await db.DCRRequests.SingleAsync(x => x.Id == id);
                request.Status = status;
                db.EmailOutbox.Add(new EmailOutboxItem { RequestId = id, Recipient = "approver@example.invalid", Status = EmailOutboxStatuses.Pending });
                await db.SaveChangesAsync();
            }
            var admin = new AdminService(CreateDb, _settings);
            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.DeleteDcrAsync(id, _creator, false));
            await admin.DeleteDcrAsync(id, _admin, true);
            await using var verify = CreateDb();
            Assert.False(await verify.DCRRequests.AnyAsync(x => x.Id == id));
            Assert.False(await verify.DCRApprovalFlows.AnyAsync(x => x.RequestId == id));
            Assert.True(await verify.DCRDeletionLogs.AnyAsync(x => x.OriginalRequestId == id));
            Assert.All(await verify.EmailOutbox.Where(x => x.RequestId == id).ToListAsync(), x => Assert.Equal(EmailOutboxStatuses.Cancelled, x.Status));
        }
    }

    [DcrLocalDbFact]
    public async Task UpgradeReclassifiesLegacyReturnsAndPreservesNeverSubmittedDrafts()
    {
        var draft = NewDraft();
        await SaveAsync(draft);
        var legacy = NewDraft();
        await SaveAsync(legacy);
        await SubmitAsync(legacy);
        await DecideAsync(legacy.Id!.Value, ApprovalDecisions.Returned);
        await using var db = CreateDb();
        // Recreate the pre-upgrade schema and a return identified only by its approval history.
        await db.Database.ExecuteSqlRawAsync("UPDATE DCRRequests SET Status='Draft', ReturnedDate=NULL WHERE Status='Returned'; DROP INDEX UX_DCRRequests_CreatedBy_CreationToken ON DCRRequests; ALTER TABLE DCRRequests DROP COLUMN CreationToken; DELETE FROM DCRSchemaVersions WHERE Version='2026.09.06.1';");
        DatabaseUpgradeService.Apply(db);
        DatabaseUpgradeService.Apply(db);
        Assert.Equal(RequestStatuses.Draft, (await db.DCRRequests.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status);
        Assert.Equal(RequestStatuses.Returned, (await db.DCRRequests.AsNoTracking().SingleAsync(x => x.Id == legacy.Id)).Status);
    }
}
