param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [string]$LogFile,

    [string]$ExecuteMethod,

    [int]$BackgroundWaitSeconds = 300
)

$ErrorActionPreference = 'Stop'

if (Test-Path $LogFile)
{
    Remove-Item $LogFile -Force
}

$logDirectory = Split-Path -Parent $LogFile
if (![string]::IsNullOrWhiteSpace($logDirectory))
{
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}

$start = Get-Date
$arguments = @(
    '-batchmode'
    '-quit'
    '-projectPath'
    $ProjectPath
)

if (-not [string]::IsNullOrWhiteSpace($ExecuteMethod))
{
    $arguments += @('-executeMethod', $ExecuteMethod)
}

$arguments += @('-logFile', $LogFile)

& $UnityPath @arguments

$deadline = (Get-Date).AddSeconds([Math]::Max(5, $BackgroundWaitSeconds))
do
{
    Start-Sleep -Seconds 2
    $activeProcesses = @(
        Get-Process Unity, UnityPackageManager, bee_backend -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $start }
    )
}
while ($activeProcesses.Count -gt 0 -and (Get-Date) -lt $deadline)

if (!(Test-Path $LogFile))
{
    throw "Unity log was not created: $LogFile"
}

Get-Content $LogFile -Tail 200
