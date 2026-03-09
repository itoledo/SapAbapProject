namespace SapAbapProject.Core.Models;

/// <summary>
/// Types of ABAP repository objects that can be extracted from SAP.
/// </summary>
public enum AbapObjectType
{
    FunctionGroup,
    FunctionModule,
    DataElement,
    Domain,
    Structure,
    TransparentTable,
    TableType,
    Program,
    Include,
}
