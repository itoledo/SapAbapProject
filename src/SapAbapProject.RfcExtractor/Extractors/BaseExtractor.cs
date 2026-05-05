using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Extractors;

internal abstract class BaseExtractor : IObjectExtractor
{
    protected readonly SapConnection Connection;
    protected readonly IReadOnlyList<string> DescriptionLanguages;

    protected BaseExtractor(SapConnection connection, IReadOnlyList<string>? descriptionLanguages = null)
    {
        Connection = connection;
        DescriptionLanguages = descriptionLanguages is { Count: > 0 } ? descriptionLanguages : new[] { "E" };
    }

    /// <summary>
    /// Reads description rows trying each configured language in order. Returns
    /// the first non-empty result, or an empty list if none of the languages match.
    /// </summary>
    protected IReadOnlyList<Dictionary<string, string>> ReadTableInLanguages(
        string tableName,
        string[] fields,
        string baseWhere,
        string languageField,
        int maxRows = 10000)
    {
        foreach (var lang in DescriptionLanguages)
        {
            var where = string.IsNullOrEmpty(baseWhere)
                ? $"{languageField} = '{lang}'"
                : $"{baseWhere} AND {languageField} = '{lang}'";
            var rows = ReadTable(tableName, fields, where, maxRows);
            if (rows.Count > 0) return rows;
        }
        return Array.Empty<Dictionary<string, string>>();
    }

    public abstract AbapObjectType ObjectType { get; }

    public abstract Task<IReadOnlyList<AbapObject>> ExtractAsync(
        ImportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls RFC_READ_TABLE to read rows from an SAP table with optional filter.
    /// Default <paramref name="maxRows"/> is 10000 — some SAP systems interpret
    /// <c>ROWCOUNT=0</c> as "return zero rows" rather than "no limit".
    /// </summary>
    protected IReadOnlyList<Dictionary<string, string>> ReadTable(
        string tableName,
        string[] fields,
        string? whereClause = null,
        int maxRows = 10000)
    {
        using var function = Connection.CreateFunction("RFC_READ_TABLE");
        var output = function.Invoke<RfcReadTableOutput>(new RfcReadTableInput
        {
            QueryTable = tableName,
            Delimiter = "|",
            RowCount = maxRows,
            Fields = fields.Select(f => new RfcTableField { FieldName = f }).ToArray(),
            Options = string.IsNullOrEmpty(whereClause)
                ? []
                : SplitWhereClause(whereClause!),
        });

        var data = output.Data ?? [];
        var fieldList = output.Fields ?? [];

        var result = new List<Dictionary<string, string>>();
        foreach (var row in data)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var values = row.Wa.Split('|');
            for (int i = 0; i < fieldList.Length && i < values.Length; i++)
            {
                dict[fieldList[i].FieldName.Trim()] = values[i].Trim();
            }
            result.Add(dict);
        }
        return result;
    }

    /// <summary>
    /// Calls RFC_READ_TABLE and returns both the field metadata and the rows.
    /// Used by the public ReadTableAsync escape hatch.
    /// </summary>
    internal TableReadResult ReadTableWithMetadata(
        string tableName,
        IReadOnlyList<string> fields,
        string? whereClause,
        int maxRows)
    {
        using var function = Connection.CreateFunction("RFC_READ_TABLE");
        var output = function.Invoke<RfcReadTableOutput>(new RfcReadTableInput
        {
            QueryTable = tableName,
            Delimiter = "|",
            RowCount = maxRows,
            Fields = fields.Select(f => new RfcTableField { FieldName = f }).ToArray(),
            Options = string.IsNullOrEmpty(whereClause)
                ? []
                : SplitWhereClause(whereClause!),
        });

        var data = output.Data ?? [];
        var fieldList = output.Fields ?? [];

        var fieldInfos = fieldList.Select(f => new TableReadField(
            Name: f.FieldName.Trim(),
            Type: f.Type.Trim(),
            Length: int.TryParse(f.Length.Trim(), out var len) ? len : 0,
            Offset: int.TryParse(f.Offset.Trim(), out var off) ? off : 0,
            Description: string.IsNullOrWhiteSpace(f.FieldText) ? null : f.FieldText.Trim()
        )).ToList();

        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var row in data)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var values = row.Wa.Split('|');
            for (int i = 0; i < fieldInfos.Count && i < values.Length; i++)
            {
                dict[fieldInfos[i].Name] = values[i].Trim();
            }
            rows.Add(dict);
        }

        return new TableReadResult(fieldInfos, rows);
    }

    /// <summary>
    /// Splits a WHERE clause into 72-character chunks as required by RFC_READ_TABLE OPTIONS parameter.
    /// </summary>
    private static RfcReadTableOption[] SplitWhereClause(string where)
    {
        var options = new List<RfcReadTableOption>();
        for (int i = 0; i < where.Length; i += 72)
        {
            var chunk = where.Substring(i, Math.Min(72, where.Length - i));
            options.Add(new RfcReadTableOption { Text = chunk });
        }
        return options.ToArray();
    }
}

// Output model for RFC_READ_TABLE
internal sealed class RfcReadTableOutput
{
    [SapName("DATA")]
    public RfcReadTableDataRow[] Data { get; set; } = [];

    [SapName("FIELDS")]
    public RfcReadTableFieldInfo[] Fields { get; set; } = [];
}

// Input/output models for RFC_READ_TABLE
internal sealed class RfcReadTableInput
{
    [SapName("QUERY_TABLE")]
    public string QueryTable { get; set; } = string.Empty;

    [SapName("DELIMITER")]
    public string Delimiter { get; set; } = "|";

    [SapName("ROWCOUNT")]
    public int RowCount { get; set; }

    [SapName("FIELDS")]
    public RfcTableField[] Fields { get; set; } = [];

    [SapName("OPTIONS")]
    public RfcReadTableOption[] Options { get; set; } = [];
}

internal sealed class RfcTableField
{
    [SapName("FIELDNAME")]
    public string FieldName { get; set; } = string.Empty;
}

internal sealed class RfcReadTableOption
{
    [SapName("TEXT")]
    public string Text { get; set; } = string.Empty;
}

internal sealed class RfcReadTableDataRow
{
    [SapName("WA")]
    public string Wa { get; set; } = string.Empty;
}

internal sealed class RfcReadTableFieldInfo
{
    [SapName("FIELDNAME")]
    public string FieldName { get; set; } = string.Empty;

    [SapName("OFFSET")]
    public string Offset { get; set; } = string.Empty;

    [SapName("LENGTH")]
    public string Length { get; set; } = string.Empty;

    [SapName("TYPE")]
    public string Type { get; set; } = string.Empty;

    [SapName("FIELDTEXT")]
    public string FieldText { get; set; } = string.Empty;
}
