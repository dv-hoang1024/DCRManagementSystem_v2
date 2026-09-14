using DCRManagementSystem.Models;

namespace DCRManagementSystem.Helpers;

public static class CurrentUser
{
    public static User? User { get; private set; }
    public static string SessionId { get; private set; } = string.Empty;
    public static string AuthMethod { get; private set; } = string.Empty;
    public static string WindowsIdentity { get; private set; } = string.Empty;
    public static bool IsAuthenticated => User is not null;
    public static bool IsAdmin => User?.Role == RoleNames.Administrator;

    public static void Set(User user, string authMethod = AuthMethods.InternalPassword, string? windowsIdentity = null)
    {
        User = user;
        AuthMethod = authMethod;
        WindowsIdentity = windowsIdentity ?? AuditEnvironment.WindowsIdentityName;
        SessionId = Guid.NewGuid().ToString("N");
    }

    public static void Clear()
    {
        User = null;
        SessionId = string.Empty;
        AuthMethod = string.Empty;
        WindowsIdentity = string.Empty;
    }
}
