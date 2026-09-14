namespace DCRManagementSystem.Models;

public sealed class AuthenticationResult
{
    public required User User { get; init; }
    public string AuthMethod { get; init; } = AuthMethods.InternalPassword;
    public string WindowsIdentity { get; init; } = string.Empty;
}
