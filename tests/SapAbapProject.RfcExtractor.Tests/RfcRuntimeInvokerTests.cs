using SapAbapProject.Core.Models;
using SapAbapProject.RfcExtractor;
using SapNwRfc;

namespace SapAbapProject.RfcExtractor.Tests;

public sealed class RfcRuntimeInvokerTests
{
    [Fact]
    public void Invoke_MapsNestedInputsAndBoundsTableOutputs()
    {
        var headerType = new TestTypeMetadata("ZHEADER",
        [
            new TestFieldMetadata("DOC_ID", SapRfcType.RFCTYPE_CHAR),
            new TestFieldMetadata("ACTIVE", SapRfcType.RFCTYPE_CHAR),
        ]);
        var itemDetailsType = new TestTypeMetadata("ZITEM_DETAILS",
        [
            new TestFieldMetadata("CODE", SapRfcType.RFCTYPE_CHAR),
        ]);
        var itemType = new TestTypeMetadata("ZITEM",
        [
            new TestFieldMetadata("ITEM", SapRfcType.RFCTYPE_INT),
            new TestFieldMetadata("TEXT", SapRfcType.RFCTYPE_STRING),
            new TestFieldMetadata("DETAILS", SapRfcType.RFCTYPE_STRUCTURE, itemDetailsType),
        ]);
        var messageType = new TestTypeMetadata("BAPIRET2",
        [
            new TestFieldMetadata("TYPE", SapRfcType.RFCTYPE_CHAR),
            new TestFieldMetadata("MESSAGE", SapRfcType.RFCTYPE_STRING),
        ]);

        var metadata = new TestFunctionMetadata("Z_TEST_RFC",
        [
            new TestParameterMetadata("IV_COUNT", SapRfcType.RFCTYPE_INT, SapRfcDirection.RFC_IMPORT),
            new TestParameterMetadata("IS_HEADER", SapRfcType.RFCTYPE_STRUCTURE, SapRfcDirection.RFC_IMPORT, headerType),
            new TestParameterMetadata("IT_ITEMS", SapRfcType.RFCTYPE_TABLE, SapRfcDirection.RFC_TABLES, itemType),
            new TestParameterMetadata("EV_RESULT", SapRfcType.RFCTYPE_CHAR, SapRfcDirection.RFC_EXPORT),
            new TestParameterMetadata("ET_MESSAGES", SapRfcType.RFCTYPE_TABLE, SapRfcDirection.RFC_TABLES, messageType),
        ]);

        var function = new TestFunction(metadata, (outputType, output) =>
        {
            Assert.NotEqual(typeof(object), outputType);
            SetProperty(output, "EV_RESULT", "OK");
            SetTableProperty(
                output,
                "ET_MESSAGES",
                [
                    new Dictionary<string, object?> { ["TYPE"] = "S", ["MESSAGE"] = "First" },
                    new Dictionary<string, object?> { ["TYPE"] = "W", ["MESSAGE"] = "Second" },
                ]);
        });

        var result = RfcRuntimeInvoker.Invoke(
            function,
            metadata.GetName(),
            new Dictionary<string, object?>
            {
                ["IV_COUNT"] = 2L,
                ["IS_HEADER"] = new Dictionary<string, object?>
                {
                    ["DOC_ID"] = "4711",
                    ["ACTIVE"] = true,
                },
                ["IT_ITEMS"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["ITEM"] = 10L,
                        ["TEXT"] = "One",
                        ["DETAILS"] = new Dictionary<string, object?> { ["CODE"] = "A" },
                    },
                    new Dictionary<string, object?>
                    {
                        ["ITEM"] = 20L,
                        ["TEXT"] = "Two",
                        ["DETAILS"] = new Dictionary<string, object?> { ["CODE"] = "B" },
                    },
                },
            },
            maxTableRows: 1);

        Assert.NotNull(function.Input);
        Assert.Equal(2, GetProperty<int>(function.Input!, "IV_COUNT"));

        var header = GetProperty<object>(function.Input!, "IS_HEADER");
        Assert.Equal("4711", GetProperty<string>(header, "DOC_ID"));
        Assert.Equal("X", GetProperty<string>(header, "ACTIVE"));

        var items = GetProperty<Array>(function.Input!, "IT_ITEMS");
        Assert.Equal(2, items.Length);
        Assert.Equal(10, GetProperty<int>(items.GetValue(0)!, "ITEM"));
        Assert.Equal("Two", GetProperty<string>(items.GetValue(1)!, "TEXT"));
        var secondDetails = GetProperty<object>(items.GetValue(1)!, "DETAILS");
        Assert.Equal("B", GetProperty<string>(secondDetails, "CODE"));

        Assert.Equal("OK", result.Output["EV_RESULT"]);
        var messages = Assert.IsAssignableFrom<IReadOnlyList<object?>>(result.Output["ET_MESSAGES"]);
        Assert.Single(messages);
        Assert.Contains("ET_MESSAGES", result.TruncatedTableParameters);
    }

    [Fact]
    public void Invoke_MapsNestedAndBinaryOutputValues()
    {
        var resultType = new TestTypeMetadata("ZRESULT",
        [
            new TestFieldMetadata("TEXT", SapRfcType.RFCTYPE_STRING),
            new TestFieldMetadata("CREATED_ON", SapRfcType.RFCTYPE_DATE),
        ]);
        var metadata = new TestFunctionMetadata("Z_OUTPUT_RFC",
        [
            new TestParameterMetadata("EV_DATE", SapRfcType.RFCTYPE_DATE, SapRfcDirection.RFC_EXPORT),
            new TestParameterMetadata("EV_TIME", SapRfcType.RFCTYPE_TIME, SapRfcDirection.RFC_EXPORT),
            new TestParameterMetadata("EV_RAW", SapRfcType.RFCTYPE_XSTRING, SapRfcDirection.RFC_EXPORT),
            new TestParameterMetadata("ES_RESULT", SapRfcType.RFCTYPE_STRUCTURE, SapRfcDirection.RFC_EXPORT, resultType),
        ]);
        var function = new TestFunction(metadata, (_, output) =>
        {
            SetProperty(output, "EV_DATE", new DateTime(2026, 8, 12));
            SetProperty(output, "EV_TIME", new TimeSpan(10, 30, 15));
            SetProperty(output, "EV_RAW", new byte[] { 1, 2, 3 });

            var result = CreatePropertyValue(output, "ES_RESULT");
            SetProperty(result, "TEXT", "Success");
            SetProperty(result, "CREATED_ON", new DateTime(2026, 8, 11));
            SetProperty(output, "ES_RESULT", result);
        });

        var execution = RfcRuntimeInvoker.Invoke(
            function,
            metadata.GetName(),
            new Dictionary<string, object?>(),
            maxTableRows: 10);

        Assert.Null(function.Input);
        Assert.Equal("2026-08-12", execution.Output["EV_DATE"]);
        Assert.Equal("10:30:15", execution.Output["EV_TIME"]);
        Assert.Equal("AQID", execution.Output["EV_RAW"]);

        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(execution.Output["ES_RESULT"]);
        Assert.Equal("Success", result["TEXT"]);
        Assert.Equal("2026-08-11", result["CREATED_ON"]);
    }

    [Fact]
    public void Invoke_RejectsInvalidInputBeforeCallingSap()
    {
        var rowType = new TestTypeMetadata("ZROW",
        [
            new TestFieldMetadata("ID", SapRfcType.RFCTYPE_INT),
            new TestFieldMetadata("TEXT", SapRfcType.RFCTYPE_STRING),
        ]);
        var metadata = new TestFunctionMetadata("Z_VALIDATE_RFC",
        [
            new TestParameterMetadata("IV_ID", SapRfcType.RFCTYPE_INT, SapRfcDirection.RFC_IMPORT),
            new TestParameterMetadata("IT_ROWS", SapRfcType.RFCTYPE_TABLE, SapRfcDirection.RFC_TABLES, rowType),
            new TestParameterMetadata("EV_RESULT", SapRfcType.RFCTYPE_STRING, SapRfcDirection.RFC_EXPORT),
        ]);
        var function = new TestFunction(metadata, (_, _) => { });

        Assert.Contains(
            "no parameter named 'UNKNOWN'",
            Assert.Throws<ArgumentException>(() => InvokeWith("UNKNOWN", "value")).Message);
        Assert.Contains(
            "export-only",
            Assert.Throws<ArgumentException>(() => InvokeWith("EV_RESULT", "value")).Message);
        Assert.Contains(
            "cannot be null",
            Assert.Throws<ArgumentException>(() => InvokeWith("IV_ID", null)).Message);

        var inconsistentRows = new List<object?>
        {
            new Dictionary<string, object?> { ["ID"] = 1, ["TEXT"] = "One" },
            new Dictionary<string, object?> { ["ID"] = 2 },
        };
        Assert.Contains(
            "must contain the same fields",
            Assert.Throws<ArgumentException>(() => InvokeWith("IT_ROWS", inconsistentRows)).Message);

        Assert.False(function.WasInvoked);

        RfcExecutionResult InvokeWith(string name, object? value) =>
            RfcRuntimeInvoker.Invoke(
                function,
                metadata.GetName(),
                new Dictionary<string, object?> { [name] = value },
                maxTableRows: 10);
    }

    private static T GetProperty<T>(object instance, string name) =>
        (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;

    private static void SetProperty(object instance, string name, object? value) =>
        instance.GetType().GetProperty(name)!.SetValue(instance, value);

    private static object CreatePropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName)!;
        return Activator.CreateInstance(property.PropertyType)!;
    }

    private static void SetTableProperty(
        object instance,
        string propertyName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var property = instance.GetType().GetProperty(propertyName)!;
        var rowType = property.PropertyType.GetElementType()!;
        var table = Array.CreateInstance(rowType, rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = Activator.CreateInstance(rowType)!;
            foreach (var pair in rows[index])
                SetProperty(row, pair.Key, pair.Value);
            table.SetValue(row, index);
        }

        property.SetValue(instance, table);
    }

    private sealed class TestFunction : ISapFunction
    {
        private readonly Action<Type, object> _configureOutput;

        public TestFunction(ISapFunctionMetadata metadata, Action<Type, object> configureOutput)
        {
            Metadata = metadata;
            _configureOutput = configureOutput;
        }

        public object? Input { get; private set; }
        public bool WasInvoked { get; private set; }
        public ISapFunctionMetadata Metadata { get; }
        public bool HasParameter(string parameterName) => Metadata.Parameters.TryGetValue(parameterName, out _);
        public void Invoke() => WasInvoked = true;

        public void Invoke(object input)
        {
            Input = input;
            WasInvoked = true;
        }

        public TOutput Invoke<TOutput>() => CreateOutput<TOutput>();

        public TOutput Invoke<TOutput>(object input)
        {
            Input = input;
            return CreateOutput<TOutput>();
        }

        private TOutput CreateOutput<TOutput>()
        {
            WasInvoked = true;
            var output = Activator.CreateInstance<TOutput>()!;
            _configureOutput(typeof(TOutput), output!);
            return output;
        }

        public void Dispose() { }
    }

    private sealed class TestFunctionMetadata : ISapFunctionMetadata
    {
        private readonly string _name;

        public TestFunctionMetadata(string name, IReadOnlyList<ISapParameterMetadata> parameters)
        {
            _name = name;
            Parameters = new TestMetadataCollection<ISapParameterMetadata>(
                parameters,
                parameter => parameter.Name);
            Exceptions = new TestMetadataCollection<ISapExceptionMetadata>(
                Array.Empty<ISapExceptionMetadata>(),
                exception => exception.Key);
        }

        public ISapMetadataCollection<ISapParameterMetadata> Parameters { get; }
        public ISapMetadataCollection<ISapExceptionMetadata> Exceptions { get; }
        public string GetName() => _name;
    }

    private sealed class TestParameterMetadata : ISapParameterMetadata
    {
        private readonly ISapTypeMetadata? _typeMetadata;

        public TestParameterMetadata(
            string name,
            SapRfcType type,
            SapRfcDirection direction,
            ISapTypeMetadata? typeMetadata = null)
        {
            Name = name;
            Type = type;
            Direction = direction;
            _typeMetadata = typeMetadata;
        }

        public string Name { get; }
        public SapRfcType Type { get; }
        public SapRfcDirection Direction { get; }
        public uint NucLength => 0;
        public uint UcLength => 0;
        public uint Decimals => 0;
        public string DefaultValue => string.Empty;
        public bool IsOptional => false;
        public string Description => string.Empty;
        public ISapTypeMetadata GetTypeMetadata() => _typeMetadata!;
    }

    private sealed class TestTypeMetadata : ISapTypeMetadata
    {
        private readonly string _name;

        public TestTypeMetadata(string name, IReadOnlyList<ISapFieldMetadata> fields)
        {
            _name = name;
            Fields = new TestMetadataCollection<ISapFieldMetadata>(fields, field => field.Name);
        }

        public ISapMetadataCollection<ISapFieldMetadata> Fields { get; }
        public string GetName() => _name;
    }

    private sealed class TestFieldMetadata : ISapFieldMetadata
    {
        private readonly ISapTypeMetadata? _typeMetadata;

        public TestFieldMetadata(string name, SapRfcType type, ISapTypeMetadata? typeMetadata = null)
        {
            Name = name;
            Type = type;
            _typeMetadata = typeMetadata;
        }

        public string Name { get; }
        public SapRfcType Type { get; }
        public uint NucLength => 0;
        public uint NucOffset => 0;
        public uint UcLength => 0;
        public uint UcOffset => 0;
        public uint Decimals => 0;
        public ISapTypeMetadata GetTypeMetadata() => _typeMetadata!;
    }

    private sealed class TestMetadataCollection<T> : ISapMetadataCollection<T>
    {
        private readonly IReadOnlyList<T> _items;
        private readonly Func<T, string> _getName;

        public TestMetadataCollection(IReadOnlyList<T> items, Func<T, string> getName)
        {
            _items = items;
            _getName = getName;
        }

        public int Count => _items.Count;
        public T this[int index] => _items[index];

        public bool TryGetValue(string name, out T value)
        {
            foreach (var item in _items)
            {
                if (string.Equals(_getName(item), name, StringComparison.OrdinalIgnoreCase))
                {
                    value = item;
                    return true;
                }
            }

            value = default!;
            return false;
        }

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
