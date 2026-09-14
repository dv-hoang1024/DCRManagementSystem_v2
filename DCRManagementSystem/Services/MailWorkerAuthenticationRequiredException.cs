namespace DCRManagementSystem.Services;

public sealed class MailWorkerAuthenticationRequiredException : InvalidOperationException
{
    public MailWorkerAuthenticationRequiredException(string message)
        : base(message)
    {
    }

    public MailWorkerAuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
