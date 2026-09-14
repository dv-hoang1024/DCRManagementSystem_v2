namespace DCRManagementSystem.Api;

public sealed class ApiServerOptions
{
    public int AccessTokenHours { get; set; } = 8;
    public int RefreshTokenDays { get; set; } = 30;
    public int DecisionProofMinutes { get; set; } = 5;

    // Protect SQL from request bursts when many WinForms clients refresh/save at once.
    public int MaxConcurrentRequests { get; set; } = 32;
    public int RequestQueueLimit { get; set; } = 160;
    public int RequestQueueTimeoutSeconds { get; set; } = 20;
    public int UserSessionCacheSeconds { get; set; } = 30;
    public int ReferenceCacheSeconds { get; set; } = 60;
}
