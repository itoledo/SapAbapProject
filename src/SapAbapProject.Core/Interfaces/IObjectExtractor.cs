using SapAbapProject.Core.Models;

namespace SapAbapProject.Core.Interfaces;

public interface IObjectExtractor
{
    AbapObjectType ObjectType { get; }
    Task<IReadOnlyList<AbapObject>> ExtractAsync(ImportOptions options, CancellationToken cancellationToken = default);
}
