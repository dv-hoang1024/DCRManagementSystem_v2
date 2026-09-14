using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public static class DcrValidationService
{
    public static ValidationResultModel ValidateStep(DcrEditModel model, int step)
    {
        var result = new ValidationResultModel();

        switch (step)
        {
            case 1:
                ValidateGeneral(model, result);
                break;
            case 2:
                ValidateParts(model, result);
                break;
            case 3:
                ValidateDeviation(model, result);
                break;
            case 4:
                ValidatePlan(model, result);
                break;
            case 5:
                ValidateGeneral(model, result);
                ValidateParts(model, result);
                ValidateDeviation(model, result);
                ValidatePlan(model, result);
                ValidateApprovalPlan(model, result);
                break;
            default:
                result.Errors.Add("Bước DCR không hợp lệ.");
                break;
        }

        return result;
    }

    public static ValidationResultModel ValidateRequestForSubmit(DCRRequest request)
    {
        var model = new DcrEditModel
        {
            Title = request.Title,
            Rank = request.Rank,
            RequestingDepartmentId = request.RequestingDepartmentId,
            Program = request.Program,
            BuildStage = request.BuildStage,
            ProblemDescription = request.ProblemDescription,
            Solution = request.Solution,
            MaterialChangeDescription = request.MaterialChangeDescription,
            MaterialIdentificationRequired = request.MaterialIdentificationRequired,
            MaterialUsageStation = request.MaterialUsageStation,
            SupplierSupportsMRD = request.SupplierSupportsMRD,
            ExpectedArrivalDate = request.ExpectedArrivalDate,
            PlannedStartDate = request.PlannedStartDate,
            PlannedEndDate = request.PlannedEndDate,
            Parts = request.Parts.Select(x => new PartEditItem
            {
                ChangeType = x.ChangeType,
                PartNumber = x.PartNumber,
                PartName = x.PartName,
                Quantity = x.Quantity
            }).ToList(),
            ImpactedDepartments = request.ImpactedDepartments.Select(x => new ImpactDepartmentEditItem
            {
                DepartmentId = x.DepartmentId,
                EstimatedCost = x.EstimatedCost
            }).ToList(),
            ApprovalPlan = request.ApprovalPlan.Select(x => new ApprovalPlanEditItem
            {
                Id = x.Id,
                LevelNumber = x.LevelNumber,
                LevelName = x.LevelName,
                ApproverId = x.ApproverId,
                ApproverName = x.Approver?.FullName ?? string.Empty,
                ApproverEmail = x.Approver?.Email ?? string.Empty,
                Sequence = x.Sequence
            }).ToList()
        };

        return ValidateStep(model, 5);
    }

    private static void ValidateGeneral(DcrEditModel model, ValidationResultModel result)
    {
        if (model.RequestingDepartmentId <= 0)
            result.Errors.Add("Requesting Department là bắt buộc.");
        if (string.IsNullOrWhiteSpace(model.Title))
            result.Errors.Add("Title là bắt buộc.");
        if (!DcrRanks.IsValid(model.Rank))
            result.Errors.Add("Rank DCR phải là A, B, C hoặc S.");
        if (string.IsNullOrWhiteSpace(model.Program))
            result.Errors.Add("Dòng sản phẩm là bắt buộc.");
        if (string.IsNullOrWhiteSpace(model.BuildStage))
            result.Errors.Add("Build Stage là bắt buộc.");
    }

    private static void ValidateParts(DcrEditModel model, ValidationResultModel result)
    {
        var parts = model.Parts.Where(IsMeaningfulPart).ToList();
        if (parts.Count == 0)
        {
            result.Errors.Add("Phải có ít nhất một linh kiện trong Part List.");
            return;
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i].ChangeType))
                result.Errors.Add($"Part #{i + 1}: Loại thay đổi là bắt buộc.");
            if (string.IsNullOrWhiteSpace(parts[i].PartNumber))
                result.Errors.Add($"Part #{i + 1}: Part Number là bắt buộc.");
            if (string.IsNullOrWhiteSpace(parts[i].PartName))
                result.Errors.Add($"Part #{i + 1}: Part Name là bắt buộc.");
            if (string.IsNullOrWhiteSpace(parts[i].Quantity))
                result.Errors.Add($"Part #{i + 1}: Quantity là bắt buộc.");
        }
    }

    private static void ValidateDeviation(DcrEditModel model, ValidationResultModel result)
    {
        if (string.IsNullOrWhiteSpace(model.ProblemDescription))
            result.Errors.Add("Problem Description là bắt buộc.");
        if (string.IsNullOrWhiteSpace(model.Solution))
            result.Errors.Add("Solution là bắt buộc.");
        if (model.ImpactedDepartments.All(x => x.DepartmentId <= 0))
            result.Errors.Add("Phải chọn ít nhất một Impacted Department.");
    }

    private static void ValidatePlan(DcrEditModel model, ValidationResultModel result)
    {
        if (model.MaterialIdentificationRequired && string.IsNullOrWhiteSpace(model.MaterialUsageStation))
            result.Errors.Add("Station for Material Usage là bắt buộc khi yêu cầu nhận diện vật liệu.");

        if (model.PlannedStartDate.HasValue && model.PlannedEndDate.HasValue &&
            model.PlannedStartDate.Value.Date > model.PlannedEndDate.Value.Date)
        {
            result.Errors.Add("Planned Start Date không được sau Planned End Date.");
        }
    }

    private static void ValidateApprovalPlan(DcrEditModel model, ValidationResultModel result)
    {
        var plan = model.ApprovalPlan
            .Where(x => x.ApproverId > 0 || x.LevelNumber > 0)
            .OrderBy(x => x.LevelNumber)
            .ThenBy(x => x.Sequence)
            .ToList();

        if (plan.Count == 0)
        {
            result.Errors.Add($"DCR Rank {DcrRanks.Normalize(model.Rank)} phải có line phê duyệt. Hãy nạp mẫu cá nhân, dùng gợi ý theo Rank hoặc tự chọn người phê duyệt.");
            return;
        }

        if (plan.Any(x => x.LevelNumber <= 0 || x.ApproverId <= 0))
            result.Errors.Add("Luồng phê duyệt tùy chọn có cấp hoặc người phê duyệt không hợp lệ.");

        var levels = plan.Select(x => x.LevelNumber).Distinct().OrderBy(x => x).ToList();
        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i] != i + 1)
            {
                result.Errors.Add("Các cấp phê duyệt tùy chọn phải liên tục 1, 2, 3... không được bỏ trống cấp.");
                break;
            }
        }

        if (plan.GroupBy(x => x.ApproverId).Any(x => x.Count() > 1))
            result.Errors.Add("Mỗi người chỉ được xuất hiện một lần trong toàn bộ luồng phê duyệt của DCR.");

        foreach (var approver in plan.Where(x => string.IsNullOrWhiteSpace(x.ApproverEmail)))
        {
            var approverLabel = string.IsNullOrWhiteSpace(approver.ApproverName)
                ? $"UserId={approver.ApproverId}"
                : approver.ApproverName;
            result.Errors.Add($"Approver '{approverLabel}' chưa có Email. Hãy khai báo Email trong Quản lý người dùng trước khi Submit.");
        }
    }

    private static bool IsMeaningfulPart(PartEditItem x) =>
        !string.IsNullOrWhiteSpace(x.PartNumber) ||
        !string.IsNullOrWhiteSpace(x.PartName) ||
        !string.IsNullOrWhiteSpace(x.Quantity) ||
        !string.IsNullOrWhiteSpace(x.ReplacedBy);
}
