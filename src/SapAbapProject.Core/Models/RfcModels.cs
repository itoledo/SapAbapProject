namespace SapAbapProject.Core.Models;

public sealed record RepositoryObjectSummary(
    string ProgramId,
    string ObjectType,
    string Name,
    string? PackageName);

public sealed record RfcFunctionDefinition(
    string Name,
    IReadOnlyList<RfcParameterDefinition> Parameters,
    IReadOnlyList<RfcExceptionDefinition> Exceptions);

public sealed record RfcParameterDefinition(
    string Name,
    string Direction,
    string Type,
    string? TypeName,
    uint Length,
    uint Decimals,
    bool Optional,
    string? DefaultValue,
    string? Description,
    IReadOnlyList<RfcFieldDefinition>? Fields);

public sealed record RfcFieldDefinition(
    string Name,
    string Type,
    string? TypeName,
    uint Length,
    uint Decimals,
    IReadOnlyList<RfcFieldDefinition>? Fields);

public sealed record RfcExceptionDefinition(string Key, string? Message);

public sealed record RfcExecutionResult(
    string FunctionName,
    long DurationMilliseconds,
    IReadOnlyDictionary<string, object?> Output,
    IReadOnlyList<string> TruncatedTableParameters);

public sealed record AbapSourceResult(
    string Name,
    string SourceKind,
    string? PackageName,
    string Source);
