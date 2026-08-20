using SapAbapProject.Core.Models;

namespace SapAbapProject.McpServer;

/// <summary>
/// Reads MCP server configuration from environment variables.
/// All SAP credentials live here — the calling agent never sees them.
/// </summary>
internal static class SapMcpConfiguration
{
    public const string EnvSdkPath = "SAP_RFC_SDK_PATH";
    public const string EnvHost = "SAP_HOST";
    public const string EnvSysNr = "SAP_SYSNR";
    public const string EnvClient = "SAP_CLIENT";
    public const string EnvUser = "SAP_USER";
    public const string EnvPassword = "SAP_PASSWORD";
    public const string EnvLanguage = "SAP_LANGUAGE";
    public const string EnvSapRouter = "SAP_ROUTER";
    public const string EnvMessageHost = "SAP_MSHOST";
    public const string EnvLogonGroup = "SAP_GROUP";
    public const string EnvSystemId = "SAP_SYSID";
    public const string EnvSncMode = "SAP_SNC_MODE";
    public const string EnvSncPartner = "SAP_SNC_PARTNERNAME";
    public const string EnvSncLibrary = "SAP_SNC_LIB";
    public const string EnvSncQop = "SAP_SNC_QOP";
    public const string EnvDescriptionLanguages = "SAP_DESCRIPTION_LANGUAGES";

    public static string? GetSdkPath() => Environment.GetEnvironmentVariable(EnvSdkPath);

    public static SapConnectionSettings BuildConnectionSettings()
    {
        var host = Environment.GetEnvironmentVariable(EnvHost);
        var messageHost = Environment.GetEnvironmentVariable(EnvMessageHost);
        var group = Environment.GetEnvironmentVariable(EnvLogonGroup);
        var systemId = Environment.GetEnvironmentVariable(EnvSystemId);
        ValidateConnectionTarget(host, messageHost, group, systemId);

        var user = Require(EnvUser);
        var password = Require(EnvPassword);

        return new SapConnectionSettings
        {
            AppServerHost = host ?? string.Empty,
            SystemNumber = Environment.GetEnvironmentVariable(EnvSysNr) ?? "00",
            Client = Environment.GetEnvironmentVariable(EnvClient) ?? "100",
            User = user,
            Password = password,
            Language = Environment.GetEnvironmentVariable(EnvLanguage) ?? "EN",
            SapRouter = Environment.GetEnvironmentVariable(EnvSapRouter),
            MessageServerHost = messageHost,
            Group = group,
            SystemId = systemId,
            UseSncConnection = string.Equals(Environment.GetEnvironmentVariable(EnvSncMode), "1", StringComparison.Ordinal),
            SncPartnerName = Environment.GetEnvironmentVariable(EnvSncPartner),
            SncLibraryPath = Environment.GetEnvironmentVariable(EnvSncLibrary),
            SncQop = Environment.GetEnvironmentVariable(EnvSncQop),
            DescriptionLanguages = ParseDescriptionLanguages(Environment.GetEnvironmentVariable(EnvDescriptionLanguages)),
        };
    }

    private static void ValidateConnectionTarget(
        string? host,
        string? messageHost,
        string? group,
        string? systemId)
    {
        var usesMessageServer =
            !string.IsNullOrWhiteSpace(messageHost) ||
            !string.IsNullOrWhiteSpace(group) ||
            !string.IsNullOrWhiteSpace(systemId);

        if (usesMessageServer)
        {
            if (string.IsNullOrWhiteSpace(messageHost) ||
                string.IsNullOrWhiteSpace(group) ||
                string.IsNullOrWhiteSpace(systemId))
            {
                throw new InvalidOperationException(
                    $"{EnvMessageHost}, {EnvLogonGroup}, and {EnvSystemId} must all be set for message-server logon.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException(
                $"Set {EnvHost} for direct application-server logon, or set " +
                $"{EnvMessageHost}, {EnvLogonGroup}, and {EnvSystemId} for load-balanced logon.");
    }

    private static IReadOnlyList<string>? ParseDescriptionLanguages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string Require(string envName)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Required environment variable '{envName}' is not set. " +
                "See README for the full list of SAP_* environment variables.");
        return value;
    }
}
