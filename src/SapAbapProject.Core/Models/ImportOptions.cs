namespace SapAbapProject.Core.Models;

public sealed record ImportOptions
{
    public IReadOnlyList<string> Packages { get; init; } = [];
    public IReadOnlyList<AbapObjectType> ObjectTypes { get; init; } = [];
    public string? FunctionModuleNamePattern { get; init; }
    public string? FunctionGroupFilter { get; init; }
    public bool OverwriteExisting { get; init; }
    public bool IncludeDocumentation { get; init; } = true;
    public bool IncludeSignature { get; init; } = true;
}
