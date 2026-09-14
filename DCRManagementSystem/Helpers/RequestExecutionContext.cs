using System.Threading;

namespace DCRManagementSystem.Helpers;

/// <summary>
/// Per-operation metadata used by both the desktop DirectSql path and the HTTP API path.
/// AsyncLocal keeps concurrent API requests isolated and avoids using the WinForms
/// CurrentUser singleton on the server.
/// </summary>
public static class RequestExecutionContext
{
    private sealed class State
    {
        public string SessionId { get; init; } = string.Empty;
        public string AuthMethod { get; init; } = string.Empty;
        public string WindowsIdentity { get; init; } = string.Empty;
        public string ComputerName { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
    }

    private static readonly AsyncLocal<State?> Current = new();

    public static string SessionId => Current.Value?.SessionId ?? CurrentUser.SessionId;
    public static string AuthMethod => Current.Value?.AuthMethod ?? CurrentUser.AuthMethod;
    public static string WindowsIdentity => Current.Value?.WindowsIdentity ?? CurrentUser.WindowsIdentity;
    public static string ComputerName => Current.Value?.ComputerName ?? string.Empty;
    public static string IpAddress => Current.Value?.IpAddress ?? string.Empty;

    public static IDisposable Push(
        string sessionId,
        string authMethod,
        string windowsIdentity,
        string computerName,
        string ipAddress)
    {
        var previous = Current.Value;
        Current.Value = new State
        {
            SessionId = sessionId ?? string.Empty,
            AuthMethod = authMethod ?? string.Empty,
            WindowsIdentity = windowsIdentity ?? string.Empty,
            ComputerName = computerName ?? string.Empty,
            IpAddress = ipAddress ?? string.Empty
        };
        return new PopScope(previous);
    }

    private sealed class PopScope(State? previous) : IDisposable
    {
        private State? _previous = previous;
        public void Dispose()
        {
            Current.Value = _previous;
            _previous = null;
        }
    }
}
