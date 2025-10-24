namespace BicepLocalExtension.Generator;

[AttributeUsage(AttributeTargets.Property)]
public class BicepStringPatternAttribute : Attribute
{
    /// <summary>
    /// Gets the regex patter to apply during bicep compile-time validation for the bicep string
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The regex validation pattern to be applied to the string
    /// </summary>
    public BicepStringPatternAttribute(string value)
    {
        Value = value;
    }
}