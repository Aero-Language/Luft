$runtimes = @("win-x64", "linux-x64", "osx-arm64")

foreach ($rid in $runtimes) {
    Write-Host "Building for $rid..."
    dotnet publish -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "./publish/$rid"}
