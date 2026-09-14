namespace DCRManagementSystem.Models;

public static class OrganizationUnitTypes
{
    public const string Company = "Company";
    public const string ExecutiveBlock = "ExecutiveBlock";
    public const string Division = "Division";
    public const string Center = "Center";
    public const string Factory = "Factory";
    public const string Region = "Region";
    public const string Other = "Other";

    public static readonly string[] All = [Company, ExecutiveBlock, Division, Center, Factory, Region, Other];

    public static string DisplayName(string? value) => value switch
    {
        Company => "Công ty",
        ExecutiveBlock => "Khối điều hành",
        Division => "Khối/Ban",
        Center => "Trung tâm",
        Factory => "Nhà máy",
        Region => "Khu vực",
        _ => "Đơn vị khác"
    };
}

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string CEO = "CEO";
    public const string DCEO = "DCEO";
    public const string COO = "COO";
    public const string CTO = "CTO";
    public const string Director = "Director";
    public const string Manager = "Manager";
    public const string Staff = "Staff";

    // Compatibility aliases for older databases/source references.
    public const string Initiator = Staff;
    public const string DesignManager = Manager;
    public const string MEManager = Manager;
    public const string ChiefEngineer = Director;
    public const string Viewer = Staff;

    public static readonly string[] All =
    {
        Administrator,
        CEO,
        DCEO,
        COO,
        CTO,
        Director,
        Manager,
        Staff
    };
}

public static class RequestStatuses
{
    public const string Draft = "Draft";
    public const string Returned = "Returned";
    public const string InApproval = "InApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";

    public static bool IsEditable(string? status) =>
        string.Equals(status, Draft, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Returned, StringComparison.OrdinalIgnoreCase);
}

public static class DcrRanks
{
    public const string A = "A";
    public const string B = "B";
    public const string C = "C";
    public const string S = "S";

    public static readonly string[] All = [A, B, C, S];

    public static string Normalize(string? value)
    {
        var rank = (value ?? string.Empty).Trim().ToUpperInvariant();
        return All.Contains(rank, StringComparer.Ordinal) ? rank : C;
    }

    public static bool IsValid(string? value) =>
        All.Contains((value ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class ApprovalDecisions
{
    public const string Submitted = "Submitted";
    public const string Waiting = "Waiting";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Returned = "Returned";
    public const string Skipped = "Skipped";
    public const string Cancelled = "Cancelled";
}

public static class AttachmentTypes
{
    public const string General = "General";
    public const string PartIllustration = "PartIllustration";
    public const string Specification = "Specification";
    public const string TemporaryProcess = "TemporaryProcess";
    public const string ReworkInstruction = "ReworkInstruction";
    public const string SupportingDocument = "SupportingDocument";
    public const string Other = "Other";

    public static readonly string[] All =
    {
        General,
        PartIllustration,
        Specification,
        TemporaryProcess,
        ReworkInstruction,
        SupportingDocument,
        Other
    };

    public static string? Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var canonical = All.FirstOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (canonical is not null) return canonical;

        // Compatibility with the original DCR Web form, which used labels that
        // were never part of the API contract.
        return normalized.ToUpperInvariant() switch
        {
            "TECHNICALDOCUMENT" => SupportingDocument,
            "DRAWING" => PartIllustration,
            "PHOTO" => PartIllustration,
            _ => null
        };
    }

}

public static class WorkflowStageCodes
{
    public const string Submission = "SUBMISSION";
    public const string DesignManager = "DESIGN_MANAGER";
    public const string ImpactedDepartment = "IMPACTED_DEPARTMENT";
    public const string MEManager = "ME_MANAGER";
    public const string ChiefEngineer = "CHIEF_ENGINEER";
}

public static class ApproverSources
{
    public const string RequestingDepartmentManager = "RequestingDepartmentManager";
    public const string ImpactedDepartmentManager = "ImpactedDepartmentManager";
    public const string TargetDepartmentManager = "TargetDepartmentManager";
    public const string Role = "Role";
    public const string SpecificUser = "SpecificUser";

    public static readonly string[] All =
    {
        RequestingDepartmentManager,
        ImpactedDepartmentManager,
        TargetDepartmentManager,
        Role,
        SpecificUser
    };
}

public static class NotificationTypes
{
    public const string ApprovalAssigned = "ApprovalAssigned";
    public const string Reminder = "Reminder";
    public const string Escalation = "Escalation";
    public const string Returned = "Returned";
    public const string Rejected = "Rejected";
    public const string Approved = "Approved";
    public const string PdfGenerated = "PdfGenerated";
}

public static class AuthMethods
{
    public const string InternalPassword = "InternalPassword";
    public const string WindowsIntegrated = "WindowsIntegrated";
    public const string Ldap = "LDAP";
    public const string ApplicationSession = "ApplicationSession";
}

public static class AuthenticationModes
{
    public const string LocalOnly = "LocalOnly";
    public const string WindowsPreferred = "WindowsPreferred";
    public const string WindowsOnly = "WindowsOnly";
}

public static class StorageProviders
{
    public const string FileSystem = "FileSystem";
    public const string NetworkShare = "NetworkShare";
}

public static class EmailOutboxStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string RequiresSignIn = "RequiresSignIn";
    public const string Cancelled = "Cancelled";
}
