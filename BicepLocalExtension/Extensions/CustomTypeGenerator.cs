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


public class CustomTypeProvider : ITypeProvider
{
    private readonly Assembly[] assemblies;

    public CustomTypeProvider(Assembly[]? assemblies = null)
    {
        this.assemblies = assemblies ?? GetAssembliesInReferenceScope();
    }

    private static Assembly[] GetAssembliesInReferenceScope()
    {
        var executingAssembly = Assembly.GetExecutingAssembly();
        return executingAssembly
            .GetReferencedAssemblies()
            .Select(Assembly.Load)
            .Append(executingAssembly)
            .ToArray();
    }

    /// <summary>
    /// Provides resource type discovery for Bicep extensions by scanning loaded assemblies for types
    /// annotated with <see cref="ResourceTypeAttribute"/>.
    /// </summary>
    public IEnumerable<(Type type, ResourceTypeAttribute attribute)> GetResourceTypes(bool throwOnDuplicate)
    {
        var result = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(x => x.IsVisible)
            .Select(x => (type: x, attribute: x.GetCustomAttribute<ResourceTypeAttribute>(true)!))
            .Where(x => x.attribute is not null)
            .ToImmutableArray();

        foreach (var group in result.GroupBy(x => x.attribute.FullName))
        {
            yield return group.First();
        }
    }
}


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

    private ResourceType GenerateResource(TypeFactory typeFactory, ConcurrentDictionary<Type, TypeBase> typeCache,
        Type type, ResourceTypeAttribute attribute)
        => (ResourceType) typeFactory.AddOrGetReference(new ResourceType(
            name: attribute.FullName,
            body: typeFactory.GetReference(typeFactory.AddOrGetReference(GenerateForRecord(typeFactory, typeCache, type)).Type),
            functions: null,
            writableScopes_in: ScopeType.All,
            readableScopes_in: ScopeType.All)).Type;

    private TypeBase GenerateForRecord(TypeFactory factory, ConcurrentDictionary<Type, TypeBase> typeCache, Type type)
    {
        var typeProperties = new Dictionary<string, ObjectTypeProperty>();

        // Handle discriminated types
        if (!visited.Contains(type) && type.GetCustomAttribute<JsonPolymorphicAttribute>() is { } polymorphicAttribute
                                    && type.GetCustomAttributes<JsonDerivedTypeAttribute>() is
                                        { } derivedTypesAttribute)
        {
            visited.Add(type);
            var baseProperties = (ObjectType)GenerateForRecord(factory, typeCache, type);
            var childTypesDictionary = new Dictionary<string, ITypeReference>();
            foreach (var derivedType in derivedTypesAttribute)
            {
                string? typeDiscriminator = derivedType.TypeDiscriminator?.ToString();
                if (typeDiscriminator is null)
                {
                    throw new ArgumentNullException(nameof(derivedType.TypeDiscriminator),
                        "The type discriminator property from JsonDerivedTypeAttribute cannot be null.");
                }
                else
                {
                    var discriminatedTypeProperties = typeCache.GetOrAdd(derivedType.DerivedType,
                        _ => (ObjectType)GenerateForRecord(factory, typeCache, derivedType.DerivedType));
                    var concreteDiscriminatedTypeProperties = (ObjectType)discriminatedTypeProperties;
                    var discriminatorTypeReference =
                        factory.AddOrGetReference(new StringLiteralType(typeDiscriminator));
                    var newProperties =
                            new Dictionary<string, ObjectTypeProperty>()
                            {
                                {
                                    polymorphicAttribute.TypeDiscriminatorPropertyName!,
                                    new ObjectTypeProperty(
                                        discriminatorTypeReference, ObjectTypePropertyFlags.Required,
                                        "The discriminator for derived types.")
                                }
                            }
                        ;
                    foreach (var kvp in concreteDiscriminatedTypeProperties.Properties)
                    {
                        newProperties.TryAdd(kvp.Key, kvp.Value);

                        if (baseProperties.Properties.TryGetValue(kvp.Key, out var baseProperty))
                        {
                            newProperties.Remove(kvp.Key);
                        }
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
            var annotation = property.GetCustomAttributes<ExtendedTypePropertyAttribute>(true).FirstOrDefault()
                             ?? new ExtendedTypePropertyAttribute(null);
            
            var propertyType = property.PropertyType;
            if (propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)
                && propertyType.GetGenericArguments()[0].IsEnum is false)
            {
                propertyType = propertyType.GetGenericArguments()[0];
            }

            // Compare against the containing type (self-reference), not arbitrary property types
            if (visited.Contains(type) &&
                propertyType == type &&
                typeCache.TryGetValue(type, out var containingTypeRef))
            {
                typeProperties[CamelCase(property.Name)] = new ObjectTypeProperty(
                    factory.AddOrGetReference(containingTypeRef),
                    annotation?.Flags ?? ObjectTypePropertyFlags.None,
                    annotation?.Description);
                continue;
            }

            
            var isNullable = annotation?.IsNullable ?? false;
            var minimumLengthAttribute = property.GetCustomAttribute<MinLengthAttribute>(false);
            var maximumLengthAttribute = property.GetCustomAttribute<MaxLengthAttribute>(false);
            var patternAttribute = property.GetCustomAttribute<BicepStringPatternAttribute>(false);
            annotation?.MergeStringPropertyAttribute(maximumLengthAttribute, minimumLengthAttribute, patternAttribute);

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
                            factory.AddOrGetReference(typeCache.GetOrAdd(valueType,
                                _ => GenerateForRecord(factory, typeCache, valueType)));
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
                    typeReference = factory.AddOrGetReference(typeCache.GetOrAdd(propertyType, _ => new ObjectType(typeName,
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
                        elementTypeReference = typeCache.GetOrAdd(elementType,
                            _ => factory.AddOrGetReference(GenerateForRecord(factory, typeCache, elementType)).Type);
                    }

                    typeReference = typeCache.GetOrAdd(propertyType,
                        _ => factory.AddOrGetReference(new ArrayType(factory.GetReference(elementTypeReference))).Type);
                }
                else if (propertyType.IsClass)
                {
                    visited.Add(property.PropertyType);

                    typeReference = typeCache.GetOrAdd(propertyType,
                        _ => factory.AddOrGetReference(GenerateForRecord(factory, typeCache, propertyType)).Type);
                }
                else if (propertyType.IsGenericType &&
                         propertyType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                         propertyType.GetGenericArguments()[0] is { IsEnum: true } enumType)
                {
                    var enumMembers = enumType.GetEnumNames()
                        .Select(x => factory.AddOrGetReference(new StringLiteralType(x)).Type)
                        .Select(x => factory.GetReference(x))
                        .ToImmutableArray();

                    typeReference = typeCache.GetOrAdd(propertyType,
                        _ => factory.AddOrGetReference(new UnionType(enumMembers)).Type);
                }
                else if (propertyType is { IsEnum: true } enumTypeNonNullable)
                {
                    var enumMembers = enumTypeNonNullable.GetEnumNames()
                        .Select(x => factory.AddOrGetReference(new StringLiteralType(x)).Type)
                        .Select(x => factory.GetReference(x))
                        .ToImmutableArray();

                    typeReference = typeCache.GetOrAdd(propertyType,
                        _ => factory.AddOrGetReference(new UnionType(enumMembers)).Type);
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

    private bool TryResolveTypeReference(Type type, ExtendedTypePropertyAttribute? annotation,
        [NotNullWhen(true)] out TypeBase? typeReference)
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