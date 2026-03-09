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

        var missing = new List<string>();
        foreach (var obj in objects)
        {
            // Strip the generated header (lines starting with *)
            var bodyLines = obj.SourceCode
                .Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .SkipWhile(l => l.StartsWith("*"))
                .ToList();
            var body = string.Join("\n", bodyLines).Trim();

            if (string.IsNullOrWhiteSpace(body))
            {
                missing.Add(obj.Name);
                _output.WriteLine($"  [NO SOURCE] {obj.Name} [{obj.PackageName}] — total length: {obj.SourceCode.Length}");
            }
        }

        var withSource = objects.Count - missing.Count;
        _output.WriteLine($"\nSummary: {withSource}/{objects.Count} have source code, {missing.Count} missing.");

        if (missing.Count > 0)
        {
            _output.WriteLine($"\nFunction modules WITHOUT source code ({missing.Count}):");
            foreach (var name in missing)
                _output.WriteLine($"  - {name}");
        }

        Assert.NotEmpty(objects);
        Assert.Empty(missing);
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

    [Fact]
    public async Task DiagnoseSourceExtraction_ForFailingFunction()
    {
        var funcName = "ZSW_APP_CALC_STOCK_DISPONIBLE";

        using var extractor = _fixture.CreateExtractor();
        // Force connection by pinging
        await extractor.TestConnectionAsync();

        // Access connection via internal field
        var connField = typeof(AbapObjectExtractor).GetField("_connection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var conn = (SapNwRfc.SapConnection)connField!.GetValue(extractor)!;

        // 1. Try RPY_FUNCTIONMODULE_READ for SOURCE
        _output.WriteLine("=== 1. RPY_FUNCTIONMODULE_READ (SOURCE) ===");
        try
        {
            using var rpyFunc = conn.CreateFunction("RPY_FUNCTIONMODULE_READ");
            var output = rpyFunc.Invoke<Extractors.RpyFuncReadOutput>(
                new Extractors.RpyFuncReadInput { FunctionName = funcName });
            var lines = output.Source ?? [];
            _output.WriteLine($"  SOURCE lines: {lines.Length}");
            foreach (var l in lines.Take(10))
                _output.WriteLine($"    '{l.Line}'");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        // 2. TFDIR lookup (PNAME + INCLUDE)
        _output.WriteLine("\n=== 2. TFDIR lookup ===");
        string? include = null;
        string? pname = null;
        try
        {
            using var rfcRead = conn.CreateFunction("RFC_READ_TABLE");
            var output = rfcRead.Invoke<Extractors.RfcReadTableOutput>(new Extractors.RfcReadTableInput
            {
                QueryTable = "TFDIR",
                Delimiter = "|",
                Fields = [new Extractors.RfcTableField { FieldName = "PNAME" }],
                Options = [new Extractors.RfcReadTableOption { Text = $"FUNCNAME = '{funcName}'" }],
            });
            var data = output.Data ?? [];
            _output.WriteLine($"  TFDIR rows: {data.Length}");
            if (data.Length > 0)
            {
                pname = data[0].Wa.Trim();
                _output.WriteLine($"  PNAME: '{pname}'");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        // 2b. TFDIR INCLUDE field
        _output.WriteLine("\n=== 2b. TFDIR INCLUDE field ===");
        try
        {
            using var rfcRead = conn.CreateFunction("RFC_READ_TABLE");
            var output = rfcRead.Invoke<Extractors.RfcReadTableOutput>(new Extractors.RfcReadTableInput
            {
                QueryTable = "TFDIR",
                Delimiter = "|",
                Fields = [new Extractors.RfcTableField { FieldName = "INCLUDE" }],
                Options = [new Extractors.RfcReadTableOption { Text = $"FUNCNAME = '{funcName}'" }],
            });
            var data = output.Data ?? [];
            _output.WriteLine($"  rows: {data.Length}");
            if (data.Length > 0)
            {
                include = data[0].Wa.Trim();
                _output.WriteLine($"  INCLUDE: '{include}'");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        // 3. ENLFDIR lookup
        _output.WriteLine("\n=== 3. ENLFDIR lookup ===");
        try
        {
            using var rfcRead = conn.CreateFunction("RFC_READ_TABLE");
            var output = rfcRead.Invoke<Extractors.RfcReadTableOutput>(new Extractors.RfcReadTableInput
            {
                QueryTable = "ENLFDIR",
                Delimiter = "|",
                Fields = [
                    new Extractors.RfcTableField { FieldName = "AREA" },
                    new Extractors.RfcTableField { FieldName = "INCLUDE" },
                ],
                Options = [new Extractors.RfcReadTableOption { Text = $"FUNCNAME = '{funcName}'" }],
            });
            var data = output.Data ?? [];
            var fields = output.Fields ?? [];
            _output.WriteLine($"  ENLFDIR rows: {data.Length}");
            _output.WriteLine($"  Fields: {string.Join(", ", fields.Select(f => f.FieldName.Trim()))}");
            if (data.Length > 0)
            {
                _output.WriteLine($"  DATA[0]: '{data[0].Wa}'");
                var parts = data[0].Wa.Split('|');
                if (parts.Length >= 2)
                {
                    var area = parts[0].Trim();
                    include = parts[1].Trim();
                    _output.WriteLine($"  AREA='{area}', INCLUDE='{include}'");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        // 4. RFC_READ_REPORT with include name
        if (!string.IsNullOrEmpty(include))
        {
            _output.WriteLine($"\n=== 4. RFC_READ_REPORT include='{include}' ===");
            try
            {
                using var readReport = conn.CreateFunction("RFC_READ_REPORT");
                var output = readReport.Invoke<Extractors.ReadReportOutput>(
                    new Extractors.ReadReportInput { ProgramName = include });
                var lines = output.Source ?? [];
                _output.WriteLine($"  REPORT_TAB lines: {lines.Length}");
                foreach (var l in lines.Take(10))
                    _output.WriteLine($"    '{l.Line}'");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }
        else
        {
            _output.WriteLine("\n=== 4. RFC_READ_REPORT — skipped (no include found) ===");
        }

        // 5. Try the full extraction with RPY_FUNCTIONMODULE_READ_NEW + StringTableReader
        _output.WriteLine("\n=== 5. RPY_FUNCTIONMODULE_READ_NEW + SapRfcStringTableReader ===");
        try
        {
            using var rpyNew = conn.CreateFunction("RPY_FUNCTIONMODULE_READ_NEW");
            rpyNew.Invoke(new Extractors.RpyFuncReadInput { FunctionName = funcName });

            var lines = Extractors.SapRfcStringTableReader.ReadStringTable(rpyNew, "NEW_SOURCE");
            _output.WriteLine($"  Lines: {lines?.Count ?? -1}");
            if (lines is { Count: > 0 })
            {
                foreach (var l in lines.Take(15))
                    _output.WriteLine($"    '{l}'");
            }
            else
            {
                _output.WriteLine("  (empty or null)");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                _output.WriteLine($"    Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }

        _output.WriteLine("\n=== Diagnostic complete ===");
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
