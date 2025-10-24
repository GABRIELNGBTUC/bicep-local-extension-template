using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Azure.Bicep.Types.Concrete;
using BicepLocalExtension.Generator;

namespace BicepLocalExtension.Models;

public enum AuthenticationMode
{
    ApiKey,
    AzureCli,
    ManagedIdentity,
}

//Only compatible with the custom type generator under the "Generator" folder
[JsonPolymorphic(TypeDiscriminatorPropertyName = "authenticationMode")]
[JsonDerivedType(typeof(ApiKeyConfiguration), typeDiscriminator: nameof(AuthenticationMode.ApiKey))]
[JsonDerivedType(typeof(AzureCliConfiguration), typeDiscriminator: nameof(AuthenticationMode.AzureCli))]
[JsonDerivedType(typeof(ManagedIdentityConfiguration), typeDiscriminator: nameof(AuthenticationMode.ManagedIdentity))]
public record Configuration(
    [property: ExtendedTypeProperty(null, ObjectTypePropertyFlags.Required),
    JsonConverter(typeof(JsonStringEnumConverter))]
    AuthenticationMode AuthenticationMode);
public record ApiKeyConfiguration(
    [property: ExtendedTypeProperty(null, ObjectTypePropertyFlags.Required, true),
    MinLength(32), MaxLength(64)]
    string ApiKey) : Configuration(AuthenticationMode.ApiKey);

public record AzureCliConfiguration() : Configuration(AuthenticationMode.AzureCli);

public record ManagedIdentityConfiguration(
     [property: ExtendedTypeProperty("User-assigned identity resource object ID. Leave to null if using system-assigned identity", ObjectTypePropertyFlags.None),
     MinLength(36), MaxLength(36), BicepStringPattern("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    string? ObjectId
    ) : Configuration(AuthenticationMode.ManagedIdentity);