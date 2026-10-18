param([switch]$SkipChecks)

$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet build PixelArtAlignment.Tests -c Release --nologo -m:1 -p:TargetPlatformDisplayName=Windows
    if ($LASTEXITCODE -ne 0) { throw 'Prototype build failed.' }
    if (!$SkipChecks) {
        & .\PixelArtAlignment.Tests\bin\Release\net8.0-windows\PixelArtAlignment.Tests.exe --workspace --localization --invariants --ui
        if ($LASTEXITCODE -ne 0) { throw 'Prototype verification failed.' }
    }
    $prototypeOutput = Join-Path $PSScriptRoot 'artifacts\workspace-preview'
    dotnet publish DomainColorTest -c Release -m:1 --nologo -p:PublishProfile=win-x64 -p:TargetPlatformDisplayName=Windows "-p:PublishDir=$prototypeOutput/"
    if ($LASTEXITCODE -ne 0) { throw 'Prototype publish failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\WORKSPACE_PROTOTYPE.md') -Destination (Join-Path $prototypeOutput 'README.md') -Force
    Write-Output (Join-Path $prototypeOutput 'Tessera.exe')
}
finally { Pop-Location }
