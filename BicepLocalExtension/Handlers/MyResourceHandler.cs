using BicepLocalExtension.Exceptions;
using BicepLocalExtension.Extensions;
using BicepLocalExtension.Models;

namespace BicepLocalExtension.Handlers;

using Bicep.Local.Extension.Host.Handlers;

public class MyResourceHandler : TypedResourceHandler<MyResource, MyResourceIdentifiers>
{
    protected override async Task<ResourceResponse> Preview(ResourceRequest request,
        CancellationToken cancellationToken)
    {
        // Apply the modifications to the request properties but do not apply them
        await Task.CompletedTask;

        // Return the request with the modified properties
        return GetResponse(request);
    }

    protected override async Task<ResourceResponse> CreateOrUpdate(ResourceRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        request.Properties = request.Properties with
        {
            Output = request.Properties.Operation switch
            {
                OperationType.Uppercase => request.Properties.Name.ToUpperInvariant(),
                OperationType.Lowercase => request.Properties.Name.ToLowerInvariant(),
                OperationType.Reverse => new([.. request.Properties.Name.Reverse()]),
                _ => throw new PropertyMissingException(nameof(request.Properties.Operation), typeof(OperationType))
            }
        };

        return GetResponse(request);
    }

    protected override Task<ResourceResponse> Delete(ReferenceRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    protected override async Task<ResourceResponse> Get(ReferenceRequest request, CancellationToken cancellationToken)
    {
        //Get data based on the request identifiers
        await Task.CompletedTask;
        

        return this.CreateGetResponse(
            new MyResource("SomeFetchedData", null,null,null), 
            request);
    }

    protected override MyResourceIdentifiers GetIdentifiers(MyResource properties)
        => new(properties.Name);
}