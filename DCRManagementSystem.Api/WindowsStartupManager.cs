using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace DCRManagementSystem.Api;

internal enum WindowsStartupEntryState
{
    Absent,
    Current,
    DifferentCommand,
    Unknown
}

internal enum LegacyStartupTaskState
{
    Absent,
    Present,
    Unknown
}

internal sealed record WindowsStartupStatus(
    WindowsStartupEntryState RegistryEntry,
    LegacyStartupTaskState LegacyTask,
    string Message)
{
    public bool IsConfigured =>
        RegistryEntry is WindowsStartupEntryState.Current or WindowsStartupEntryState.DifferentCommand ||
        LegacyTask == LegacyStartupTaskState.Present;

    public bool IsIndeterminate =>
        !IsConfigured &&
        (RegistryEntry == WindowsStartupEntryState.Unknown || LegacyTask == LegacyStartupTaskState.Unknown);
}

internal sealed record WindowsStartupActionResult(
    bool Success,
    string Message,
    WindowsStartupStatus Status);

/// <summary>
/// Manages the per-user Windows startup entry for the interactive DCR API tray process.
/// New registrations use HKCU so enabling startup does not require administrator rights.
/// The legacy scheduled task created by Install-Api-LogonTask.ps1 remains detectable and
/// is removed when startup is disabled, preventing the UI from reporting a false Off state.
/// </summary>
internal sealed class WindowsStartupManager
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DCRManagementSystem.Api";
    private const string LegacyTaskName = "DCR Management API";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(8);

    private readonly string? _startupCommand = BuildStartupCommand();

    public WindowsStartupStatus GetStatus()
    {
        var registryEntry = GetRegistryEntryState();
        var legacyTask = GetLegacyTaskState();
        return new WindowsStartupStatus(registryEntry, legacyTask, BuildStatusMessage(registryEntry, legacyTask));
    }

    public WindowsStartupActionResult SetEnabled(bool enabled)
    {
        return enabled ? Enable() : Disable();
    }

    private WindowsStartupActionResult Enable()
    {
        var current = GetStatus();
        if (current.LegacyTask == LegacyStartupTaskState.Present)
        {
            return new WindowsStartupActionResult(
                true,
                "DCR API đã được cấu hình khởi động cùng Windows bằng Task Scheduler.",
                current);
        }

        if (string.IsNullOrWhiteSpace(_startupCommand))
        {
            return new WindowsStartupActionResult(
                false,
                "Không xác định được lệnh khởi động của DCR API.",
                current);
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true)
                            ?? throw new InvalidOperationException("Không mở được khóa Windows Startup của người dùng hiện tại.");
            key.SetValue(RunValueName, _startupCommand, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            var failedStatus = GetStatus();
            return new WindowsStartupActionResult(
                false,
                "Không thể bật khởi động cùng Windows: " + ex.GetBaseException().Message,
                failedStatus);
        }

        var status = GetStatus();
        var success = status.RegistryEntry == WindowsStartupEntryState.Current;
        return new WindowsStartupActionResult(
            success,
            success
                ? "Đã bật khởi động DCR API cùng Windows cho tài khoản hiện tại."
                : "Windows chưa lưu được cấu hình khởi động DCR API.",
            status);
    }

    private WindowsStartupActionResult Disable()
    {
        string? registryError = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            registryError = ex.GetBaseException().Message;
        }

        var legacyState = GetLegacyTaskState();
        string? taskError = null;
        if (legacyState == LegacyStartupTaskState.Present)
        {
            var deletion = DeleteLegacyTask(elevated: false);
            if (!deletion.Success)
            {
                deletion = DeleteLegacyTask(elevated: true);
                if (!deletion.Success)
                    taskError = deletion.Message;
            }
        }
        else if (legacyState == LegacyStartupTaskState.Unknown)
        {
            taskError = "Không kiểm tra được Scheduled Task cũ 'DCR Management API'.";
        }

        var status = GetStatus();
        var success = registryError is null && taskError is null &&
                      status.RegistryEntry == WindowsStartupEntryState.Absent &&
                      status.LegacyTask == LegacyStartupTaskState.Absent;
        if (success)
        {
            return new WindowsStartupActionResult(
                true,
                "Đã tắt khởi động DCR API cùng Windows.",
                status);
        }

        var errors = new[] { registryError, taskError }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var details = string.Join("\r\n", errors);
        if (string.IsNullOrWhiteSpace(details))
            details = status.Message;

        return new WindowsStartupActionResult(
            false,
            "Chưa thể tắt hoàn toàn khởi động cùng Windows.\r\n\r\n" + details,
            status);
    }

    private WindowsStartupEntryState GetRegistryEntryState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            var command = key?.GetValue(RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (string.IsNullOrWhiteSpace(command))
                return WindowsStartupEntryState.Absent;
            return string.Equals(command.Trim(), _startupCommand?.Trim(), StringComparison.OrdinalIgnoreCase)
                ? WindowsStartupEntryState.Current
                : WindowsStartupEntryState.DifferentCommand;
        }
        catch
        {
            return WindowsStartupEntryState.Unknown;
        }
    }

    private static LegacyStartupTaskState GetLegacyTaskState()
    {
        try
        {
            var result = RunSchtasks(["/Query", "/TN", LegacyTaskName], elevated: false);
            if (result.ExitCode == 0)
                return LegacyStartupTaskState.Present;
            return result.ExitCode == 1 && IndicatesMissingTask(result.Output)
                ? LegacyStartupTaskState.Absent
                : LegacyStartupTaskState.Unknown;
        }
        catch
        {
            return LegacyStartupTaskState.Unknown;
        }
    }

    private static ProcessResult DeleteLegacyTask(bool elevated)
    {
        try
        {
            var result = RunSchtasks(["/Delete", "/TN", LegacyTaskName, "/F"], elevated);
            if (result.ExitCode == 0)
                return new ProcessResult(true, 0, string.Empty);

            var message = string.IsNullOrWhiteSpace(result.Output)
                ? "Không xóa được Scheduled Task cũ 'DCR Management API'."
                : result.Output.Trim();
            return new ProcessResult(false, result.ExitCode, message);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new ProcessResult(false, ex.NativeErrorCode, "Bạn đã hủy yêu cầu quyền Administrator để xóa Scheduled Task cũ.");
        }
        catch (Exception ex)
        {
            return new ProcessResult(false, -1, ex.GetBaseException().Message);
        }
    }

    private static ProcessResult RunSchtasks(IReadOnlyList<string> arguments, bool elevated)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = elevated,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (elevated)
        {
            startInfo.Verb = "runas";
        }
        else
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Không khởi chạy được Windows Task Scheduler.");
        if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(false, -1, "Windows Task Scheduler không phản hồi.");
        }

        var output = elevated
            ? string.Empty
            : string.Join("\r\n", process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
        return new ProcessResult(process.ExitCode == 0, process.ExitCode, output);
    }

    private static string BuildStatusMessage(
        WindowsStartupEntryState registryEntry,
        LegacyStartupTaskState legacyTask)
    {
        if (registryEntry == WindowsStartupEntryState.Current && legacyTask == LegacyStartupTaskState.Present)
            return "Đã bật bằng Windows Startup và Task Scheduler cũ; tắt tùy chọn để dọn cả hai.";
        if (registryEntry == WindowsStartupEntryState.DifferentCommand && legacyTask == LegacyStartupTaskState.Present)
            return "Đang có cấu hình Windows Startup cũ và Task Scheduler cũ.";
        if (legacyTask == LegacyStartupTaskState.Present)
            return "Đã bật bằng Task Scheduler cũ.";
        if (registryEntry == WindowsStartupEntryState.Current)
            return legacyTask == LegacyStartupTaskState.Unknown
                ? "Đã bật bằng Windows Startup; không kiểm tra được Task Scheduler cũ."
                : "Đã bật cho tài khoản Windows hiện tại.";
        if (registryEntry == WindowsStartupEntryState.DifferentCommand)
            return "Đã có cấu hình Windows Startup trỏ tới phiên bản/đường dẫn khác.";
        if (registryEntry == WindowsStartupEntryState.Unknown || legacyTask == LegacyStartupTaskState.Unknown)
            return "Không đọc được đầy đủ trạng thái khởi động cùng Windows.";
        return "Chưa bật.";
    }

    private static bool IndicatesMissingTask(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        return output.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("không thể tìm thấy", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("không tìm thấy", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("không tồn tại", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath;
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return QuoteCommandArgument(Path.GetFullPath(processPath));
        }

        if (!string.IsNullOrWhiteSpace(processPath) && !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return $"{QuoteCommandArgument(Path.GetFullPath(processPath))} {QuoteCommandArgument(Path.GetFullPath(entryAssemblyPath))}";
        }

        return string.IsNullOrWhiteSpace(entryAssemblyPath)
            ? null
            : QuoteCommandArgument(Path.GetFullPath(entryAssemblyPath));
    }

    private static string QuoteCommandArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";

    private sealed record ProcessResult(bool Success, int ExitCode, string Output)
    {
        public string Message => Output;
    }
}
