using Microsoft.Extensions.Configuration;
using SapAbapProject.Core.Models;

namespace SapAbapProject.RfcExtractor.Tests;

/// <summary>
/// Shared fixture that loads SAP connection settings from testsettings.json
/// and ensures the RFC SDK is loaded once for all tests in the collection.
///
/// Configure your connection in testsettings.local.json (git-ignored).
/// </summary>
public sealed class SapTestFixture : IDisposable
{
    public IConfiguration Configuration { get; }
    public SapConnectionSettings ConnectionSettings { get; }
    public ImportOptions DefaultImportOptions { get; }
    public string SdkPath { get; }

    public SapTestFixture()
    {
        Configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("testsettings.json", optional: false)
            .AddJsonFile("testsettings.local.json", optional: true)
            .AddEnvironmentVariables("SAP_")
            .Build();

        var sap = Configuration.GetSection("Sap");
        SdkPath = sap["SdkPath"] ?? throw new InvalidOperationException(
            "Sap:SdkPath not configured in testsettings.json");

        ConnectionSettings = new SapConnectionSettings
        {
            AppServerHost = sap["AppServerHost"] ?? throw new InvalidOperationException("Sap:AppServerHost required"),
            SystemNumber = sap["SystemNumber"] ?? "00",
            Client = sap["Client"] ?? "100",
            User = sap["User"] ?? throw new InvalidOperationException("Sap:User required"),
            Password = sap["Password"] ?? throw new InvalidOperationException("Sap:Password required"),
            Language = sap["Language"] ?? "EN",
            SapRouter = string.IsNullOrWhiteSpace(sap["SapRouter"]) ? null : sap["SapRouter"],
        };

        var import = Configuration.GetSection("Import");
        var objectTypeNames = import.GetSection("ObjectTypes").Get<string[]>() ?? [
            "FunctionModule", "DataElement", "Domain", "TransparentTable", "Structure", "TableType"
        ];

        DefaultImportOptions = new ImportOptions
        {
            Packages = import.GetSection("Packages").Get<string[]>() ?? ["ZTEST"],
            ObjectTypes = objectTypeNames
                .Select(n => Enum.Parse<AbapObjectType>(n))
                .ToList(),
            FunctionModuleNamePattern = import["FunctionModuleNamePattern"],
            FunctionGroupFilter = string.IsNullOrWhiteSpace(import["FunctionGroupFilter"])
                ? null : import["FunctionGroupFilter"],
            OverwriteExisting = bool.TryParse(import["OverwriteExisting"], out var ow) && ow,
            IncludeDocumentation = !bool.TryParse(import["IncludeDocumentation"], out var doc) || doc,
            IncludeSignature = !bool.TryParse(import["IncludeSignature"], out var sig) || sig,
        };

        // Ensure SDK is loaded
        if (!SapRfcSdkManager.IsLoaded)
        {
            if (!SapRfcSdkManager.EnsureSdkLoaded(SdkPath))
                throw new InvalidOperationException(
                    $"Failed to load SAP RFC SDK from '{SdkPath}'. Check Sap:SdkPath in testsettings.json.");
        }
    }

    public AbapObjectExtractor CreateExtractor() => new(ConnectionSettings);

    public string GetSetting(string key) => Configuration[key] ?? "";

    public void Dispose() { }
}

[CollectionDefinition("SAP")]
public class SapTestCollection : ICollectionFixture<SapTestFixture>;
