using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using SapAbapProject.Core.Models;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor;

internal static class RfcRuntimeInvoker
{
    private static readonly AssemblyBuilder RuntimeAssembly =
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("SapAbapProject.RfcRuntimeInputs"),
            AssemblyBuilderAccess.Run);

    private static readonly ModuleBuilder RuntimeModule =
        RuntimeAssembly.DefineDynamicModule("RfcRuntimeInputs");

    private static readonly ConcurrentDictionary<string, Type> RuntimeTypes =
        new(StringComparer.Ordinal);

    private static readonly MethodInfo InvokeWithoutInputMethod = typeof(ISapFunction)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(ISapFunction.Invoke)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 0);

    private static readonly MethodInfo InvokeWithInputMethod = typeof(ISapFunction)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(ISapFunction.Invoke)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 1);

    private static readonly object RuntimeTypeLock = new();
    private static int _nextTypeId;

    public static RfcFunctionDefinition GetDefinition(ISapFunctionMetadata metadata)
    {
        var parameters = metadata.Parameters
            .Select(parameter => new RfcParameterDefinition(
                Name: parameter.Name,
                Direction: TrimPrefix(parameter.Direction.ToString(), "RFC_"),
                Type: TrimPrefix(parameter.Type.ToString(), "RFCTYPE_"),
                TypeName: GetTypeName(parameter.Type, parameter.GetTypeMetadata),
                Length: parameter.NucLength,
                Decimals: parameter.Decimals,
                Optional: parameter.IsOptional,
                DefaultValue: NullIfBlank(parameter.DefaultValue),
                Description: NullIfBlank(parameter.Description),
                Fields: GetFields(parameter.Type, parameter.GetTypeMetadata, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase))))
            .ToList();

        var exceptions = metadata.Exceptions
            .Select(exception => new RfcExceptionDefinition(exception.Key, NullIfBlank(exception.Message)))
            .ToList();

        return new RfcFunctionDefinition(metadata.GetName(), parameters, exceptions);
    }

    public static RfcExecutionResult Invoke(
        ISapFunction function,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        int maxTableRows)
    {
        var input = BuildInput(function.Metadata, parameters);
        var outputType = BuildOutputType(function.Metadata);
        var stopwatch = Stopwatch.StartNew();
        var rawOutput = Invoke(function, outputType, input);
        stopwatch.Stop();

        var output = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var truncated = new List<string>();

        foreach (var parameter in function.Metadata.Parameters)
        {
            if (parameter.Direction == SapRfcDirection.RFC_IMPORT)
                continue;

            var value = outputType.GetProperty(parameter.Name)!.GetValue(rawOutput);
            output[parameter.Name] = ConvertOutputValue(value, maxTableRows, parameter.Name, truncated);
        }

        return new RfcExecutionResult(
            functionName,
            stopwatch.ElapsedMilliseconds,
            output,
            truncated);
    }

    private static Type BuildOutputType(ISapFunctionMetadata metadata)
    {
        var properties = metadata.Parameters
            .Where(parameter => parameter.Direction != SapRfcDirection.RFC_IMPORT)
            .Select(parameter => BuildOutputProperty(
                parameter.Name,
                parameter.Type,
                parameter.GetTypeMetadata,
                parameter.Name))
            .ToList();

        return CreateRuntimeType(properties);
    }

    private static RuntimeProperty BuildOutputProperty(
        string name,
        SapRfcType rfcType,
        Func<ISapTypeMetadata> getTypeMetadata,
        string path)
    {
        if (rfcType == SapRfcType.RFCTYPE_STRUCTURE)
        {
            var structureType = BuildOutputComplexType(getTypeMetadata(), path);
            return new RuntimeProperty(name, structureType, null);
        }

        if (rfcType == SapRfcType.RFCTYPE_TABLE)
        {
            var rowType = BuildOutputComplexType(getTypeMetadata(), $"{path}[]");
            return new RuntimeProperty(name, rowType.MakeArrayType(), null);
        }

        return new RuntimeProperty(name, GetScalarType(rfcType), null);
    }

    private static Type BuildOutputComplexType(ISapTypeMetadata metadata, string path)
    {
        var properties = metadata.Fields
            .Select(field => BuildOutputProperty(
                field.Name,
                field.Type,
                field.GetTypeMetadata,
                $"{path}.{field.Name}"))
            .ToList();

        return CreateRuntimeType(properties);
    }

    private static object Invoke(ISapFunction function, Type outputType, object? input)
    {
        var method = (input is null ? InvokeWithoutInputMethod : InvokeWithInputMethod)
            .MakeGenericMethod(outputType);

        try
        {
            return method.Invoke(function, input is null ? null : [input])
                ?? throw new InvalidOperationException(
                    $"RFC '{function.Metadata.GetName()}' returned no output object.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object? BuildInput(
        ISapFunctionMetadata metadata,
        IReadOnlyDictionary<string, object?> values)
    {
        if (values.Count == 0)
            return null;

        var properties = new List<RuntimeProperty>();
        foreach (var pair in values)
        {
            var parameter = FindParameter(metadata, pair.Key)
                ?? throw new ArgumentException($"RFC '{metadata.GetName()}' has no parameter named '{pair.Key}'.", nameof(values));

            if (parameter.Direction == SapRfcDirection.RFC_EXPORT)
                throw new ArgumentException($"RFC parameter '{parameter.Name}' is export-only and cannot be supplied.", nameof(values));

            properties.Add(BuildProperty(
                parameter.Name,
                parameter.Type,
                parameter.GetTypeMetadata,
                pair.Value,
                parameter.Name));
        }

        return CreateRuntimeObject(properties);
    }

    private static RuntimeProperty BuildProperty(
        string name,
        SapRfcType rfcType,
        Func<ISapTypeMetadata> getTypeMetadata,
        object? rawValue,
        string path)
    {
        if (rawValue is null)
            throw new ArgumentException($"RFC input '{path}' cannot be null. Omit optional values instead.");

        if (rfcType == SapRfcType.RFCTYPE_STRUCTURE)
        {
            var dictionary = AsDictionary(rawValue, path);
            var structure = BuildComplexObject(getTypeMetadata(), dictionary, path);
            return new RuntimeProperty(name, structure.GetType(), structure);
        }

        if (rfcType == SapRfcType.RFCTYPE_TABLE)
        {
            var rows = AsRows(rawValue, path);
            var tableMetadata = getTypeMetadata();

            if (rows.Count == 0)
            {
                var emptyRowType = CreateRuntimeType(Array.Empty<RuntimeProperty>());
                return new RuntimeProperty(name, emptyRowType.MakeArrayType(), Array.CreateInstance(emptyRowType, 0));
            }

            var expectedKeys = rows[0].Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
            var firstProperties = BuildComplexProperties(tableMetadata, rows[0], $"{path}[0]");
            var rowType = CreateRuntimeType(firstProperties);
            var table = Array.CreateInstance(rowType, rows.Count);
            table.SetValue(CreateRuntimeObject(rowType, firstProperties), 0);

            for (var index = 1; index < rows.Count; index++)
            {
                var actualKeys = rows[index].Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
                if (!expectedKeys.SequenceEqual(actualKeys, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException($"All rows in RFC table input '{path}' must contain the same fields.");

                var rowProperties = BuildComplexProperties(tableMetadata, rows[index], $"{path}[{index}]");
                table.SetValue(CreateRuntimeObject(rowType, rowProperties), index);
            }

            return new RuntimeProperty(name, rowType.MakeArrayType(), table);
        }

        var targetType = GetScalarType(rfcType);
        return new RuntimeProperty(name, targetType, ConvertScalar(rawValue, targetType, path));
    }

    private static object BuildComplexObject(
        ISapTypeMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        string path)
    {
        var properties = BuildComplexProperties(metadata, values, path);
        return CreateRuntimeObject(properties);
    }

    private static IReadOnlyList<RuntimeProperty> BuildComplexProperties(
        ISapTypeMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        string path)
    {
        var properties = new List<RuntimeProperty>();
        foreach (var pair in values)
        {
            var field = FindField(metadata, pair.Key)
                ?? throw new ArgumentException($"RFC structure '{path}' has no field named '{pair.Key}'.");

            properties.Add(BuildProperty(
                field.Name,
                field.Type,
                field.GetTypeMetadata,
                pair.Value,
                $"{path}.{field.Name}"));
        }

        return properties;
    }

    private static Type CreateRuntimeType(IReadOnlyList<RuntimeProperty> properties)
    {
        var schemaKey = string.Join(
            "|",
            properties.Select(property =>
                $"{property.Name}:{property.Type.AssemblyQualifiedName}"));
        if (RuntimeTypes.TryGetValue(schemaKey, out var existingType))
            return existingType;

        lock (RuntimeTypeLock)
        {
            if (RuntimeTypes.TryGetValue(schemaKey, out existingType))
                return existingType;

            var typeBuilder = RuntimeModule.DefineType(
                $"RfcInput_{Interlocked.Increment(ref _nextTypeId)}",
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);

            typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

            foreach (var property in properties)
            {
                var fieldBuilder = typeBuilder.DefineField(
                    $"_{property.Name}_{Interlocked.Increment(ref _nextTypeId)}",
                    property.Type,
                    FieldAttributes.Private);

                var propertyBuilder = typeBuilder.DefineProperty(
                    property.Name,
                    PropertyAttributes.None,
                    property.Type,
                    Type.EmptyTypes);

                var getter = typeBuilder.DefineMethod(
                    $"get_{property.Name}",
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    property.Type,
                    Type.EmptyTypes);
                var getterIl = getter.GetILGenerator();
                getterIl.Emit(OpCodes.Ldarg_0);
                getterIl.Emit(OpCodes.Ldfld, fieldBuilder);
                getterIl.Emit(OpCodes.Ret);

                var setter = typeBuilder.DefineMethod(
                    $"set_{property.Name}",
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    null,
                    [property.Type]);
                var setterIl = setter.GetILGenerator();
                setterIl.Emit(OpCodes.Ldarg_0);
                setterIl.Emit(OpCodes.Ldarg_1);
                setterIl.Emit(OpCodes.Stfld, fieldBuilder);
                setterIl.Emit(OpCodes.Ret);

                propertyBuilder.SetGetMethod(getter);
                propertyBuilder.SetSetMethod(setter);
            }

            var createdType = typeBuilder.CreateTypeInfo()!.AsType();
            RuntimeTypes[schemaKey] = createdType;
            return createdType;
        }
    }

    private static object CreateRuntimeObject(IReadOnlyList<RuntimeProperty> properties)
    {
        var type = CreateRuntimeType(properties);
        return CreateRuntimeObject(type, properties);
    }

    private static object CreateRuntimeObject(Type type, IReadOnlyList<RuntimeProperty> properties)
    {
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Could not create runtime RFC input type '{type.Name}'.");

        foreach (var property in properties)
        {
            type.GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(instance, property.Value);
        }

        return instance;
    }

    private static Type GetScalarType(SapRfcType type) => type switch
    {
        SapRfcType.RFCTYPE_INT or SapRfcType.RFCTYPE_INT1 or SapRfcType.RFCTYPE_INT2 => typeof(int),
        SapRfcType.RFCTYPE_INT8 => typeof(long),
        SapRfcType.RFCTYPE_FLOAT => typeof(double),
        SapRfcType.RFCTYPE_DECF16 or SapRfcType.RFCTYPE_DECF34 => typeof(decimal),
        SapRfcType.RFCTYPE_DATE => typeof(DateTime),
        SapRfcType.RFCTYPE_TIME => typeof(TimeSpan),
        SapRfcType.RFCTYPE_BYTE or SapRfcType.RFCTYPE_XSTRING => typeof(byte[]),
        _ => typeof(string),
    };

    private static object ConvertScalar(object value, Type targetType, string path)
    {
        try
        {
            if (targetType == typeof(string))
                return value is bool boolean
                    ? boolean ? "X" : string.Empty
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            if (targetType == typeof(byte[]))
            {
                if (value is byte[] bytes)
                    return bytes;
                return Convert.FromBase64String(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            if (targetType == typeof(DateTime))
            {
                if (value is DateTime dateTime)
                    return dateTime;

                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (DateTime.TryParseExact(text, ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out dateTime))
                    return dateTime;
                throw new FormatException("Expected yyyyMMdd or yyyy-MM-dd.");
            }

            if (targetType == typeof(TimeSpan))
            {
                if (value is TimeSpan timeSpan)
                    return timeSpan;

                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out timeSpan))
                    return timeSpan;
                throw new FormatException("Expected HH:mm:ss.");
            }

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"RFC input '{path}' could not be converted to {targetType.Name}: {ex.Message}", ex);
        }
    }

    private static object? ConvertOutputValue(
        object? value,
        int maxTableRows,
        string parameterName,
        ICollection<string> truncated)
    {
        if (value is null)
            return null;

        if (value is byte[] bytes)
            return Convert.ToBase64String(bytes);

        if (value is DateTime dateTime)
            return dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (value is TimeSpan timeSpan)
            return timeSpan.ToString("c", CultureInfo.InvariantCulture);

        if (value is string or decimal || value.GetType().IsPrimitive || value.GetType().IsEnum)
            return value;

        if (value is IReadOnlyDictionary<string, object> dictionary)
        {
            return dictionary.ToDictionary(
                pair => pair.Key,
                pair => ConvertOutputValue(pair.Value, maxTableRows, parameterName, truncated),
                StringComparer.OrdinalIgnoreCase);
        }

        if (value is IEnumerable enumerable and not string)
        {
            var rows = new List<object?>();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count >= maxTableRows)
                {
                    if (!truncated.Contains(parameterName))
                        truncated.Add(parameterName);
                    break;
                }

                rows.Add(ConvertOutputValue(item, maxTableRows, parameterName, truncated));
                count++;
            }

            return rows;
        }

        var properties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToList();
        if (properties.Count > 0)
        {
            return properties.ToDictionary(
                property => property.Name,
                property => ConvertOutputValue(
                    property.GetValue(value),
                    maxTableRows,
                    parameterName,
                    truncated),
                StringComparer.OrdinalIgnoreCase);
        }

        return value;
    }

    private static ISapParameterMetadata? FindParameter(ISapFunctionMetadata metadata, string name) =>
        metadata.Parameters.FirstOrDefault(
            parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ISapFieldMetadata? FindField(ISapTypeMetadata metadata, string name) =>
        metadata.Fields.FirstOrDefault(
            field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, object?> AsDictionary(object value, string path)
    {
        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
            return readOnlyDictionary;

        if (value is IDictionary<string, object?> dictionary)
            return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);

        throw new ArgumentException($"RFC input '{path}' must be a JSON object.");
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> AsRows(object value, string path)
    {
        if (value is not IEnumerable enumerable || value is string)
            throw new ArgumentException($"RFC table input '{path}' must be a JSON array of objects.");

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var item in enumerable)
        {
            if (item is null)
                throw new ArgumentException($"RFC table input '{path}' cannot contain null rows.");
            rows.Add(AsDictionary(item, path));
        }

        return rows;
    }

    private static IReadOnlyList<RfcFieldDefinition>? GetFields(
        SapRfcType type,
        Func<ISapTypeMetadata> getTypeMetadata,
        int depth,
        ISet<string> visited)
    {
        if (type is not (SapRfcType.RFCTYPE_STRUCTURE or SapRfcType.RFCTYPE_TABLE) || depth >= 3)
            return null;

        var typeMetadata = getTypeMetadata();
        var typeName = typeMetadata.GetName();
        if (!visited.Add(typeName))
            return null;

        var fields = typeMetadata.Fields
            .Select(field => new RfcFieldDefinition(
                Name: field.Name,
                Type: TrimPrefix(field.Type.ToString(), "RFCTYPE_"),
                TypeName: GetTypeName(field.Type, field.GetTypeMetadata),
                Length: field.NucLength,
                Decimals: field.Decimals,
                Fields: GetFields(field.Type, field.GetTypeMetadata, depth + 1, visited)))
            .ToList();

        visited.Remove(typeName);
        return fields;
    }

    private static string? GetTypeName(SapRfcType type, Func<ISapTypeMetadata> getTypeMetadata) =>
        type is SapRfcType.RFCTYPE_STRUCTURE or SapRfcType.RFCTYPE_TABLE
            ? NullIfBlank(getTypeMetadata().GetName())
            : null;

    private static string TrimPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record RuntimeProperty(string Name, Type Type, object? Value);
}
