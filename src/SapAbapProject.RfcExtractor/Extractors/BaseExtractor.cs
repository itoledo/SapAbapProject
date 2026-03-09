using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Extractors;

internal abstract class BaseExtractor : IObjectExtractor
{
    protected readonly SapConnection Connection;

    protected BaseExtractor(SapConnection connection)
    {
        Connection = connection;
    }

    public abstract AbapObjectType ObjectType { get; }

    public abstract Task<IReadOnlyList<AbapObject>> ExtractAsync(
        ImportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls RFC_READ_TABLE to read rows from an SAP table with optional filter.
    /// </summary>
    protected IReadOnlyList<Dictionary<string, string>> ReadTable(
        string tableName,
        string[] fields,
        string? whereClause = null,
        int maxRows = 0)
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
