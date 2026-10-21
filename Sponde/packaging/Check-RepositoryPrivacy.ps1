[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskRepository = Split-Path $taskRoot -Parent
$taskPaths = @(git -C $taskRepository ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked files for privacy checks.' }
$taskFailures = [System.Collections.Generic.List[string]]::new()
foreach ($taskPath in $taskPaths) {
    $taskExtension = [System.IO.Path]::GetExtension($taskPath).ToLowerInvariant()
    if ($taskExtension -in @('.pdf', '.xlsx', '.xls', '.sqlite', '.sqlite3', '.db', '.spondebackup', '.zip', '.msi', '.rpm')) {
        $taskFailures.Add("$taskPath : private document/database or generated package must not be tracked")
        continue
    }
    if ($taskExtension -in @('.png', '.ico')) { continue }
    $taskText = [System.IO.File]::ReadAllText((Join-Path $taskRepository $taskPath))
    if ($taskText -match '(?i)[A-Z]:[\\/](?:Users|Documents and Settings)[\\/]|/(?:home|Users)/[^/\s]+/') { $taskFailures.Add("$taskPath : personal filesystem path") }
    if ($taskText -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\bghp_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}') { $taskFailures.Add("$taskPath : credential-like content") }
    foreach ($taskEmail in [regex]::Matches($taskText, '[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}')) {
        if ($taskEmail.Value -notmatch '@(?:users\.noreply\.github\.com|example\.(?:com|org|net))$') { $taskFailures.Add("$taskPath : non-example email address") }
    }
    foreach ($taskMatch in [regex]::Matches($taskText, '\b[A-Z]{2}\d{2}(?: ?[A-Z0-9]){11,30}\b')) {
        $taskIban = $taskMatch.Value.Replace(' ', '')
        $taskReordered = $taskIban.Substring(4) + $taskIban.Substring(0, 4)
        $taskRemainder = 0
        foreach ($taskCharacter in $taskReordered.ToCharArray()) {
            $taskDigits = if ([char]::IsDigit($taskCharacter)) { [string]$taskCharacter } else { ([int]$taskCharacter - [int][char]'A' + 10).ToString() }
            foreach ($taskDigit in $taskDigits.ToCharArray()) { $taskRemainder = ($taskRemainder * 10 + [int]::Parse([string]$taskDigit)) % 97 }
        }
        if ($taskRemainder -eq 1) { $taskFailures.Add("$taskPath : checksum-valid bank identifier") }
    }
}
if ($taskFailures.Count -gt 0) { $taskFailures | Write-Output; throw 'Repository privacy checks failed. Sensitive values are deliberately not printed.' }
Write-Output 'Tracked-file privacy checks passed. Manual review is still required for names, document-derived facts and business-specific parser assumptions.'
