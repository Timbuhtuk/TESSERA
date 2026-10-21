$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot ('artifacts/notes-script-' + [guid]::NewGuid().ToString('N'))
$sourceTitle = ((Get-Content (Join-Path $taskRoot 'release-notes/catalog.json') -Raw | ConvertFrom-Json).releases | Where-Object version -eq 'next').title.en
$global:notesFixtureMode = 'curated'

# Test API array handling, binary JSON downloads and unchanged-source fallback without a network.
function Invoke-RestMethod {
    param($Uri, $Headers)
    if ($Uri -notlike 'https://api.github.com/repos/Test/Notes/releases?*') { throw "Unexpected API request: $Uri" }
    $items = @(
        [pscustomobject]@{ tag_name = 'v9000.0.100'; name = 'Tessera v9000.0.100'; draft = $false; prerelease = $false; body = 'Public notes'; assets = @(
            [pscustomobject]@{ name = 'ReleaseNotes.json'; size = 500; browser_download_url = 'https://github.com/Test/Notes/releases/download/v9000.0.100/ReleaseNotes.json' }
        ) },
        [pscustomobject]@{ tag_name = 'v9000.0.98'; name = 'Older release'; draft = $false; prerelease = $false; body = 'Older API notes'; assets = @() },
        [pscustomobject]@{ tag_name = 'v9001.0.0'; draft = $true; prerelease = $false; body = 'Never bundle drafts'; assets = @() }
    )
    Write-Output -NoEnumerate $items
}
function Invoke-WebRequest {
    param($Uri, $Headers)
    if ($Uri -ne 'https://github.com/Test/Notes/releases/download/v9000.0.100/ReleaseNotes.json') { throw 'Unexpected asset URL' }
    $json = @'
{"releases":[{"version":"9000.0.100","title":{"en":"Last release","ru":"Предыдущий релиз"},"notes":{"en":"Previous changes","ru":"Русская история сохранена"}},{"version":"9000.0.99","title":{"en":"Skipped release"},"notes":{"en":"Skipped changes"}}]}
'@
    return @{ Content = [Text.Encoding]::UTF8.GetBytes($json) }
}
function git {
    if ($args -contains 'hash-object') { $global:LASTEXITCODE = 0; return 'source-new' }
    if ($args -contains 'rev-parse') {
        $global:LASTEXITCODE = 0
        if ($args -contains '--short') { return 'abcdef1' }
        if ($global:notesFixtureMode -eq 'curated') { return 'source-old' }
        return 'source-new'
    }
    if ($args -contains 'log') {
        $global:LASTEXITCODE = 0
        if ($global:notesFixtureMode -eq 'rebuild') { return }
        return @('Fix the actual export path', 'Improve the current editor')
    }
    throw "Unexpected git call: $args"
}
function Require($condition, $message) { if (!$condition) { throw $message } }

foreach ($mode in @('curated', 'commits', 'rebuild')) {
    $global:notesFixtureMode = $mode
    $output = Join-Path $taskOutput $mode
    & (Join-Path $taskRoot 'Prepare-ReleaseNotes.ps1') -Version '9000.0.101' -OutputDirectory $output -Repository 'Test/Notes'
    $catalog = Get-Content (Join-Path $output 'ReleaseNotes.json') -Raw | ConvertFrom-Json
    $body = Get-Content (Join-Path $output 'ReleaseNotes.md') -Raw
    Require ($catalog.releases[0].version -eq '9000.0.101') 'Current version is not stamped'
    Require ($catalog.releases[1].version -eq '9000.0.100') 'API array or archive history was lost'
    Require ($catalog.releases[1].notes.ru -eq 'Русская история сохранена') 'Translated history was lost'
    Require (@($catalog.releases | Where-Object version -eq '9000.0.99').Count -eq 1) 'Skipped upgrade history was lost'
    Require (@($catalog.releases | Where-Object version -eq '9000.0.98').Count -eq 1) 'Older release API notes were lost'
    Require (@($catalog.releases | Where-Object version -eq '9001.0.0').Count -eq 0) 'Draft notes were bundled'
    Require ($body.Contains($catalog.releases[0].notes.en)) 'GitHub and bundled change notes differ'
    if ($mode -eq 'curated') { Require ($catalog.releases[0].title.en -eq $sourceTitle) 'Curated notes were discarded' }
    if ($mode -eq 'commits') { Require ($catalog.releases[0].notes.en.Contains('- Fix the actual export path')) 'Unchanged notes did not use actual commits' }
    if ($mode -eq 'rebuild') { Require ($catalog.releases[0].notes.en.Contains('Rebuilt the current main revision (abcdef1)')) 'A same-commit rebuild reused stale changes' }
    Write-Output "PASS Release note preparation: $mode, archive history, translations, version and GitHub parity"
}
