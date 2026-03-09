namespace SapAbapProject.Core.Models;

public sealed record ImportProgress
{
    public required string CurrentObject { get; init; }
    public AbapObjectType ObjectType { get; init; }
    public int ProcessedCount { get; init; }
    public int TotalCount { get; init; }
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }

    public double PercentComplete => TotalCount > 0
        ? (double)ProcessedCount / TotalCount * 100.0
        : 0.0;
}
