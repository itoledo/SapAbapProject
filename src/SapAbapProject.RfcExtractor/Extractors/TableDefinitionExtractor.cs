using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Extractors;

internal sealed class TableDefinitionExtractor : BaseExtractor
{
    private readonly AbapObjectType _objectType;

    private TableDefinitionExtractor(SapConnection connection, AbapObjectType objectType, IReadOnlyList<string>? descriptionLanguages)
        : base(connection, descriptionLanguages)
    {
        _objectType = objectType;
    }

    public static TableDefinitionExtractor ForTables(SapConnection connection, IReadOnlyList<string>? descriptionLanguages = null) =>
        new(connection, AbapObjectType.TransparentTable, descriptionLanguages);

    public static TableDefinitionExtractor ForStructures(SapConnection connection, IReadOnlyList<string>? descriptionLanguages = null) =>
        new(connection, AbapObjectType.Structure, descriptionLanguages);

    public override AbapObjectType ObjectType => _objectType;

    /// <summary>Extracts a single table or structure by name. Auto-detects type and looks up package.</summary>
    internal Task<AbapObject?> ExtractByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var tabClass = _objectType == AbapObjectType.TransparentTable ? "TRANSP" : "INTTAB";
            var dd02l = ReadTable("DD02L",
                ["TABCLASS"],
                $"TABNAME = '{name}' AND AS4LOCAL = 'A' AND TABCLASS = '{tabClass}'");
            if (dd02l.Count == 0)
                return null;

            var tadirObject = "TABL";
            var tadir = ReadTable("TADIR", ["DEVCLASS"],
                $"PGMID = 'R3TR' AND OBJECT = '{tadirObject}' AND OBJ_NAME = '{name}'");
            var package = tadir.Count > 0 ? tadir[0]["DEVCLASS"].Trim() : "";

            return ExtractTableDefinition(name, package, tabClass);
        }, cancellationToken);

    /// <summary>
    /// Lists tables (or structures) matching a name pattern, optionally filtered by package.
    /// </summary>
    internal Task<IReadOnlyList<AbapObjectSummary>> ListAsync(
        string? namePattern,
        string? packageFilter,
        int maxRows,
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<AbapObjectSummary>>(() =>
        {
            var tabClass = _objectType == AbapObjectType.TransparentTable ? "TRANSP" : "INTTAB";
            var conditions = new List<string> { $"TABCLASS = '{tabClass}'", "AS4LOCAL = 'A'" };
            if (!string.IsNullOrEmpty(namePattern))
                conditions.Add($"TABNAME LIKE '{SapRfcQuery.LikePattern(namePattern!)}'");

            var rows = ReadTable("DD02L", ["TABNAME"], string.Join(" AND ", conditions), maxRows);

            var results = new List<AbapObjectSummary>();
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = row["TABNAME"].Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var tadir = ReadTable("TADIR", ["DEVCLASS"],
                    $"PGMID = 'R3TR' AND OBJECT = 'TABL' AND OBJ_NAME = '{name}'");
                var package = tadir.Count > 0 ? tadir[0]["DEVCLASS"].Trim() : null;

                if (!string.IsNullOrEmpty(packageFilter) &&
                    !string.Equals(package, packageFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                string? description = null;
                var dd02t = ReadTableInLanguages("DD02T", ["DDTEXT"],
                    $"TABNAME = '{name}' AND AS4LOCAL = 'A'", "DDLANGUAGE");
                if (dd02t.Count > 0)
                    description = dd02t[0].GetValueOrDefault("DDTEXT", "").Trim();

                results.Add(new AbapObjectSummary(name, _objectType, package, null, description));
            }

            return results;
        }, cancellationToken);

    public override async Task<IReadOnlyList<AbapObject>> ExtractAsync(
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<AbapObject>();
            var tabClass = _objectType == AbapObjectType.TransparentTable ? "TRANSP" : "INTTAB";
            var tadirObject = "TABL";

            foreach (var package in options.Packages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tadir = ReadTable("TADIR",
                    ["OBJ_NAME"],
                    $"PGMID = 'R3TR' AND OBJECT = '{tadirObject}' AND DEVCLASS = '{package}'");

                foreach (var entry in tadir)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tableName = entry["OBJ_NAME"].Trim();

                    try
                    {
                        // Check if it's the right type (TRANSP vs INTTAB)
                        var dd02l = ReadTable("DD02L",
                            ["TABNAME", "TABCLASS", "SQLTAB"],
                            $"TABNAME = '{tableName}' AND AS4LOCAL = 'A' AND TABCLASS = '{tabClass}'");

                        if (dd02l.Count == 0)
                            continue;

                        var obj = ExtractTableDefinition(tableName, package, tabClass);
                        if (obj is not null)
                            results.Add(obj);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error extracting {tadirObject} {tableName}: {ex.Message}");
                    }
                }
            }

            return results;
        }, cancellationToken);
    }

    private AbapObject? ExtractTableDefinition(string name, string package, string tabClass)
    {
        // Description
        string? description = null;
        var dd02t = ReadTableInLanguages("DD02T", ["DDTEXT"],
            $"TABNAME = '{name}' AND AS4LOCAL = 'A'", "DDLANGUAGE");
        if (dd02t.Count > 0)
            description = dd02t[0].GetValueOrDefault("DDTEXT", "").Trim();

        // Fields
        var dd03l = ReadTable("DD03L",
            ["FIELDNAME", "POSITION", "KEYFLAG", "ROLLNAME", "DATATYPE", "LENG", "DECIMALS", "NOTNULL"],
            $"TABNAME = '{name}' AND AS4LOCAL = 'A' AND FIELDNAME <> '.INCLUDE'");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"* {(_objectType == AbapObjectType.TransparentTable ? "Table" : "Structure")}: {name}");
        sb.AppendLine($"* Package: {package}");
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"* Description: {description}");
        sb.AppendLine($"* Table Class: {tabClass}");
        sb.AppendLine($"* Extracted: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("*----------------------------------------------------------------------*");

        var keyword = _objectType == AbapObjectType.TransparentTable ? "TABLE" : "STRUCTURE";
        sb.AppendLine($"@AbapCatalog.tableCategory: '{tabClass}'");
        sb.AppendLine($"DEFINE {keyword} {name}.");

        // Sort by position
        var sortedFields = dd03l
            .OrderBy(f =>
            {
                var pos = f.GetValueOrDefault("POSITION", "0").Trim();
                return int.TryParse(pos, out var p) ? p : 0;
            })
            .ToList();

        // Key fields
        var keyFields = sortedFields.Where(f => f.GetValueOrDefault("KEYFLAG", "").Trim() == "X").ToList();
        if (keyFields.Count > 0)
        {
            sb.AppendLine($"  KEY: {string.Join(", ", keyFields.Select(f => f["FIELDNAME"].Trim()))}.");
        }

        sb.AppendLine();
        foreach (var field in sortedFields)
        {
            var fieldName = field["FIELDNAME"].Trim();
            var rollname = field.GetValueOrDefault("ROLLNAME", "").Trim();
            var dataType = field.GetValueOrDefault("DATATYPE", "").Trim();
            var length = field.GetValueOrDefault("LENG", "").Trim();
            var decimals = field.GetValueOrDefault("DECIMALS", "").Trim();
            var isKey = field.GetValueOrDefault("KEYFLAG", "").Trim() == "X";
            var notNull = field.GetValueOrDefault("NOTNULL", "").Trim() == "X";

            sb.Append($"  {fieldName,-30}");
            if (!string.IsNullOrEmpty(rollname))
                sb.Append($" TYPE {rollname}");
            else if (!string.IsNullOrEmpty(dataType))
            {
                sb.Append($" TYPE {dataType}({length}");
                if (!string.IsNullOrEmpty(decimals) && decimals != "0" && decimals != "000000")
                    sb.Append($",{decimals}");
                sb.Append(")");
            }

            if (notNull)
                sb.Append(" NOT NULL");

            sb.AppendLine(";");
        }

        sb.AppendLine($"END-{keyword}.");

        var fields = sortedFields.Select(f => new TableField(
            Name: f["FIELDNAME"].Trim(),
            Position: int.TryParse(f.GetValueOrDefault("POSITION", "0").Trim(), out var p) ? p : 0,
            IsKey: f.GetValueOrDefault("KEYFLAG", "").Trim() == "X",
            DataElement: string.IsNullOrWhiteSpace(f.GetValueOrDefault("ROLLNAME", "")) ? null : f["ROLLNAME"].Trim(),
            DataType: string.IsNullOrWhiteSpace(f.GetValueOrDefault("DATATYPE", "")) ? null : f["DATATYPE"].Trim(),
            Length: string.IsNullOrWhiteSpace(f.GetValueOrDefault("LENG", "")) ? null : f["LENG"].Trim(),
            Decimals: string.IsNullOrWhiteSpace(f.GetValueOrDefault("DECIMALS", "")) ? null : f["DECIMALS"].Trim(),
            NotNull: f.GetValueOrDefault("NOTNULL", "").Trim() == "X"
        )).ToList();

        AbapObjectMetadata metadata = _objectType == AbapObjectType.TransparentTable
            ? new TableMetadata(tabClass, fields, keyFields.Select(k => k["FIELDNAME"].Trim()).ToList())
            : new StructureMetadata(fields);

        return new AbapObject
        {
            Name = name,
            ObjectType = _objectType,
            PackageName = string.IsNullOrEmpty(package) ? null : package,
            Description = description,
            SourceCode = sb.ToString(),
            Metadata = metadata,
        };
    }
}
