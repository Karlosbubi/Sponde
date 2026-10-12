$ErrorActionPreference = 'Stop'
$taskExecutable = Join-Path $PSScriptRoot '.local/publish/win-x64/Sponde.exe'
$taskDataDirectory = Join-Path $PSScriptRoot '.local/pdf-data'
if (-not (Test-Path -LiteralPath $taskExecutable)) {
    throw 'Build first: dotnet publish Sponde/Sponde.csproj -c Release -r win-x64 --self-contained true -o .local/publish/win-x64'
}
& $taskExecutable --data-dir $taskDataDirectory
