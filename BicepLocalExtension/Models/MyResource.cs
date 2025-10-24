using BicepLocalExtension.Generator;

namespace BicepLocalExtension.Models;

using System.Text.Json.Serialization;
using Azure.Bicep.Types.Concrete;
using Bicep.Local.Extension.Types.Attributes;

public enum OperationType
{
    Uppercase,
    Lowercase,
    Reverse,
}

public record MyResourceIdentifiers(
    [property: ExtendedTypeProperty("The resource name", ObjectTypePropertyFlags.Identifier | ObjectTypePropertyFlags.Required)]
    string Name
);

[ResourceType("MyResource")]
public record MyResource(
    string Name,
    [property: ExtendedTypeProperty("The resource operation type", ObjectTypePropertyFlags.Required),
               JsonConverter(typeof(JsonStringEnumConverter))]
    OperationType? Operation,
    [property: ExtendedTypeProperty("The text output")]
    string? Output
    , [property: ExtendedTypeProperty("The resource tags")]
     Dictionary<string, string>? Tags
    ) : MyResourceIdentifiers(Name);