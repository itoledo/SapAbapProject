namespace SapAbapProject.RfcExtractor;

internal static class SapRfcQuery
{
    public static string Literal(string value) => value.Replace("'", "''");

    public static string LikePattern(string value) => Literal(value.Replace('*', '%'));

    public static string Identifier(string value, string parameterName)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("SAP identifier cannot be empty.", parameterName);

        foreach (var character in normalized)
        {
            if (!char.IsLetterOrDigit(character) &&
                character is not ('_' or '/' or '$' or '%' or '-'))
            {
                throw new ArgumentException(
                    $"SAP identifier '{value}' contains unsupported characters.",
                    parameterName);
            }
        }

        return normalized;
    }
}
