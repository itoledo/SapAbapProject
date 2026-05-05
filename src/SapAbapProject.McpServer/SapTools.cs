using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;

namespace SapAbapProject.McpServer;

/// <summary>
/// MCP tools that expose the SAP ABAP repository to an LLM agent.
/// All tools are read-only (the underlying RFCs are SE37/SE11 metadata reads,
/// plus a generic RFC_READ_TABLE for ad-hoc queries).
/// </summary>
[McpServerToolType]
public sealed class SapTools
{
    private readonly IAbapExtractor _extractor;
    private readonly ILogger<SapTools> _logger;

    public SapTools(IAbapExtractor extractor, ILogger<SapTools> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    [McpServerTool, Description("Pings the SAP system via RFC_PING. Use this first to verify connectivity.")]
    public async Task<string> SapTestConnection(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_test_connection");
        await _extractor.TestConnectionAsync(cancellationToken);
        return "ok";
    }

    [McpServerTool, Description("Lists ABAP packages (DEVCLASS) matching a name pattern. Use '*' as wildcard.")]
    public Task<IReadOnlyList<string>> SapListPackages(
        CancellationToken cancellationToken,
        [Description("Pattern with '*' wildcards, e.g. 'Z*' for custom packages. Default '*' matches all.")] string pattern = "*")
    {
        _logger.LogInformation("Tool: sap_list_packages pattern={Pattern}", pattern);
        return _extractor.GetPackagesAsync(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern, cancellationToken);
    }

    [McpServerTool, Description("Lists function groups (FUGR), optionally filtered to a single package.")]
    public Task<IReadOnlyList<string>> SapListFunctionGroups(
        CancellationToken cancellationToken,
        [Description("Optional package name to filter by, or null/empty for all packages.")] string? package = null)
    {
        _logger.LogInformation("Tool: sap_list_function_groups package={Package}", package);
        return _extractor.GetFunctionGroupsAsync(string.IsNullOrWhiteSpace(package) ? null : package, cancellationToken);
    }

    [McpServerTool, Description("Lists function modules matching the given criteria (name pattern, package, function group). Returns lightweight summaries; use sap_get_function_module to fetch full source.")]
    public async Task<IReadOnlyList<SapObjectSummaryResponse>> SapListFunctionModules(
        CancellationToken cancellationToken,
        [Description("Name pattern with '*' wildcards (e.g. 'Z_*'). Optional.")] string? namePattern = null,
        [Description("Package (DEVCLASS) to restrict to. Optional.")] string? package = null,
        [Description("Function group to restrict to. Optional.")] string? functionGroup = null,
        [Description("Maximum rows to return. Default 200.")] int maxRows = 200)
    {
        _logger.LogInformation("Tool: sap_list_function_modules pattern={Pattern} package={Package} group={Group}",
            namePattern, package, functionGroup);
        var results = await _extractor.ListFunctionModulesAsync(
            namePattern: NullIfBlank(namePattern),
            packageFilter: NullIfBlank(package),
            functionGroupFilter: NullIfBlank(functionGroup),
            maxRows: maxRows > 0 ? maxRows : 200,
            cancellationToken: cancellationToken);
        return results.Select(SapObjectSummaryResponse.From).ToList();
    }

    [McpServerTool, Description("Lists transparent tables (and optionally structures) matching a name pattern, optionally filtered by package.")]
    public async Task<IReadOnlyList<SapObjectSummaryResponse>> SapListTables(
        CancellationToken cancellationToken,
        [Description("Table name pattern with '*' wildcards (e.g. 'Z*'). Optional.")] string? namePattern = null,
        [Description("Package (DEVCLASS) to restrict to. Optional.")] string? package = null,
        [Description("If true, also include INTTAB structures. Default false (transparent tables only).")] bool includeStructures = false,
        [Description("Maximum rows to return. Default 200.")] int maxRows = 200)
    {
        _logger.LogInformation("Tool: sap_list_tables pattern={Pattern} package={Package} structs={IncludeStructures}",
            namePattern, package, includeStructures);
        var results = await _extractor.ListTablesAsync(
            namePattern: NullIfBlank(namePattern),
            packageFilter: NullIfBlank(package),
            includeStructures: includeStructures,
            maxRows: maxRows > 0 ? maxRows : 200,
            cancellationToken: cancellationToken);
        return results.Select(SapObjectSummaryResponse.From).ToList();
    }

    [McpServerTool, Description("Returns the source code and signature of a single function module. Includes both rendered ABAP and structured parameter metadata.")]
    public async Task<SapObjectResponse?> SapGetFunctionModule(
        [Description("Function module name, e.g. 'BAPI_USER_GET_DETAIL'.")] string name,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_function_module name={Name}", name);
        var obj = await _extractor.GetFunctionModuleAsync(name, cancellationToken);
        return obj is null ? null : SapObjectResponse.From(obj);
    }

    [McpServerTool, Description("Returns the definition of a transparent table including all fields (name, type, key flag, length, decimals).")]
    public async Task<SapObjectResponse?> SapGetTable(
        [Description("Table name, e.g. 'MARA' or 'ZMY_TABLE'.")] string name,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_table name={Name}", name);
        var obj = await _extractor.GetTableAsync(name, cancellationToken);
        return obj is null ? null : SapObjectResponse.From(obj);
    }

    [McpServerTool, Description("Returns the definition of an INTTAB structure including all fields.")]
    public async Task<SapObjectResponse?> SapGetStructure(
        [Description("Structure name.")] string name,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_structure name={Name}", name);
        var obj = await _extractor.GetStructureAsync(name, cancellationToken);
        return obj is null ? null : SapObjectResponse.From(obj);
    }

    [McpServerTool, Description("Returns the definition of a data element including its referenced domain and labels.")]
    public async Task<SapObjectResponse?> SapGetDataElement(
        [Description("Data element name (ROLLNAME).")] string name,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_data_element name={Name}", name);
        var obj = await _extractor.GetDataElementAsync(name, cancellationToken);
        return obj is null ? null : SapObjectResponse.From(obj);
    }

    [McpServerTool, Description("Returns the definition of a domain including its data type and any fixed values.")]
    public async Task<SapObjectResponse?> SapGetDomain(
        [Description("Domain name (DOMNAME).")] string name,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_domain name={Name}", name);
        var obj = await _extractor.GetDomainAsync(name, cancellationToken);
        return obj is null ? null : SapObjectResponse.From(obj);
    }

    [McpServerTool, Description("Returns the definition of a table type including its row type and access mode.")]
    public async Task<SapObjectResponse?> SapGetTableType(
        [Description("Table type name (TYPENAME).")] string name,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_table_type name={Name}", name);
        var obj = await _extractor.GetTableTypeAsync(name, cancellationToken);
        return obj is null ? null : SapObjectResponse.From(obj);
    }

    [McpServerTool, Description("Reads rows from any SAP table via RFC_READ_TABLE. Read-only escape hatch for ad-hoc queries. WHERE clause uses native ABAP SQL syntax (e.g. \"MATNR LIKE 'Z%' AND MTART = 'FERT'\").")]
    public Task<TableReadResult> SapReadTable(
        CancellationToken cancellationToken,
        [Description("Table name (e.g. 'T001' for company codes, 'USR02' for users).")] string table,
        [Description("Field names to retrieve. Empty array returns all fields (capped by SAP's default).")] string[] fields,
        [Description("Optional WHERE clause in ABAP SQL syntax. No quotes around the whole thing — just the predicate.")] string? where = null,
        [Description("Maximum rows to return. Default 100. Cap at a few thousand to avoid memory blowups.")] int maxRows = 100)
    {
        _logger.LogInformation("Tool: sap_read_table table={Table} fieldCount={FieldCount} maxRows={MaxRows} where={Where}",
            table, fields?.Length ?? 0, maxRows, where);
        return _extractor.ReadTableAsync(
            tableName: table,
            fields: fields ?? Array.Empty<string>(),
            whereClause: NullIfBlank(where),
            maxRows: maxRows > 0 ? maxRows : 100,
            cancellationToken: cancellationToken);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
