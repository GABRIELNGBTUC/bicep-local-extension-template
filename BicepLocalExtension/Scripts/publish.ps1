param(
[Parameter(Mandatory=$false)]
[string] $ExtensionName = 'my-ext',
[Parameter(Mandatory=$true)]
[string] $ProjectNamespace
)

dotnet publish --configuration release -r osx-arm64 .
dotnet publish --configuration release -r linux-x64 .
dotnet publish --configuration release -r win-x64 .

bicep publish-extension --bin-osx-arm64 ./bin/release/net10.0/osx-arm64/publish/$($ProjectNamespace) --bin-linux-x64 ./bin/release/net10.0/linux-x64/publish/$($ProjectNamespace) --bin-win-x64 ./bin/release/net10.0/win-x64/publish/$($ProjectNamespace).exe --target ./bin/$($ExtensionName) --force
