using System.Text.Json;
using Bicep.Local.Rpc;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using BicepLocalExtension.Exceptions;
using BicepLocalExtension.Handlers;
using BicepLocalExtension.Models;

namespace BicepLocalExtension.UnitTests.Handlers;

[TestClass]
public class MyResourceHandlerTests
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private MyResourceHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _handler = new MyResourceHandler(NullLogger<MyResourceHandler>.Instance);
    }

    [TestMethod]
    public async Task CreateOrUpdate_WithUppercaseOperation_ReturnsUppercasedOutput()
    {
        // Arrange
        var properties = new MyResource("hello", OperationType.Uppercase, null, null);
        var request = new ResourceSpecification
        {
            Type = _handler.Type,
            Properties = JsonSerializer.Serialize(properties, _jsonOptions)
        };

        // Act
        var response = await _handler.CreateOrUpdate(request, CancellationToken.None);

        // Assert
        response.Resource.Should().NotBeNull();
        var result = JsonSerializer.Deserialize<MyResource>(response.Resource!.Properties, _jsonOptions);
        result!.Output.Should().Be("HELLO");
    }

    [TestMethod]
    public async Task CreateOrUpdate_WithLowercaseOperation_ReturnsLowercasedOutput()
    {
        // Arrange
        var properties = new MyResource("WORLD", OperationType.Lowercase, null, null);
        var request = new ResourceSpecification
        {
            Type = _handler.Type,
            Properties = JsonSerializer.Serialize(properties, _jsonOptions)
        };

        // Act
        var response = await _handler.CreateOrUpdate(request, CancellationToken.None);

        // Assert
        response.Resource.Should().NotBeNull();
        var result = JsonSerializer.Deserialize<MyResource>(response.Resource!.Properties, _jsonOptions);
        result!.Output.Should().Be("world");
    }

    [TestMethod]
    public async Task CreateOrUpdate_WithReverseOperation_ReturnsReversedOutput()
    {
        // Arrange
        var properties = new MyResource("abc", OperationType.Reverse, null, null);
        var request = new ResourceSpecification
        {
            Type = _handler.Type,
            Properties = JsonSerializer.Serialize(properties, _jsonOptions)
        };

        // Act
        var response = await _handler.CreateOrUpdate(request, CancellationToken.None);

        // Assert
        response.Resource.Should().NotBeNull();
        var result = JsonSerializer.Deserialize<MyResource>(response.Resource!.Properties, _jsonOptions);
        result!.Output.Should().Be("cba");
    }

    [TestMethod]
    public async Task CreateOrUpdate_WithNullOperation_ReturnsErrorResponse()
    {
        // Arrange
        var properties = new MyResource("hello", null, null, null);
        var request = new ResourceSpecification
        {
            Type = _handler.Type,
            Properties = JsonSerializer.Serialize(properties, _jsonOptions)
        };

        // Act
        var response = await _handler.CreateOrUpdate(request, CancellationToken.None);

        // Assert
        response.Resource.Should().BeNull();
        response.ErrorData.Should().NotBeNull();
        response.ErrorData!.Error.Message.Should().Contain(nameof(MyResource.Operation).ToLower()[0]
            + nameof(MyResource.Operation)[1..]);
    }

    [TestMethod]
    public async Task Get_ReturnsResourceResponse()
    {
        // Arrange
        var identifiers = new MyResourceIdentifiers("MyResourceName");
        var reference = new ResourceReference
        {
            Type = _handler.Type,
            Identifiers = JsonSerializer.Serialize(identifiers, _jsonOptions)
        };

        // Act
        var response = await _handler.Get(reference, CancellationToken.None);

        // Assert
        response.Resource.Should().NotBeNull();
        var result = JsonSerializer.Deserialize<MyResource>(response.Resource!.Properties, _jsonOptions);
        result.Should().NotBeNull();
        result!.Name.Should().Be("SomeFetchedData");
    }

    [TestMethod]
    public async Task Preview_ReturnsUnchangedRequest()
    {
        // Arrange
        var properties = new MyResource("hello", OperationType.Uppercase, null, null);
        var request = new ResourceSpecification
        {
            Type = _handler.Type,
            Properties = JsonSerializer.Serialize(properties, _jsonOptions)
        };

        // Act
        var response = await _handler.Preview(request, CancellationToken.None);

        // Assert
        response.Resource.Should().NotBeNull();
        var result = JsonSerializer.Deserialize<MyResource>(response.Resource!.Properties, _jsonOptions);
        result!.Name.Should().Be("hello");
        result.Operation.Should().Be(OperationType.Uppercase);
    }
}

