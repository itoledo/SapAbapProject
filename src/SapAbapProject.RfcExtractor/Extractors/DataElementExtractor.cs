using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Extractors;

internal sealed class DataElementExtractor : BaseExtractor
{
    public DataElementExtractor(SapConnection connection) : base(connection) { }

    public override AbapObjectType ObjectType => AbapObjectType.DataElement;

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

                // Get data elements in the package from TADIR
                var tadir = ReadTable("TADIR",
                    ["OBJ_NAME"],
                    $"PGMID = 'R3TR' AND OBJECT = 'DTEL' AND DEVCLASS = '{package}'");

                foreach (var entry in tadir)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dtelName = entry["OBJ_NAME"].Trim();

                    try
                    {
                        var obj = ExtractDataElement(dtelName, package);
                        if (obj is not null)
                            results.Add(obj);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error extracting DTEL {dtelName}: {ex.Message}");
                    }
                }
            }

            return results;
        }, cancellationToken);
    }

    private AbapObject? ExtractDataElement(string name, string package)
    {
        // Read main definition from DD04L
        var dd04l = ReadTable("DD04L",
            ["ROLLNAME", "DOMNAME", "DATATYPE", "LENG", "DECIMALS", "LOGFLAG"],
            $"ROLLNAME = '{name}' AND AS4LOCAL = 'A'");

        if (dd04l.Count == 0)
            return null;

        var row = dd04l[0];
        var domainName = row.GetValueOrDefault("DOMNAME", "").Trim();
        var dataType = row.GetValueOrDefault("DATATYPE", "").Trim();
        var length = row.GetValueOrDefault("LENG", "").Trim();
        var decimals = row.GetValueOrDefault("DECIMALS", "").Trim();

        // Read description from DD04T
        string? description = null;
        var dd04t = ReadTable("DD04T",
            ["DDTEXT", "REPTEXT", "SCRTEXT_S", "SCRTEXT_M", "SCRTEXT_L"],
            $"ROLLNAME = '{name}' AND DDLANGUAGE = 'E' AND AS4LOCAL = 'A'");

        string shortText = "", mediumText = "", longText = "", headingText = "";
        if (dd04t.Count > 0)
        {
            description = dd04t[0].GetValueOrDefault("DDTEXT", "").Trim();
            shortText = dd04t[0].GetValueOrDefault("SCRTEXT_S", "").Trim();
            mediumText = dd04t[0].GetValueOrDefault("SCRTEXT_M", "").Trim();
            longText = dd04t[0].GetValueOrDefault("SCRTEXT_L", "").Trim();
            headingText = dd04t[0].GetValueOrDefault("REPTEXT", "").Trim();
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"* Data Element: {name}");
        sb.AppendLine($"* Package:      {package}");
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"* Description:  {description}");
        sb.AppendLine($"* Extracted:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("*----------------------------------------------------------------------*");
        sb.AppendLine($"DATA-ELEMENT {name}.");
        if (!string.IsNullOrEmpty(domainName))
            sb.AppendLine($"  DOMAIN: {domainName}.");
        if (!string.IsNullOrEmpty(dataType))
        {
            sb.Append($"  DATA-TYPE: {dataType}");
            if (!string.IsNullOrEmpty(length))
                sb.Append($"({length}");
            if (!string.IsNullOrEmpty(decimals) && decimals != "0" && decimals != "000000")
                sb.Append($",{decimals}");
            if (!string.IsNullOrEmpty(length))
                sb.Append(")");
            sb.AppendLine(".");
        }
        if (!string.IsNullOrEmpty(shortText))
            sb.AppendLine($"  FIELD-LABEL SHORT: '{shortText}'.");
        if (!string.IsNullOrEmpty(mediumText))
            sb.AppendLine($"  FIELD-LABEL MEDIUM: '{mediumText}'.");
        if (!string.IsNullOrEmpty(longText))
            sb.AppendLine($"  FIELD-LABEL LONG: '{longText}'.");
        if (!string.IsNullOrEmpty(headingText))
            sb.AppendLine($"  FIELD-LABEL HEADING: '{headingText}'.");
        sb.AppendLine($"END-DATA-ELEMENT.");

        return new AbapObject
        {
            Name = name,
            ObjectType = AbapObjectType.DataElement,
            PackageName = package,
            Description = description,
            SourceCode = sb.ToString(),
        };
    }
}
