using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Extractors;

internal sealed class FunctionModuleExtractor : BaseExtractor
{
    public FunctionModuleExtractor(SapConnection connection) : base(connection) { }

    public override AbapObjectType ObjectType => AbapObjectType.FunctionModule;

    public override async Task<IReadOnlyList<AbapObject>> ExtractAsync(
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var functionNames = await Task.Run(() => SearchFunctionModules(options), cancellationToken);
        var results = new List<AbapObject>();

        foreach (var funcName in functionNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var abapObject = await Task.Run(() => ExtractFunctionModule(funcName, options), cancellationToken);
                if (abapObject is not null)
                    results.Add(abapObject);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting {funcName}: {ex.Message}");
            }
        }

        return results;
    }

    private IReadOnlyList<string> SearchFunctionModules(ImportOptions options)
    {
        var results = new List<string>();

        if (!string.IsNullOrEmpty(options.FunctionModuleNamePattern))
        {
            var pattern = options.FunctionModuleNamePattern!.Replace('*', '%');
            var where = $"FUNCNAME LIKE '{pattern}'";

            if (!string.IsNullOrEmpty(options.FunctionGroupFilter))
                where += $" AND PNAME = 'SAPL{options.FunctionGroupFilter}'";

            var funcs = ReadTable("TFDIR", ["FUNCNAME"], where, maxRows: 1000);
            results.AddRange(funcs.Select(f => f["FUNCNAME"].Trim()));
        }

        if (options.Packages.Count > 0)
        {
            foreach (var package in options.Packages)
            {
                var fugrs = ReadTable("TADIR", ["OBJ_NAME"],
                    $"PGMID = 'R3TR' AND OBJECT = 'FUGR' AND DEVCLASS = '{package}'");
                foreach (var fugr in fugrs)
                {
                    var area = fugr["OBJ_NAME"].Trim();
                    var funcs = ReadTable("TFDIR", ["FUNCNAME"], $"PNAME = 'SAPL{area}'");
                    results.AddRange(funcs.Select(f => f["FUNCNAME"].Trim()));
                }
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private AbapObject? ExtractFunctionModule(string functionName, ImportOptions options)
    {
        // Get source code with fallback chain:
        // 1. RPY_FUNCTIONMODULE_READ (works for lines <= 72 chars)
        // 2. RPY_FUNCTIONMODULE_READ_NEW + NEW_SOURCE (handles lines > 72 chars)
        // 3. RFC_READ_REPORT via include name (if available)
        var source = GetSourceViaRpy(functionName)
                  ?? GetSourceViaRpyNew(functionName)
                  ?? GetSourceViaReadReport(functionName)
                  ?? "";

        // Get function interface with fallback
        var signature = GetInterfaceViaRpy(functionName)
                     ?? GetInterfaceViaRfc(functionName)
                     ?? [];

        // Get metadata via ReadTable (reliable)
        string? funcGroup = null;
        string? packageName = null;
        string? description = null;

        try
        {
            var tfdir = ReadTable("TFDIR", ["PNAME"], $"FUNCNAME = '{functionName}'");
            if (tfdir.Count > 0)
            {
                var pname = tfdir[0]["PNAME"].Trim();
                if (pname.StartsWith("SAPL", StringComparison.OrdinalIgnoreCase))
                    funcGroup = pname.Substring(4);
            }

            if (funcGroup is not null)
            {
                var devclass = ReadTable("TADIR", ["DEVCLASS"],
                    $"PGMID = 'R3TR' AND OBJECT = 'FUGR' AND OBJ_NAME = '{funcGroup}'");
                if (devclass.Count > 0)
                    packageName = devclass[0]["DEVCLASS"].Trim();
            }

            var tftit = ReadTable("TFTIT", ["STEXT"], $"FUNCNAME = '{functionName}' AND SPRAS = 'E'");
            if (tftit.Count > 0)
                description = tftit[0]["STEXT"].Trim();
        }
        catch
        {
            // Non-critical metadata
        }

        var header = BuildHeader(functionName, funcGroup, packageName, description, signature, options);

        return new AbapObject
        {
            Name = functionName,
            ObjectType = AbapObjectType.FunctionModule,
            PackageName = packageName,
            FunctionGroup = funcGroup,
            Description = description,
            SourceCode = header + source,
        };
    }

    /// <summary>
    /// RPY_FUNCTIONMODULE_READ — available on most SAP systems (used by SE37).
    /// </summary>
    private string? GetSourceViaRpy(string functionName)
    {
        try
        {
            using var func = Connection.CreateFunction("RPY_FUNCTIONMODULE_READ");
            var output = func.Invoke<RpyFuncReadOutput>(
                new RpyFuncReadInput { FunctionName = functionName });
            var lines = output.Source ?? [];
            if (lines.Length > 0)
                return string.Join(Environment.NewLine, lines.Select(l => l.Line));
        }
        catch { }
        return null;
    }

    /// <summary>
    /// RPY_FUNCTIONMODULE_READ_NEW — handles source lines longer than 72 chars.
    /// The source is returned in the CHANGING parameter NEW_SOURCE (RSFB_SOURCE = STRING_TABLE)
    /// which SapNwRfc cannot map natively. We read it via reflection + SAP RFC C SDK interop.
    /// </summary>
    private string? GetSourceViaRpyNew(string functionName)
    {
        try
        {
            using var func = Connection.CreateFunction("RPY_FUNCTIONMODULE_READ_NEW");
            func.Invoke(new RpyFuncReadInput { FunctionName = functionName });

            var lines = SapRfcStringTableReader.ReadStringTable(func, "NEW_SOURCE");
            if (lines is { Count: > 0 })
                return string.Join(Environment.NewLine, lines);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Fallback: construct include name from TFDIR (PNAME + INCLUDE suffix),
    /// then read it via RFC_READ_REPORT.
    /// TFDIR.INCLUDE stores a numeric suffix (e.g. "11"), the real include
    /// program is L&lt;AREA&gt;U&lt;NN&gt; where AREA = PNAME minus "SAPL" prefix.
    /// </summary>
    private string? GetSourceViaReadReport(string functionName)
    {
        try
        {
            var tfdir = ReadTable("TFDIR", ["PNAME", "INCLUDE"], $"FUNCNAME = '{functionName}'");
            if (tfdir.Count == 0)
                return null;

            var pname = tfdir[0].GetValueOrDefault("PNAME", "").Trim();
            var includeSuffix = tfdir[0].GetValueOrDefault("INCLUDE", "").Trim();

            if (string.IsNullOrEmpty(pname))
                return null;

            // Derive function group area from pool name (SAPLZSW_APP → ZSW_APP)
            var area = pname.StartsWith("SAPL", StringComparison.OrdinalIgnoreCase)
                ? pname.Substring(4)
                : pname;

            // Build the include program name: L<AREA>U<suffix>
            string includeName;
            if (!string.IsNullOrEmpty(includeSuffix))
                includeName = $"L{area}U{includeSuffix}";
            else
                includeName = $"L{area}U01"; // default to U01 if no suffix

            return ReadReportSource(includeName);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Read an ABAP program's source via RFC_READ_REPORT.
    /// </summary>
    private string? ReadReportSource(string programName)
    {
        try
        {
            using var readReport = Connection.CreateFunction("RFC_READ_REPORT");
            var output = readReport.Invoke<ReadReportOutput>(
                new ReadReportInput { ProgramName = programName });
            var lines = output.Source ?? [];
            if (lines.Length > 0)
                return string.Join(Environment.NewLine, lines.Select(l => l.Line));
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Get interface from RPY_FUNCTIONMODULE_READ export parameters.
    /// </summary>
    private FuncParam[]? GetInterfaceViaRpy(string functionName)
    {
        try
        {
            using var func = Connection.CreateFunction("RPY_FUNCTIONMODULE_READ");
            var output = func.Invoke<RpyFuncInterfaceOutput>(
                new RpyFuncReadInput { FunctionName = functionName });

            var all = new List<FuncParam>();
            foreach (var p in output.ImportParams ?? [])
                all.Add(new FuncParam("I", p.Parameter.Trim(), p.Reference.Trim(), p.DefaultValue.Trim(), p.Optional.Trim()));
            foreach (var p in output.ExportParams ?? [])
                all.Add(new FuncParam("E", p.Parameter.Trim(), p.Reference.Trim(), "", ""));
            foreach (var p in output.ChangingParams ?? [])
                all.Add(new FuncParam("C", p.Parameter.Trim(), p.Reference.Trim(), p.DefaultValue.Trim(), p.Optional.Trim()));
            foreach (var p in output.TableParams ?? [])
                all.Add(new FuncParam("T", p.Parameter.Trim(), p.Reference.Trim(), "", ""));

            if (all.Count > 0)
                return all.ToArray();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Fallback: RFC_GET_FUNCTION_INTERFACE.
    /// </summary>
    private FuncParam[]? GetInterfaceViaRfc(string functionName)
    {
        try
        {
            using var ifFunc = Connection.CreateFunction("RFC_GET_FUNCTION_INTERFACE");
            var ifOutput = ifFunc.Invoke<FuncInterfaceOutput>(
                new ReadFuncModuleInput { FuncName = functionName });
            var raw = ifOutput.Params ?? [];
            if (raw.Length > 0)
                return raw.Select(r => new FuncParam(
                    r.ParamKind.Trim(), r.Parameter.Trim(),
                    $"{r.TabName.Trim()} {r.FieldName.Trim()}".Trim(),
                    r.Default.Trim(), r.Optional.Trim())).ToArray();
        }
        catch { }
        return null;
    }

    private static string BuildHeader(
        string name,
        string? funcGroup,
        string? packageName,
        string? description,
        FuncParam[] signature,
        ImportOptions options)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"* Function Module: {name}");
        if (funcGroup is not null)
            sb.AppendLine($"* Function Group:  {funcGroup}");
        if (packageName is not null)
            sb.AppendLine($"* Package:         {packageName}");
        if (description is not null)
            sb.AppendLine($"* Description:     {description}");
        sb.AppendLine($"* Extracted:       {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        if (options.IncludeSignature && signature.Length > 0)
        {
            sb.AppendLine("*----------------------------------------------------------------------*");
            foreach (var kind in new[] { ("I", "IMPORTING"), ("E", "EXPORTING"), ("C", "CHANGING"), ("T", "TABLES") })
            {
                var group = signature.Where(s => s.Kind == kind.Item1).ToArray();
                if (group.Length > 0)
                {
                    sb.AppendLine($"* {kind.Item2}");
                    foreach (var p in group)
                        sb.AppendLine($"*   {p.Name,-30} TYPE {p.TypeRef}".TrimEnd());
                }
            }
        }

        sb.AppendLine("*----------------------------------------------------------------------*");
        return sb.ToString();
    }
}

// Unified parameter info
internal sealed record FuncParam(string Kind, string Name, string TypeRef, string DefaultValue, string Optional);

// RPY_FUNCTIONMODULE_READ models
internal sealed class RpyFuncReadInput
{
    [SapName("FUNCTIONNAME")]
    public string FunctionName { get; set; } = string.Empty;
}

internal sealed class RpyFuncReadOutput
{
    [SapName("SOURCE")]
    public SourceLine[] Source { get; set; } = [];
}

internal sealed class RpyFuncReadNewOutput
{
    [SapName("NEW_SOURCE")]
    public StringTableRow[] NewSource { get; set; } = [];

    [SapName("SOURCE")]
    public SourceLine[] Source { get; set; } = [];
}

internal sealed class StringTableRow
{
    [SapName("TABLE_LINE")]
    public string Value { get; set; } = string.Empty;
}

internal sealed class RpyFuncInterfaceOutput
{
    [SapName("IMPORT_PARAMETER")]
    public RpyParam[] ImportParams { get; set; } = [];

    [SapName("EXPORT_PARAMETER")]
    public RpyParam[] ExportParams { get; set; } = [];

    [SapName("CHANGING_PARAMETER")]
    public RpyParam[] ChangingParams { get; set; } = [];

    [SapName("TABLES_PARAMETER")]
    public RpyParam[] TableParams { get; set; } = [];
}

internal sealed class RpyParam
{
    [SapName("PARAMETER")]
    public string Parameter { get; set; } = string.Empty;

    [SapName("REFERENCE")]
    public string Reference { get; set; } = string.Empty;

    [SapName("DEFAULT")]
    public string DefaultValue { get; set; } = string.Empty;

    [SapName("OPTIONAL")]
    public string Optional { get; set; } = string.Empty;
}

// RFC_READ_REPORT models
internal sealed class ReadReportInput
{
    [SapName("PROGRAM")]
    public string ProgramName { get; set; } = string.Empty;
}

internal sealed class ReadReportOutput
{
    [SapName("REPORT_TAB")]
    public SourceLine[] Source { get; set; } = [];
}

// RFC_GET_FUNCTION_INTERFACE models (fallback)
internal sealed class FuncInterfaceOutput
{
    [SapName("PARAMS")]
    public FuncInterfaceParam[] Params { get; set; } = [];
}

internal sealed class ReadFuncModuleInput
{
    [SapName("FUNCNAME")]
    public string FuncName { get; set; } = string.Empty;
}

internal sealed class SourceLine
{
    [SapName("LINE")]
    public string Line { get; set; } = string.Empty;
}

internal sealed class FuncInterfaceParam
{
    [SapName("PARAMCLASS")]
    public string ParamKind { get; set; } = string.Empty;

    [SapName("PARAMETER")]
    public string Parameter { get; set; } = string.Empty;

    [SapName("TABNAME")]
    public string TabName { get; set; } = string.Empty;

    [SapName("FIELDNAME")]
    public string FieldName { get; set; } = string.Empty;

    [SapName("EXID")]
    public string Exid { get; set; } = string.Empty;

    [SapName("OPTIONAL")]
    public string Optional { get; set; } = string.Empty;

    [SapName("DEFAULT")]
    public string Default { get; set; } = string.Empty;
}
