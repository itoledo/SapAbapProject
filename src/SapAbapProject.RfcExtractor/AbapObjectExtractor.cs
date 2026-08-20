using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;
using SapAbapProject.RfcExtractor.Extractors;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor;

public sealed class AbapObjectExtractor : IAbapExtractor
{
    private readonly SapConnectionSettings _settings;
    private readonly IReadOnlyList<string> _descriptionLanguages;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private SapConnection? _connection;

    private FunctionModuleExtractor? _fmExtractor;
    private DataElementExtractor? _dtelExtractor;
    private DomainExtractor? _domaExtractor;
    private TableDefinitionExtractor? _tableExtractor;
    private TableDefinitionExtractor? _structureExtractor;
    private TableTypeExtractor? _ttypExtractor;

    public AbapObjectExtractor(SapConnectionSettings settings)
    {
        _settings = settings;
        _descriptionLanguages = settings.DescriptionLanguages is { Count: > 0 }
            ? settings.DescriptionLanguages.Select(SapLanguageCode.Resolve).Distinct().ToList()
            : SapLanguageCode.DefaultCascade(settings.Language);
    }

    private SapConnection EnsureConnection()
    {
        if (_connection is not null)
            return _connection;

        if (!SapRfcSdkManager.IsLoaded)
        {
            if (!SapRfcSdkManager.EnsureSdkLoaded())
                throw new InvalidOperationException(
                    "SAP NetWeaver RFC SDK is not configured. Please configure the SDK path in the extension settings.");
        }

        var connParams = new SapConnectionParameters
        {
            Client = _settings.Client,
            User = _settings.User,
            Password = _settings.Password,
            Language = _settings.Language,
        };

        if (!string.IsNullOrEmpty(_settings.MessageServerHost) && !string.IsNullOrEmpty(_settings.Group))
        {
            connParams.MessageServerHost = _settings.MessageServerHost;
            connParams.LogonGroup = _settings.Group;
            if (!string.IsNullOrEmpty(_settings.SystemId))
                connParams.SystemId = _settings.SystemId;
        }
        else
        {
            connParams.AppServerHost = _settings.AppServerHost;
            connParams.SystemNumber = _settings.SystemNumber;
        }

        if (_settings.UseSncConnection && !string.IsNullOrEmpty(_settings.SncPartnerName))
        {
            connParams.SncMode = "1";
            connParams.SncPartnerName = _settings.SncPartnerName;
            if (!string.IsNullOrEmpty(_settings.SncLibraryPath))
                connParams.SncLibraryPath = _settings.SncLibraryPath;
            if (!string.IsNullOrEmpty(_settings.SncQop))
                connParams.SncQop = _settings.SncQop;
        }

        if (!string.IsNullOrEmpty(_settings.SapRouter))
            connParams.SapRouter = _settings.SapRouter;

        _connection = new SapConnection(connParams);
        _connection.Connect();
        return _connection;
    }

    private FunctionModuleExtractor FunctionModule => _fmExtractor ??= new FunctionModuleExtractor(EnsureConnection(), _descriptionLanguages);
    private DataElementExtractor DataElement => _dtelExtractor ??= new DataElementExtractor(EnsureConnection(), _descriptionLanguages);
    private DomainExtractor Domain => _domaExtractor ??= new DomainExtractor(EnsureConnection(), _descriptionLanguages);
    private TableDefinitionExtractor Table => _tableExtractor ??= TableDefinitionExtractor.ForTables(EnsureConnection(), _descriptionLanguages);
    private TableDefinitionExtractor Structure => _structureExtractor ??= TableDefinitionExtractor.ForStructures(EnsureConnection(), _descriptionLanguages);
    private TableTypeExtractor TableType => _ttypExtractor ??= new TableTypeExtractor(EnsureConnection(), _descriptionLanguages);

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
            {
                var conn = EnsureConnection();
                using var func = conn.CreateFunction("RFC_PING");
                func.Invoke();
            }, cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<IReadOnlyList<string>> GetPackagesAsync(
        string searchPattern = "*",
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                var conn = EnsureConnection();
                using var func = conn.CreateFunction("RFC_READ_TABLE");
                var where = $"DEVCLASS LIKE '{SapRfcQuery.LikePattern(searchPattern)}'";
                var output = func.Invoke<RfcReadTableOutput>(new RfcReadTableInput
                {
                    QueryTable = "TDEVC",
                    Delimiter = "|",
                    RowCount = 500,
                    Fields = [new RfcTableField { FieldName = "DEVCLASS" }],
                    Options = [new RfcReadTableOption { Text = where }],
                });

                var data = output.Data ?? [];
                return (IReadOnlyList<string>)data
                    .Select(r => r.Wa.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .OrderBy(s => s)
                    .ToList();
            }, cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<IReadOnlyList<string>> GetFunctionGroupsAsync(
        string? packageFilter = null,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                var conn = EnsureConnection();
                using var func = conn.CreateFunction("RFC_READ_TABLE");

                var where = string.IsNullOrEmpty(packageFilter)
                    ? "PGMID = 'R3TR' AND OBJECT = 'FUGR'"
                    : $"PGMID = 'R3TR' AND OBJECT = 'FUGR' AND DEVCLASS = '{SapRfcQuery.Literal(packageFilter!)}'";

                var output = func.Invoke<RfcReadTableOutput>(new RfcReadTableInput
                {
                    QueryTable = "TADIR",
                    Delimiter = "|",
                    RowCount = 1000,
                    Fields = [new RfcTableField { FieldName = "OBJ_NAME" }],
                    Options = [new RfcReadTableOption { Text = where }],
                });

                var data = output.Data ?? [];
                return (IReadOnlyList<string>)data
                    .Select(r => r.Wa.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .OrderBy(s => s)
                    .ToList();
            }, cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<IReadOnlyList<AbapObjectSummary>> ListFunctionModulesAsync(
        string? namePattern = null,
        string? packageFilter = null,
        string? functionGroupFilter = null,
        int maxRows = 1000,
        bool remoteOnly = false,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            return await FunctionModule.ListAsync(
                namePattern,
                packageFilter,
                functionGroupFilter,
                NormalizeMaxRows(maxRows, 1000),
                remoteOnly,
                cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<IReadOnlyList<AbapObjectSummary>> ListTablesAsync(
        string? namePattern = null,
        string? packageFilter = null,
        bool includeStructures = false,
        int maxRows = 1000,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            var normalizedMaxRows = NormalizeMaxRows(maxRows, 1000);
            var tables = await Table.ListAsync(namePattern, packageFilter, normalizedMaxRows, cancellationToken);
            if (!includeStructures) return tables;

            var structures = await Structure.ListAsync(namePattern, packageFilter, normalizedMaxRows, cancellationToken);
            return tables.Concat(structures).ToList();
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<IReadOnlyList<RepositoryObjectSummary>> ListRepositoryObjectsAsync(
        string? objectType = null,
        string? namePattern = null,
        string? packageFilter = null,
        int maxRows = 1000,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnection();
            return await Task.Run<IReadOnlyList<RepositoryObjectSummary>>(() =>
            {
                var conditions = new List<string> { "PGMID = 'R3TR'" };
                if (!string.IsNullOrWhiteSpace(objectType))
                    conditions.Add($"OBJECT = '{SapRfcQuery.Literal(objectType!.Trim().ToUpperInvariant())}'");
                if (!string.IsNullOrWhiteSpace(namePattern))
                    conditions.Add($"OBJ_NAME LIKE '{SapRfcQuery.LikePattern(namePattern!.Trim().ToUpperInvariant())}'");
                if (!string.IsNullOrWhiteSpace(packageFilter))
                    conditions.Add($"DEVCLASS = '{SapRfcQuery.Literal(packageFilter!.Trim().ToUpperInvariant())}'");

                var result = Table.ReadTableWithMetadata(
                    "TADIR",
                    ["PGMID", "OBJECT", "OBJ_NAME", "DEVCLASS"],
                    string.Join(" AND ", conditions),
                    NormalizeMaxRows(maxRows, 1000),
                    0);

                return result.Rows
                    .Select(row => new RepositoryObjectSummary(
                        ProgramId: GetValue(row, "PGMID").Trim(),
                        ObjectType: GetValue(row, "OBJECT").Trim(),
                        Name: GetValue(row, "OBJ_NAME").Trim(),
                        PackageName: NullIfBlank(GetValue(row, "DEVCLASS").Trim())))
                    .Where(item => item.Name.Length > 0)
                    .ToList();
            }, cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<AbapObject?> GetFunctionModuleAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = SapRfcQuery.Identifier(name, nameof(name));
        await _connectionLock.WaitAsync(cancellationToken);
        try { return await FunctionModule.ExtractByNameAsync(normalizedName, cancellationToken); }
        finally { _connectionLock.Release(); }
    }

    public async Task<AbapObject?> GetTableAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = SapRfcQuery.Identifier(name, nameof(name));
        await _connectionLock.WaitAsync(cancellationToken);
        try { return await Table.ExtractByNameAsync(normalizedName, cancellationToken); }
        finally { _connectionLock.Release(); }
    }

    public async Task<AbapObject?> GetStructureAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = SapRfcQuery.Identifier(name, nameof(name));
        await _connectionLock.WaitAsync(cancellationToken);
        try { return await Structure.ExtractByNameAsync(normalizedName, cancellationToken); }
        finally { _connectionLock.Release(); }
    }

    public async Task<AbapObject?> GetDataElementAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = SapRfcQuery.Identifier(name, nameof(name));
        await _connectionLock.WaitAsync(cancellationToken);
        try { return await DataElement.ExtractByNameAsync(normalizedName, cancellationToken); }
        finally { _connectionLock.Release(); }
    }

    public async Task<AbapObject?> GetDomainAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = SapRfcQuery.Identifier(name, nameof(name));
        await _connectionLock.WaitAsync(cancellationToken);
        try { return await Domain.ExtractByNameAsync(normalizedName, cancellationToken); }
        finally { _connectionLock.Release(); }
    }

    public async Task<AbapObject?> GetTableTypeAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = SapRfcQuery.Identifier(name, nameof(name));
        await _connectionLock.WaitAsync(cancellationToken);
        try { return await TableType.ExtractByNameAsync(normalizedName, cancellationToken); }
        finally { _connectionLock.Release(); }
    }

    public async Task<AbapSourceResult?> GetAbapSourceAsync(
        string name,
        string sourceKind,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = sourceKind.Trim().ToLowerInvariant();
        if (normalizedKind == "function_module")
        {
            var functionModule = await GetFunctionModuleAsync(name.Trim().ToUpperInvariant(), cancellationToken);
            return functionModule is null
                ? null
                : new AbapSourceResult(
                    functionModule.Name,
                    normalizedKind,
                    functionModule.PackageName,
                    functionModule.SourceCode);
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                var objectName = SapRfcQuery.Identifier(name, nameof(name));
                string reportName;
                string tadirType;
                string tadirName;

                switch (normalizedKind)
                {
                    case "program":
                    case "include":
                        reportName = objectName;
                        tadirType = "PROG";
                        tadirName = objectName;
                        break;
                    case "function_group":
                        reportName = objectName.StartsWith("SAPL", StringComparison.Ordinal)
                            ? objectName
                            : $"SAPL{objectName}";
                        tadirType = "FUGR";
                        tadirName = objectName.StartsWith("SAPL", StringComparison.Ordinal)
                            ? objectName.Substring(4)
                            : objectName;
                        break;
                    default:
                        throw new ArgumentException(
                            "sourceKind must be function_module, program, include, or function_group.",
                            nameof(sourceKind));
                }

                var conn = EnsureConnection();
                using var readReport = conn.CreateFunction("RFC_READ_REPORT");
                var output = readReport.Invoke<ReadReportOutput>(
                    new ReadReportInput { ProgramName = reportName });
                var lines = output.Source ?? [];
                if (lines.Length == 0)
                    return null;

                var packageRows = Table.ReadTableWithMetadata(
                    "TADIR",
                    ["DEVCLASS"],
                    $"PGMID = 'R3TR' AND OBJECT = '{tadirType}' AND OBJ_NAME = '{SapRfcQuery.Literal(tadirName)}'",
                    1,
                    0);
                var package = packageRows.Rows.Count == 0
                    ? null
                    : NullIfBlank(GetValue(packageRows.Rows[0], "DEVCLASS").Trim());

                return new AbapSourceResult(
                    name.Trim().ToUpperInvariant(),
                    normalizedKind,
                    package,
                    string.Join(Environment.NewLine, lines.Select(line => line.Line)));
            }, cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<RfcFunctionDefinition> GetRfcFunctionDefinitionAsync(
        string functionName,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => RfcRuntimeInvoker.GetDefinition(
                    EnsureConnection().GetFunctionMetadata(
                        SapRfcQuery.Identifier(functionName, nameof(functionName)))),
                cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<RfcExecutionResult> ExecuteRfcAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        int maxTableRows = 500,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                var normalizedName = SapRfcQuery.Identifier(functionName, nameof(functionName));
                using var function = EnsureConnection().CreateFunction(normalizedName);
                return RfcRuntimeInvoker.Invoke(
                    function,
                    normalizedName,
                    parameters,
                    Math.Min(Math.Max(maxTableRows, 1), 5000));
            }, cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<TableReadResult> ReadTableAsync(
        string tableName,
        IReadOnlyList<string> fields,
        string? whereClause = null,
        int maxRows = 100,
        int rowSkip = 0,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnection();
            return await Task.Run(
                () => Table.ReadTableWithMetadata(
                    SapRfcQuery.Identifier(tableName, nameof(tableName)),
                    fields.Select(field => SapRfcQuery.Identifier(field, nameof(fields))).ToList(),
                    whereClause,
                    Math.Min(Math.Max(maxRows, 1), 10000),
                    Math.Max(rowSkip, 0)),
                cancellationToken);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<IReadOnlyList<AbapObject>> ExtractObjectsAsync(
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            var conn = EnsureConnection();

            var extractors = new List<IObjectExtractor>();
            foreach (var objectType in options.ObjectTypes)
            {
                var extractor = CreateExtractor(objectType, conn);
                if (extractor is not null)
                    extractors.Add(extractor);
            }

            var allObjects = new List<AbapObject>();
            int totalTypes = extractors.Count;
            int processed = 0;

            foreach (var extractor in extractors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new ImportProgress
                {
                    CurrentObject = $"Extracting {extractor.ObjectType}...",
                    ObjectType = extractor.ObjectType,
                    ProcessedCount = processed,
                    TotalCount = totalTypes,
                });

                try
                {
                    var objects = await extractor.ExtractAsync(options, cancellationToken);
                    allObjects.AddRange(objects);

                    progress?.Report(new ImportProgress
                    {
                        CurrentObject = $"Extracted {objects.Count} {extractor.ObjectType} objects",
                        ObjectType = extractor.ObjectType,
                        ProcessedCount = ++processed,
                        TotalCount = totalTypes,
                    });
                }
                catch (Exception ex)
                {
                    progress?.Report(new ImportProgress
                    {
                        CurrentObject = $"Error extracting {extractor.ObjectType}: {ex.Message}",
                        ObjectType = extractor.ObjectType,
                        ProcessedCount = ++processed,
                        TotalCount = totalTypes,
                        IsError = true,
                        ErrorMessage = ex.Message,
                    });
                }
            }

            return allObjects;
        }
        finally { _connectionLock.Release(); }
    }

    private IObjectExtractor? CreateExtractor(AbapObjectType type, SapConnection conn) => type switch
    {
        AbapObjectType.FunctionModule => FunctionModule,
        AbapObjectType.DataElement => DataElement,
        AbapObjectType.Domain => Domain,
        AbapObjectType.TransparentTable => Table,
        AbapObjectType.Structure => Structure,
        AbapObjectType.TableType => TableType,
        _ => null,
    };

    private static int NormalizeMaxRows(int maxRows, int defaultValue) =>
        Math.Min(maxRows > 0 ? maxRows : defaultValue, 10000);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string GetValue(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value : string.Empty;

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _connectionLock.Dispose();
    }
}
