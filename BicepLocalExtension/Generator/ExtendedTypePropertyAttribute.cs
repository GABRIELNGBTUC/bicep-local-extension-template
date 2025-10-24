using System.ComponentModel.DataAnnotations;
using Azure.Bicep.Types.Concrete;
using Bicep.Local.Extension.Types.Attributes;

namespace BicepLocalExtension.Generator;

/// <summary>
/// To only use with the custom type generator <see cref="CustomTypeGenerator"/>.
/// <inheritdoc cref="TypePropertyAttribute"/>
/// </summary>
/// <param name="description">A human-readable description of the property, or <c>null</c> if not specified.</param>
/// <param name="flags">Flags that describe the property's characteristics (e.g., required, read-only).</param>
/// <param name="isSecure">Indicates whether the property contains sensitive information and should be treated as secure.</param>
[AttributeUsage(AttributeTargets.Property)]
public class ExtendedTypePropertyAttribute(
    string? description,
    ObjectTypePropertyFlags flags = ObjectTypePropertyFlags.None,
    bool isSecure = false)
    : TypePropertyAttribute(description, flags, isSecure)
{
    /// <summary>
    /// Gets a value indicating whether the property should be marked as nullable by bicep.
    /// </summary>
    public bool IsNullable { get; }

    /// <summary>
    /// Gets the maximum length of a bicep string.
    /// </summary>
    public int? MaxLength { get; private set; }

    /// <summary>
    /// Gets the minimum length of a bicep string.
    /// </summary>
    public int? MinLength { get; private set; }

    /// <summary>
    /// Gets the regex pattern to validate the bicep string.
    /// </summary>
    public string? Pattern { get; private set; }

    /// <summary>
    /// Copies into the attribute any relevant attribute values used to generate a string from the <see cref="MaxLengthAttribute"/>, <see cref="MinLengthAttribute"/> and <see cref="BicepStringPatternAttribute"/> classes.
    /// </summary>
    /// <param name="maxLengthAttribute"></param>
    /// <param name="minLengthAttribute"></param>
    /// <param name="patternAttribute"></param>
    public void MergeStringPropertyAttribute(MaxLengthAttribute? maxLengthAttribute = null,
        MinLengthAttribute? minLengthAttribute = null, BicepStringPatternAttribute? patternAttribute = null)
    {
        MaxLength = maxLengthAttribute?.Length;
        MinLength = minLengthAttribute?.Length;
        Pattern = patternAttribute?.Value;
    }
}