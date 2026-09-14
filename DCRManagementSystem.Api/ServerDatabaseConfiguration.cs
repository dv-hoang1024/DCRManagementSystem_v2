using System.Diagnostics;
using System.Drawing;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Forms;
using DCRManagementSystem.Helpers;
using Microsoft.Data.SqlClient;

namespace DCRManagementSystem.Api;

internal static class ServerDatabaseConfiguration
{
    internal const string DcrConnectionEnvironmentVariable = "DCR_CONNECTION_STRING";
    internal const string GgpConnectionEnvironmentVariable = "ConnectionStrings__ProdDb";
    internal const string ConfigureArgument = "--configure-server";

    public static void RefreshProcessEnvironmentFromMachine()
    {
        RefreshOne(DcrConnectionEnvironmentVariable);
        RefreshOne(GgpConnectionEnvironmentVariable);
    }

    private static void RefreshOne(string name)
    {
        try
        {
            var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(value))
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        }
        catch
        {
            // Reading Machine environment may be unavailable in very restricted contexts.
            // AppSettings will still fall back to the process/appsettings configuration.
        }
    }

    public static bool HasUsableDcrConfiguration(string apiSettingsPath)
    {
        try
        {
            RefreshProcessEnvironmentFromMachine();
            var settings = AppSettings.LoadFromFile(apiSettingsPath);
            return !settings.UseRemoteApi && !string.IsNullOrWhiteSpace(settings.ConnectionString);
        }
        catch
        {
            return false;
        }
    }

    public static bool EnsureConfiguredBeforeStartup(string apiSettingsPath)
    {
        if (HasUsableDcrConfiguration(apiSettingsPath))
            return true;

        if (!Environment.UserInteractive)
            return false;

        var result = MessageBox.Show(
            "DCR API chưa có cấu hình SQL Server hợp lệ.\r\n\r\n" +
            "Bạn có muốn cấu hình ngay bây giờ không?\r\n\r\n" +
            "Cửa sổ cấu hình sẽ lưu kết nối DCR và GGP/WebDashboard ở cấp máy (Machine) để khi chuyển server chỉ cần cấu hình lại một lần.",
            "DCR API - Cấu hình SQL Server",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result != DialogResult.Yes)
            return false;

        if (!OpenConfiguratorAndWait(apiSettingsPath))
            return false;

        RefreshProcessEnvironmentFromMachine();
        return HasUsableDcrConfiguration(apiSettingsPath);
    }

    public static bool OpenConfiguratorAndWait(string apiSettingsPath)
    {
        return LaunchConfiguratorProcess(elevate: !IsAdministrator());
    }

    private static bool LaunchConfiguratorProcess(bool elevate)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                throw new InvalidOperationException("Không xác định được DCRManagementSystem.Api.exe để mở cửa sổ cấu hình.");

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = ConfigureArgument,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (elevate)
                startInfo.Verb = "runas";

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit();
            RefreshProcessEnvironmentFromMachine();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                "Cấu hình SQL cần quyền Administrator để lưu cho toàn máy. Yêu cầu UAC đã bị hủy.",
                "DCR API - Cấu hình SQL Server",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể mở cấu hình SQL Server.\r\n\r\n" + ex.Message,
                "DCR API - Cấu hình SQL Server",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    public static int RunStandaloneConfigurator(string apiSettingsPath)
    {
        if (!Environment.UserInteractive)
            return 1;

        if (!IsAdministrator())
            return LaunchConfiguratorProcess(elevate: true) ? 0 : 1;

        return ShowConfigurationDialog(apiSettingsPath) ? 0 : 1;
    }

    private static bool ShowConfigurationDialog(string apiSettingsPath)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new ServerDatabaseConfigurationForm(apiSettingsPath);
        return form.ShowDialog() == DialogResult.OK;
    }

    public static bool IsLikelyDatabaseStartupError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            if (current is SqlException)
                return true;

            var message = current.Message ?? string.Empty;
            if (message.Contains("SQL Username", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("SQL Password", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Login failed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Cannot open database", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("DCR_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private sealed class ServerDatabaseConfigurationForm : Form
    {
        private readonly string _apiSettingsPath;
        private readonly TextBox _txtServer = new();
        private readonly NumericUpDown _numPort = new();
        private readonly TextBox _txtDcrDatabase = new();
        private readonly TextBox _txtGgpDatabase = new();
        private readonly ComboBox _cmbAuthentication = new();
        private readonly TextBox _txtUsername = new();
        private readonly TextBox _txtPassword = new();
        private readonly CheckBox _chkEncrypt = new();
        private readonly CheckBox _chkTrustCertificate = new();
        private readonly Label _lblStatus = new();
        private readonly Button _btnTest = new();
        private readonly Button _btnSave = new();
        private readonly Button _btnCancel = new();
        private bool _lastDcrTestSucceeded;
        private bool _lastGgpTestSucceeded;

        public ServerDatabaseConfigurationForm(string apiSettingsPath)
        {
            _apiSettingsPath = apiSettingsPath;
            Text = "DCR API - Cấu hình Server";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(610, 520);
            Font = new Font("Segoe UI", 9F);

            BuildUi();
            LoadDefaults();
            UpdateAuthenticationFields();
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = "Cấu hình kết nối SQL cho DCR API Server",
                Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 20)
            };
            var subtitle = new Label
            {
                Text = "Cấu hình này dùng cho DCRManagement và GGP/WebDashboard trên máy server. Password không được ghi vào appsettings/source.",
                AutoSize = false,
                Location = new Point(27, 56),
                Size = new Size(555, 42),
                ForeColor = Color.DimGray
            };
            Controls.Add(title);
            Controls.Add(subtitle);

            var table = new TableLayoutPanel
            {
                Location = new Point(28, 105),
                Size = new Size(550, 285),
                ColumnCount = 2,
                RowCount = 9,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < table.RowCount; i++)
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));

            ConfigureTextBox(_txtServer);
            _numPort.Minimum = 0;
            _numPort.Maximum = 65535;
            _numPort.Width = 120;
            ConfigureTextBox(_txtDcrDatabase);
            ConfigureTextBox(_txtGgpDatabase);
            _cmbAuthentication.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbAuthentication.Items.AddRange(new object[] { "SQL Server Authentication", "Windows Authentication" });
            _cmbAuthentication.SelectedIndexChanged += (_, _) => UpdateAuthenticationFields();
            ConfigureTextBox(_txtUsername);
            ConfigureTextBox(_txtPassword);
            _txtPassword.UseSystemPasswordChar = true;
            _chkEncrypt.Text = "Encrypt=True";
            _chkEncrypt.AutoSize = true;
            _chkTrustCertificate.Text = "TrustServerCertificate=True";
            _chkTrustCertificate.AutoSize = true;

            AddRow(table, 0, "SQL Server", _txtServer);
            AddRow(table, 1, "Port", _numPort);
            AddRow(table, 2, "DCR Database", _txtDcrDatabase);
            AddRow(table, 3, "GGP Database", _txtGgpDatabase);
            AddRow(table, 4, "Authentication", _cmbAuthentication);
            AddRow(table, 5, "Username", _txtUsername);
            AddRow(table, 6, "Password", _txtPassword);

            var securityPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            securityPanel.Controls.Add(_chkEncrypt);
            securityPanel.Controls.Add(_chkTrustCertificate);
            AddRow(table, 7, "SQL Security", securityPanel);

            var note = new Label
            {
                Text = "Lưu ở Machine environment: DCR_CONNECTION_STRING + ConnectionStrings__ProdDb",
                AutoSize = true,
                ForeColor = Color.SteelBlue,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            AddRow(table, 8, "Nơi lưu", note);
            Controls.Add(table);

            _lblStatus.Location = new Point(28, 398);
            _lblStatus.Size = new Size(550, 48);
            _lblStatus.Text = "Nhấn 'Kiểm tra kết nối' trước khi lưu.";
            _lblStatus.ForeColor = Color.DimGray;
            Controls.Add(_lblStatus);

            _btnTest.Text = "Kiểm tra kết nối";
            _btnTest.Size = new Size(145, 38);
            _btnTest.Location = new Point(28, 458);
            _btnTest.Click += async (_, _) => await TestConnectionsAsync();

            _btnSave.Text = "Lưu & Khởi động API";
            _btnSave.Size = new Size(180, 38);
            _btnSave.Location = new Point(280, 458);
            _btnSave.Click += async (_, _) => await SaveAsync();

            _btnCancel.Text = "Hủy";
            _btnCancel.Size = new Size(110, 38);
            _btnCancel.Location = new Point(468, 458);
            _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_btnTest);
            Controls.Add(_btnSave);
            Controls.Add(_btnCancel);
            AcceptButton = _btnSave;
            CancelButton = _btnCancel;
        }

        private static void ConfigureTextBox(TextBox textBox)
        {
            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 3, 0, 3);
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            var lbl = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
            control.Dock = control is NumericUpDown ? DockStyle.Left : DockStyle.Fill;
            table.Controls.Add(lbl, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private void LoadDefaults()
        {
            var defaults = ReadDefaultsFromApiSettings(_apiSettingsPath);
            var dcrCs = Environment.GetEnvironmentVariable(DcrConnectionEnvironmentVariable, EnvironmentVariableTarget.Machine);
            var ggpCs = Environment.GetEnvironmentVariable(GgpConnectionEnvironmentVariable, EnvironmentVariableTarget.Machine);

            SqlConnectionStringBuilder? dcrBuilder = TryParseConnectionString(dcrCs);
            SqlConnectionStringBuilder? ggpBuilder = TryParseConnectionString(ggpCs);
            var primary = dcrBuilder ?? ggpBuilder;

            _txtServer.Text = primary is null ? defaults.ServerAddress : ExtractServer(primary.DataSource, out _);
            _numPort.Value = primary is null
                ? Math.Clamp(defaults.Port, 0, 65535)
                : Math.Clamp(ExtractPort(primary.DataSource), 0, 65535);
            _txtDcrDatabase.Text = dcrBuilder?.InitialCatalog ?? defaults.DcrDatabase;
            _txtGgpDatabase.Text = ggpBuilder?.InitialCatalog ?? "DB_PARTLISTMAKER";

            var windows = primary?.IntegratedSecurity ?? defaults.WindowsAuthentication;
            _cmbAuthentication.SelectedIndex = windows ? 1 : 0;
            _txtUsername.Text = primary?.UserID ?? defaults.Username;
            _txtPassword.Text = primary?.Password ?? string.Empty;
            _chkEncrypt.Checked = primary is null ? defaults.Encrypt : ReadBoolean(primary, "Encrypt", true);
            _chkTrustCertificate.Checked = primary is null ? defaults.TrustServerCertificate : ReadBoolean(primary, "TrustServerCertificate", true);
        }

        private static (string ServerAddress, int Port, string DcrDatabase, bool WindowsAuthentication, string Username, bool Encrypt, bool TrustServerCertificate) ReadDefaultsFromApiSettings(string path)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var db = document.RootElement.GetProperty("Database");
                return (
                    db.TryGetProperty("ServerAddress", out var server) ? server.GetString() ?? "172.168.8.183" : "172.168.8.183",
                    db.TryGetProperty("Port", out var port) ? port.GetInt32() : 3333,
                    db.TryGetProperty("DatabaseName", out var name) ? name.GetString() ?? "DCRManagement" : "DCRManagement",
                    db.TryGetProperty("AuthenticationMode", out var auth) && string.Equals(auth.GetString(), DatabaseAuthenticationModes.Windows, StringComparison.OrdinalIgnoreCase),
                    db.TryGetProperty("Username", out var user) ? user.GetString() ?? string.Empty : string.Empty,
                    !db.TryGetProperty("Encrypt", out var encrypt) || encrypt.GetBoolean(),
                    !db.TryGetProperty("TrustServerCertificate", out var trust) || trust.GetBoolean());
            }
            catch
            {
                return ("172.168.8.183", 3333, "DCRManagement", false, string.Empty, true, true);
            }
        }

        private static SqlConnectionStringBuilder? TryParseConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return null;
            try { return new SqlConnectionStringBuilder(connectionString); }
            catch { return null; }
        }

        private static bool ReadBoolean(SqlConnectionStringBuilder builder, string key, bool fallback)
        {
            try { return builder.ContainsKey(key) ? Convert.ToBoolean(builder[key]) : fallback; }
            catch { return fallback; }
        }

        private static string ExtractServer(string dataSource, out int port)
        {
            port = ExtractPort(dataSource);
            var index = dataSource.LastIndexOf(',');
            return index > 0 ? dataSource[..index].Trim() : dataSource.Trim();
        }

        private static int ExtractPort(string dataSource)
        {
            var index = dataSource.LastIndexOf(',');
            if (index > 0 && int.TryParse(dataSource[(index + 1)..], out var port))
                return port;
            return 0;
        }

        private void UpdateAuthenticationFields()
        {
            var sqlAuth = _cmbAuthentication.SelectedIndex != 1;
            _txtUsername.Enabled = sqlAuth;
            _txtPassword.Enabled = sqlAuth;
        }

        private DatabaseConnectionSettings CreateSettings(string database)
        {
            return new DatabaseConnectionSettings
            {
                ServerAddress = _txtServer.Text.Trim(),
                Port = (int)_numPort.Value,
                DatabaseName = database.Trim(),
                AuthenticationMode = _cmbAuthentication.SelectedIndex == 1 ? DatabaseAuthenticationModes.Windows : DatabaseAuthenticationModes.SqlServer,
                Username = _txtUsername.Text.Trim(),
                Password = _txtPassword.Text,
                Encrypt = _chkEncrypt.Checked,
                TrustServerCertificate = _chkTrustCertificate.Checked,
                ConnectTimeoutSeconds = 10,
                CommandTimeoutSeconds = 30,
                MinPoolSize = 4,
                MaxPoolSize = 64
            };
        }

        private async Task<bool> TestOneAsync(DatabaseConnectionSettings settings)
        {
            settings.Validate();
            await using var connection = new SqlConnection(settings.BuildConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 10;
            var value = await command.ExecuteScalarAsync();
            return Convert.ToInt32(value) == 1;
        }

        private async Task TestConnectionsAsync()
        {
            SetBusy(true);
            try
            {
                var dcr = CreateSettings(_txtDcrDatabase.Text);
                var ggp = CreateSettings(_txtGgpDatabase.Text);

                _lastDcrTestSucceeded = await TestOneAsync(dcr);
                string ggpMessage;
                try
                {
                    _lastGgpTestSucceeded = await TestOneAsync(ggp);
                    ggpMessage = "GGP/WebDashboard: OK";
                }
                catch (Exception ex)
                {
                    _lastGgpTestSucceeded = false;
                    ggpMessage = "GGP/WebDashboard: " + ex.GetBaseException().Message;
                }

                _lblStatus.ForeColor = _lastDcrTestSucceeded && _lastGgpTestSucceeded ? Color.DarkGreen : Color.DarkOrange;
                _lblStatus.Text = $"DCR: {(_lastDcrTestSucceeded ? "OK" : "FAIL")}   |   {ggpMessage}";
            }
            catch (Exception ex)
            {
                _lastDcrTestSucceeded = false;
                _lastGgpTestSucceeded = false;
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "DCR: " + ex.GetBaseException().Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task SaveAsync()
        {
            await TestConnectionsAsync();
            if (!_lastDcrTestSucceeded)
            {
                MessageBox.Show(
                    "Không thể lưu vì kết nối DCR Database chưa thành công.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!_lastGgpTestSucceeded)
            {
                var continueSave = MessageBox.Show(
                    "DCR Database kết nối được nhưng GGP/WebDashboard Database chưa kết nối được.\r\n\r\n" +
                    "Bạn vẫn muốn lưu cấu hình không? WebDashboard/Production-Warehouse có thể chưa hoạt động cho đến khi GGP Database kết nối được.",
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (continueSave != DialogResult.Yes)
                    return;
            }

            try
            {
                var dcr = CreateSettings(_txtDcrDatabase.Text);
                var ggp = CreateSettings(_txtGgpDatabase.Text);
                var dcrConnection = dcr.BuildConnectionString();
                var ggpConnection = ggp.BuildConnectionString();

                Environment.SetEnvironmentVariable(DcrConnectionEnvironmentVariable, dcrConnection, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable(GgpConnectionEnvironmentVariable, ggpConnection, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable(DcrConnectionEnvironmentVariable, dcrConnection, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(GgpConnectionEnvironmentVariable, ggpConnection, EnvironmentVariableTarget.Process);

                MessageBox.Show(
                    "Đã lưu cấu hình SQL cho toàn máy.\r\n\r\n" +
                    "DCR API: DCR_CONNECTION_STRING\r\n" +
                    "GGP/WebDashboard: ConnectionStrings__ProdDb\r\n\r\n" +
                    "Nếu WebDashboard/DCR API đang chạy bằng process khác, hãy khởi động lại process đó để nhận cấu hình mới.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể lưu cấu hình Machine environment. Hãy chắc chắn cửa sổ đang chạy với quyền Administrator.\r\n\r\n" + ex.GetBaseException().Message,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy)
        {
            _btnTest.Enabled = !busy;
            _btnSave.Enabled = !busy;
            _btnCancel.Enabled = !busy;
            UseWaitCursor = busy;
        }
    }
}
