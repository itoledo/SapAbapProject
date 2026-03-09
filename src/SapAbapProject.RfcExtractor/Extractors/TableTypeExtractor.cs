using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Extractors;

internal sealed class TableTypeExtractor : BaseExtractor
{
    public TableTypeExtractor(SapConnection connection) : base(connection) { }

    public override AbapObjectType ObjectType => AbapObjectType.TableType;

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
                    $"PGMID = 'R3TR' AND OBJECT = 'TTYP' AND DEVCLASS = '{package}'");

                foreach (var entry in tadir)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ttypName = entry["OBJ_NAME"].Trim();

                    try
                    {
                        var obj = ExtractTableType(ttypName, package);
                        if (obj is not null)
                            results.Add(obj);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error extracting TTYP {ttypName}: {ex.Message}");
                    }
                }
            }

            return results;
        }, cancellationToken);
    }

    private AbapObject? ExtractTableType(string name, string package)
    {
        var dd40l = ReadTable("DD40L",
            ["TYPENAME", "ROWTYPE", "ROWKIND", "ACCESSMODE", "KEYDEF", "KEYKIND"],
            $"TYPENAME = '{name}' AND AS4LOCAL = 'A'");

        if (dd40l.Count == 0)
            return null;

        var row = dd40l[0];
        var rowType = row.GetValueOrDefault("ROWTYPE", "").Trim();
        var rowKind = row.GetValueOrDefault("ROWKIND", "").Trim();
        var accessMode = row.GetValueOrDefault("ACCESSMODE", "").Trim();
        var keyDef = row.GetValueOrDefault("KEYDEF", "").Trim();

        // Description
        string? description = null;
        var dd40t = ReadTable("DD40T", ["DDTEXT"],
            $"TYPENAME = '{name}' AND DDLANGUAGE = 'E' AND AS4LOCAL = 'A'");
        if (dd40t.Count > 0)
            description = dd40t[0].GetValueOrDefault("DDTEXT", "").Trim();

        var accessDesc = accessMode switch
        {
            "S" => "STANDARD TABLE",
            "T" => "SORTED TABLE",
            "H" => "HASHED TABLE",
            _ => "TABLE",
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"* Table Type: {name}");
        sb.AppendLine($"* Package: {package}");
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"* Description: {description}");
        sb.AppendLine($"* Extracted: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"TABLE-TYPE {name}.");
        sb.AppendLine($"  ROW-TYPE: {rowType}.");
        sb.AppendLine($"  ACCESS-MODE: {accessDesc}.");

        // Key components
        if (keyDef == "K")
        {
            var dd42v = ReadTable("DD42V",
                ["SECKEYNAME", "KEYFIELD"],
                $"TYPENAME = '{name}' AND AS4LOCAL = 'A'");

            if (dd42v.Count > 0)
            {
                sb.AppendLine($"  KEY:");
                foreach (var kf in dd42v)
                    sb.AppendLine($"    {kf["KEYFIELD"].Trim()}.");
            }
        }

        sb.AppendLine("END-TABLE-TYPE.");

        return new AbapObject
        {
            Name = name,
            ObjectType = AbapObjectType.TableType,
            PackageName = package,
            Description = description,
            SourceCode = sb.ToString(),
        };
    }
}
