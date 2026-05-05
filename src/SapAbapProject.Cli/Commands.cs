using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;
using SapAbapProject.RfcExtractor;

namespace SapAbapProject.Cli;

internal sealed class Commands
{
    private readonly IAbapExtractor _extractor;
    private readonly IScriptWriter _writer;
    private readonly CliOptions _opts;

    public Commands(IAbapExtractor extractor, IScriptWriter writer, CliOptions opts)
    {
        _extractor = extractor;
        _writer = writer;
        _opts = opts;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        return (_opts.Command, _opts.Subcommand) switch
        {
            ("test", _) => await RunTestAsync(ct),

            ("download", "fugr")          => await DownloadFunctionGroupAsync(ct),
            ("download", "fm")            => await DownloadByNameAsync(_extractor.GetFunctionModuleAsync, "function module", ct),
            ("download", "table")         => await DownloadByNameAsync(_extractor.GetTableAsync, "table", ct),
            ("download", "structure")     => await DownloadByNameAsync(_extractor.GetStructureAsync, "structure", ct),
            ("download", "data-element")  => await DownloadByNameAsync(_extractor.GetDataElementAsync, "data element", ct),
            ("download", "domain")        => await DownloadByNameAsync(_extractor.GetDomainAsync, "domain", ct),
            ("download", "table-type")    => await DownloadByNameAsync(_extractor.GetTableTypeAsync, "table type", ct),
            ("download", "package")       => await DownloadPackageAsync(ct),

            ("list", "packages")          => await ListPackagesAsync(ct),
            ("list", "fugrs")             => await ListFunctionGroupsAsync(ct),
            ("list", "fms")               => await ListFunctionModulesAsync(ct),
            ("list", "tables")            => await ListTablesAsync(ct),

            _ => UnknownCommand(),
        };
    }

    private static int UnknownCommand()
    {
        Console.Error.WriteLine("Unknown command. Run `sap-cli --help`.");
        return 2;
    }

    private async Task<int> RunTestAsync(CancellationToken ct)
    {
        await _extractor.TestConnectionAsync(ct);
        Console.WriteLine("Connection OK.");
        return 0;
    }

    private async Task<int> DownloadFunctionGroupAsync(CancellationToken ct)
    {
        if (_opts.Positionals.Count == 0)
        {
            Console.Error.WriteLine("Usage: sap-cli download fugr <function-group> [-o <dir>]");
            return 2;
        }

        int totalSaved = 0;
        foreach (var fugr in _opts.Positionals)
        {
            Console.WriteLine($"Listing function modules in group {fugr}...");
            var summaries = await _extractor.ListFunctionModulesAsync(
                namePattern: null,
                packageFilter: null,
                functionGroupFilter: fugr,
                maxRows: 5000,
                cancellationToken: ct);

            if (summaries.Count == 0)
            {
                Console.Error.WriteLine($"  no function modules found in group '{fugr}'.");
                continue;
            }

            Console.WriteLine($"  found {summaries.Count} module(s).");

            var objects = new List<AbapObject>();
            int idx = 0;
            foreach (var s in summaries)
            {
                ct.ThrowIfCancellationRequested();
                idx++;
                Console.WriteLine($"  [{idx}/{summaries.Count}] fetching {s.Name}");
                var obj = await _extractor.GetFunctionModuleAsync(s.Name, ct);
                if (obj is not null) objects.Add(obj);
            }

            await WriteAsync(objects, ct);
            totalSaved += objects.Count;
        }

        Console.WriteLine($"Done. {totalSaved} object(s) written to {Path.GetFullPath(_opts.OutputDir)}");
        return 0;
    }

    private async Task<int> DownloadByNameAsync(
        Func<string, CancellationToken, Task<AbapObject?>> fetch,
        string label,
        CancellationToken ct)
    {
        if (_opts.Positionals.Count == 0)
        {
            Console.Error.WriteLine($"Usage: sap-cli download {_opts.Subcommand} <name> [<name>...] [-o <dir>]");
            return 2;
        }

        var objects = new List<AbapObject>();
        foreach (var name in _opts.Positionals)
        {
            ct.ThrowIfCancellationRequested();
            Console.WriteLine($"Fetching {label} {name}...");
            var obj = await fetch(name, ct);
            if (obj is null)
                Console.Error.WriteLine($"  not found: {name}");
            else
                objects.Add(obj);
        }

        await WriteAsync(objects, ct);
        Console.WriteLine($"Done. {objects.Count} object(s) written to {Path.GetFullPath(_opts.OutputDir)}");
        return objects.Count == _opts.Positionals.Count ? 0 : 1;
    }

    private async Task<int> DownloadPackageAsync(CancellationToken ct)
    {
        if (_opts.Positionals.Count == 0)
        {
            Console.Error.WriteLine("Usage: sap-cli download package <package> [--types fm,table,...] [-o <dir>]");
            return 2;
        }

        var types = _opts.Types.Count > 0 ? _opts.Types : DefaultPackageTypes();

        var options = new ImportOptions
        {
            Packages = _opts.Positionals,
            ObjectTypes = types,
            OverwriteExisting = _opts.Overwrite,
            IncludeSignature = _opts.IncludeSignature,
        };

        var progress = new Progress<ImportProgress>(p =>
        {
            if (p.IsError)
                Console.Error.WriteLine($"  ! {p.CurrentObject}");
            else
                Console.WriteLine($"  {p.CurrentObject}");
        });

        Console.WriteLine($"Extracting from package(s) {string.Join(", ", _opts.Positionals)}...");
        var objects = await _extractor.ExtractObjectsAsync(options, progress, ct);

        await WriteAsync(objects, ct);
        Console.WriteLine($"Done. {objects.Count} object(s) written to {Path.GetFullPath(_opts.OutputDir)}");
        return 0;
    }

    private static IReadOnlyList<AbapObjectType> DefaultPackageTypes() =>
    [
        AbapObjectType.FunctionModule,
        AbapObjectType.TransparentTable,
        AbapObjectType.Structure,
        AbapObjectType.DataElement,
        AbapObjectType.Domain,
        AbapObjectType.TableType,
    ];

    private async Task<int> ListPackagesAsync(CancellationToken ct)
    {
        var pattern = _opts.Positionals.Count > 0 ? _opts.Positionals[0] : (_opts.Pattern ?? "*");
        var packages = await _extractor.GetPackagesAsync(pattern, ct);
        foreach (var p in packages) Console.WriteLine(p);
        return 0;
    }

    private async Task<int> ListFunctionGroupsAsync(CancellationToken ct)
    {
        var pkg = _opts.Positionals.Count > 0 ? _opts.Positionals[0] : _opts.Package;
        var fugrs = await _extractor.GetFunctionGroupsAsync(pkg, ct);
        foreach (var f in fugrs) Console.WriteLine(f);
        return 0;
    }

    private async Task<int> ListFunctionModulesAsync(CancellationToken ct)
    {
        var summaries = await _extractor.ListFunctionModulesAsync(
            namePattern: _opts.Pattern,
            packageFilter: _opts.Package,
            functionGroupFilter: _opts.FunctionGroup,
            maxRows: _opts.MaxRows,
            cancellationToken: ct);

        foreach (var s in summaries)
            Console.WriteLine($"{s.Name}\t{s.PackageName ?? "-"}\t{s.FunctionGroup ?? "-"}\t{s.Description ?? ""}");

        return 0;
    }

    private async Task<int> ListTablesAsync(CancellationToken ct)
    {
        var summaries = await _extractor.ListTablesAsync(
            namePattern: _opts.Pattern,
            packageFilter: _opts.Package,
            includeStructures: _opts.IncludeStructures,
            maxRows: _opts.MaxRows,
            cancellationToken: ct);

        foreach (var s in summaries)
            Console.WriteLine($"{s.Name}\t{s.ObjectType}\t{s.PackageName ?? "-"}\t{s.Description ?? ""}");

        return 0;
    }

    private Task WriteAsync(IReadOnlyList<AbapObject> objects, CancellationToken ct)
    {
        if (objects.Count == 0) return Task.CompletedTask;

        var writeOptions = new ImportOptions { OverwriteExisting = _opts.Overwrite };
        Directory.CreateDirectory(_opts.OutputDir);
        return _writer.WriteAsync(_opts.OutputDir, objects, writeOptions, progress: null, ct);
    }
}
