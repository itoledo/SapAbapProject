namespace SapAbapProject.Core.Models;

public sealed record AbapObject
{
    public required string Name { get; init; }
    public required AbapObjectType ObjectType { get; init; }
    public string? PackageName { get; init; }
    public string? FunctionGroup { get; init; }
    public string? Description { get; init; }
    public required string SourceCode { get; init; }

    public string FolderName => ObjectType switch
    {
        AbapObjectType.FunctionGroup => "FunctionGroups",
        AbapObjectType.FunctionModule => "FunctionModules",
        AbapObjectType.DataElement => "DataElements",
        AbapObjectType.Domain => "Domains",
        AbapObjectType.Structure => "Structures",
        AbapObjectType.TransparentTable => "Tables",
        AbapObjectType.TableType => "TableTypes",
        AbapObjectType.Program => "Programs",
        AbapObjectType.Include => "Includes",
        _ => "Other",
    };

    public string FileName => $"{Name}.abap";

    public string RelativePath => PackageName is not null
        ? Path.Combine(FolderName, PackageName, FileName)
        : Path.Combine(FolderName, FileName);
}
