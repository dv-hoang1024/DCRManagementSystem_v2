namespace DCRManagementSystem.Helpers;

public static class ApplicationDataPaths
{
    public static string PersistentRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DCRManagementSystem");

    public static string DatabaseConfigPath => Path.Combine(
        PersistentRoot,
        "Data",
        "Config",
        "database.config.json");

    public static string ApiClientConfigPath => Path.Combine(
        PersistentRoot,
        "Data",
        "Config",
        "api-client.config.json");

    public static string RememberedWindowsLoginPath => Path.Combine(
        PersistentRoot,
        "Auth",
        "remembered-windows-login.dat");

    public static string ProvisioningRoot => Path.Combine(AppContext.BaseDirectory, "Provisioning");

    public static string ProvisionedSigningKeyPath => Path.Combine(
        ProvisioningRoot,
        "approval-signing.key");

    public static string ProvisionedDatabaseConfigPath => Path.Combine(
        ProvisioningRoot,
        "database.config.json");

    public static string ResolvePersistentDataPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("Đường dẫn dữ liệu cục bộ không hợp lệ.");

        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        return Path.Combine(PersistentRoot, configuredPath);
    }
}
