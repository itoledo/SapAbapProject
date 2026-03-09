using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Extractors;

internal sealed class DomainExtractor : BaseExtractor
{
    public DomainExtractor(SapConnection connection) : base(connection) { }

    public override AbapObjectType ObjectType => AbapObjectType.Domain;

    public override async Task<IReadOnlyList<AbapObject>> ExtractAsync(
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<AbapObject>();

            foreach (var package in options.Packages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tadir = ReadTable("TADIR",
                    ["OBJ_NAME"],
                    $"PGMID = 'R3TR' AND OBJECT = 'DOMA' AND DEVCLASS = '{package}'");

                foreach (var entry in tadir)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var domaName = entry["OBJ_NAME"].Trim();

                    try
                    {
                        var obj = ExtractDomain(domaName, package);
                        if (obj is not null)
                            results.Add(obj);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error extracting DOMA {domaName}: {ex.Message}");
                    }
                }
            }

            return results;
        }, cancellationToken);
    }

    private AbapObject? ExtractDomain(string name, string package)
    {
        var dd01l = ReadTable("DD01L",
            ["DOMNAME", "DATATYPE", "LENG", "DECIMALS", "OUTPUTLEN", "ENTITYTAB"],
            $"DOMNAME = '{name}' AND AS4LOCAL = 'A'");

        if (dd01l.Count == 0)
            return null;

        var row = dd01l[0];
        var dataType = row.GetValueOrDefault("DATATYPE", "").Trim();
        var length = row.GetValueOrDefault("LENG", "").Trim();
        var decimals = row.GetValueOrDefault("DECIMALS", "").Trim();
        var outputLen = row.GetValueOrDefault("OUTPUTLEN", "").Trim();

        // Description
        string? description = null;
        var dd01t = ReadTable("DD01T", ["DDTEXT"],
            $"DOMNAME = '{name}' AND DDLANGUAGE = 'E' AND AS4LOCAL = 'A'");
        if (dd01t.Count > 0)
            description = dd01t[0].GetValueOrDefault("DDTEXT", "").Trim();

        // Fixed values
        var fixedValues = ReadTable("DD07L",
            ["DOMVALUE_L", "DOMVALUE_H"],
            $"DOMNAME = '{name}' AND AS4LOCAL = 'A'");

        // Fixed value texts
        var fixedValueTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fixedValues.Count > 0)
        {
            var dd07t = ReadTable("DD07T",
                ["DOMVALUE_L", "DDTEXT"],
                $"DOMNAME = '{name}' AND DDLANGUAGE = 'E' AND AS4LOCAL = 'A'");
            foreach (var fvt in dd07t)
                fixedValueTexts[fvt["DOMVALUE_L"].Trim()] = fvt.GetValueOrDefault("DDTEXT", "").Trim();
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"* Domain: {name}");
        sb.AppendLine($"* Package: {package}");
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"* Description: {description}");
        sb.AppendLine($"* Extracted: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"DOMAIN {name}.");
        sb.AppendLine($"  DATA-TYPE: {dataType}({length}).");
        if (!string.IsNullOrEmpty(decimals) && decimals != "0" && decimals != "000000")
            sb.AppendLine($"  DECIMALS: {decimals}.");
        if (!string.IsNullOrEmpty(outputLen) && outputLen != "000000")
            sb.AppendLine($"  OUTPUT-LENGTH: {outputLen}.");

        if (fixedValues.Count > 0)
        {
            sb.AppendLine("  FIXED-VALUES:");
            foreach (var fv in fixedValues)
            {
                var val = fv["DOMVALUE_L"].Trim();
                var high = fv.GetValueOrDefault("DOMVALUE_H", "").Trim();
                var text = fixedValueTexts.GetValueOrDefault(val, "");
                if (!string.IsNullOrEmpty(high))
                    sb.AppendLine($"    '{val}' - '{high}'" + (text != "" ? $"  \"{text}\"" : "") + ".");
                else
                    sb.AppendLine($"    '{val}'" + (text != "" ? $"  \"{text}\"" : "") + ".");
            }
        }

        sb.AppendLine("END-DOMAIN.");

        return new AbapObject
        {
            Name = name,
            ObjectType = AbapObjectType.Domain,
            PackageName = package,
            Description = description,
            SourceCode = sb.ToString(),
        };
    }
}
