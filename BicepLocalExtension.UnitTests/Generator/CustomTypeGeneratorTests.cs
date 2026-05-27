using System.Collections.Immutable;
using System.Text.Json;
using Azure.Bicep.Types.Concrete;
using FluentAssertions;
using BicepLocalExtension.Generator;
using BicepLocalExtension.Models;

namespace BicepLocalExtension.UnitTests.Generator;

[TestClass]
public class CustomTypeGeneratorTests
{
    [TestMethod]
    public void GenerateTypeDefinition_MyResource_ContainsExpectedProperties()
    {
        var typeDictionary = new Dictionary<Type, Func<TypeBase>>
        {
            { typeof(string), () => new StringType() },
            { typeof(bool), () => new BooleanType() },
            { typeof(int), () => new IntegerType() }
        }.ToImmutableDictionary();

        var typeFactory = new TypeFactory([]);
        foreach (var primitiveType in typeDictionary)
        {
            typeFactory.Create(primitiveType.Value);
        }

        var generator = new CustomTypeGenerator(
            name: "MyExtension",
            version: "0.0.1",
            isSingleton: true,
            configurationType: null,
            typeFactory,
            new CustomTypeProvider([typeof(MyResource).Assembly]),
            typeDictionary);

        var definition = generator.GenerateTypeDefinition();
        var typesJson = definition.TypeFileContents["types.json"];

        using var doc = JsonDocument.Parse(typesJson);
        var types = doc.RootElement;

        var myResourceType = types.EnumerateArray()
            .First(t => t.GetProperty("$type").GetString() == "ObjectType" &&
                        t.GetProperty("name").GetString() == nameof(MyResource));

        var properties = myResourceType.GetProperty("properties");
        properties.TryGetProperty("name", out _).Should().BeTrue();
        properties.TryGetProperty("operation", out _).Should().BeTrue();
        properties.TryGetProperty("output", out _).Should().BeTrue();
        properties.TryGetProperty("tags", out _).Should().BeTrue();
    }

    [TestMethod]
    public void GenerateTypeDefinition_MyResource_OperationIsUnionType()
    {
        var typeDictionary = new Dictionary<Type, Func<TypeBase>>
        {
            { typeof(string), () => new StringType() },
            { typeof(bool), () => new BooleanType() },
            { typeof(int), () => new IntegerType() }
        }.ToImmutableDictionary();

        var typeFactory = new TypeFactory([]);
        foreach (var primitiveType in typeDictionary)
        {
            typeFactory.Create(primitiveType.Value);
        }

        var generator = new CustomTypeGenerator(
            name: "MyExtension",
            version: "0.0.1",
            isSingleton: true,
            configurationType: null,
            typeFactory,
            new CustomTypeProvider([typeof(MyResource).Assembly]),
            typeDictionary);

        var definition = generator.GenerateTypeDefinition();
        var typesJson = definition.TypeFileContents["types.json"];

        using var doc = JsonDocument.Parse(typesJson);
        var types = doc.RootElement;

        var myResourceType = types.EnumerateArray()
            .First(t => t.GetProperty("$type").GetString() == "ObjectType" &&
                        t.GetProperty("name").GetString() == nameof(MyResource));

        // Operation is a nullable enum → should resolve to a union type reference
        var operationTypeRef = myResourceType
            .GetProperty("properties")
            .GetProperty("operation")
            .GetProperty("type")
            .GetProperty("$ref")
            .GetString();

        operationTypeRef.Should().NotBeNull();
        operationTypeRef.Should().StartWith("#/");
    }
}

