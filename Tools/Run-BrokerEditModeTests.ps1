param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$LogFile = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'Temp\unity-broker-editmode.log'),
    [string]$ResultsFile = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'Temp\unity-broker-editmode-results.xml')
)

$ErrorActionPreference = 'Stop'

$logDirectory = Split-Path -Parent $LogFile
$resultsDirectory = Split-Path -Parent $ResultsFile
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

Remove-Item -LiteralPath $LogFile -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $ResultsFile -Force -ErrorAction SilentlyContinue

$arguments = @(
    '-batchmode',
    '-projectPath', $ProjectPath,
    '-runTests',
    '-testPlatform', 'EditMode',
    '-testFilter', 'TheBigRedButtonInstitute.RustyXrBroker.Tests.RustyXrBrokerProtocolTests',
    '-testResults', $ResultsFile,
    '-logFile', $LogFile
)

$start = Get-Date
& $UnityPath @arguments
$exitCode = $LASTEXITCODE

$deadline = (Get-Date).AddMinutes(10)
do
{
    Start-Sleep -Seconds 2
    $activeProcesses = @(
        Get-Process Unity, UnityPackageManager, bee_backend -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $start }
    )
}
while ($activeProcesses.Count -gt 0 -and (Get-Date) -lt $deadline)

$defaultResults = Join-Path $env:LOCALAPPDATA '..\LocalLow\The Big Red Button Institute\The Big Red Button Institute\TestResults.xml'
$defaultResults = [System.IO.Path]::GetFullPath($defaultResults)
if (!(Test-Path -LiteralPath $ResultsFile) -and (Test-Path -LiteralPath $defaultResults))
{
    Copy-Item -LiteralPath $defaultResults -Destination $ResultsFile -Force
}

if (!(Test-Path -LiteralPath $ResultsFile))
{
    throw "Unity test results were not created: $ResultsFile"
}

[xml]$results = Get-Content -LiteralPath $ResultsFile
$run = $results.'test-run'
Write-Host "Broker edit-mode tests: result=$($run.result) total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped)"

if ($exitCode -ne 0)
{
    exit $exitCode
}

if ($run.result -ne 'Passed')
{
    exit 1
}
