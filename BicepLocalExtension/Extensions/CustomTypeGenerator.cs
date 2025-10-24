using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Azure.Bicep.Types;
using Azure.Bicep.Types.Concrete;
using Azure.Bicep.Types.Index;
using Azure.Bicep.Types.Serialization;
using Bicep.Local.Extension.Types;
using Bicep.Local.Extension.Types.Attributes;
using BicepLocalExtension.Extensions;

namespace BicepLocalExtension.Generator;

public sealed class CustomTypeGenerator : ITypeDefinitionBuilder
{
    private readonly HashSet<Type> visited;
    private readonly ITypeProvider typeProvider;
    private readonly IDictionary<Type, Func<TypeBase>> typeToTypeBaseMap;

    private readonly ConcurrentDictionary<Type, TypeBase> typeCache;
    private readonly string name;
    private readonly string version;
    private readonly bool isSingleton;
    private readonly Type? configurationType;
    private readonly TypeFactory factory;


    /// <summary>
    /// Provides functionality to generate Bicep resource type definitions from .NET types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="TypeDefinitionBuilder"/> inspects resource types provided by an <see cref="ITypeProvider"/>,
    /// analyzes their public properties and associated <see cref="TypePropertyAttribute"/> metadata,
    /// and produces a <see cref="TypeDefinition"/> containing serialized type and index metadata
    /// suitable for Bicep extension consumption.
    /// </para>
    /// <para>
    /// The builder supports primitive types (string, int, bool), arrays, nullable enums, and nested complex types.
    /// If a property type cannot be mapped to a supported Bicep type, a <see cref="NotImplementedException"/> is thrown.
    /// </para>
    /// </remarks>
    public CustomTypeGenerator(
        string name,
        string version,
        bool isSingleton,
        Type? configurationType,
        TypeFactory factory,
        ITypeProvider typeProvider,
        IDictionary<Type, Func<TypeBase>> typeToTypeBaseMap)
    {
        this.name = name;
        this.version = version;
        this.isSingleton = isSingleton;
        this.configurationType = configurationType;
        this.factory = factory;
        this.typeProvider = typeProvider;

        this.typeToTypeBaseMap = typeToTypeBaseMap is null || typeToTypeBaseMap.Count == 0
            ? throw new ArgumentException(nameof(typeToTypeBaseMap))
            : typeToTypeBaseMap;

        this.visited = new HashSet<Type>();
        this.typeCache = new ConcurrentDictionary<Type, TypeBase>();
    }

    /// <summary>
    /// Generates Bicep resource type definitions based on the types provided by the <see cref="ITypeProvider"/>.
    /// This method inspects the resource types, their properties, and associated attributes to produce
    /// a <see cref="TypeDefinition"/> containing the serialized type and index metadata for use in Bicep extensions.
    /// </summary>
    /// <returns>
    /// A <see cref="TypeDefinition"/> object containing the JSON representations of the resource types and their index.
    /// </returns>
    /// <remarks>
    /// This method will throw a <see cref="NotImplementedException"/> if a property type is encountered that cannot be mapped
    /// to a supported Bicep type (e.g., unsupported primitives or collections).
    /// </remarks>
    public TypeDefinition GenerateTypeDefinition()
    {
        var typesJsonPath = "types.json";
        var resourceTypes = typeProvider.GetResourceTypes()
            .Select(x => GenerateResource(factory, typeCache, x.type, x.attribute))
            .ToDictionary(rt => rt.Name, rt => new CrossFileTypeReference(typesJsonPath, factory.GetIndex(rt)));

        CrossFileTypeReference? config = null;
        if (configurationType is not null)
        {
            var configReference = factory.AddOrGetReference(GenerateForRecord(factory, typeCache, configurationType));
            config = new CrossFileTypeReference(typesJsonPath, factory.GetIndex(configReference.Type));
        }

        var index = new TypeIndex(
            resourceTypes,
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<CrossFileTypeReference>>>(),
            new(name, version, isSingleton, configurationType: config!),
            null);

        return new(
            IndexFileContent: GetString(stream => TypeSerializer.SerializeIndex(stream, index)),
            TypeFileContents: new Dictionary<string, string>
            {
                [typesJsonPath] = GetString(stream => TypeSerializer.Serialize(stream, factory.GetTypes())),
            }.ToImmutableDictionary());
    }

    private ResourceType GenerateResource(TypeFactory typeFactory, ConcurrentDictionary<Type, TypeBase> typeCache, Type type, ResourceTypeAttribute attribute)
        => typeFactory.Create(() => new ResourceType(
            name: attribute.FullName,
            body: typeFactory.GetReference(typeFactory.Create(() => GenerateForRecord(typeFactory, typeCache, type))),
            functions: null,
            writableScopes_in: ScopeType.All,
            readableScopes_in: ScopeType.All));

    private TypeBase GenerateForRecord(TypeFactory factory, ConcurrentDictionary<Type, TypeBase> typeCache, Type type)
    {
        var typeProperties = new Dictionary<string, ObjectTypeProperty>();

        // Handle discriminated types
        if (!visited.Contains(type) && type.GetCustomAttribute<JsonPolymorphicAttribute>() is { } polymorphicAttribute
                                    && type.GetCustomAttributes<JsonDerivedTypeAttribute>() is { } derivedTypesAttribute)
        {
            visited.Add(type);
            var baseProperties = (ObjectType)GenerateForRecord(factory, typeCache, type);
            var childTypesDictionary = new Dictionary<string, ITypeReference>();
            foreach (var derivedType in derivedTypesAttribute)
            {
                string? typeDiscriminator = derivedType.TypeDiscriminator?.ToString();
                if (typeDiscriminator is null)
                {
                    throw new ArgumentNullException(nameof(derivedType.TypeDiscriminator), "The type discriminator property from JsonDerivedTypeAttribute cannot be null.");
                }
                else
                {
                    var discriminatedTypeProperties = typeCache.GetOrAdd(derivedType.DerivedType, _ => (ObjectType)GenerateForRecord(factory, typeCache, derivedType.DerivedType));
                    var concreteDiscriminatedTypeProperties = (ObjectType)discriminatedTypeProperties;
                    var discriminatorTypeReference = factory.AddOrGetReference(new StringLiteralType(typeDiscriminator));
                    var newProperties =
                            new Dictionary<string, ObjectTypeProperty>()
                            {
                                {
                                    polymorphicAttribute.TypeDiscriminatorPropertyName!,
                                    new ObjectTypeProperty(
                                        discriminatorTypeReference, ObjectTypePropertyFlags.Required, "The discriminator for derived types.")
                                }
                            }
                        ;
                    foreach (var kvp in concreteDiscriminatedTypeProperties.Properties)
                    {
                        newProperties.TryAdd(kvp.Key, kvp.Value);
                    }

                    var newObjectType = new ObjectType(concreteDiscriminatedTypeProperties.Name,
                        newProperties
                            .ToImmutableDictionary(),
                        concreteDiscriminatedTypeProperties.AdditionalProperties);

                    childTypesDictionary.Add(derivedType.DerivedType.Name, factory.AddOrGetReference(newObjectType));
                }
            }

            var typeReference = typeCache.GetOrAdd(type, _ => factory.AddOrGetReference(new DiscriminatedObjectType(
                type.Name,
                polymorphicAttribute.TypeDiscriminatorPropertyName!, baseProperties.Properties
                , childTypesDictionary)).Type);

            // We return here since we already explored the base and derived types
            return typeReference;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (visited.Contains(property.PropertyType))
            {
                continue;
            }

            var annotation = property.GetCustomAttributes<ExtendedTypePropertyAttribute>(true).FirstOrDefault()
                             ?? new ExtendedTypePropertyAttribute(null);
            var isNullable = annotation?.IsNullable ?? false;
            var minimumLengthAttribute = property.GetCustomAttribute<MinLengthAttribute>(false);
            var maximumLengthAttribute = property.GetCustomAttribute<MaxLengthAttribute>(false);
            var patternAttribute = property.GetCustomAttribute<BicepStringPatternAttribute>(false);
            annotation?.MergeStringPropertyAttribute(maximumLengthAttribute, minimumLengthAttribute, patternAttribute);

            var propertyType = property.PropertyType;
            //We will generate nullable generics as non-nullable and convert it into a union type with NullType
            if (propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)
                && propertyType.GetGenericArguments()[0].IsEnum is false)
            {
                propertyType = propertyType.GetGenericArguments()[0];
                //We ignore strings since we cannot differentiate from string and string?
                //Nullability must be set through the necessary attribute for strings
                if (propertyType != typeof(string))
                {
                    isNullable = true;
                }
            }

            TypeBase? typeReference = null;

            if (!TryResolveTypeReference(propertyType, annotation, out typeReference))
            {
                if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    visited.Add(propertyType);
                    var genericArguments = propertyType.GetGenericArguments();
                    if (genericArguments.Length != 2)
                    {
                        throw new ArgumentException("Dictionary must have exactly two generic arguments");
                    }

                    if (genericArguments[0] != typeof(string))
                    {
                        throw new ArgumentException("Dictionary must have a string as key");
                    }

                    var valueType = genericArguments[1];
                    ITypeReference additionalPropertiesReference;
                    if (!valueType.IsPrimitive && valueType != typeof(string))
                    {
                        additionalPropertiesReference =
                            factory.AddOrGetReference(typeCache.GetOrAdd(valueType, _ => GenerateForRecord(factory, typeCache, valueType)));
                    }
                    else
                    {
                        if (valueType == typeof(bool))
                        {
                            additionalPropertiesReference = factory.AddOrGetReference(
                                new BooleanType()
                            );
                        }
                        else if (valueType == typeof(int))
                        {
                            additionalPropertiesReference = factory.AddOrGetReference(
                                new IntegerType());
                        }
                        else
                        {
                            additionalPropertiesReference = factory.AddOrGetReference(
                                new StringType(
                                    annotation?.IsSecure,
                                    annotation?.MinLength,
                                    annotation?.MaxLength,
                                    annotation?.Pattern
                                ));
                        }
                    }

                    var typeName = $"Dictionary<string, {valueType.Name}>";
                    typeReference = factory.AddOrGetReference(typeCache.GetOrAdd(type, _ => new ObjectType(typeName,
                        new Dictionary<string, ObjectTypeProperty>(),
                        additionalPropertiesReference))).Type;
                }

                else if (propertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propertyType))
                {
                    // protect against infinite recursion
                    visited.Add(property.PropertyType);

                    Type? elementType = null;
                    if (propertyType.IsArray)
                    {
                        elementType = propertyType.GetElementType();
                    }
                    else if (propertyType.IsGenericType &&
                             propertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        elementType = propertyType.GetGenericArguments()[0];
                    }

                    if (elementType is null)
                    {
                        throw new NotImplementedException($"Unsupported collection type {elementType}");
                    }

                    if (!TryResolveTypeReference(elementType, annotation, out var elementTypeReference))
                    {
                        elementTypeReference = typeCache.GetOrAdd(elementType, _ => factory.Create(() => GenerateForRecord(factory, typeCache, elementType)));
                    }

                    typeReference = typeCache.GetOrAdd(propertyType, _ => factory.Create(() => new ArrayType(factory.GetReference(elementTypeReference))));
                }
                else if (propertyType.IsClass)
                {
                    visited.Add(property.PropertyType);

                    typeReference = typeCache.GetOrAdd(propertyType, _ => factory.Create(() => GenerateForRecord(factory, typeCache, propertyType)));
                }
                else if (propertyType.IsGenericType &&
                         propertyType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                         propertyType.GetGenericArguments()[0] is { IsEnum: true } enumType)
                {
                    var enumMembers = enumType.GetEnumNames()
                        .Select(x => factory.Create(() => new StringLiteralType(x)))
                        .Select(x => factory.GetReference(x))
                        .ToImmutableArray();

                    typeReference = typeCache.GetOrAdd(propertyType, _ => factory.Create(() => new UnionType(enumMembers)));
                }
                else if (propertyType is { IsEnum: true } enumTypeNonNullable)
                {
                    var enumMembers = enumTypeNonNullable.GetEnumNames()
                        .Select(x => factory.Create(() => new StringLiteralType(x)))
                        .Select(x => factory.GetReference(x))
                        .ToImmutableArray();

                    typeReference = typeCache.GetOrAdd(propertyType, _ => factory.Create(() => new UnionType(enumMembers)));
                }

                else
                {
                    throw new NotImplementedException($"Unsupported property type {propertyType}");
                }
            }

            if (isNullable)
            {
                var unionType = factory.AddOrGetReference(new UnionType([
                    factory.GetReference(typeReference),
                    factory.AddOrGetReference(new NullType())
                ]));
                typeReference = unionType.Type;
            }

            typeProperties[CamelCase(property.Name)] = new ObjectTypeProperty(
                factory.GetReference(typeReference),
                annotation?.Flags ?? ObjectTypePropertyFlags.None,
                annotation?.Description);
        }

        return new ObjectType(
            $"{type.Name}",
            typeProperties,
            null);
    }

    private bool TryResolveTypeReference(Type type, ExtendedTypePropertyAttribute? annotation, [NotNullWhen(true)] out TypeBase? typeReference)
    {
        typeReference = null;
        if (type == typeof(string))
        {
            //TODO: Find a way to make strings compatible with the type cache
            typeReference = factory.AddOrGetReference(new StringType(
                sensitive: annotation?.IsSecure,
                minLength: annotation?.MinLength,
                maxLength: annotation?.MaxLength,
                pattern: annotation?.Pattern)).Type;
        }
        else if (typeToTypeBaseMap.TryGetValue(type, out var typeFunc))
        {
            typeReference = typeCache.GetOrAdd(type, _ => factory.Create(typeFunc));
        }

        return typeReference is not null;
    }


    private string GetString(Action<Stream> streamWriteFunc)
    {
        using var memoryStream = new MemoryStream();
        streamWriteFunc(memoryStream);

        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    private static string CamelCase(string input)
        => $"{input[..1].ToLowerInvariant()}{input[1..]}";
}