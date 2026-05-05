namespace SapAbapProject.Core.Models;

/// <summary>
/// Maps language identifiers to the single-character SAP DDIC language codes
/// stored in fields like <c>DDLANGUAGE</c> / <c>SPRAS</c>.
/// </summary>
public static class SapLanguageCode
{
    /// <summary>
    /// Returns the SAP single-char code for the given input. Accepts:
    /// already-single-char SAP codes (passed through, uppercased), ISO 639-1
    /// two-char codes (mapped via the common SAP table), and a few common
    /// long names. Falls back to the first letter for unknown two-char codes.
    /// </summary>
    public static string Resolve(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "E";
        var c = code.Trim().ToUpperInvariant();
        if (c.Length == 1) return c;
        return c switch
        {
            "EN" => "E",
            "ES" => "S",
            "DE" => "D",
            "FR" => "F",
            "IT" => "I",
            "PT" => "P",
            "NL" => "N",
            "JA" => "J",
            "RU" => "R",
            "PL" => "L",
            "TR" => "T",
            "CS" => "C",
            "HU" => "H",
            "FI" => "U",
            "DA" => "K",
            "NO" => "O",
            "SV" => "V",
            "EL" => "G",
            "AR" => "A",
            "ZH" => "1",
            "KO" => "3",
            "TH" => "2",
            "HE" => "B",
            _ => c.Substring(0, 1),
        };
    }

    /// <summary>
    /// Builds the default description-language cascade: the connection language
    /// resolved to its SAP code, followed by English (<c>"E"</c>) as a fallback,
    /// with duplicates removed.
    /// </summary>
    public static IReadOnlyList<string> DefaultCascade(string connectionLanguage)
    {
        var primary = Resolve(connectionLanguage);
        return primary == "E" ? new[] { "E" } : new[] { primary, "E" };
    }
}
