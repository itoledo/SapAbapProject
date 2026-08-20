using SapAbapProject.Core.Models;

namespace SapAbapProject.Core.Interfaces;

public interface IAbapExtractor : IDisposable
{
    Task TestConnectionAsync(CancellationToken cancellationToken = default);

    // Discovery
    Task<IReadOnlyList<string>> GetPackagesAsync(string searchPattern = "*", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetFunctionGroupsAsync(string? packageFilter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AbapObjectSummary>> ListFunctionModulesAsync(string? namePattern = null, string? packageFilter = null, string? functionGroupFilter = null, int maxRows = 1000, bool remoteOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AbapObjectSummary>> ListTablesAsync(string? namePattern = null, string? packageFilter = null, bool includeStructures = false, int maxRows = 1000, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepositoryObjectSummary>> ListRepositoryObjectsAsync(string? objectType = null, string? namePattern = null, string? packageFilter = null, int maxRows = 1000, CancellationToken cancellationToken = default);

    // Single-object lookup
    Task<AbapObject?> GetFunctionModuleAsync(string name, CancellationToken cancellationToken = default);
    Task<AbapObject?> GetTableAsync(string name, CancellationToken cancellationToken = default);
    Task<AbapObject?> GetStructureAsync(string name, CancellationToken cancellationToken = default);
    Task<AbapObject?> GetDataElementAsync(string name, CancellationToken cancellationToken = default);
    Task<AbapObject?> GetDomainAsync(string name, CancellationToken cancellationToken = default);
    Task<AbapObject?> GetTableTypeAsync(string name, CancellationToken cancellationToken = default);
    Task<AbapSourceResult?> GetAbapSourceAsync(string name, string sourceKind, CancellationToken cancellationToken = default);

    // Generic RFC metadata and execution
    Task<RfcFunctionDefinition> GetRfcFunctionDefinitionAsync(string functionName, CancellationToken cancellationToken = default);
    Task<RfcExecutionResult> ExecuteRfcAsync(string functionName, IReadOnlyDictionary<string, object?> parameters, int maxTableRows = 500, CancellationToken cancellationToken = default);

    // Bulk extract (used by VS extension import wizard)
    Task<IReadOnlyList<AbapObject>> ExtractObjectsAsync(ImportOptions options, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);

    // Generic escape hatch
    Task<TableReadResult> ReadTableAsync(string tableName, IReadOnlyList<string> fields, string? whereClause = null, int maxRows = 100, int rowSkip = 0, CancellationToken cancellationToken = default);
}
