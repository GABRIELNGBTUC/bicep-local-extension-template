using BicepLocalExtension.Extensions;

namespace BicepLocalExtension.Exceptions;

/// <summary>
/// Returns an error message when a property is missing. Can optionally return a list of possible values when the property is an enum.
/// </summary>
public class PropertyMissingException : Exception
{
    public PropertyMissingException(string propertyName) : base(ReturnMessage(propertyName))
    {
    }

    public PropertyMissingException(string propertyName, Type enumType) : base(ReturnMessage(propertyName, enumType))
    {
    }

    private static string ReturnMessage(string propertyName)
    {
        return $"Property {propertyName} is missing";
    }

    private static string ReturnMessage(string propertyName, Type enumType)
    {
        return enumType.IsEnum ? 
            $"Property \"{propertyName.ToCamelCase()}\" is missing. Possible values are: {string.Join(", ", Enum.GetNames(enumType))}" 
            : ReturnMessage(propertyName);
    }
}