using SapAbapProject.Core.Models;

namespace SapAbapProject.Cli;

/// <summary>
/// Reads SAP connection settings from <c>SAP_*</c> environment variables (same names
/// as <c>SapAbapProject.McpServer</c>), letting CLI flags override individual values.
/// </summary>
internal static class CliConfig
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
    public const string EnvDescriptionLanguages = "SAP_DESCRIPTION_LANGUAGES";

    public static string? ResolveSdkPath(string? explicitPath) =>
        FirstNonBlank(explicitPath, Environment.GetEnvironmentVariable(EnvSdkPath));

    public static SapConnectionSettings BuildConnectionSettings(CliOverrides overrides)
    {
        var host = Require(EnvHost, overrides.Host);
        var user = Require(EnvUser, overrides.User);
        var password = Require(EnvPassword, overrides.Password);

        return new SapConnectionSettings
        {
            AppServerHost = host,
            SystemNumber = FirstNonBlank(overrides.SystemNumber, Environment.GetEnvironmentVariable(EnvSysNr)) ?? "00",
            Client = FirstNonBlank(overrides.Client, Environment.GetEnvironmentVariable(EnvClient)) ?? "100",
            User = user,
            Password = password,
            Language = FirstNonBlank(overrides.Language, Environment.GetEnvironmentVariable(EnvLanguage)) ?? "EN",
            SapRouter = FirstNonBlank(overrides.SapRouter, Environment.GetEnvironmentVariable(EnvSapRouter)),
            MessageServerHost = Environment.GetEnvironmentVariable(EnvMessageHost),
            Group = Environment.GetEnvironmentVariable(EnvLogonGroup),
            SystemId = Environment.GetEnvironmentVariable(EnvSystemId),
            UseSncConnection = string.Equals(Environment.GetEnvironmentVariable(EnvSncMode), "1", StringComparison.Ordinal),
            SncPartnerName = Environment.GetEnvironmentVariable(EnvSncPartner),
            DescriptionLanguages = ParseDescriptionLanguages(Environment.GetEnvironmentVariable(EnvDescriptionLanguages)),
        };
    }

    private static IReadOnlyList<string>? ParseDescriptionLanguages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string Require(string envName, string? cliOverride)
    {
        var value = FirstNonBlank(cliOverride, Environment.GetEnvironmentVariable(envName));
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Missing required SAP setting. Set environment variable '{envName}' " +
                "or pass it via CLI flags (e.g. --user/--password/--host). See `sap-cli --help`.");
        return value!;
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v;
        return null;
    }
}

/// <summary>Connection-related values that can be passed on the command line.</summary>
internal sealed record CliOverrides
{
    public string? Host { get; init; }
    public string? SystemNumber { get; init; }
    public string? Client { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public string? Language { get; init; }
    public string? SapRouter { get; init; }
    public string? SdkPath { get; init; }
}
