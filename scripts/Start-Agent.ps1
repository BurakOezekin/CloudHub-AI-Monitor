param(
    [ValidateRange(1, 65535)]
    [int]$Port = 5081
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$agentProject = Join-Path $projectRoot 'src\CloudHub.Agent\CloudHub.Agent.csproj'
$previousPort = $env:CLOUDHUB_API_PORT
try {
    $env:CLOUDHUB_API_PORT = [string]$Port
    Write-Output "Agent sendet an http://127.0.0.1:$Port. Dieses Fenster offen lassen. Stoppen mit Strg+C."
    dotnet run --project $agentProject --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Agent beendet mit Fehlercode $LASTEXITCODE." }
} finally {
    $env:CLOUDHUB_API_PORT = $previousPort
}
