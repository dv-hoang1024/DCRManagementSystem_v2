namespace DCRManagementSystem.Models;

public sealed class DecisionAuthentication
{
    public string AuthMethod { get; set; } = AuthMethods.InternalPassword;
    public DateTime AuthenticatedAt { get; set; } = DateTime.Now;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string ProofToken { get; set; } = string.Empty;
}
