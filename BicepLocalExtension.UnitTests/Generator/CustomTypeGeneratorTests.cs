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

    [TestMethod]
    public void GenerateTypeDefinition_Configuration_UsesStringLiteralDiscriminatorsOnMembers()
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
            configurationType: typeof(Configuration),
            typeFactory,
            new CustomTypeProvider([typeof(MyResource).Assembly]),
            typeDictionary);

        var definition = generator.GenerateTypeDefinition();
        var typesJson = definition.TypeFileContents["types.json"];

        using var doc = JsonDocument.Parse(typesJson);
        var types = doc.RootElement;

        AssertDiscriminatorLiteral(
            types,
            discriminatedTypeName: nameof(Configuration),
            discriminatorPropertyName: "authenticationMode",
            (nameof(ApiKeyConfiguration), nameof(AuthenticationMode.ApiKey)),
            (nameof(AzureCliConfiguration), nameof(AuthenticationMode.AzureCli)),
            (nameof(ManagedIdentityConfiguration), nameof(AuthenticationMode.ManagedIdentity)));
    }

    private static void AssertDiscriminatorLiteral(
        JsonElement types,
        string discriminatedTypeName,
        string discriminatorPropertyName,
        params (string memberTypeName, string literal)[] members)
    {
        var discriminatedType = types.EnumerateArray()
            .First(t => t.GetProperty("$type").GetString() == "DiscriminatedObjectType" &&
                        t.GetProperty("name").GetString() == discriminatedTypeName);

        discriminatedType.GetProperty("discriminator").GetString().Should().Be(discriminatorPropertyName);

        foreach (var (memberTypeName, literal) in members)
        {
            var memberType = types.EnumerateArray()
                .First(t => t.GetProperty("$type").GetString() == "ObjectType" &&
                            t.GetProperty("name").GetString() == memberTypeName);

            var discriminatorProperty = memberType
                .GetProperty("properties")
                .GetProperty(discriminatorPropertyName);

            discriminatorProperty.GetProperty("flags").GetInt32().Should().Be(1);

            var discriminatorRef = discriminatorProperty
                .GetProperty("type")
                .GetProperty("$ref")
                .GetString() ?? throw new InvalidOperationException($"Missing discriminator reference for {memberTypeName}.");

            var discriminatorType = types[int.Parse(discriminatorRef[2..])];

            discriminatorType.GetProperty("$type").GetString().Should().Be("StringLiteralType");
            discriminatorType.GetProperty("value").GetString().Should().Be(literal);
        }
    }
}

