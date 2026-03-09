using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;

namespace SapAbapProject.RfcExtractor;

public sealed class ScriptFileWriter : IScriptWriter
{
    public async Task WriteAsync(
        string projectRootPath,
        IReadOnlyList<AbapObject> objects,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int total = objects.Count;
        int written = 0;

        foreach (var obj in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = obj.RelativePath;
            var fullPath = Path.Combine(projectRootPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath)!;

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(fullPath) && !options.OverwriteExisting)
            {
                written++;
                continue;
            }

            // Use synchronous write wrapped in Task.Run for net472 compatibility
            // (File.WriteAllTextAsync is not available on net472)
            await Task.Run(() => File.WriteAllText(fullPath, obj.SourceCode), cancellationToken);

            progress?.Report(new ImportProgress
            {
                CurrentObject = $"Written: {relativePath}",
                ObjectType = obj.ObjectType,
                ProcessedCount = ++written,
                TotalCount = total,
            });
        }
    }
}
