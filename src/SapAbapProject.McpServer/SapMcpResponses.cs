using SapAbapProject.Core.Models;

namespace SapAbapProject.McpServer;

/// <summary>
/// Unified response shape for object-detail tools. Carries the rendered ABAP
/// source so an LLM can read it as text, plus a structured Metadata object
/// for programmatic use.
/// </summary>
public sealed record SapObjectResponse(
    string Name,
    string ObjectType,
    string? Package,
    string? FunctionGroup,
    string? Description,
    string Source,
    AbapObjectMetadata? Metadata)
{
    public static SapObjectResponse From(AbapObject obj) => new(
        Name: obj.Name,
        ObjectType: obj.ObjectType.ToString(),
        Package: obj.PackageName,
        FunctionGroup: obj.FunctionGroup,
        Description: obj.Description,
        Source: obj.SourceCode,
        Metadata: obj.Metadata);
}

public sealed record SapObjectSummaryResponse(
    string Name,
    string ObjectType,
    string? Package,
    string? FunctionGroup,
    string? Description)
{
    public static SapObjectSummaryResponse From(AbapObjectSummary s) => new(
        s.Name, s.ObjectType.ToString(), s.PackageName, s.FunctionGroup, s.Description);
}

public sealed record ConnectionTestResponse(bool Connected);

public sealed record SapRepositoryObjectResponse(
    string ProgramId,
    string ObjectType,
    string Name,
    string? Package)
{
    public static SapRepositoryObjectResponse From(RepositoryObjectSummary item) =>
        new(item.ProgramId, item.ObjectType, item.Name, item.PackageName);
}

public sealed record DictionaryObjectTypeResponse(
    string Name,
    string RepositoryType,
    string Description);

public sealed record DictionaryObjectResponse(
    string Name,
    string DictionaryType,
    string RepositoryType,
    string? Package,
    string? Description)
{
    public static DictionaryObjectResponse From(
        RepositoryObjectSummary item,
        string dictionaryType) =>
        new(item.Name, dictionaryType, item.ObjectType, item.PackageName, null);

    public static DictionaryObjectResponse From(
        AbapObjectSummary item,
        string dictionaryType,
        string repositoryType) =>
        new(item.Name, dictionaryType, repositoryType, item.PackageName, item.Description);
}
