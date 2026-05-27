using System.Text.Json;
using Bicep.Local.Rpc;
using Grpc.Net.Client;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var argsList = args.ToList();
var address = GetOption(argsList, "--address") ?? "http://localhost:5189";
var command = GetOption(argsList, "--command") ?? "get-type-files";
var outputDir = GetOption(argsList, "--output");

if (argsList.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
    argsList.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    PrintHelp();
    return;
}

using var channel = GrpcChannel.ForAddress(address);
var client = new BicepExtension.BicepExtensionClient(channel);

switch (command.ToLowerInvariant())
{
    case "ping":
        await client.PingAsync(new Empty());
        Console.WriteLine("Ping succeeded.");
        break;

    case "get-type-files":
        await RunGetTypeFilesAsync(client, outputDir);
        break;

    default:
        Console.Error.WriteLine($"Unsupported command '{command}'.");
        PrintHelp();
        Environment.ExitCode = 1;
        break;
}

static async Task RunGetTypeFilesAsync(BicepExtension.BicepExtensionClient client, string? outputDir)
{
    var response = await client.GetTypeFilesAsync(new Empty());

    Console.WriteLine($"Index file content length: {response.IndexFile.Length}");
    Console.WriteLine($"Type files returned: {response.TypeFiles.Count}");

    if (TryCountResources(response.IndexFile, out var resourceCount))
    {
        Console.WriteLine($"Resources in index: {resourceCount}");
    }

    foreach (var kv in response.TypeFiles.OrderBy(k => k.Key))
    {
        Console.WriteLine($"- {kv.Key} ({kv.Value.Length} chars)");
    }

    if (string.IsNullOrWhiteSpace(outputDir))
        return;

    Directory.CreateDirectory(outputDir);

    var indexFilePath = Path.Combine(outputDir, "index.json");
    await File.WriteAllTextAsync(indexFilePath, response.IndexFile);

    foreach (var kv in response.TypeFiles)
    {
        var path = Path.Combine(outputDir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, kv.Value);
    }

    Console.WriteLine($"Saved type files to: {Path.GetFullPath(outputDir)}");
}

static bool TryCountResources(string indexFileJson, out int resourceCount)
{
    resourceCount = 0;

    try
    {
        using var doc = JsonDocument.Parse(indexFileJson);

        if (!doc.RootElement.TryGetProperty("resources", out var resources) ||
            resources.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        resourceCount = resources.EnumerateObject().Count();
        return true;
    }
    catch (JsonException)
    {
        return false;
    }
}

static string? GetOption(List<string> args, string name)
{
    for (var i = 0; i < args.Count - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  BicepLocalExtension.GrpcProbe --command <ping|get-type-files> [--address <url>] [--output <path>]");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  BicepLocalExtension.GrpcProbe --command ping --address http://localhost:5189");
    Console.WriteLine("  BicepLocalExtension.GrpcProbe --command get-type-files --address http://localhost:5189 --output .\\type-files");
}

