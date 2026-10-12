[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$PreviousInstaller,
    [Parameter(Mandatory)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
$taskInstaller = (Resolve-Path -LiteralPath $Installer).Path
$taskTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('sponde-msi-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTestRoot | Out-Null
$taskData = Join-Path $taskTestRoot 'data'
New-Item -ItemType Directory -Path $taskData | Out-Null
$taskSentinel = Join-Path $taskData 'keep.txt'
[System.IO.File]::WriteAllText($taskSentinel, 'User data must survive uninstall.')
function Invoke-Msi([string]$Operation, [string]$Package, [string]$LogName) {
    $taskArguments = @($Operation, ('"' + $Package + '"'), '/qn', '/norestart', '/l*v', ('"' + (Join-Path $taskTestRoot $LogName) + '"'))
    $taskProcess = Start-Process msiexec.exe -ArgumentList $taskArguments -Wait -PassThru -WindowStyle Hidden
    if ($taskProcess.ExitCode -notin @(0, 3010)) { Get-Content (Join-Path $taskTestRoot $LogName) -Tail 80; throw "Windows Installer failed: $($taskProcess.ExitCode)" }
}
Invoke-Msi '/i' (Resolve-Path -LiteralPath $PreviousInstaller).Path 'previous-install.log'
Invoke-Msi '/i' $taskInstaller 'upgrade.log'
try {
    $taskRegistrations = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' | Get-ItemProperty | Where-Object { $_.DisplayName -eq 'Sponde' })
    if ($taskRegistrations.Count -ne 1 -or $taskRegistrations[0].DisplayVersion -ne $ExpectedVersion) { throw 'Upgrade did not leave exactly one current-version registration.' }
    $taskExecutable = Join-Path $env:ProgramFiles 'Sponde/Sponde.exe'
    if (-not (Test-Path -LiteralPath $taskExecutable)) { throw 'Installed executable missing.' }
    $taskApplication = Start-Process -FilePath $taskExecutable -ArgumentList @('--data-dir', ('"' + $taskData + '"')) -PassThru -WindowStyle Hidden
    try {
        $taskDeadline = [DateTime]::UtcNow.AddSeconds(45)
        do {
            $taskApplication.Refresh()
            if ($taskApplication.HasExited) { throw "Installed application exited during startup: $($taskApplication.ExitCode)" }
            $taskReady = (Test-Path -LiteralPath (Join-Path $taskData 'finance.sqlite')) -and $taskApplication.MainWindowHandle -ne [IntPtr]::Zero
            if (-not $taskReady) { Start-Sleep -Milliseconds 250 }
        } while (-not $taskReady -and [DateTime]::UtcNow -lt $taskDeadline)
        if (-not $taskReady) { throw 'Installed application did not initialize its dataset and GUI before the startup deadline.' }
    } finally { if (-not $taskApplication.HasExited) { Stop-Process -Id $taskApplication.Id } }
} finally { Invoke-Msi '/x' $taskInstaller 'uninstall.log' }
if (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'Sponde/Sponde.exe')) { throw 'Uninstall left the executable installed.' }
if (-not (Test-Path -LiteralPath $taskSentinel)) { throw 'Uninstall removed user data.' }
if (-not (Test-Path -LiteralPath (Join-Path $taskData 'finance.sqlite'))) { throw 'Uninstall removed the database.' }
Write-Output 'MSI install, upgrade, native app startup, uninstall and user-data preservation passed.'
