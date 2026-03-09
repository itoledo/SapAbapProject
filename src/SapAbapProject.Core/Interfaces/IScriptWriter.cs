using SapAbapProject.Core.Models;

namespace SapAbapProject.Core.Interfaces;

public interface IScriptWriter
{
    Task WriteAsync(string projectRootPath, IReadOnlyList<AbapObject> objects, ImportOptions options, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);
}
