using System.Net;
using System.Net.Sockets;
using System.Security.Principal;

namespace DCRManagementSystem.Helpers;

public static class AuditEnvironment
{
    public static string ComputerName =>
        string.IsNullOrWhiteSpace(RequestExecutionContext.ComputerName)
            ? Environment.MachineName
            : RequestExecutionContext.ComputerName;

    public static string WindowsIdentityName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(RequestExecutionContext.WindowsIdentity))
                return RequestExecutionContext.WindowsIdentity;
            try
            {
                return WindowsIdentity.GetCurrent().Name ?? Environment.UserName;
            }
            catch
            {
                return Environment.UserName;
            }
        }
    }

    public static string LocalIpAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(RequestExecutionContext.IpAddress))
                return RequestExecutionContext.IpAddress;
            try
            {
                return Dns.GetHostEntry(Dns.GetHostName())
                    .AddressList
                    .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
                    ?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
