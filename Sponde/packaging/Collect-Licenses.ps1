[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskPayload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$taskAssets = Get-Content (Join-Path $taskRoot 'Sponde/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$taskPackages = @{}
foreach ($taskLibrary in $taskAssets.libraries.GetEnumerator()) {
    if ($taskLibrary.Value.type -eq 'package') { $taskPackages[$taskLibrary.Value.path] = $true }
}
foreach ($taskFramework in $taskAssets.project.frameworks.Values) {
    foreach ($taskDownload in $taskFramework.downloadDependencies) {
        $taskVersion = $taskDownload.version.Trim('[', ']').Split(',')[0]
        $taskPackages[($taskDownload.name.ToLowerInvariant() + '/' + $taskVersion)] = $true
    }
}
$taskNotices = Join-Path $taskPayload 'licenses'
New-Item -ItemType Directory -Path $taskNotices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path (Split-Path $taskRoot -Parent) 'LICENSE') -Destination (Join-Path $taskNotices 'Sponde-LICENSE.txt')
$taskInventory = [System.Collections.Generic.List[string]]::new()
$taskInventory.Add('# Third-party dependency notices')
$taskInventory.Add('Inventory of restored application dependencies and runtime packs; platform-specific packages may not all ship on this platform. Bundled license/notice files are preserved in this directory.')
foreach ($taskPackage in ($taskPackages.Keys | Sort-Object)) {
    $taskDirectory = $null
    foreach ($taskFolder in $taskAssets.packageFolders.Keys) {
        $taskCandidate = Join-Path $taskFolder $taskPackage
        if (Test-Path -LiteralPath $taskCandidate) { $taskDirectory = $taskCandidate; break }
    }
    if (-not $taskDirectory) { throw "Restored package missing: $taskPackage" }
    $taskNuspec = Get-ChildItem -LiteralPath $taskDirectory -Filter '*.nuspec' -File | Select-Object -First 1
    [xml]$taskMetadata = Get-Content -LiteralPath $taskNuspec.FullName -Raw
    $taskInfo = $taskMetadata.package.metadata
    $taskInventory.Add("`n## $taskPackage`nAuthors: $($taskInfo.authors)`nLicense: $($taskInfo.license.InnerText)`nLicense URL: $($taskInfo.licenseUrl)`nProject: $($taskInfo.projectUrl)")
    $taskFiles = @(Get-ChildItem -LiteralPath $taskDirectory -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD[-_]?PARTY[-_]?NOTICES|COPYING)(\..*)?$' })
    if ($taskInfo.license.type -eq 'file') {
        $taskFiles += Get-Item -LiteralPath (Join-Path $taskDirectory $taskInfo.license.InnerText)
    }
    foreach ($taskFile in ($taskFiles | Sort-Object FullName -Unique)) {
        $taskDestination = Join-Path $taskNotices ($taskPackage.Replace('/', '-') + '/' + [System.IO.Path]::GetRelativePath($taskDirectory, $taskFile.FullName))
        New-Item -ItemType Directory -Path (Split-Path $taskDestination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination
    }
}
[System.IO.File]::WriteAllLines((Join-Path $taskNotices 'THIRD-PARTY-NOTICES.md'), $taskInventory)
