[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskSolutionRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $taskSolutionRoot '..'))
$taskPayload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$taskOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $taskPayload 'Sponde.exe'))) { throw 'Published Windows executable missing.' }
$taskPrivateFiles = Get-ChildItem -LiteralPath $taskPayload -Recurse -File | Where-Object { $_.Name -match '\.(pdf|xlsx|spondebackup)$|\.sqlite(?:3)?(?:-wal|-shm)?$' }
if ($taskPrivateFiles) { throw 'Application payload contains private data.' }
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskLicense = [System.IO.File]::ReadAllText((Join-Path $taskRepositoryRoot 'LICENSE'))
$taskEscapedLicense = $taskLicense.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}').Replace("`r", '').Replace("`n", '\par ')
$taskLicenseFile = Join-Path $taskOutput 'license.rtf'
[System.IO.File]::WriteAllText($taskLicenseFile, '{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 ' + $taskEscapedLicense + '}')
$env:DOTNET_ROLL_FORWARD = 'Major'
Push-Location $taskSolutionRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'WiX tool restore failed.' }
    dotnet tool run wix -- extension add WixToolset.UI.wixext/6.0.2
    if ($LASTEXITCODE -ne 0) { throw 'WiX UI extension restore failed.' }
    dotnet tool run wix -- build (Join-Path $PSScriptRoot 'Package.wxs') -arch x64 -culture en-US -ext WixToolset.UI.wixext -d "PayloadDir=$taskPayload" -d "ProductVersion=$Version" -d "AssetsDir=$(Join-Path $taskSolutionRoot 'packaging/assets')" -d "LicenseRtf=$taskLicenseFile" -o (Join-Path $taskOutput "Sponde-$Version-win-x64.msi")
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
} finally { Pop-Location }
