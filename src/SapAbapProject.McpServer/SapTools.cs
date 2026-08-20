using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;

namespace SapAbapProject.McpServer;

[McpServerToolType]
public sealed class SapTools
{
    private static readonly IReadOnlyList<DictionaryObjectTypeResponse> DictionaryObjectTypes =
    [
        new("table", "TABL", "Transparent database tables"),
        new("structure", "TABL", "ABAP Dictionary structures (INTTAB)"),
        new("view", "VIEW", "Dictionary views"),
        new("data_element", "DTEL", "Data elements"),
        new("domain", "DOMA", "Domains"),
        new("table_type", "TTYP", "Table types"),
        new("search_help", "SHLP", "Search helps"),
        new("lock_object", "ENQU", "Lock objects"),
        new("type_group", "TYPE", "Type groups"),
        new("cds_ddl_source", "DDLS", "CDS DDL sources"),
    ];

    private readonly IAbapExtractor _extractor;
    private readonly ILogger<SapTools> _logger;

    public SapTools(IAbapExtractor extractor, ILogger<SapTools> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    [McpServerTool(
        Name = "sap_test_connection",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Pings the SAP system via RFC_PING. Use this first to verify connectivity.")]
    public async Task<ConnectionTestResponse> SapTestConnection(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_test_connection");
        await _extractor.TestConnectionAsync(cancellationToken);
        return new ConnectionTestResponse(true);
    }

    [McpServerTool(
        Name = "sap_list_packages",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists ABAP packages (DEVCLASS) matching a name pattern. Use '*' as wildcard.")]
    public Task<IReadOnlyList<string>> SapListPackages(
        CancellationToken cancellationToken,
        [Description("Pattern with '*' wildcards, e.g. 'Z*'. Default '*' matches all.")] string pattern = "*")
    {
        _logger.LogInformation("Tool: sap_list_packages pattern={Pattern}", pattern);
        return _extractor.GetPackagesAsync(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern, cancellationToken);
    }

    [McpServerTool(
        Name = "sap_list_function_groups",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists ABAP function groups, optionally restricted to one package.")]
    public Task<IReadOnlyList<string>> SapListFunctionGroups(
        CancellationToken cancellationToken,
        [Description("Optional package name, or null/empty for all packages.")] string? package = null)
    {
        _logger.LogInformation("Tool: sap_list_function_groups package={Package}", package);
        return _extractor.GetFunctionGroupsAsync(NullIfBlank(package), cancellationToken);
    }

    [McpServerTool(
        Name = "sap_list_rfcs",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists remote-enabled SAP function modules (RFCs) by name, package, or function group.")]
    public Task<IReadOnlyList<SapObjectSummaryResponse>> SapListRfcs(
        CancellationToken cancellationToken,
        [Description("Name pattern with '*' wildcards, e.g. 'BAPI_*' or 'Z_*'.")] string? namePattern = null,
        [Description("Optional package (DEVCLASS).")] string? package = null,
        [Description("Optional function group.")] string? functionGroup = null,
        [Description("Maximum rows. Default 200, hard cap 10000.")] int maxRows = 200) =>
        ListFunctionModules(namePattern, package, functionGroup, maxRows, true, cancellationToken);

    [McpServerTool(
        Name = "sap_list_function_modules",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists function modules. Set remoteOnly=true to return only RFC-enabled modules.")]
    public Task<IReadOnlyList<SapObjectSummaryResponse>> SapListFunctionModules(
        CancellationToken cancellationToken,
        [Description("Name pattern with '*' wildcards.")] string? namePattern = null,
        [Description("Optional package (DEVCLASS).")] string? package = null,
        [Description("Optional function group.")] string? functionGroup = null,
        [Description("Maximum rows. Default 200, hard cap 10000.")] int maxRows = 200,
        [Description("Only return remote-enabled function modules.")] bool remoteOnly = false) =>
        ListFunctionModules(namePattern, package, functionGroup, maxRows, remoteOnly, cancellationToken);

    [McpServerTool(
        Name = "sap_list_repository_objects",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists any TADIR repository object type, enabling discovery beyond the built-in DDIC categories.")]
    public async Task<IReadOnlyList<SapRepositoryObjectResponse>> SapListRepositoryObjects(
        CancellationToken cancellationToken,
        [Description("Optional TADIR object type such as PROG, CLAS, INTF, FUGR, TABL, DTEL, DOMA, TTYP, VIEW, SHLP, ENQU, or DDLS.")] string? objectType = null,
        [Description("Optional object-name pattern with '*' wildcards.")] string? namePattern = null,
        [Description("Optional package (DEVCLASS).")] string? package = null,
        [Description("Maximum rows. Default 200, hard cap 10000.")] int maxRows = 200)
    {
        _logger.LogInformation(
            "Tool: sap_list_repository_objects type={Type} pattern={Pattern} package={Package}",
            objectType,
            namePattern,
            package);
        var results = await _extractor.ListRepositoryObjectsAsync(
            NullIfBlank(objectType),
            NullIfBlank(namePattern),
            NullIfBlank(package),
            maxRows,
            cancellationToken);
        return results.Select(SapRepositoryObjectResponse.From).ToList();
    }

    [McpServerTool(
        Name = "sap_list_dictionary_object_types",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the supported ABAP Dictionary categories and their TADIR object codes.")]
    public IReadOnlyList<DictionaryObjectTypeResponse> SapListDictionaryObjectTypes() => DictionaryObjectTypes;

    [McpServerTool(
        Name = "sap_list_dictionary_objects",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists ABAP Dictionary objects: tables, structures, views, data elements, domains, table types, search helps, lock objects, type groups, or CDS DDL sources.")]
    public async Task<IReadOnlyList<DictionaryObjectResponse>> SapListDictionaryObjects(
        [Description("Dictionary category returned by sap_list_dictionary_object_types.")] string dictionaryType,
        CancellationToken cancellationToken,
        [Description("Optional name pattern with '*' wildcards.")] string? namePattern = null,
        [Description("Optional package (DEVCLASS).")] string? package = null,
        [Description("Maximum rows. Default 200, hard cap 10000.")] int maxRows = 200)
    {
        var normalizedType = dictionaryType.Trim().ToLowerInvariant();
        var typeInfo = DictionaryObjectTypes.FirstOrDefault(type => type.Name == normalizedType)
            ?? throw new ArgumentException($"Unknown dictionary type '{dictionaryType}'.", nameof(dictionaryType));

        _logger.LogInformation(
            "Tool: sap_list_dictionary_objects type={Type} pattern={Pattern} package={Package}",
            normalizedType,
            namePattern,
            package);

        if (normalizedType is "table" or "structure")
        {
            var includeStructures = normalizedType == "structure";
            var results = await _extractor.ListTablesAsync(
                NullIfBlank(namePattern),
                NullIfBlank(package),
                includeStructures,
                maxRows,
                cancellationToken);

            return results
                .Where(item => normalizedType == "structure"
                    ? item.ObjectType == AbapObjectType.Structure
                    : item.ObjectType == AbapObjectType.TransparentTable)
                .Select(item => DictionaryObjectResponse.From(item, normalizedType, typeInfo.RepositoryType))
                .ToList();
        }

        var repositoryObjects = await _extractor.ListRepositoryObjectsAsync(
            typeInfo.RepositoryType,
            NullIfBlank(namePattern),
            NullIfBlank(package),
            maxRows,
            cancellationToken);
        return repositoryObjects
            .Select(item => DictionaryObjectResponse.From(item, normalizedType))
            .ToList();
    }

    [McpServerTool(
        Name = "sap_list_tables",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists transparent tables and optionally ABAP Dictionary structures.")]
    public async Task<IReadOnlyList<SapObjectSummaryResponse>> SapListTables(
        CancellationToken cancellationToken,
        [Description("Table name pattern with '*' wildcards.")] string? namePattern = null,
        [Description("Optional package (DEVCLASS).")] string? package = null,
        [Description("Also include INTTAB structures.")] bool includeStructures = false,
        [Description("Maximum rows. Default 200, hard cap 10000.")] int maxRows = 200)
    {
        var results = await _extractor.ListTablesAsync(
            NullIfBlank(namePattern),
            NullIfBlank(package),
            includeStructures,
            maxRows,
            cancellationToken);
        return results.Select(SapObjectSummaryResponse.From).ToList();
    }

    [McpServerTool(Name = "sap_get_function_module", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns source code and structured signature metadata for one function module.")]
    public async Task<SapObjectResponse?> SapGetFunctionModule(
        [Description("Function module name.")] string name,
        CancellationToken cancellationToken) =>
        ToResponse(await _extractor.GetFunctionModuleAsync(name, cancellationToken));

    [McpServerTool(Name = "sap_get_table", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns a transparent table definition and field metadata.")]
    public async Task<SapObjectResponse?> SapGetTable(
        [Description("Table name.")] string name,
        CancellationToken cancellationToken) =>
        ToResponse(await _extractor.GetTableAsync(name, cancellationToken));

    [McpServerTool(Name = "sap_get_structure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns an ABAP Dictionary structure definition and field metadata.")]
    public async Task<SapObjectResponse?> SapGetStructure(
        [Description("Structure name.")] string name,
        CancellationToken cancellationToken) =>
        ToResponse(await _extractor.GetStructureAsync(name, cancellationToken));

    [McpServerTool(Name = "sap_get_data_element", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns a data element definition, labels, and referenced domain.")]
    public async Task<SapObjectResponse?> SapGetDataElement(
        [Description("Data element name (ROLLNAME).")] string name,
        CancellationToken cancellationToken) =>
        ToResponse(await _extractor.GetDataElementAsync(name, cancellationToken));

    [McpServerTool(Name = "sap_get_domain", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns a domain definition and fixed values.")]
    public async Task<SapObjectResponse?> SapGetDomain(
        [Description("Domain name (DOMNAME).")] string name,
        CancellationToken cancellationToken) =>
        ToResponse(await _extractor.GetDomainAsync(name, cancellationToken));

    [McpServerTool(Name = "sap_get_table_type", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns a table type definition, row type, access mode, and key fields.")]
    public async Task<SapObjectResponse?> SapGetTableType(
        [Description("Table type name (TYPENAME).")] string name,
        CancellationToken cancellationToken) =>
        ToResponse(await _extractor.GetTableTypeAsync(name, cancellationToken));

    [McpServerTool(Name = "sap_get_abap_source", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns ABAP source for a function module, program, include, or function group main program.")]
    public Task<AbapSourceResult?> SapGetAbapSource(
        [Description("ABAP object name.")] string name,
        [Description("One of: function_module, program, include, function_group.")] string sourceKind,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_abap_source kind={Kind} name={Name}", sourceKind, name);
        return _extractor.GetAbapSourceAsync(name, sourceKind, cancellationToken);
    }

    [McpServerTool(Name = "sap_get_rfc_definition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns the live SAP RFC metadata for a remote-enabled function, including directions, scalar types, structures, tables, defaults, and exceptions.")]
    public Task<RfcFunctionDefinition> SapGetRfcDefinition(
        [Description("Remote-enabled function module name.")] string functionName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tool: sap_get_rfc_definition function={Function}", functionName);
        return _extractor.GetRfcFunctionDefinitionAsync(functionName, cancellationToken);
    }

    [McpServerTool(
        Name = "sap_execute_rfc",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Executes any RFC allowed for the configured SAP user. Call sap_get_rfc_definition first. This tool may modify SAP data, trigger commits, or execute business operations depending on the selected RFC.")]
    public Task<RfcExecutionResult> SapExecuteRfc(
        [Description("Remote-enabled function module name.")] string functionName,
        CancellationToken cancellationToken,
        [Description("RFC input parameters as a JSON object. Structures are objects and TABLES parameters are arrays of objects.")] Dictionary<string, JsonElement>? parameters = null,
        [Description("Maximum rows returned per output table. Default 500, hard cap 5000.")] int maxTableRows = 500)
    {
        _logger.LogWarning("Tool: sap_execute_rfc function={Function}", functionName);
        var convertedParameters = parameters?.ToDictionary(
            pair => pair.Key,
            pair => ConvertJson(pair.Value),
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        return _extractor.ExecuteRfcAsync(
            functionName,
            convertedParameters,
            maxTableRows,
            cancellationToken);
    }

    [McpServerTool(Name = "sap_read_table", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Reads SAP table rows through RFC_READ_TABLE. WHERE uses native ABAP SQL syntax.")]
    public Task<TableReadResult> SapReadTable(
        CancellationToken cancellationToken,
        [Description("Table or view name.")] string table,
        [Description("Fields to return. Use an empty array to request the RFC default field set.")] string[] fields,
        [Description("Optional ABAP SQL predicate, e.g. \"BUKRS = '1000'\".")] string? where = null,
        [Description("Maximum rows. Default 100, hard cap 10000.")] int maxRows = 100,
        [Description("Rows to skip for pagination. Default 0.")] int rowSkip = 0)
    {
        _logger.LogInformation(
            "Tool: sap_read_table table={Table} fieldCount={FieldCount} maxRows={MaxRows} rowSkip={RowSkip}",
            table,
            fields?.Length ?? 0,
            maxRows,
            rowSkip);
        return _extractor.ReadTableAsync(
            table,
            fields ?? Array.Empty<string>(),
            NullIfBlank(where),
            maxRows,
            rowSkip,
            cancellationToken);
    }

    private async Task<IReadOnlyList<SapObjectSummaryResponse>> ListFunctionModules(
        string? namePattern,
        string? package,
        string? functionGroup,
        int maxRows,
        bool remoteOnly,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Tool: list function modules pattern={Pattern} package={Package} group={Group} remoteOnly={RemoteOnly}",
            namePattern,
            package,
            functionGroup,
            remoteOnly);
        var results = await _extractor.ListFunctionModulesAsync(
            NullIfBlank(namePattern),
            NullIfBlank(package),
            NullIfBlank(functionGroup),
            maxRows,
            remoteOnly,
            cancellationToken);
        return results.Select(SapObjectSummaryResponse.From).ToList();
    }

    private static SapObjectResponse? ToResponse(AbapObject? obj) =>
        obj is null ? null : SapObjectResponse.From(obj);

    private static object? ConvertJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ConvertJson(property.Value),
            StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText(),
    };

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
