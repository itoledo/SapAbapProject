using SapAbapProject.Core.Models;

namespace SapAbapProject.Core.Interfaces;

public interface IAbapExtractor : IDisposable
{
    Task TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetPackagesAsync(string searchPattern = "*", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetFunctionGroupsAsync(string? packageFilter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AbapObject>> ExtractObjectsAsync(ImportOptions options, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);
}
