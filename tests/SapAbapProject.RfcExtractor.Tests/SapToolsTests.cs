using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using NSubstitute;
using SapAbapProject.Core.Interfaces;
using SapAbapProject.Core.Models;
using SapAbapProject.McpServer;

namespace SapAbapProject.RfcExtractor.Tests;

public sealed class SapToolsTests
{
    [Fact]
    public void StructuredToolJson_IncludesRequiredNullableProperties()
    {
        var response = new SapObjectResponse(
            Name: "BAPI_PO_RELEASE",
            ObjectType: nameof(AbapObjectType.FunctionModule),
            Package: "ME",
            FunctionGroup: null,
            Description: null,
            Source: "",
            Metadata: new FunctionModuleMetadata(
            [
                new FunctionParameter(
                    Kind: "EXPORT",
                    Name: "RETURN",
                    TypeRef: "BAPIRETURN",
                    DefaultValue: null,
                    Optional: false),
            ]));
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        var json = JsonSerializer.SerializeToElement(response, options);

        Assert.Equal(JsonValueKind.Null, json.GetProperty("functionGroup").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("description").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            json.GetProperty("metadata")
                .GetProperty("parameters")[0]
                .GetProperty("defaultValue")
                .ValueKind);
    }

    [Fact]
    public async Task SapExecuteRfc_ConvertsJsonParametersAndReturnsExtractorResult()
    {
        const string functionName = "ZSW_APP_OC_CONDICIONES_PRECIO";
        IReadOnlyDictionary<string, object?>? capturedParameters = null;
        var expected = new RfcExecutionResult(
            functionName,
            12,
            new Dictionary<string, object?> { ["ET_CONDITIONS"] = Array.Empty<object>() },
            Array.Empty<string>());
        var extractor = Substitute.For<IAbapExtractor>();
        extractor.ExecuteRfcAsync(
                functionName,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                25,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedParameters = call.ArgAt<IReadOnlyDictionary<string, object?>>(1);
                return expected;
            });
        var tools = new SapTools(extractor, NullLogger<SapTools>.Instance);

        using var document = JsonDocument.Parse(
            """
            {
              "IV_PURCHASE_ORDER": "4500000012",
              "IS_CONTEXT": {
                "ACTIVE": true,
                "COUNT": 2
              },
              "IT_ITEMS": [
                {
                  "ITEM": 10,
                  "AMOUNT": 12.5
                }
              ]
            }
            """);
        var parameters = document.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);

        var result = await tools.SapExecuteRfc(
            functionName,
            CancellationToken.None,
            parameters,
            maxTableRows: 25);

        Assert.Same(expected, result);
        Assert.NotNull(capturedParameters);
        Assert.Equal("4500000012", capturedParameters["IV_PURCHASE_ORDER"]);

        var context = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            capturedParameters["IS_CONTEXT"]);
        Assert.Equal(true, context["ACTIVE"]);
        Assert.Equal(2L, context["COUNT"]);

        var items = Assert.IsAssignableFrom<IReadOnlyList<object?>>(capturedParameters["IT_ITEMS"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Assert.Single(items));
        Assert.Equal(10L, item["ITEM"]);
        Assert.Equal(12.5m, item["AMOUNT"]);
    }
}
