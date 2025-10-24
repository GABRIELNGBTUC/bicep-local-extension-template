using System.Collections.Immutable;
using Azure.Bicep.Types.Concrete;
using Microsoft.AspNetCore.Builder;
using Bicep.Local.Extension.Host.Extensions;
using Bicep.Local.Extension.Types;
using BicepLocalExtension.Generator;
using BicepLocalExtension.Handlers;
using BicepLocalExtension.Models;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder();

builder.AddBicepExtensionHost(args);
builder.Services
    .AddBicepExtension(
        name: "MyExtension",
        version: "0.0.1",
        isSingleton: true,
        typeAssembly: typeof(Program).Assembly)
    .WithResourceHandler<MyResourceHandler>();

// Comment the block below make use of the built-in generator in the Bicep extension nuget package
// This custom generator has more features than the 0.38 built-in generator but may contain bugs and comparatively generates a larger index file
// ******************************************
var typeDictionary = new Dictionary<Type, Func<TypeBase>>
{
    { typeof(string), () => new StringType() },
    { typeof(bool), () => new BooleanType() },
    { typeof(int), () => new IntegerType() }
}.ToImmutableDictionary();
var typeFactory = new TypeFactory([]);

foreach (var type in typeDictionary)
{
    typeFactory.Create(type.Value);
}

builder.Services.AddSingleton<ITypeDefinitionBuilder>(sp => new CustomTypeGenerator(
    name: "MyExtension",
    version: "0.0.1",
    isSingleton: true,
    typeof(Configuration),
    typeFactory,
    sp.GetRequiredService<ITypeProvider>(),
    typeDictionary));
// ******************************************

var app = builder.Build();

app.MapBicepExtension();

await app.RunAsync();                   