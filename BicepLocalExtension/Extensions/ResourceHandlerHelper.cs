using Bicep.Local.Extension.Host.Handlers;

namespace BicepLocalExtension.Extensions;

public static class TypedResourceHandlerExtensions
{
    /// <summary>
    /// Returns the expected return object for the handler GET method
    /// </summary>
    /// <param name="handler">The handler executing this method</param>
    /// <param name="resource">The resource handled by the handler</param>
    /// <param name="request">The GET request from BICEP</param>
    /// <typeparam name="T">The C# type of the bicep resource</typeparam>
    /// <typeparam name="U">The C# type for the bicep resource identifiers</typeparam>
    /// <returns>A valid ResourceResponse for the GET operation</returns>
    public static TypedResourceHandler<T, U>.ResourceResponse CreateGetResponse<T, U>(this TypedResourceHandler<T, U> handler,
        T resource, TypedResourceHandler<T,U>.ReferenceRequest request)
        where T : class where U : class
    {
        return new()
        {
            Identifiers = request.Identifiers,
            Type = request.Type,
            ApiVersion = request.ApiVersion,
            Properties = resource,
        };
    }
}