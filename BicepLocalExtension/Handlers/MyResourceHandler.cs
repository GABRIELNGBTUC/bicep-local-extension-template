using BicepLocalExtension.Exceptions;
using BicepLocalExtension.Extensions;
using BicepLocalExtension.Models;
using Microsoft.Extensions.Logging;

namespace BicepLocalExtension.Handlers;

using Bicep.Local.Extension.Host.Handlers;

public partial class MyResourceHandler : TypedResourceHandler<MyResource, MyResourceIdentifiers>
{
    private readonly ILogger<MyResourceHandler> _logger;

    public MyResourceHandler(ILogger<MyResourceHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<ResourceResponse> Preview(ResourceRequest request,
        CancellationToken cancellationToken)
    {
        LogPreviewRequested(request.Properties.Name);
        // Apply the modifications to the request properties but do not apply them
        await Task.CompletedTask;

        // Return the request with the modified properties
        return GetResponse(request);
    }

    protected override async Task<ResourceResponse> CreateOrUpdate(ResourceRequest request,
        CancellationToken cancellationToken)
    {
        LogCreatingOrUpdating(request.Properties.Name, request.Properties.Operation);

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

        LogProcessingComplete(request.Properties.Name, request.Properties.Output);
        return GetResponse(request);
    }

    protected override Task<ResourceResponse> Delete(ReferenceRequest request, CancellationToken cancellationToken)
    {
        LogDeleteNotImplemented();
        throw new NotImplementedException();
    }

    protected override async Task<ResourceResponse> Get(ReferenceRequest request, CancellationToken cancellationToken)
    {
        LogGettingResource(request.Identifiers.Name);
        //Get data based on the request identifiers
        await Task.CompletedTask;

        return this.CreateGetResponse(
            new MyResource("SomeFetchedData", null, null, null),
            request);
    }

    protected override MyResourceIdentifiers GetIdentifiers(MyResource properties)
        => new(properties.Name);

    [LoggerMessage(1, LogLevel.Information, "MyResource: preview requested for resource '{Name}'")]
    private partial void LogPreviewRequested(string name);

    [LoggerMessage(2, LogLevel.Information, "MyResource: creating/updating resource '{Name}' with operation '{Operation}'")]
    private partial void LogCreatingOrUpdating(string name, OperationType? operation);

    [LoggerMessage(3, LogLevel.Information, "MyResource: resource '{Name}' processed successfully, output: '{Output}'")]
    private partial void LogProcessingComplete(string name, string? output);

    [LoggerMessage(4, LogLevel.Warning, "MyResource: Delete operation is not implemented")]
    private partial void LogDeleteNotImplemented();

    [LoggerMessage(5, LogLevel.Information, "MyResource: getting resource with identifier '{Name}'")]
    private partial void LogGettingResource(string name);
}