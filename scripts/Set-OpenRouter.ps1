$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $projectRoot 'src\CloudHub.Api\appsettings.Local.json'
if (Test-Path -LiteralPath $configPath) {
    throw 'Lokale Konfiguration existiert bereits. Bearbeite sie lokal oder entferne sie selbst, bevor du den Schlüssel erneut einrichtest.'
}
$secureKey = Read-Host 'OpenRouter-Key (Eingabe verborgen)' -AsSecureString
$keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $keyText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    if ([string]::IsNullOrWhiteSpace($keyText)) { throw 'Kein Schlüssel eingegeben.' }
    @{ OpenRouter = @{ ApiKey = $keyText } } | ConvertTo-Json -Depth 3 |
        Set-Content -LiteralPath $configPath -Encoding utf8
    Write-Output 'Schlüssel lokal gespeichert. Starte die API aus src/CloudHub.Api neu. Die Datei ist durch .gitignore ausgeschlossen.'
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    $keyText = $null
    $secureKey.Dispose()
}
