using SapAbapProject.Core.Models;
using Xunit.Abstractions;

namespace SapAbapProject.RfcExtractor.Tests;

/// <summary>
/// Integration tests for SAP RFC extraction.
/// Requires a live SAP connection — configure testsettings.local.json.
/// </summary>
[Collection("SAP")]
public class RfcExtractionTests
{
    private readonly SapTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RfcExtractionTests(SapTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task TestConnection_ShouldSucceed()
    {
        using var extractor = _fixture.CreateExtractor();
        await extractor.TestConnectionAsync();
        _output.WriteLine("RFC_PING succeeded.");
    }

    [Fact]
    public async Task GetPackages_ShouldReturnResults()
    {
        var pattern = _fixture.GetSetting("Import:PackageSearchPattern");
        if (string.IsNullOrEmpty(pattern)) pattern = "Z*";

        using var extractor = _fixture.CreateExtractor();
        var packages = await extractor.GetPackagesAsync(pattern);

        _output.WriteLine($"Found {packages.Count} packages matching '{pattern}':");
        foreach (var p in packages.Take(20))
            _output.WriteLine($"  {p}");

        Assert.NotEmpty(packages);
    }

    [Fact]
    public async Task GetFunctionGroups_ShouldReturnResults()
    {
        var package = _fixture.DefaultImportOptions.Packages.FirstOrDefault();

        using var extractor = _fixture.CreateExtractor();
        var groups = await extractor.GetFunctionGroupsAsync(package);

        _output.WriteLine($"Found {groups.Count} function groups in '{package}':");
        foreach (var g in groups.Take(20))
            _output.WriteLine($"  {g}");

        // May be empty if the package has no function groups — just log
        if (groups.Count == 0)
            _output.WriteLine("(no function groups — not a failure, package may not contain any)");
    }

    [Fact]
    public async Task ExtractFunctionModules_ShouldReturnSource()
    {
        var options = _fixture.DefaultImportOptions with
        {
            ObjectTypes = [AbapObjectType.FunctionModule],
        };

        using var extractor = _fixture.CreateExtractor();
        var progress = new TestProgress(_output);
        var objects = await extractor.ExtractObjectsAsync(options, progress);

        _output.WriteLine($"\nExtracted {objects.Count} function modules.");
        foreach (var obj in objects.Take(5))
        {
            _output.WriteLine($"  {obj.Name} [{obj.PackageName}] — {obj.Description}");
            _output.WriteLine($"    Source length: {obj.SourceCode.Length} chars");
            _output.WriteLine($"    Path: {obj.RelativePath}");
        }

        Assert.NotEmpty(objects);
        Assert.All(objects, o =>
        {
            Assert.Equal(AbapObjectType.FunctionModule, o.ObjectType);
            Assert.False(string.IsNullOrWhiteSpace(o.SourceCode));
        });
    }

    [Fact]
    public async Task ExtractDataElements_ShouldReturnDefinitions()
    {
        var options = _fixture.DefaultImportOptions with
        {
            ObjectTypes = [AbapObjectType.DataElement],
        };

        using var extractor = _fixture.CreateExtractor();
        var progress = new TestProgress(_output);
        var objects = await extractor.ExtractObjectsAsync(options, progress);

        _output.WriteLine($"\nExtracted {objects.Count} data elements.");
        foreach (var obj in objects.Take(5))
        {
            _output.WriteLine($"  {obj.Name} [{obj.PackageName}] — {obj.Description}");
            _output.WriteLine($"    Path: {obj.RelativePath}");
        }

        if (objects.Count == 0)
            _output.WriteLine("(no data elements found — package may not contain any)");
    }

    [Fact]
    public async Task ExtractDomains_ShouldReturnDefinitions()
    {
        var options = _fixture.DefaultImportOptions with
        {
            ObjectTypes = [AbapObjectType.Domain],
        };

        using var extractor = _fixture.CreateExtractor();
        var progress = new TestProgress(_output);
        var objects = await extractor.ExtractObjectsAsync(options, progress);

        _output.WriteLine($"\nExtracted {objects.Count} domains.");
        foreach (var obj in objects.Take(5))
        {
            _output.WriteLine($"  {obj.Name} [{obj.PackageName}] — {obj.Description}");
            _output.WriteLine($"    Path: {obj.RelativePath}");
        }

        if (objects.Count == 0)
            _output.WriteLine("(no domains found — package may not contain any)");
    }

    [Fact]
    public async Task ExtractTables_ShouldReturnDefinitions()
    {
        var options = _fixture.DefaultImportOptions with
        {
            ObjectTypes = [AbapObjectType.TransparentTable],
        };

        using var extractor = _fixture.CreateExtractor();
        var progress = new TestProgress(_output);
        var objects = await extractor.ExtractObjectsAsync(options, progress);

        _output.WriteLine($"\nExtracted {objects.Count} transparent tables.");
        foreach (var obj in objects.Take(5))
        {
            _output.WriteLine($"  {obj.Name} [{obj.PackageName}] — {obj.Description}");
            _output.WriteLine($"    Path: {obj.RelativePath}");
        }

        if (objects.Count == 0)
            _output.WriteLine("(no tables found — package may not contain any)");
    }

    [Fact]
    public async Task ExtractStructures_ShouldReturnDefinitions()
    {
        var options = _fixture.DefaultImportOptions with
        {
            ObjectTypes = [AbapObjectType.Structure],
        };

        using var extractor = _fixture.CreateExtractor();
        var progress = new TestProgress(_output);
        var objects = await extractor.ExtractObjectsAsync(options, progress);

        _output.WriteLine($"\nExtracted {objects.Count} structures.");
        foreach (var obj in objects.Take(5))
        {
            _output.WriteLine($"  {obj.Name} [{obj.PackageName}] — {obj.Description}");
            _output.WriteLine($"    Path: {obj.RelativePath}");
        }

        if (objects.Count == 0)
            _output.WriteLine("(no structures found — package may not contain any)");
    }

    [Fact]
    public async Task ExtractTableTypes_ShouldReturnDefinitions()
    {
        var options = _fixture.DefaultImportOptions with
        {
            ObjectTypes = [AbapObjectType.TableType],
        };

        using var extractor = _fixture.CreateExtractor();
        var progress = new TestProgress(_output);
        var objects = await extractor.ExtractObjectsAsync(options, progress);

        _output.WriteLine($"\nExtracted {objects.Count} table types.");
        foreach (var obj in objects.Take(5))
        {
            _output.WriteLine($"  {obj.Name} [{obj.PackageName}] — {obj.Description}");
            _output.WriteLine($"    Path: {obj.RelativePath}");
        }

        if (objects.Count == 0)
            _output.WriteLine("(no table types found — package may not contain any)");
    }

    [Fact]
    public async Task FullImport_ShouldExtractAllTypes()
    {
        var options = _fixture.DefaultImportOptions;

        _output.WriteLine("Import options:");
        _output.WriteLine($"  Packages: {string.Join(", ", options.Packages)}");
        _output.WriteLine($"  Object types: {string.Join(", ", options.ObjectTypes)}");
        _output.WriteLine($"  FM pattern: {options.FunctionModuleNamePattern ?? "(none)"}");
        _output.WriteLine($"  FG filter: {options.FunctionGroupFilter ?? "(none)"}");

        using var extractor = _fixture.CreateExtractor();
        var progress = new TestProgress(_output);
        var objects = await extractor.ExtractObjectsAsync(options, progress);

        _output.WriteLine($"\n=== Total extracted: {objects.Count} objects ===");
        var grouped = objects.GroupBy(o => o.ObjectType);
        foreach (var group in grouped.OrderBy(g => g.Key.ToString()))
        {
            _output.WriteLine($"  {group.Key}: {group.Count()}");
            foreach (var obj in group.Take(3))
                _output.WriteLine($"    - {obj.Name} ({obj.SourceCode.Length} chars)");
            if (group.Count() > 3)
                _output.WriteLine($"    ... and {group.Count() - 3} more");
        }

        Assert.NotEmpty(objects);
    }

    [Fact]
    public async Task FullImport_WriteFiles_ShouldCreateAbapFiles()
    {
        var options = _fixture.DefaultImportOptions;
        var outputDir = Path.Combine(Path.GetTempPath(), "SapAbapProject_Test_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            using var extractor = _fixture.CreateExtractor();
            var progress = new TestProgress(_output);
            var objects = await extractor.ExtractObjectsAsync(options, progress);

            _output.WriteLine($"Extracted {objects.Count} objects. Writing to {outputDir}...");

            var writer = new ScriptFileWriter();
            await writer.WriteAsync(outputDir, objects, options, progress);

            var files = Directory.GetFiles(outputDir, "*.abap", SearchOption.AllDirectories);
            _output.WriteLine($"Written {files.Length} .abap files:");
            foreach (var f in files.Take(20))
                _output.WriteLine($"  {Path.GetRelativePath(outputDir, f)}");

            Assert.Equal(objects.Count, files.Length);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    private sealed class TestProgress : IProgress<ImportProgress>
    {
        private readonly ITestOutputHelper _output;
        public TestProgress(ITestOutputHelper output) => _output = output;

        public void Report(ImportProgress value)
        {
            var prefix = value.IsError ? "[ERROR]" : "[OK]";
            _output.WriteLine($"  {prefix} [{value.ProcessedCount}/{value.TotalCount}] {value.CurrentObject}");
            if (value.ErrorMessage is not null)
                _output.WriteLine($"         {value.ErrorMessage}");
        }
    }
}
