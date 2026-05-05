namespace SapAbapProject.Core.Models;

/// <summary>Lightweight summary returned by list/discovery operations.</summary>
public sealed record AbapObjectSummary(
    string Name,
    AbapObjectType ObjectType,
    string? PackageName,
    string? FunctionGroup,
    string? Description);

/// <summary>Result of a generic <c>RFC_READ_TABLE</c> call.</summary>
public sealed record TableReadResult(
    IReadOnlyList<TableReadField> Fields,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed record TableReadField(
    string Name,
    string Type,
    int Length,
    int Offset,
    string? Description);
