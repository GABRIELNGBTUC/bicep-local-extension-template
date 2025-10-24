using Azure.Bicep.Types.Concrete;

namespace BicepLocalExtension.Extensions;

public static class TypeFactoryExtensions
{
    public static ITypeReference AddOrGetReference(this TypeFactory factory, TypeBase type)
    {
        try
        {
            var typeReference = factory.Create(() => type);
            return factory.GetReference(typeReference);
        }
        catch (ArgumentException)
        {
        }

        return factory.GetReference(type);
    }
}