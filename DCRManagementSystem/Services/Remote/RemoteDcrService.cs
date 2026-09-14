using System.Reflection;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteDcrService : IDcrService
{
    private readonly RemoteApiClient _api;

    public RemoteDcrService(Helpers.AppSettings settings) => _api = new RemoteApiClient(settings);

    public Task<List<Department>> GetActiveDepartmentsAsync() =>
        _api.GetAsync<List<Department>>("api/dcr/reference/departments");

    public Task<List<ProductLineDefinition>> GetActiveProductLinesAsync() =>
        _api.GetAsync<List<ProductLineDefinition>>("api/dcr/reference/product-lines");

    public Task<List<PartChangeTypeDefinition>> GetActivePartChangeTypesAsync() =>
        _api.GetAsync<List<PartChangeTypeDefinition>>("api/dcr/reference/change-types");

    public Task<List<ApproverSearchItem>> SearchApproversAsync(string searchText, int maxResults = 40) =>
        _api.GetAsync<List<ApproverSearchItem>>($"api/dcr/approvers/search?q={Uri.EscapeDataString(searchText ?? string.Empty)}&maxResults={Math.Clamp(maxResults, 1, 100)}");

    public Task<ApprovalPlanSuggestionResult> GetSuggestedApprovalPlanAsync(int userId, string rank) =>
        _api.GetAsync<ApprovalPlanSuggestionResult>($"api/dcr/approval-plan/suggested?rank={Uri.EscapeDataString(DcrRanks.Normalize(rank))}");

    public Task<List<ApprovalPlanTemplateEditModel>> GetApprovalPlanTemplatesAsync(string rank) =>
        _api.GetAsync<List<ApprovalPlanTemplateEditModel>>($"api/dcr/approval-plan/templates?rank={Uri.EscapeDataString(DcrRanks.Normalize(rank))}");

    public Task<List<ApprovalPlanEditItem>> GetSavedApprovalPlanAsync(int userId, string rank) =>
        _api.GetAsync<List<ApprovalPlanEditItem>>($"api/dcr/approval-plan/saved?rank={Uri.EscapeDataString(DcrRanks.Normalize(rank))}");

    public Task<List<ApprovalPlanEditItem>> SaveUserApprovalPlanAsync(
        int userId,
        string rank,
        IEnumerable<ApprovalPlanEditItem> approvalPlan) =>
        _api.PostAsync<ApiSaveUserApprovalPlanRequest, List<ApprovalPlanEditItem>>(
            "api/dcr/approval-plan/saved",
            new ApiSaveUserApprovalPlanRequest { Rank = DcrRanks.Normalize(rank), ApprovalPlan = approvalPlan.ToList() });

    public Task<List<DcrListItem>> GetListAsync(string scope, int userId, string searchText, bool isAdmin = false) =>
        _api.GetAsync<List<DcrListItem>>($"api/dcr?scope={Uri.EscapeDataString(scope ?? string.Empty)}&search={Uri.EscapeDataString(searchText ?? string.Empty)}");

    public Task<int> GetPendingApprovalCountAsync(int userId) =>
        _api.GetAsync<int>("api/dcr/pending-count");

    public Task<bool> CanViewAsync(int requestId, int userId, bool isAdmin) =>
        _api.GetAsync<bool>($"api/dcr/{requestId}/can-view");

    public Task<DcrEditModel> CreateNewModelAsync(int userId) =>
        _api.GetAsync<DcrEditModel>("api/dcr/new");

    public Task<DcrEditModel> LoadEditDataAsync(int requestId) =>
        _api.GetAsync<DcrEditModel>($"api/dcr/{requestId}");

    public async Task<int> SaveDraftAsync(DcrEditModel model, int userId, bool isAdmin, int draftStep = 0, bool isAutoSave = false)
    {
        var response = await _api.PostAsync<ApiSaveDraftRequest, ApiSaveDraftResponse>(
            "api/dcr/save-draft",
            new ApiSaveDraftRequest { Model = model, DraftStep = draftStep, IsAutoSave = isAutoSave }).ConfigureAwait(false);
        CopyWritableProperties(response.Model, model);
        if (response.AlreadyExisted)
            throw new DcrAlreadyCreatedException(response.RequestId);
        return response.RequestId;
    }

    public Task<SubmitDcrResult> SubmitAsync(int requestId, int userId, bool isAdmin, byte[] expectedRowVersion) =>
        _api.PostAsync<ApiSubmitRequest, SubmitDcrResult>(
            $"api/dcr/{requestId}/submit",
            new ApiSubmitRequest { ExpectedRowVersion = expectedRowVersion ?? Array.Empty<byte>() });

    public Task<bool> CanApproveAsync(int requestId, int userId) =>
        _api.GetAsync<bool>($"api/dcr/{requestId}/can-approve");

    public Task<DecisionProcessResult> ProcessDecisionAsync(
        int requestId,
        int userId,
        string decision,
        string comment,
        DecisionAuthentication authentication,
        byte[] expectedRowVersion) =>
        _api.PostAsync<ApiDecisionRequest, DecisionProcessResult>(
            $"api/dcr/{requestId}/decision",
            new ApiDecisionRequest
            {
                Decision = decision ?? string.Empty,
                Comment = comment ?? string.Empty,
                Authentication = authentication,
                ExpectedRowVersion = expectedRowVersion ?? Array.Empty<byte>()
            });

    public Task<List<ApprovalHistoryItem>> GetApprovalHistoryAsync(int requestId) =>
        _api.GetAsync<List<ApprovalHistoryItem>>($"api/dcr/{requestId}/approval-history");

    public Task<List<AttachmentListItem>> GetAttachmentsAsync(int requestId) =>
        _api.GetAsync<List<AttachmentListItem>>($"api/dcr/{requestId}/attachments");

    public Task<List<AuditListItem>> GetAuditLogsAsync(int requestId) =>
        _api.GetAsync<List<AuditListItem>>($"api/dcr/{requestId}/audit-logs");

    public Task<string> GetDcrNumberAsync(int requestId) =>
        _api.GetAsync<string>($"api/dcr/{requestId}/number");

    private static void CopyWritableProperties(DcrEditModel source, DcrEditModel target)
    {
        foreach (var property in typeof(DcrEditModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.CanWrite)
                property.SetValue(target, property.GetValue(source));
        }
    }
}
