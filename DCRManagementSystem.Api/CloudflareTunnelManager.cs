using System.Diagnostics;
using System.Text;

namespace DCRManagementSystem.Api;

public enum CloudflareTunnelState
{
    Disabled,
    Stopped,
    RunningManaged,
    RunningExternal,
    Error
}

public sealed class CloudflareTunnelStatus
{
    public CloudflareTunnelState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string ConfigPath { get; init; } = string.Empty;
    public string LastCommand { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public bool CanStopFromDcrApi => State == CloudflareTunnelState.RunningManaged;
}

public sealed class TunnelActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class CloudflareTunnelManager : IDisposable
{
    private readonly CloudflareTunnelOptions _options;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _monitorCts = new();
    private Process? _managedProcess;
    private Task? _monitorTask;
    private bool _manualStop;
    private string _lastError = string.Empty;
    private string _lastCommand = string.Empty;
    private string _resolvedExecutable = string.Empty;
    private string _resolvedWorkingDirectory = string.Empty;
    private string _resolvedConfig = string.Empty;
    private readonly string _trackedProcessFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DCRManagementSystem",
        "Api",
        "Logs",
        "cloudflared-dcr-tunnel.pid");

    public event EventHandler? StatusChanged;

    public CloudflareTunnelManager(CloudflareTunnelOptions options)
    {
        _options = options;
        ResolvePaths();
    }

    public CloudflareTunnelOptions Options => _options;

    public void StartMonitoring()
    {
        if (!_options.Enabled || _monitorTask is not null) return;

        _monitorTask = Task.Run(async () =>
        {
            var delay = TimeSpan.FromSeconds(Math.Clamp(_options.MonitorIntervalSeconds, 5, 300));
            while (!_monitorCts.IsCancellationRequested)
            {
                try
                {
                    if (_options.AutoStart && !_manualStop && !IsDcrTunnelRunning())
                        StartTunnelInternal(markAsManualStart: false);
                }
                catch (Exception ex)
                {
                    SetError(ex.GetBaseException().Message);
                }

                try
                {
                    await Task.Delay(delay, _monitorCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    public TunnelActionResult EnsureRunning()
    {
        if (!_options.Enabled)
            return new TunnelActionResult { Success = false, Message = "Cloudflare Tunnel đang bị tắt trong api.appsettings.json." };

        if (IsDcrTunnelRunning())
        {
            RaiseStatusChanged();
            return new TunnelActionResult { Success = true, Message = "Cloudflare Tunnel đã chạy." };
        }

        return StartTunnelInternal(markAsManualStart: true);
    }

    public TunnelActionResult RepairDnsRoute()
    {
        if (!_options.EnableDnsRouteRepairAction)
            return new TunnelActionResult { Success = false, Message = "Chức năng sửa DNS Route đang bị tắt trong api.appsettings.json." };

        ResolvePaths();
        if (string.IsNullOrWhiteSpace(_resolvedExecutable) || !File.Exists(_resolvedExecutable))
            return new TunnelActionResult { Success = false, Message = "Không tìm thấy cloudflared.exe để cấu hình DNS Route." };
        var tunnelTarget = GetTunnelTarget();
        if (string.IsNullOrWhiteSpace(tunnelTarget))
            return new TunnelActionResult { Success = false, Message = "CloudflareTunnel.TunnelName và TunnelId đang trống." };
        if (string.IsNullOrWhiteSpace(_options.DnsHostname))
            return new TunnelActionResult { Success = false, Message = "CloudflareTunnel.DnsHostname đang trống." };

        // Dùng đúng cú pháp đã được người vận hành kiểm chứng thủ công trên máy này.
        var arguments = $"tunnel route dns -f \"{tunnelTarget}\" \"{_options.DnsHostname}\"";
        var result = RunOneShot(arguments, 30000);
        if (result.Success)
        {
            return new TunnelActionResult
            {
                Success = true,
                Message = $"Đã đăng ký/cập nhật DNS Route cho {_options.DnsHostname}.\r\n\r\nĐây là thao tác provisioning một lần; không cần chạy lại mỗi lần DCR API khởi động."
            };
        }

        return new TunnelActionResult
        {
            Success = false,
            Message = "Không thể đăng ký DNS Route. cloudflared cần quyền Cloudflare/cert.pem của user hiện tại.\r\n\r\n" + result.Message
        };
    }

    public TunnelActionResult StopTunnel()
    {
        lock (_sync)
        {
            _manualStop = true;
            if (!IsDcrTunnelRunning() || _managedProcess is null)
            {
                _managedProcess?.Dispose();
                _managedProcess = null;
                _lastError = string.Empty;
                ClearTrackedProcess();

                RaiseStatusChanged();
                return new TunnelActionResult { Success = true, Message = $"Tunnel DCR '{_options.TunnelName}' đã dừng." };
            }

            try
            {
                var processId = _managedProcess.Id;
                _managedProcess.Kill(entireProcessTree: true);
                _managedProcess.WaitForExit(5000);
                _managedProcess.Dispose();
                _managedProcess = null;
                ClearTrackedProcess(processId);
                _lastError = string.Empty;
                WriteTunnelLog($"DCR API stopped its dedicated cloudflared process (PID {processId}).");
                RaiseStatusChanged();
                return new TunnelActionResult { Success = true, Message = $"Đã dừng tunnel DCR riêng '{_options.TunnelName}'." };
            }
            catch (Exception ex)
            {
                SetError(ex.GetBaseException().Message);
                return new TunnelActionResult { Success = false, Message = "Không thể dừng Cloudflare Tunnel: " + ex.GetBaseException().Message };
            }
        }
    }

    public TunnelActionResult RestartTunnel()
    {
        var status = GetStatus();
        if (status.State == CloudflareTunnelState.RunningExternal)
        {
            return new TunnelActionResult
            {
                Success = false,
                Message = "Tunnel đang chạy ngoài DCR API. Hãy restart Windows Service/cloudflared bên ngoài hoặc dừng tiến trình đó trước."
            };
        }

        StopTunnel();
        Thread.Sleep(350);
        return StartTunnelInternal(markAsManualStart: true);
    }

    public CloudflareTunnelStatus GetStatus()
    {
        if (!_options.Enabled)
        {
            return NewStatus(CloudflareTunnelState.Disabled, "Đã tắt");
        }

        lock (_sync)
        {
            if (_managedProcess is not null && !HasExited(_managedProcess))
            {
                return NewStatus(CloudflareTunnelState.RunningManaged, "Đang chạy - DCR API quản lý", _managedProcess.Id);
            }
        }

        if (IsDcrTunnelRunning())
        {
            lock (_sync)
            {
                if (_managedProcess is not null && !HasExited(_managedProcess))
                    return NewStatus(CloudflareTunnelState.RunningManaged, "Đang chạy - DCR API quản lý", _managedProcess.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(_lastError))
            return NewStatus(CloudflareTunnelState.Error, _lastError);

        return NewStatus(CloudflareTunnelState.Stopped, _manualStop ? "Đã dừng thủ công" : "Chưa chạy");
    }

    private CloudflareTunnelStatus NewStatus(CloudflareTunnelState state, string message, int? pid = null)
    {
        return new CloudflareTunnelStatus
        {
            State = state,
            Message = message,
            ExecutablePath = _resolvedExecutable,
            WorkingDirectory = _resolvedWorkingDirectory,
            ConfigPath = _resolvedConfig,
            LastCommand = _lastCommand,
            ProcessId = pid
        };
    }

    private TunnelActionResult StartTunnelInternal(bool markAsManualStart)
    {
        lock (_sync)
        {
            if (markAsManualStart)
                _manualStop = false;

            if (IsDcrTunnelRunning())
            {
                _lastError = string.Empty;
                RaiseStatusChanged();
                return new TunnelActionResult { Success = true, Message = "Cloudflare Tunnel đã chạy." };
            }

            ResolvePaths();

            if (string.IsNullOrWhiteSpace(_resolvedExecutable) || !File.Exists(_resolvedExecutable))
            {
                var message = "Không tìm thấy cloudflared.exe. Máy hiện tại đang dùng thủ công C:\\Cloudflare\\cloudflared.exe; hãy kiểm tra file này hoặc CloudflareTunnel.ExecutablePath.";
                SetError(message);
                return new TunnelActionResult { Success = false, Message = message };
            }

            if (string.IsNullOrWhiteSpace(_resolvedConfig) || !File.Exists(_resolvedConfig))
            {
                var message = $"Không tìm thấy config riêng của tunnel DCR: {_resolvedConfig}. Hãy tạo dcr-config.yml theo hướng dẫn CLOUDFLARE_SEPARATE_TUNNELS_SETUP.md.";
                SetError(message);
                return new TunnelActionResult { Success = false, Message = message };
            }

            var ingressError = ValidateStandaloneIngressConfig();
            if (!string.IsNullOrWhiteSpace(ingressError))
            {
                SetError(ingressError);
                return new TunnelActionResult { Success = false, Message = ingressError };
            }

            var attempts = BuildRunAttempts();
            string lastMessage = string.Empty;
            foreach (var arguments in attempts)
            {
                var started = TryStartLongRunning(arguments);
                if (started.Success)
                {
                    _lastError = string.Empty;
                    RaiseStatusChanged();
                    return new TunnelActionResult
                    {
                        Success = true,
                        Message = "Đã khởi động Cloudflare Tunnel ẩn ở nền.\r\n\r\nLệnh: " + _resolvedExecutable + " " + _lastCommand
                    };
                }

                lastMessage = started.Message;
            }

            SetError(lastMessage);
            return new TunnelActionResult
            {
                Success = false,
                Message = "Không thể khởi động Cloudflare Tunnel.\r\n\r\n" + lastMessage + "\r\n\r\nXem log: " + ApiLog.TunnelLogFile
            };
        }
    }

    private string ValidateStandaloneIngressConfig()
    {
        try
        {
            var config = File.ReadAllText(_resolvedConfig);
            if (!config.Contains(_options.DnsHostname, StringComparison.OrdinalIgnoreCase))
                return $"dcr-config.yml không có hostname {_options.DnsHostname}. Hãy chạy Scripts\\Repair-Dcr-Tunnel-Config.cmd.";

            // The old architecture sent the DCR UI to WebDashboard :5000.
            // Refuse to start that configuration because changing WebDashboard routing would
            // immediately make dcr.ggpcontrol.cloud return 404 again.
            if (config.Contains("127.0.0.1:5000", StringComparison.OrdinalIgnoreCase) ||
                config.Contains("localhost:5000", StringComparison.OrdinalIgnoreCase))
            {
                return "dcr-config.yml vẫn trỏ giao diện DCR sang WebDashboard :5000. " +
                       "Hãy chạy Scripts\\Repair-Dcr-Tunnel-Config.cmd để chuyển toàn bộ dcr.ggpcontrol.cloud sang DCR API/Web Portal :5080.";
            }

            if (!config.Contains("127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) &&
                !config.Contains("localhost:5080", StringComparison.OrdinalIgnoreCase))
            {
                return "dcr-config.yml không trỏ dcr.ggpcontrol.cloud tới port 5080. " +
                       "Hãy chạy Scripts\\Repair-Dcr-Tunnel-Config.cmd.";
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            return "Không đọc được dcr-config.yml để kiểm tra ingress: " + ex.GetBaseException().Message;
        }
    }

    private List<string> BuildRunAttempts()
    {
        var attempts = new List<string>();
        var target = GetTunnelTarget();

        if (!string.IsNullOrWhiteSpace(_resolvedConfig) && File.Exists(_resolvedConfig))
        {
            // Luôn truyền config riêng để không thể vô tình khởi động tunnel WebDashboard.
            if (!string.IsNullOrWhiteSpace(target))
                attempts.Add($"tunnel --config \"{_resolvedConfig}\" run \"{target}\"");
            attempts.Add($"tunnel --config \"{_resolvedConfig}\" run");
        }

        // Tùy chọn tương thích cũ chỉ là fallback cuối cùng và mặc định bị tắt.
        if (_options.PreferManualCompatibleCommand && !string.IsNullOrWhiteSpace(target))
            attempts.Add($"tunnel run \"{target}\"");

        return attempts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private TunnelActionResult TryStartLongRunning(string arguments)
    {
        Process? process = null;
        try
        {
            var startInfo = CreateStartInfo(arguments, redirectOutput: true);
            process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) WriteTunnelLog("OUT: " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) WriteTunnelLog("ERR: " + e.Data); };

            _lastCommand = arguments;
            WriteTunnelLog($"START TRY: cd /d \"{startInfo.WorkingDirectory}\" && \"{startInfo.FileName}\" {arguments}");

            if (!process.Start())
                throw new InvalidOperationException("Windows không khởi động được cloudflared.exe.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var validationMs = Math.Clamp(_options.StartupValidationMilliseconds, 500, 10000);
            if (process.WaitForExit(validationMs))
            {
                var exitCode = process.ExitCode;
                WriteTunnelLog($"START FAILED EARLY: ExitCode={exitCode}; args={arguments}");
                process.Dispose();
                process = null;
                return new TunnelActionResult
                {
                    Success = false,
                    Message = $"cloudflared thoát ngay khi startup (ExitCode={exitCode}). Lệnh: {arguments}"
                };
            }

            TrackManagedProcess(process, persist: true);
            if (HasExited(process))
                return new TunnelActionResult { Success = false, Message = "cloudflared đã thoát ngay sau bước kiểm tra startup." };
            return new TunnelActionResult { Success = true, Message = "Started" };
        }
        catch (Exception ex)
        {
            try { process?.Dispose(); } catch { }
            return new TunnelActionResult { Success = false, Message = ex.GetBaseException().Message };
        }
    }

    private TunnelActionResult RunOneShot(string arguments, int timeoutMs)
    {
        try
        {
            var startInfo = CreateStartInfo(arguments, redirectOutput: true);
            using var process = new Process { StartInfo = startInfo };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            _lastCommand = arguments;
            WriteTunnelLog($"ONE SHOT: cd /d \"{startInfo.WorkingDirectory}\" && \"{startInfo.FileName}\" {arguments}");
            if (!process.Start())
                throw new InvalidOperationException("Windows không khởi động được cloudflared.exe.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new TunnelActionResult { Success = false, Message = "Timeout khi chạy cloudflared." };
            }

            process.WaitForExit();
            var output = (stdout.ToString() + Environment.NewLine + stderr.ToString()).Trim();
            if (!string.IsNullOrWhiteSpace(output))
                WriteTunnelLog(output);

            return new TunnelActionResult
            {
                Success = process.ExitCode == 0,
                Message = string.IsNullOrWhiteSpace(output) ? $"ExitCode={process.ExitCode}" : output
            };
        }
        catch (Exception ex)
        {
            return new TunnelActionResult { Success = false, Message = ex.GetBaseException().Message };
        }
        finally
        {
            RaiseStatusChanged();
        }
    }

    private ProcessStartInfo CreateStartInfo(string arguments, bool redirectOutput)
    {
        return new ProcessStartInfo
        {
            FileName = _resolvedExecutable,
            Arguments = arguments,
            WorkingDirectory = _resolvedWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };
    }

    private void ResolvePaths()
    {
        _resolvedExecutable = ResolveExecutablePath();
        _resolvedWorkingDirectory = ResolveWorkingDirectory(_resolvedExecutable);
        _resolvedConfig = ResolveConfigPath();
    }

    private string ResolveExecutablePath()
    {
        var configured = Expand(_options.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var candidates = new List<string>
        {
            // Deployment thực tế của user.
            @"C:\Cloudflare\cloudflared.exe",
            Path.Combine(AppContext.BaseDirectory, "cloudflared.exe"),
            @"C:\Cloudflared\bin\cloudflared.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudflared", "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloudflare", "cloudflared.exe")
        };

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            candidates.Add(Path.Combine(programFilesX86, "cloudflared", "cloudflared.exe"));

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            candidates.Add(Path.Combine(directory.Trim('"'), "cloudflared.exe"));

        return candidates.FirstOrDefault(File.Exists) ?? configured;
    }

    private string ResolveWorkingDirectory(string executable)
    {
        var configured = Expand(_options.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        if (File.Exists(@"C:\Cloudflare\cloudflared.exe") && Directory.Exists(@"C:\Cloudflare"))
            return @"C:\Cloudflare";

        var exeDir = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
        return !string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir) ? exeDir : AppContext.BaseDirectory;
    }

    private string ResolveConfigPath()
    {
        var configured = Expand(_options.ConfigPath);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cloudflared",
            "dcr-config.yml");
    }

    private static string Expand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
    }

    private string GetTunnelTarget()
    {
        if (!string.IsNullOrWhiteSpace(_options.TunnelName))
            return _options.TunnelName.Trim();
        return _options.TunnelId?.Trim() ?? string.Empty;
    }

    private bool IsDcrTunnelRunning()
    {
        lock (_sync)
        {
            if (_managedProcess is not null && !HasExited(_managedProcess))
                return true;

            try { _managedProcess?.Dispose(); } catch { }
            _managedProcess = null;
        }

        return TryAdoptTrackedProcess();
    }

    private bool TryAdoptTrackedProcess()
    {
        try
        {
            if (!File.Exists(_trackedProcessFile))
                return false;

            var lines = File.ReadAllLines(_trackedProcessFile);
            if (lines.Length < 3 ||
                !int.TryParse(lines[0], out var processId) ||
                !long.TryParse(lines[1], out var startTimeUtcTicks) ||
                !string.Equals(lines[2], GetTunnelTarget(), StringComparison.OrdinalIgnoreCase))
            {
                ClearTrackedProcess();
                return false;
            }

            var process = Process.GetProcessById(processId);
            if (HasExited(process) ||
                !string.Equals(process.ProcessName, "cloudflared", StringComparison.OrdinalIgnoreCase) ||
                process.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks)
            {
                process.Dispose();
                ClearTrackedProcess(processId);
                return false;
            }

            lock (_sync)
            {
                if (_managedProcess is not null && !HasExited(_managedProcess))
                {
                    process.Dispose();
                    return true;
                }

                TrackManagedProcess(process, persist: false);
                if (_managedProcess is null || HasExited(_managedProcess))
                    return false;
            }
            WriteTunnelLog($"Adopted tracked DCR tunnel process (PID {processId}).");
            return true;
        }
        catch
        {
            ClearTrackedProcess();
            return false;
        }
    }

    private void TrackManagedProcess(Process process, bool persist)
    {
        var processId = process.Id;
        _managedProcess = process;
        if (persist)
            PersistTrackedProcess(process);

        process.Exited += (_, _) => HandleManagedProcessExit(process, processId);
        process.EnableRaisingEvents = true;
        if (HasExited(process))
            HandleManagedProcessExit(process, processId);
    }

    private void HandleManagedProcessExit(Process process, int processId)
    {
        var shouldClear = false;
        try { WriteTunnelLog($"DCR cloudflared exited. ExitCode={process.ExitCode}"); } catch { }
        lock (_sync)
        {
            if (ReferenceEquals(_managedProcess, process))
            {
                _managedProcess = null;
                shouldClear = true;
            }
        }
        if (shouldClear)
            ClearTrackedProcess(processId);
        RaiseStatusChanged();
        try { process.Dispose(); } catch { }
    }

    private void PersistTrackedProcess(Process process)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_trackedProcessFile)!);
            var tempPath = _trackedProcessFile + ".tmp";
            File.WriteAllLines(tempPath,
            [
                process.Id.ToString(),
                process.StartTime.ToUniversalTime().Ticks.ToString(),
                GetTunnelTarget()
            ]);
            File.Move(tempPath, _trackedProcessFile, overwrite: true);
        }
        catch (Exception ex)
        {
            WriteTunnelLog("WARNING: Could not persist DCR tunnel PID: " + ex.GetBaseException().Message);
        }
    }

    private void ClearTrackedProcess(int? expectedProcessId = null)
    {
        try
        {
            if (!File.Exists(_trackedProcessFile))
                return;
            if (expectedProcessId.HasValue)
            {
                var firstLine = File.ReadLines(_trackedProcessFile).FirstOrDefault();
                if (!int.TryParse(firstLine, out var storedProcessId) || storedProcessId != expectedProcessId.Value)
                    return;
            }
            File.Delete(_trackedProcessFile);
        }
        catch { }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return true; }
    }

    private void SetError(string message)
    {
        _lastError = message;
        WriteTunnelLog("ERROR: " + message);
        RaiseStatusChanged();
    }

    private static void WriteTunnelLog(string message)
    {
        try
        {
            Directory.CreateDirectory(ApiLog.LogDirectory);
            File.AppendAllText(ApiLog.TunnelLogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void RaiseStatusChanged()
    {
        try { StatusChanged?.Invoke(this, EventArgs.Empty); }
        catch { }
    }

    public void Dispose()
    {
        _monitorCts.Cancel();
        try { _monitorTask?.Wait(1000); } catch { }

        if (_options.StopWithApi)
        {
            try { StopTunnel(); } catch { }
        }

        _monitorCts.Dispose();
        lock (_sync)
        {
            try { _managedProcess?.Dispose(); } catch { }
            _managedProcess = null;
        }
    }
}
