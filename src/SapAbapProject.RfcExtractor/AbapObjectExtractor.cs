using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;
using SapAbapProject.RfcExtractor.Extractors;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor;

public sealed class AbapObjectExtractor : IAbapExtractor
{
    private readonly SapConnectionSettings _settings;
    private SapConnection? _connection;

    public AbapObjectExtractor(SapConnectionSettings settings)
    {
        _settings = settings;
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
            AppServerHost = _settings.AppServerHost,
            SystemNumber = _settings.SystemNumber,
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

        if (_settings.UseSncConnection && !string.IsNullOrEmpty(_settings.SncPartnerName))
        {
            connParams.SncMode = "1";
            connParams.SncPartnerName = _settings.SncPartnerName;
        }

        if (!string.IsNullOrEmpty(_settings.SapRouter))
            connParams.SapRouter = _settings.SapRouter;

        _connection = new SapConnection(connParams);
        _connection.Connect();
        return _connection;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var conn = EnsureConnection();
            using var func = conn.CreateFunction("RFC_PING");
            func.Invoke();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPackagesAsync(
        string searchPattern = "*",
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var conn = EnsureConnection();
            using var func = conn.CreateFunction("RFC_READ_TABLE");
            var where = $"DEVCLASS LIKE '{searchPattern.Replace('*', '%')}'";
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

    public async Task<IReadOnlyList<string>> GetFunctionGroupsAsync(
        string? packageFilter = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var conn = EnsureConnection();
            using var func = conn.CreateFunction("RFC_READ_TABLE");

            // Use TADIR to find function groups - TLIBG doesn't have DEVCLASS
            var where = string.IsNullOrEmpty(packageFilter)
                ? "PGMID = 'R3TR' AND OBJECT = 'FUGR'"
                : $"PGMID = 'R3TR' AND OBJECT = 'FUGR' AND DEVCLASS = '{packageFilter}'";

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

    public async Task<IReadOnlyList<AbapObject>> ExtractObjectsAsync(
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
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

    private static IObjectExtractor? CreateExtractor(AbapObjectType type, SapConnection conn) => type switch
    {
        AbapObjectType.FunctionModule => new FunctionModuleExtractor(conn),
        AbapObjectType.DataElement => new DataElementExtractor(conn),
        AbapObjectType.Domain => new DomainExtractor(conn),
        AbapObjectType.TransparentTable => TableDefinitionExtractor.ForTables(conn),
        AbapObjectType.Structure => TableDefinitionExtractor.ForStructures(conn),
        AbapObjectType.TableType => new TableTypeExtractor(conn),
        _ => null,
    };

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
