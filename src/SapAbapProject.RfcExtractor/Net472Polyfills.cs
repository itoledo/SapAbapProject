#if !NET5_0_OR_GREATER

using System.Collections.Generic;

namespace SapAbapProject.RfcExtractor;

/// <summary>
/// Polyfill extension methods for .NET Framework 4.7.2 compatibility.
/// </summary>
internal static class Net472Polyfills
{
    /// <summary>
    /// Polyfill for Dictionary.GetValueOrDefault which is not available in .NET Framework 4.7.2.
    /// </summary>
    public static TValue GetValueOrDefault<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue defaultValue)
    {
        return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Polyfill for string.Contains(string, StringComparison) which is not available in .NET Framework 4.7.2.
    /// </summary>
    public static bool Contains(this string source, string value, System.StringComparison comparisonType)
    {
        return source.IndexOf(value, comparisonType) >= 0;
    }
}

#endif
