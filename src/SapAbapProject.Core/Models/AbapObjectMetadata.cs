#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
#endif

namespace SapAbapProject.Core.Models;

/// <summary>
/// Structured metadata attached to an <see cref="AbapObject"/>. Concrete subtype
/// depends on <see cref="AbapObject.ObjectType"/>. Suitable for JSON serialization
/// alongside or instead of the rendered ABAP source.
/// </summary>
#if NET8_0_OR_GREATER
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FunctionModuleMetadata), "function_module")]
[JsonDerivedType(typeof(TableMetadata), "table")]
[JsonDerivedType(typeof(StructureMetadata), "structure")]
[JsonDerivedType(typeof(DataElementMetadata), "data_element")]
[JsonDerivedType(typeof(DomainMetadata), "domain")]
[JsonDerivedType(typeof(TableTypeMetadata), "table_type")]
#endif
public abstract record AbapObjectMetadata;

public sealed record FunctionModuleMetadata(
    IReadOnlyList<FunctionParameter> Parameters) : AbapObjectMetadata;

public sealed record FunctionParameter(
    string Kind,
    string Name,
    string TypeRef,
    string? DefaultValue,
    bool Optional);

public sealed record TableMetadata(
    string TableClass,
    IReadOnlyList<TableField> Fields,
    IReadOnlyList<string> KeyFields) : AbapObjectMetadata;

public sealed record StructureMetadata(
    IReadOnlyList<TableField> Fields) : AbapObjectMetadata;

public sealed record TableField(
    string Name,
    int Position,
    bool IsKey,
    string? DataElement,
    string? DataType,
    string? Length,
    string? Decimals,
    bool NotNull);

public sealed record DataElementMetadata(
    string? Domain,
    string? DataType,
    string? Length,
    string? Decimals,
    string? ShortText,
    string? MediumText,
    string? LongText,
    string? HeadingText) : AbapObjectMetadata;

public sealed record DomainMetadata(
    string DataType,
    string? Length,
    string? Decimals,
    string? OutputLength,
    IReadOnlyList<DomainFixedValue> FixedValues) : AbapObjectMetadata;

public sealed record DomainFixedValue(string Low, string? High, string? Text);

public sealed record TableTypeMetadata(
    string RowType,
    string AccessMode,
    IReadOnlyList<string> KeyFields) : AbapObjectMetadata;
