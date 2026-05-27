# Context

This is a template repository for building Bicep local extensions in .NET. A Bicep local extension allows you to extend Azure Bicep with custom resource types that execute arbitrary .NET logic (e.g. calling APIs, manipulating files, querying Azure services) during a deployment.

The template provides all the boilerplate needed to get started: a typed resource handler, a custom type generator, a source generator for automatic handler registration, a gRPC probe for testing, and a unit test project.

## Structure

```
BicepLocalExtension/              — Main extension host (entry point)
BicepLocalExtension.SourceGenerator/ — Roslyn source generator (auto-registers handlers)
BicepLocalExtension.UnitTests/    — MSTest unit tests
BicepLocalExtension.GrpcProbe/    — Console tool for manual gRPC testing
```

### Handlers

Each task performed by the extension has a handler. Handlers are classes derived from `TypedResourceHandler<TResource, TIdentifiers>`.

Each handler requires:
1. A **resource model** — a record annotated with `[ResourceType("ResourceName")]`
2. An **identifier model** — a record with the properties used to identify the resource in `Get` operations

Despite having more methods, only `CreateOrUpdate` and `Get` should be implemented. All handlers are **automatically registered** by the source generator — no manual wiring in `Program.cs` is needed.

### Resource model

Resource models live in the `Models/` directory.

- Annotate the type with `[ResourceType("ResourceName")]`
- Annotate each property with `[ExtendedTypeProperty("Description", flags)]`

### Custom Type Generator

`CustomTypeGenerator` generates the Bicep type index from .NET model types at startup. `CustomTypeProvider` scans assemblies for `[ResourceType]`-annotated types.

### Source Generator

`BicepLocalExtension.SourceGenerator` scans for all concrete `TypedResourceHandler` subclasses and emits a `WithAllResourceHandlers(this IBicepExtensionBuilder)` extension method so `Program.cs` stays clean.

## Build

```powershell
dotnet build BicepLocalExtension.sln
```

To publish a self-contained single-file binary:

```powershell
dotnet publish BicepLocalExtension/BicepLocalExtension.csproj -c Release -r win-x64
```

Or use the provided script:

```powershell
.\BicepLocalExtension\Scripts\publish.ps1
```

## Testing

### Unit tests

Unit tests are in `BicepLocalExtension.UnitTests` using MSTest, Moq, and FluentAssertions.

```powershell
dotnet test BicepLocalExtension.UnitTests/BicepLocalExtension.UnitTests.csproj
```

### Writing new handler tests

When adding a new handler:

1. Inject external dependencies via constructor parameters using interfaces
2. Create a corresponding `*Tests.cs` in `BicepLocalExtension.UnitTests/Handlers/`
3. Use `Mock<IYourService>(MockBehavior.Strict)` for injected dependencies
4. Call `_mockService.VerifyAll()` in `[TestCleanup]`

When building `ResourceSpecification.Properties` for test inputs, serialize with `JsonSerializerDefaults.Web` options so camelCase property names are used.

### Test type generation (gRPC probe)

To test that the extension generates types correctly, use the `BicepLocalExtension.GrpcProbe` console app:

1. Start the extension host:
```powershell
dotnet run --project "BicepLocalExtension/BicepLocalExtension.csproj" -- --http 5190
```

2. Ping to verify connectivity:
```powershell
dotnet run --project "BicepLocalExtension.GrpcProbe/BicepLocalExtension.GrpcProbe.csproj" -- --command ping --address http://localhost:5190
```

3. Retrieve and save the generated type files:
```powershell
dotnet run --project "BicepLocalExtension.GrpcProbe/BicepLocalExtension.GrpcProbe.csproj" -- --command get-type-files --address http://localhost:5190 --output .\type-files
```

Expected result for `get-type-files`:
- A non-empty `Index file content length`
- `Type files returned` greater than `0`
- If `--output` is used, `index.json` and `types.json` written to the target folder

Validate the contents of `types.json` against your model records to confirm all properties are present.

## Adding a new resource

1. Create a resource model in `Models/`:
   ```csharp
   [ResourceType("MyNewResource")]
   public record MyNewResource(
       [property: ExtendedTypeProperty("The name", ObjectTypePropertyFlags.Identifier | ObjectTypePropertyFlags.Required)]
       string Name,
       [property: ExtendedTypeProperty("Some value")]
       string? Value
   ) : MyNewResourceIdentifiers(Name);
   ```

2. Create an identifier model:
   ```csharp
   public record MyNewResourceIdentifiers(string Name);
   ```

3. Create a handler in `Handlers/`:
   ```csharp
   public partial class MyNewResourceHandler : TypedResourceHandler<MyNewResource, MyNewResourceIdentifiers>
   {
       // implement CreateOrUpdate and Get
   }
   ```

4. The source generator will automatically register the handler — no changes to `Program.cs` needed.

5. Add unit tests in `BicepLocalExtension.UnitTests/Handlers/MyNewResourceHandlerTests.cs`.
````

